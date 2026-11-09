using System.Text;
using PhiDos.Core.Text;

namespace PhiDos.Core.Machine;

/// <summary>屏幕上的一个字符格。</summary>
/// <param name="Rune">显示的字符。宽字符的右半格里此值为空格，并且 <paramref name="IsWideTail"/> 为 true。</param>
/// <param name="Attribute">颜色属性，同 PC 文本模式：低 4 位前景色，高 4 位背景色。默认 0x07（灰字黑底）。</param>
/// <param name="IsWideTail">是否是宽字符的右半格，绘制时应跳过。</param>
public readonly record struct ScreenCell(Rune Rune, byte Attribute, bool IsWideTail)
{
    public static readonly ScreenCell Blank = new(new Rune(' '), 0x07, false);
}

/// <summary>
/// 80×25 的字符屏幕，相当于显卡的文本显存。
/// 虚拟机的所有控制台输出都会写到这里，Godot 宿主按格子把它画出来即可。
/// 支持 CR、LF、退格、制表符、响铃，以及 ANSI.SYS 的常用转义序列（清屏、定位光标等）。
/// </summary>
public sealed class TextScreen
{
    public const int Columns = 80;
    public const int Rows = 25;

    private readonly object _lock = new();
    private readonly ScreenCell[] _cells = new ScreenCell[Columns * Rows];
    private int _x, _y;
    private byte _attr = 0x07;
    private long _version;

    // 解析 ESC 序列用的状态
    private int _escState; // 0 普通，1 收到 ESC，2 收到 ESC[
    private readonly StringBuilder _escParams = new();
    private char _pendingHighSurrogate;

    /// <summary>收到响铃字符 (BEL, 0x07) 时触发。</summary>
    public event Action? Bell;

    public TextScreen() => Clear();

    public int CursorX { get { lock (_lock) return _x; } }
    public int CursorY { get { lock (_lock) return _y; } }

    /// <summary>每次屏幕内容变化都会加一。宿主可以用它判断需不需要重绘。</summary>
    public long Version => Interlocked.Read(ref _version);

    public ScreenCell GetCell(int x, int y)
    {
        lock (_lock) return _cells[y * Columns + x];
    }

    /// <summary>把整个屏幕复制一份出来，避免绘制时被后台线程改动。</summary>
    public ScreenCell[] Snapshot(out int cursorX, out int cursorY)
    {
        lock (_lock)
        {
            cursorX = _x;
            cursorY = _y;
            return (ScreenCell[])_cells.Clone();
        }
    }

    /// <summary>取某一行的文字（去掉行尾空格），主要用于测试。</summary>
    public string GetLine(int y)
    {
        lock (_lock)
        {
            var sb = new StringBuilder();
            for (int x = 0; x < Columns; x++)
            {
                var c = _cells[y * Columns + x];
                if (!c.IsWideTail) sb.Append(c.Rune.ToString());
            }
            return sb.ToString().TrimEnd();
        }
    }

    public string[] GetLines()
    {
        var lines = new string[Rows];
        for (int y = 0; y < Rows; y++) lines[y] = GetLine(y);
        return lines;
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Fill(_cells, new ScreenCell(new Rune(' '), _attr, false));
            _x = _y = 0;
            Touch();
        }
    }

    public void Write(string text)
    {
        bool bell = false;
        lock (_lock)
        {
            foreach (char ch in text)
            {
                if (char.IsHighSurrogate(ch)) { _pendingHighSurrogate = ch; continue; }
                Rune rune;
                if (char.IsLowSurrogate(ch))
                {
                    if (_pendingHighSurrogate == 0) continue;
                    rune = new Rune(_pendingHighSurrogate, ch);
                    _pendingHighSurrogate = '\0';
                }
                else
                {
                    rune = new Rune(ch);
                }
                if (Put(rune)) bell = true;
            }
            Touch();
        }
        if (bell) Bell?.Invoke();
    }

    private void Touch() => Interlocked.Increment(ref _version);

    /// <summary>处理一个字符，返回是否需要响铃。</summary>
    private bool Put(Rune rune)
    {
        int v = rune.Value;
        if (_escState == 1)
        {
            if (v == '[') { _escState = 2; _escParams.Clear(); }
            else _escState = 0;
            return false;
        }
        if (_escState == 2)
        {
            if ((v >= '0' && v <= '9') || v == ';' || v == '?') { _escParams.Append((char)v); return false; }
            _escState = 0;
            RunEscape((char)v, _escParams.ToString());
            return false;
        }

        switch (v)
        {
            case 0x1b: _escState = 1; return false;
            case '\r': _x = 0; return false;
            case '\n': LineFeed(); return false;
            case '\b': if (_x > 0) _x--; return false;
            case 0x07: return true;
            case '\t':
                do { PutCell(new Rune(' '), 1); } while (_x % 8 != 0 && _x != 0);
                return false;
        }
        if (v < 0x20) return false; // 其余控制字符不显示

        int width = DisplayWidth.Of(rune);
        if (width == 2 && _x == Columns - 1)
        {
            // 宽字符放不下了，先换行
            _x = 0;
            LineFeed();
        }
        PutCell(rune, width);
        return false;
    }

    private void PutCell(Rune rune, int width)
    {
        if (_x >= Columns) { _x = 0; LineFeed(); }
        _cells[_y * Columns + _x] = new ScreenCell(rune, _attr, false);
        if (width == 2)
            _cells[_y * Columns + _x + 1] = new ScreenCell(new Rune(' '), _attr, true);
        _x += width;
        if (_x >= Columns) { _x = 0; LineFeed(); }
    }

    private void LineFeed()
    {
        if (_y < Rows - 1) { _y++; return; }
        Array.Copy(_cells, Columns, _cells, 0, Columns * (Rows - 1));
        Array.Fill(_cells, new ScreenCell(new Rune(' '), _attr, false), Columns * (Rows - 1), Columns);
    }

    private void RunEscape(char command, string parameters)
    {
        var p = parameters.Split(';');
        int Arg(int i, int def) => i < p.Length && int.TryParse(p[i], out int n) ? n : def;

        switch (command)
        {
            case 'J': // ESC[2J 清屏并把光标移到左上角（ANSI.SYS 的行为）
                if (Arg(0, 0) == 2) Clear();
                break;
            case 'K': // 清除到行尾
                for (int x = _x; x < Columns; x++)
                    _cells[_y * Columns + x] = new ScreenCell(new Rune(' '), _attr, false);
                break;
            case 'H':
            case 'f':
                _y = Math.Clamp(Arg(0, 1) - 1, 0, Rows - 1);
                _x = Math.Clamp(Arg(1, 1) - 1, 0, Columns - 1);
                break;
            case 'A': _y = Math.Max(0, _y - Arg(0, 1)); break;
            case 'B': _y = Math.Min(Rows - 1, _y + Arg(0, 1)); break;
            case 'C': _x = Math.Min(Columns - 1, _x + Arg(0, 1)); break;
            case 'D': _x = Math.Max(0, _x - Arg(0, 1)); break;
            case 'm':
                foreach (var s in p) ApplyGraphicsMode(int.TryParse(s, out int n) ? n : 0);
                break;
        }
    }

    private void ApplyGraphicsMode(int n)
    {
        // ANSI 颜色顺序和 PC 文本模式的颜色顺序不同，需要换算
        ReadOnlySpan<byte> ansiToPc = [0, 4, 2, 6, 1, 5, 3, 7];
        if (n == 0) _attr = 0x07;
        else if (n == 1) _attr |= 0x08;
        else if (n == 5) _attr |= 0x80;
        else if (n == 7) _attr = (byte)(((_attr & 0x07) << 4) | ((_attr & 0x70) >> 4));
        else if (n >= 30 && n <= 37) _attr = (byte)((_attr & 0xF8) | ansiToPc[n - 30]);
        else if (n >= 40 && n <= 47) _attr = (byte)((_attr & 0x8F) | (ansiToPc[n - 40] << 4));
    }
}
