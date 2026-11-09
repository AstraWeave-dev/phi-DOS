using System.Text;
using PhiDos.Core.Machine;
using PhiDos.Core.Text;

namespace PhiDos.Core.Kernel;

internal sealed partial class DosKernel
{
    private const int MaxLineBytes = 127;

    /// <summary>上一次输入的行，F1 和 F3 从这里复制（DOS 的"模板"）。</summary>
    private string _template = "";

    /// <summary>功能键先返回 0，扫描码留到下一次 ReadKey 返回。</summary>
    private char? _pendingScanCode;

    /// <summary>直接写到屏幕（不经过标准输出，不受重定向影响）。</summary>
    public void ConsoleOut(string text, bool checkBreak)
    {
        if (checkBreak) CheckBreak(console: true);
        Console.Write(text);
    }

    /// <summary>检查有没有按下 Ctrl+C。console 为 false 时只在 BREAK ON 下检查。</summary>
    public void CheckBreak(bool console)
    {
        Gate.ThrowIfHalted();
        if ((console || BreakOn) && Console.TakeBreak()) RaiseBreak();
    }

    /// <summary>显示 ^C 并终止当前程序。</summary>
    public void RaiseBreak()
    {
        Console.FlushInput();
        Console.Write("^C\r\n");
        throw new DosBreakException();
    }

    /// <summary>从键盘读一个按键（不经过标准输入）。</summary>
    public KeyInput ReadKeyboard() => Console.ReadKey(Gate);

    /// <summary>
    /// DOS 的整行输入（INT 21h 0Ah），从键盘读，回显到屏幕。
    /// 编辑键：退格删除；Esc 作废本行（显示 \ 并换行）；F1 从模板复制一个字符；
    /// F3 复制模板剩下的部分；F5 把当前输入设为新模板（显示 @ 并换行）。
    /// 回车结束输入，只回显 CR，不换行（经典 DOS 的习惯）。
    /// </summary>
    public string ReadConsoleLine()
    {
        var line = new List<Rune>();
        int templatePos = 0; // 模板中对应的位置（按字符算）
        var template = _template.EnumerateRunes().ToList();

        while (true)
        {
            KeyInput key = ReadKeyboard();
            if (key == KeyInput.CtrlC) RaiseBreak();

            if (key.IsSpecial)
            {
                switch (key.Key)
                {
                    case DosKey.F1:
                    case DosKey.Right:
                        if (templatePos < template.Count) Append(template[templatePos]);
                        break;
                    case DosKey.F3:
                        while (templatePos < template.Count && Append(template[templatePos])) { }
                        break;
                    case DosKey.F5:
                        Console.Write("@\r\n");
                        template = new List<Rune>(line);
                        line.Clear();
                        templatePos = 0;
                        break;
                    case DosKey.Left:
                        Backspace();
                        break;
                }
                continue;
            }

            switch (key.Char)
            {
                case '\r':
                    Console.Write("\r");
                    string result = string.Concat(line.Select(r => r.ToString()));
                    _template = result;
                    return result;
                case '\n':
                    // Ctrl+J：换到下一行显示，但不结束输入
                    Console.Write("\r\n");
                    break;
                case '\b':
                    Backspace();
                    break;
                case '\x1b':
                    Console.Write("\\\r\n");
                    line.Clear();
                    templatePos = 0;
                    break;
                default:
                    Append(new Rune(key.Char));
                    break;
            }
        }

        bool Append(Rune r)
        {
            int bytes = line.Sum(x => x.Utf8SequenceLength);
            if (bytes + r.Utf8SequenceLength > MaxLineBytes)
            {
                Console.Write("\a");
                return false;
            }
            line.Add(r);
            templatePos++;
            Console.Write(Echo(r));
            return true;
        }

        void Backspace()
        {
            if (line.Count == 0) return;
            Rune last = line[^1];
            line.RemoveAt(line.Count - 1);
            if (templatePos > 0) templatePos--;
            int width = last.Value < 0x20 && last.Value != '\t' ? 2 : Math.Max(1, DisplayWidth.Of(last));
            for (int i = 0; i < width; i++) Console.Write("\b \b");
        }
    }

    /// <summary>控制字符回显成 ^X 的形式（Ctrl+Z 显示为 ^Z），其余原样显示。</summary>
    private static string Echo(Rune r)
    {
        if (r.Value == '\t') return "\t";
        if (r.Value < 0x20) return "^" + (char)('@' + r.Value);
        return r.ToString();
    }

    // ───────── 标准输入输出（会跟随重定向）─────────

    public void StdWrite(DosProcess process, string text)
    {
        if (text.Length == 0) { CheckBreak(console: true); return; }
        var file = process.GetHandle(1);
        if (file.IsConsole) ConsoleOut(text, checkBreak: true);
        else file.Write(Encoding.UTF8.GetBytes(text));
    }

    public void ErrWrite(DosProcess process, string text)
    {
        var file = process.GetHandle(2);
        if (file.IsConsole) ConsoleOut(text, checkBreak: true);
        else file.Write(Encoding.UTF8.GetBytes(text));
    }

    public char StdReadKey(DosProcess process, bool echo)
    {
        if (_pendingScanCode is char scan)
        {
            _pendingScanCode = null;
            return scan;
        }
        var file = process.GetHandle(0);
        if (!file.IsConsole) return ReadUtf8Char(file);

        CheckBreak(console: true);
        KeyInput key = ReadKeyboard();
        if (key == KeyInput.CtrlC) RaiseBreak();
        if (key.IsSpecial)
        {
            _pendingScanCode = (char)key.ScanCode;
            return '\0';
        }
        if (echo) Console.Write(key.Char < 0x20 && key.Char is not ('\r' or '\b' or '\t' or '\a') ? Echo(new Rune(key.Char)) : key.Char.ToString());
        return key.Char;
    }

    public string StdReadLine(DosProcess process)
    {
        var file = process.GetHandle(0);
        if (file.IsConsole)
        {
            CheckBreak(console: true);
            return ReadConsoleLine();
        }

        // 标准输入被重定向到文件：读到 LF 为止，去掉 CR。读到结尾时返回 "\x1a"。
        var bytes = new List<byte>();
        Span<byte> one = stackalloc byte[1];
        // stackalloc 在栈上分配一小块内存，用完自动释放，比 new byte[1] 更省事。
        bool any = false;
        while (file.Read(one) == 1)
        {
            any = true;
            if (one[0] == '\n') break;
            if (one[0] == 0x1a) { if (bytes.Count == 0) return "\x1a"; break; }
            if (one[0] != '\r') bytes.Add(one[0]);
        }
        if (!any) return "\x1a";
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    public bool StdKeyAvailable(DosProcess process)
    {
        CheckBreak(console: true);
        var file = process.GetHandle(0);
        if (!file.IsConsole) return true;
        return _pendingScanCode is not null || Console.KeyAvailable;
    }

    private static char ReadUtf8Char(OpenFile file)
    {
        Span<byte> buf = stackalloc byte[4];
        if (file.Read(buf[..1]) == 0) return '\x1a';
        int len = buf[0] switch
        {
            < 0x80 => 1,
            >= 0xF0 => 4,
            >= 0xE0 => 3,
            >= 0xC0 => 2,
            _ => 1,
        };
        int got = 1;
        while (got < len && file.Read(buf.Slice(got, 1)) == 1) got++;
        string s = Encoding.UTF8.GetString(buf[..got]);
        return s.Length > 0 ? s[0] : '?';
    }
}
