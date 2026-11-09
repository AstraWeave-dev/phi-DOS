using System;
using System.Collections.Generic;
using System.Text;
using PhiDos.Abi;

namespace PhiDos.Programs;

/// <summary>
/// VI [文件]
/// 一个模仿 vi 手感的全屏编辑器。只做常用的部分：移动、插入、删除行、复制粘贴、撤销一步、搜索、读写文件。
/// </summary>
/// <remarks>
/// 屏幕 80×25：上面 24 行显示文字，空行用 ~ 表示；最后一行显示状态和命令。
/// 全屏显示靠 ANSI 转义序列（ESC[行;列H 定位光标、ESC[K 清到行尾），phi-DOS 的屏幕支持这些序列。
/// 每按一次键就把整个屏幕重画一遍，对 80×25 来说足够快，代码也最简单。
/// </remarks>
public sealed class Vi : IPhiProgram
{
    private const int Columns = 80;
    private const int TextRows = 24;
    private const int TabWidth = 8;

    private enum Mode { Normal, Insert }

    private IDos _dos = null!;

    // 文本：每行是一个字符列表。用 Rune（一个完整的 Unicode 字符）而不是 char，汉字和表情符号才不会被切成两半。
    private List<List<Rune>> _lines = [[]];
    private int _row;          // 光标所在行
    private int _col;          // 光标所在列（第几个字符）
    private int _wantedCol;    // 上下移动时想保持的列
    private int _top;          // 屏幕第一行显示的是第几行
    private Mode _mode = Mode.Normal;

    private string? _fileName;
    private bool _modified;
    private string _message = "";

    // 撤销：改动之前存一份快照。原版 vi 只能撤销一步，再按一次 u 就是重做。
    private Snapshot? _undo;

    // 复制和删除的整行放在这里，p / P 粘贴
    private List<List<Rune>> _register = [];

    private string _lastSearch = "";
    private bool _lastSearchForward = true;

    private sealed record Snapshot(List<List<Rune>> Lines, int Row, int Col);

    public int Run(IDos dos, string[] args)
    {
        _dos = dos;
        if (args.Length > 1)
        {
            dos.WriteLine("Usage: VI [file]");
            return 1;
        }
        if (args.Length == 1)
        {
            _fileName = args[0].ToUpperInvariant();
            Load(_fileName);
        }

        try
        {
            MainLoop();
        }
        finally
        {
            // 不管怎样退出（包括 Ctrl+C），都把屏幕清干净再回到 DOS
            dos.Write("\x1b[2J");
        }
        return 0;
    }

    // ───────── 主循环 ─────────

    private void MainLoop()
    {
        string pending = ""; // 两个键组成的命令（gg、dd、yy、ZZ）的第一个键，以及数字前缀
        while (true)
        {
            Render();
            Key key = ReadKey();

            if (_mode == Mode.Insert)
            {
                HandleInsert(key);
                continue;
            }

            _message = "";
            if (key.Char is >= '1' and <= '9' || (key.Char == '0' && pending.Length > 0 && char.IsDigit(pending[^1])))
            {
                pending += key.Char;
                continue;
            }

            string count = pending.TrimEnd('g', 'd', 'y', 'Z');
            string prefix = pending[count.Length..];
            pending = "";

            if (prefix.Length > 0)
            {
                string combo = prefix + key.Char;
                switch (combo)
                {
                    case "gg": GoToLine(count.Length > 0 ? int.Parse(count) : 1); break;
                    case "dd": DeleteLine(); break;
                    case "yy": YankLine(); break;
                    case "ZZ": if (WriteAndQuit(onlyIfModified: true)) return; break;
                    default: Beep(); break;
                }
                continue;
            }

            if (key.Special != SpecialKey.None)
            {
                HandleMovementKey(key.Special);
                continue;
            }

            switch (key.Char)
            {
                case 'h': MoveLeft(); break;
                case 'l' or ' ': MoveRight(); break;
                case 'j' or '\r' or '+': MoveVertical(1); break;
                case 'k' or '-': MoveVertical(-1); break;
                case '0': _col = 0; _wantedCol = 0; break;
                case '^': _col = FirstNonBlank(); _wantedCol = _col; break;
                case '$': _col = Math.Max(0, Line.Count - 1); _wantedCol = int.MaxValue; break;
                case 'w': WordForward(); break;
                case 'b': WordBackward(); break;
                case 'G': GoToLine(count.Length > 0 ? int.Parse(count) : _lines.Count); break;
                case '\x06': PageDown(); break; // Ctrl+F
                case '\x02': PageUp(); break;   // Ctrl+B
                case '\x0c': break;              // Ctrl+L：重画（反正每次都重画）

                case 'i': StartInsert(); break;
                case 'a': StartInsert(); if (Line.Count > 0) _col++; break;
                case 'I': StartInsert(); _col = FirstNonBlank(); break;
                case 'A': StartInsert(); _col = Line.Count; break;
                case 'o': StartInsert(); OpenLine(below: true); break;
                case 'O': StartInsert(); OpenLine(below: false); break;

                case 'x': DeleteChar(); break;
                case 'p': Put(below: true); break;
                case 'P': Put(below: false); break;
                case 'u': Undo(); break;

                case '/': Search(ReadCommandLine("/"), forward: true, remember: true); break;
                case '?': Search(ReadCommandLine("?"), forward: false, remember: true); break;
                case 'n': Search(_lastSearch, _lastSearchForward, remember: false); break;
                case 'N': Search(_lastSearch, !_lastSearchForward, remember: false); break;

                case ':':
                    if (ExCommand(ReadCommandLine(":"))) return;
                    break;

                case 'g' or 'd' or 'y' or 'Z':
                    pending = count + key.Char;
                    break;

                case '\x1b': Beep(); break; // 普通模式下按 Esc，vi 会响一声
                default: Beep(); break;
            }
        }
    }

    private List<Rune> Line => _lines[_row];

    // ───────── 插入模式 ─────────

    private void StartInsert()
    {
        SaveUndo();
        _mode = Mode.Insert;
        _message = "-- INSERT --";
    }

    private void HandleInsert(Key key)
    {
        if (key.Special != SpecialKey.None)
        {
            // 插入模式里也能用方向键
            HandleMovementKey(key.Special);
            return;
        }

        switch (key.Char)
        {
            case '\x1b':
                _mode = Mode.Normal;
                _message = "";
                if (_col > 0) _col--; // 回到普通模式时光标左移一格，这是 vi 的习惯
                _wantedCol = _col;
                break;
            case '\r':
                var rest = Line.GetRange(_col, Line.Count - _col);
                Line.RemoveRange(_col, Line.Count - _col);
                _lines.Insert(_row + 1, rest);
                _row++;
                _col = 0;
                _modified = true;
                break;
            case '\b':
                if (_col > 0)
                {
                    Line.RemoveAt(--_col);
                    _modified = true;
                }
                else if (_row > 0)
                {
                    // 在行首退格：和上一行合并
                    int join = _lines[_row - 1].Count;
                    _lines[_row - 1].AddRange(Line);
                    _lines.RemoveAt(_row);
                    _row--;
                    _col = join;
                    _modified = true;
                }
                break;
            default:
                if (key.Char < ' ' && key.Char != '\t') { Beep(); break; }
                Line.Insert(_col++, key.Rune);
                _modified = true;
                break;
        }
        _wantedCol = _col;
    }

    private void OpenLine(bool below)
    {
        int at = below ? _row + 1 : _row;
        _lines.Insert(at, []);
        _row = at;
        _col = 0;
        _modified = true;
    }

    // ───────── 移动 ─────────

    private void HandleMovementKey(SpecialKey key)
    {
        switch (key)
        {
            case SpecialKey.Left: MoveLeft(); break;
            case SpecialKey.Right:
                if (_mode == Mode.Insert) { if (_col < Line.Count) _col++; _wantedCol = _col; }
                else MoveRight();
                break;
            case SpecialKey.Up: MoveVertical(-1); break;
            case SpecialKey.Down: MoveVertical(1); break;
            case SpecialKey.Home: _col = 0; _wantedCol = 0; break;
            case SpecialKey.End: _col = MaxCol(); _wantedCol = int.MaxValue; break;
            case SpecialKey.PageUp: PageUp(); break;
            case SpecialKey.PageDown: PageDown(); break;
            case SpecialKey.Delete:
                if (_mode == Mode.Normal) DeleteChar();
                else if (_col < Line.Count) { Line.RemoveAt(_col); _modified = true; }
                break;
        }
    }

    /// <summary>普通模式下光标最多到最后一个字符上，插入模式下可以到最后一个字符后面。</summary>
    private int MaxCol() => _mode == Mode.Insert ? Line.Count : Math.Max(0, Line.Count - 1);

    private void MoveLeft()
    {
        if (_col > 0) _col--;
        else Beep();
        _wantedCol = _col;
    }

    private void MoveRight()
    {
        if (_col < MaxCol()) _col++;
        else Beep();
        _wantedCol = _col;
    }

    private void MoveVertical(int delta)
    {
        int target = _row + delta;
        if (target < 0 || target >= _lines.Count) { Beep(); return; }
        _row = target;
        _col = Math.Min(_wantedCol, MaxCol());
    }

    private void GoToLine(int line)
    {
        _row = Math.Clamp(line, 1, _lines.Count) - 1;
        _col = FirstNonBlank();
        _wantedCol = _col;
    }

    private void PageDown()
    {
        _top = Math.Min(_top + TextRows - 2, Math.Max(0, _lines.Count - 1));
        _row = Math.Max(_row, _top);
        _col = Math.Min(_wantedCol, MaxCol());
    }

    private void PageUp()
    {
        _top = Math.Max(0, _top - (TextRows - 2));
        _row = Math.Min(_row, _top + TextRows - 1);
        _row = Math.Min(_row, _lines.Count - 1);
        _col = Math.Min(_wantedCol, MaxCol());
    }

    private int FirstNonBlank()
    {
        int i = 0;
        while (i < Line.Count - 1 && Line[i].Value is ' ' or '\t') i++;
        return i;
    }

    /// <summary>字符分三类：空白、单词字符（字母数字下划线和所有非 ASCII 字符）、标点。w 和 b 按类跳。</summary>
    private static int CharClass(Rune r)
    {
        if (r.Value is ' ' or '\t') return 0;
        if (r.Value > 127 || Rune.IsLetterOrDigit(r) || r.Value == '_') return 1;
        return 2;
    }

    private void WordForward()
    {
        int row = _row, col = _col;
        // 先跳过当前这个词
        if (col < _lines[row].Count)
        {
            int cls = CharClass(_lines[row][col]);
            while (col < _lines[row].Count && CharClass(_lines[row][col]) == cls && cls != 0) col++;
        }
        // 再跳过空白（可以跨行；空行本身算一个停靠点）
        while (true)
        {
            if (col >= _lines[row].Count)
            {
                if (row + 1 >= _lines.Count) { col = Math.Max(0, _lines[row].Count - 1); break; }
                row++;
                col = 0;
                if (_lines[row].Count == 0) break;
                continue;
            }
            if (CharClass(_lines[row][col]) != 0) break;
            col++;
        }
        _row = row;
        _col = col;
        _wantedCol = col;
    }

    private void WordBackward()
    {
        int row = _row, col = _col;
        // 往前退一格，跳过空白
        while (true)
        {
            if (col == 0)
            {
                if (row == 0) break;
                row--;
                col = _lines[row].Count;
                if (col == 0) break;
                continue;
            }
            col--;
            if (CharClass(_lines[row][col]) != 0) break;
        }
        // 再退到这个词的开头
        if (col < _lines[row].Count)
        {
            int cls = CharClass(_lines[row][col]);
            while (col > 0 && CharClass(_lines[row][col - 1]) == cls) col--;
        }
        _row = row;
        _col = col;
        _wantedCol = col;
    }

    // ───────── 修改 ─────────

    private void DeleteChar()
    {
        if (Line.Count == 0) { Beep(); return; }
        SaveUndo();
        Line.RemoveAt(_col);
        _col = Math.Min(_col, MaxCol());
        _modified = true;
    }

    private void DeleteLine()
    {
        SaveUndo();
        _register = [new List<Rune>(Line)];
        if (_lines.Count == 1) _lines[0] = [];
        else _lines.RemoveAt(_row);
        _row = Math.Min(_row, _lines.Count - 1);
        _col = FirstNonBlank();
        _modified = true;
    }

    private void YankLine()
    {
        _register = [new List<Rune>(Line)];
        _message = "1 line yanked";
    }

    private void Put(bool below)
    {
        if (_register.Count == 0) { Beep(); return; }
        SaveUndo();
        int at = below ? _row + 1 : _row;
        foreach (var line in _register) _lines.Insert(at++, new List<Rune>(line));
        _row = below ? _row + 1 : _row;
        _col = FirstNonBlank();
        _modified = true;
    }

    private void SaveUndo()
    {
        var copy = new List<List<Rune>>(_lines.Count);
        foreach (var l in _lines) copy.Add(new List<Rune>(l));
        _undo = new Snapshot(copy, _row, _col);
    }

    private void Undo()
    {
        if (_undo is null) { _message = "Nothing to undo"; return; }
        var current = new Snapshot(_lines, _row, _col);
        _lines = _undo.Lines;
        _row = Math.Min(_undo.Row, _lines.Count - 1);
        _col = Math.Min(_undo.Col, MaxCol());
        _undo = current; // 再按一次 u 就回来，和原版 vi 一样
        _modified = true;
    }

    // ───────── 搜索 ─────────

    private void Search(string? pattern, bool forward, bool remember)
    {
        if (pattern is null) return; // 输入时按了 Esc
        if (pattern.Length == 0) pattern = _lastSearch;
        if (pattern.Length == 0) { _message = "No previous search"; return; }
        if (remember)
        {
            _lastSearch = pattern;
            _lastSearchForward = forward;
        }

        int total = _lines.Count;
        for (int step = 0; step <= total; step++)
        {
            int row = forward ? (_row + step) % total : ((_row - step) % total + total) % total;
            string text = ToText(_lines[row]);
            int start;
            if (forward)
            {
                int from = step == 0 ? CharToIndex(_lines[row], _col + 1) : 0;
                start = from <= text.Length ? text.IndexOf(pattern, from, StringComparison.Ordinal) : -1;
            }
            else
            {
                int before = step == 0 ? CharToIndex(_lines[row], _col) - 1 : text.Length - 1;
                start = before >= 0 ? text.LastIndexOf(pattern, before, StringComparison.Ordinal) : -1;
            }
            if (start < 0) continue;

            bool wrapped = forward ? row < _row || (row == _row && step > 0) : row > _row || (row == _row && step > 0);
            _row = row;
            _col = IndexToChar(_lines[row], start);
            _wantedCol = _col;
            _message = wrapped ? (forward ? "search hit BOTTOM, continuing at TOP" : "search hit TOP, continuing at BOTTOM") : "";
            return;
        }
        _message = "Pattern not found: " + pattern;
    }

    private static string ToText(List<Rune> line)
    {
        var sb = new StringBuilder();
        foreach (var r in line) sb.Append(r.ToString());
        return sb.ToString();
    }

    /// <summary>第 n 个字符在字符串里的下标（一个字符可能占两个 char）。</summary>
    private static int CharToIndex(List<Rune> line, int n)
    {
        int index = 0;
        for (int i = 0; i < n && i < line.Count; i++) index += line[i].Utf16SequenceLength;
        return index;
    }

    private static int IndexToChar(List<Rune> line, int index)
    {
        int pos = 0;
        for (int i = 0; i < line.Count; i++)
        {
            if (pos >= index) return i;
            pos += line[i].Utf16SequenceLength;
        }
        return line.Count;
    }

    // ───────── 冒号命令 ─────────

    /// <summary>执行 : 命令，返回 true 表示退出编辑器。</summary>
    private bool ExCommand(string? command)
    {
        if (command is null) return false;
        command = command.Trim();
        if (command.Length == 0) return false;

        if (int.TryParse(command, out int line)) { GoToLine(line); return false; }
        if (command == "$") { GoToLine(_lines.Count); return false; }

        string name = command;
        string arg = "";
        int space = command.IndexOf(' ');
        if (space > 0)
        {
            name = command[..space];
            arg = command[(space + 1)..].Trim();
        }

        switch (name)
        {
            case "w":
                Write(arg.Length > 0 ? arg : null);
                return false;
            case "w!":
                Write(arg.Length > 0 ? arg : null);
                return false;
            case "q":
                if (_modified) { _message = "No write since last change (use ! to override)"; return false; }
                return true;
            case "q!":
                return true;
            case "wq":
            case "wq!":
                if (arg.Length > 0 && _fileName is null) _fileName = arg.ToUpperInvariant();
                return Write(arg.Length > 0 ? arg : null);
            case "x":
                if (arg.Length > 0 && _fileName is null) _fileName = arg.ToUpperInvariant();
                return WriteAndQuit(onlyIfModified: true);
            default:
                _message = "Not an editor command: " + command;
                return false;
        }
    }

    private bool WriteAndQuit(bool onlyIfModified)
    {
        if (onlyIfModified && !_modified) return true;
        return Write(null);
    }

    // ───────── 文件 ─────────

    private void Load(string path)
    {
        int handle;
        try
        {
            handle = _dos.Open(path, FileAccessMode.Read);
        }
        catch (DosException e) when (e.Error == DosError.FileNotFound)
        {
            _message = $"\"{path}\" [New file]";
            return;
        }
        catch (DosException e)
        {
            _message = $"\"{path}\" {e.Message}";
            return;
        }

        var bytes = new List<byte>();
        try
        {
            var buffer = new byte[4096];
            while (true)
            {
                int n = _dos.Read(handle, buffer);
                if (n == 0) break;
                int z = Array.IndexOf(buffer, (byte)0x1A, 0, n); // Ctrl+Z 是文本文件的结尾
                for (int i = 0; i < (z >= 0 ? z : n); i++) bytes.Add(buffer[i]);
                if (z >= 0) break;
            }
        }
        finally
        {
            _dos.Close(handle);
        }

        string text = Encoding.UTF8.GetString(bytes.ToArray()).Replace("\r\n", "\n").Replace('\r', '\n');
        if (text.EndsWith('\n')) text = text[..^1];
        _lines = [];
        foreach (string l in text.Split('\n'))
        {
            var runes = new List<Rune>();
            foreach (Rune r in l.EnumerateRunes()) runes.Add(r);
            _lines.Add(runes);
        }
        _message = $"\"{path}\" {Describe()}";
    }

    /// <summary>写文件，每行用 CR LF 结尾（DOS 的习惯）。返回是否成功。</summary>
    private bool Write(string? target)
    {
        string? path = target?.ToUpperInvariant() ?? _fileName;
        if (path is null) { _message = "No file name"; return false; }

        var sb = new StringBuilder();
        foreach (var line in _lines)
        {
            foreach (var r in line) sb.Append(r.ToString());
            sb.Append("\r\n");
        }
        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());

        try
        {
            int handle = _dos.Create(path);
            try { _dos.Write(handle, bytes); }
            finally { _dos.Close(handle); }
        }
        catch (DosException e)
        {
            _message = $"\"{path}\" {e.Message}";
            return false;
        }

        _fileName ??= path;
        if (path == _fileName) _modified = false;
        _message = $"\"{path}\" {Describe()} written";
        return true;
    }

    private string Describe()
    {
        int chars = 0;
        foreach (var l in _lines) chars += l.Count + 1;
        return $"{_lines.Count} line{(_lines.Count == 1 ? "" : "s")}, {chars} character{(chars == 1 ? "" : "s")}";
    }

    // ───────── 屏幕 ─────────

    private void Render()
    {
        ScrollToCursor();
        var sb = new StringBuilder();
        int screenRow = 0;
        int cursorX = 0, cursorY = 0;

        for (int i = _top; screenRow < TextRows; i++)
        {
            if (i >= _lines.Count)
            {
                DrawRow(sb, screenRow++, "~", 1);
                continue;
            }
            var segments = Wrap(_lines[i]);
            for (int s = 0; s < segments.Count && screenRow < TextRows; s++)
            {
                var seg = segments[s];
                if (i == _row && _col >= seg.Start && (_col < seg.End || s == segments.Count - 1))
                {
                    cursorY = screenRow;
                    cursorX = ColumnOf(_lines[i], seg.Start, _col);
                    if (cursorX >= Columns) { cursorX = 0; cursorY = Math.Min(screenRow + 1, TextRows - 1); }
                }
                DrawRow(sb, screenRow++, seg.Text, seg.Width);
            }
        }

        // 最后一行：状态信息，最多 79 格，写满 80 格会让屏幕滚动
        string status = Clip(_message, Columns - 1);
        sb.Append($"\x1b[{TextRows + 1};1H").Append(status).Append("\x1b[K");
        sb.Append($"\x1b[{cursorY + 1};{cursorX + 1}H");
        _dos.Write(sb.ToString());
    }

    private static void DrawRow(StringBuilder sb, int row, string text, int width)
    {
        sb.Append($"\x1b[{row + 1};1H").Append(text);
        // 写满 80 格时光标已经到了下一行，这时再清到行尾会把下一行清掉
        if (width < Columns) sb.Append("\x1b[K");
    }

    /// <summary>保证光标所在的行在屏幕上看得见。</summary>
    private void ScrollToCursor()
    {
        if (_row < _top) _top = _row;
        while (true)
        {
            int rows = 0;
            for (int i = _top; i <= _row; i++) rows += Wrap(_lines[i]).Count;
            if (rows <= TextRows || _top == _row) break;
            _top++;
        }
    }

    private readonly record struct Segment(int Start, int End, string Text, int Width);

    /// <summary>把一行折成若干段，每段最多 80 格。制表符展开成空格，宽字符不会被拆到两段里。</summary>
    private static List<Segment> Wrap(List<Rune> line)
    {
        var result = new List<Segment>();
        var sb = new StringBuilder();
        int start = 0, width = 0, column = 0;
        for (int i = 0; i < line.Count; i++)
        {
            int w = line[i].Value == '\t' ? TabWidth - column % TabWidth : CharWidth(line[i]);
            if (width + w > Columns)
            {
                result.Add(new Segment(start, i, sb.ToString(), width));
                sb.Clear();
                start = i;
                width = 0;
                if (line[i].Value == '\t') w = TabWidth - column % TabWidth;
            }
            sb.Append(line[i].Value == '\t' ? new string(' ', w) : line[i].ToString());
            width += w;
            column += w;
        }
        result.Add(new Segment(start, line.Count, sb.ToString(), width));
        return result;
    }

    /// <summary>从第 from 个字符起，到第 to 个字符时已经占了几格。</summary>
    private static int ColumnOf(List<Rune> line, int from, int to)
    {
        int width = 0, column = 0;
        for (int i = 0; i < to && i < line.Count; i++)
        {
            int w = line[i].Value == '\t' ? TabWidth - column % TabWidth : CharWidth(line[i]);
            column += w;
            if (i >= from) width += w;
        }
        return width;
    }

    private static string Clip(string text, int maxWidth)
    {
        var sb = new StringBuilder();
        int width = 0;
        foreach (Rune r in text.EnumerateRunes())
        {
            int w = CharWidth(r);
            if (width + w > maxWidth) break;
            sb.Append(r.ToString());
            width += w;
        }
        return sb.ToString();
    }

    /// <summary>字符占几格：中日韩文字和全角符号两格，其余一格。</summary>
    private static int CharWidth(Rune r)
    {
        int v = r.Value;
        if (v < 0x1100) return 1;
        bool wide = v <= 0x115F || (v >= 0x2E80 && v <= 0xA4CF && v != 0x303F) || (v >= 0xAC00 && v <= 0xD7A3) ||
                    (v >= 0xF900 && v <= 0xFAFF) || (v >= 0xFE30 && v <= 0xFE4F) || (v >= 0xFF00 && v <= 0xFF60) ||
                    (v >= 0xFFE0 && v <= 0xFFE6) || (v >= 0x1F300 && v <= 0x1F64F) || (v >= 0x1F900 && v <= 0x1F9FF) ||
                    (v >= 0x20000 && v <= 0x3FFFD);
        return wide ? 2 : 1;
    }

    /// <summary>在最后一行读一条命令（: / ?）。按 Esc 或在空行上退格时取消，返回 null。</summary>
    private string? ReadCommandLine(string prompt)
    {
        var text = new List<Rune>();
        while (true)
        {
            var sb = new StringBuilder();
            string shown = prompt + ToText(text);
            sb.Append($"\x1b[{TextRows + 1};1H").Append(Clip(shown, Columns - 1)).Append("\x1b[K");
            _dos.Write(sb.ToString());

            Key key = ReadKey();
            if (key.Special != SpecialKey.None) continue;
            switch (key.Char)
            {
                case '\r':
                    return ToText(text);
                case '\x1b':
                    return null;
                case '\b':
                    if (text.Count == 0) return null;
                    text.RemoveAt(text.Count - 1);
                    break;
                default:
                    if (key.Char >= ' ') text.Add(key.Rune);
                    break;
            }
        }
    }

    // ───────── 键盘 ─────────

    private enum SpecialKey { None, Up, Down, Left, Right, Home, End, PageUp, PageDown, Delete }

    private readonly record struct Key(char Char, Rune Rune, SpecialKey Special);

    /// <summary>
    /// 读一个按键。功能键按 DOS 的规矩先返回 0、再返回扫描码；
    /// 一个汉字之外的字符（比如表情符号）由两个 char 组成，这里拼成一个 Rune。
    /// </summary>
    private Key ReadKey()
    {
        char c = _dos.ReadKey(echo: false);
        if (c == '\0')
        {
            char scan = _dos.ReadKey(echo: false);
            SpecialKey special = scan switch
            {
                (char)72 => SpecialKey.Up,
                (char)80 => SpecialKey.Down,
                (char)75 => SpecialKey.Left,
                (char)77 => SpecialKey.Right,
                (char)71 => SpecialKey.Home,
                (char)79 => SpecialKey.End,
                (char)73 => SpecialKey.PageUp,
                (char)81 => SpecialKey.PageDown,
                (char)83 => SpecialKey.Delete,
                _ => SpecialKey.None,
            };
            return new Key('\0', new Rune(' '), special);
        }
        if (char.IsHighSurrogate(c))
        {
            char low = _dos.ReadKey(echo: false);
            return new Key(c, char.IsLowSurrogate(low) ? new Rune(c, low) : new Rune('?'), SpecialKey.None);
        }
        return new Key(c, char.IsSurrogate(c) ? new Rune('?') : new Rune(c), SpecialKey.None);
    }

    private void Beep() => _dos.Write("\a");
}
