using System.Text;
using System.Text.RegularExpressions;
using PhiDos.Abi;
using PhiDos.Core.Machine;
using PhiDos.Core.Text;

namespace PhiDos.Core.Shell;

internal sealed partial class CommandShell
{
    private const int MaxBatchLine = 127;

    /// <summary>正在运行的批处理。批处理里调用另一个批处理时直接换成新的（DOS 2.0 没有 CALL）。</summary>
    private sealed class BatchContext
    {
        public required string Path;
        public required List<string> Args; // Args[0] 是 %0，即批处理自己的名字
        public int Shift;                   // SHIFT 执行过几次
        public long Position;               // 下一行在文件里的位置
        public bool EchoBefore;             // 开始前的 ECHO 状态，结束时恢复
    }

    private BatchContext? _batch;
    private bool _inFor;

    /// <summary>开始运行一个批处理。真正的逐行执行在主循环里进行。</summary>
    private void StartBatch(string path, string name, string[] args)
    {
        bool echo = _batch?.EchoBefore ?? EchoOn;
        _batch = new BatchContext
        {
            Path = path,
            Args = [name, .. args],
            EchoBefore = echo,
        };
    }

    private void EndBatch()
    {
        if (_batch is null) return;
        EchoOn = _batch.EchoBefore;
        _batch = null;
    }

    /// <summary>执行批处理的下一行。批处理文件每读一行都重新打开一次，和原版一样。</summary>
    private void RunBatchStep()
    {
        var batch = _batch!;
        string? line;
        try
        {
            if (_kernel.Console.TakeBreak())
            {
                _kernel.Console.Write("^C\r\n");
                OnBreakDuringBatch();
                return;
            }
            line = ReadBatchLine(batch);
        }
        catch (DosException)
        {
            Err(Messages.BadBatch);
            EndBatch();
            return;
        }
        catch (DosAbortException)
        {
            EndBatch();
            return;
        }
        catch (DosBreakException)
        {
            OnBreakDuringBatch();
            return;
        }

        if (line is null)
        {
            EndBatch();
            return;
        }
        if (line.TrimStart().StartsWith(':')) return; // 标号行，不执行

        string expanded = ExpandBatchLine(line, batch);
        if (EchoOn)
        {
            Out("\r\n");
            PrintPrompt();
            Out(expanded + "\r\n");
        }
        if (expanded.Trim().Length == 0) return;
        ExecuteLine(expanded);
    }

    /// <summary>读一行，返回 null 表示文件结束。</summary>
    private string? ReadBatchLine(BatchContext batch)
    {
        int handle = _kernel.Open(_process, batch.Path, FileAccessMode.Read);
        try
        {
            _kernel.Seek(_process, handle, batch.Position, SeekOrigin.Begin);
            var bytes = new List<byte>();
            var one = new byte[1];
            bool any = false;
            while (_kernel.Read(_process, handle, one) == 1)
            {
                any = true;
                batch.Position++;
                if (one[0] == '\n') break;
                if (one[0] == 0x1A) { batch.Position = long.MaxValue / 2; break; }
                if (one[0] != '\r' && bytes.Count < MaxBatchLine) bytes.Add(one[0]);
            }
            if (!any) return null;
            if (bytes.Count == 0 && batch.Position >= long.MaxValue / 2) return null;
            return Encoding.UTF8.GetString(bytes.ToArray());
        }
        finally
        {
            _process.CloseHandle(handle);
        }
    }

    /// <summary>展开 %0 到 %9、%%（一个百分号）和 %名字%（环境变量）。</summary>
    private string ExpandBatchLine(string line, BatchContext batch)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c != '%' || i + 1 >= line.Length) { sb.Append(c); continue; }
            char n = line[i + 1];
            if (n == '%') { sb.Append('%'); i++; continue; }
            if (n >= '0' && n <= '9')
            {
                int index = n - '0' + batch.Shift;
                if (index < batch.Args.Count) sb.Append(batch.Args[index]);
                i++;
                continue;
            }
            int end = line.IndexOf('%', i + 1);
            if (end > i + 1)
            {
                sb.Append(Environment.Get(line[(i + 1)..end]) ?? "");
                i = end;
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>批处理运行中按了 Ctrl+C：询问是否终止整个批处理。</summary>
    private void OnBreakDuringBatch()
    {
        if (_batch is null) return;
        while (true)
        {
            Err(Messages.TerminateBatch);
            KeyInput key = _kernel.ReadKeyboard();
            char c = char.ToUpperInvariant(key.Char);
            if (key == KeyInput.CtrlC) c = 'Y';
            if (c is 'Y' or 'N')
            {
                _kernel.Console.Write(c + "\r\n");
                if (c == 'Y') EndBatch();
                return;
            }
            _kernel.Console.Write("\r\n");
        }
    }

    // ───────── 批处理命令 ─────────

    private void EchoCommand(ShellCommand cmd)
    {
        string arg = cmd.Tail.Length > 0 ? cmd.Tail[1..] : "";
        string word = arg.Trim().ToUpperInvariant();
        if (word.Length == 0) Out(Messages.EchoIs + (EchoOn ? Messages.On : Messages.Off));
        else if (word == "ON") EchoOn = true;
        else if (word == "OFF") EchoOn = false;
        else Out(arg + "\r\n");
    }

    private void GotoCommand(ShellCommand cmd)
    {
        if (_batch is null) return; // 不在批处理里时 GOTO 什么也不做
        string label = cmd.Args.FirstOrDefault()?.TrimStart(':') ?? "";
        label = Shorten(label);
        _batch.Position = 0;
        while (ReadBatchLine(_batch) is string line)
        {
            string t = line.TrimStart();
            if (t.StartsWith(':') && Shorten(t[1..].Split(' ', '\t')[0]) == label) return;
        }
        EndBatch();
        throw Fail(Messages.LabelNotFound);

        // 标号只比较前 8 个字符，不分大小写
        static string Shorten(string s) => (s.Length > 8 ? s[..8] : s).ToUpperInvariant();
    }

    private void ShiftCommand(ShellCommand cmd)
    {
        if (_batch is not null && _batch.Shift + 1 < _batch.Args.Count + 1) _batch.Shift++;
    }

    /// <summary>
    /// IF [NOT] ERRORLEVEL 数字 命令
    /// IF [NOT] EXIST 文件 命令
    /// IF [NOT] 字符串1==字符串2 命令
    /// </summary>
    private void IfCommand(ShellCommand cmd)
    {
        string rest = cmd.Tail.TrimStart(' ', '\t');
        bool not = false;
        if (TakeWord(ref rest, "NOT")) not = true;

        bool result;
        if (TakeWord(ref rest, "ERRORLEVEL"))
        {
            string n = NextToken(ref rest);
            if (!int.TryParse(n, out int level)) throw Fail(Messages.SyntaxError);
            result = ErrorLevel >= level;
        }
        else if (TakeWord(ref rest, "EXIST"))
        {
            string file = NextToken(ref rest);
            if (file.Length == 0) throw Fail(Messages.SyntaxError);
            try { result = _kernel.FindFiles(file, DosAttributes.None).Count > 0; }
            catch (DosException) { result = false; }
        }
        else
        {
            int eq = rest.IndexOf("==", StringComparison.Ordinal);
            if (eq < 0) throw Fail(Messages.SyntaxError);
            string left = rest[..eq].Trim();
            string afterEq = rest[(eq + 2)..].TrimStart(' ', '\t');
            string right = NextToken(ref afterEq);
            rest = afterEq;
            result = left == right;
        }

        if (result != not && rest.Trim().Length > 0) ExecuteLine(rest.Trim());
    }

    private static bool TakeWord(ref string text, string word)
    {
        string t = text.TrimStart(' ', '\t');
        if (t.Length > word.Length && t.StartsWith(word, StringComparison.OrdinalIgnoreCase) &&
            t[word.Length] is ' ' or '\t')
        {
            text = t[word.Length..].TrimStart(' ', '\t');
            return true;
        }
        return false;
    }

    private static string NextToken(ref string text)
    {
        string t = text.TrimStart(' ', '\t');
        int end = 0;
        while (end < t.Length && t[end] is not (' ' or '\t')) end++;
        text = t[end..];
        return t[..end];
    }

    private static readonly Regex ForSyntax = new(@"^\s*%(\S)\s+IN\s*\((.*?)\)\s*DO\s+(.+)$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>
    /// FOR %变量 IN (集合) DO 命令。批处理里变量写成 %%c（展开后变成 %c）。
    /// 集合里带通配符的项会展开成匹配的文件名。FOR 不能嵌套。
    /// </summary>
    private void ForCommand(ShellCommand cmd)
    {
        var m = ForSyntax.Match(cmd.Tail);
        if (!m.Success) throw Fail(Messages.SyntaxError);
        if (_inFor) throw Fail(Messages.ForCannotBeNested);

        string variable = "%" + m.Groups[1].Value;
        string command = m.Groups[3].Value;
        var items = new List<string>();
        foreach (string item in m.Groups[2].Value.Split([' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (DosNames.HasWildcards(item))
            {
                string prefix = DisplayPrefix(item);
                try
                {
                    foreach (var e in _kernel.FindFiles(item, DosAttributes.None)) items.Add(prefix + e.Name);
                }
                catch (DosException) { }
            }
            else
            {
                items.Add(item);
            }
        }

        _inFor = true;
        try
        {
            foreach (string item in items)
            {
                string line = command.Replace(variable, item, StringComparison.Ordinal);
                if (_batch is not null && EchoOn)
                {
                    Out("\r\n");
                    PrintPrompt();
                    Out(line + "\r\n");
                }
                ExecuteLine(line);
            }
        }
        finally
        {
            _inFor = false;
        }
    }
}
