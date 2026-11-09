using PhiDos.Abi;
using PhiDos.Core.Kernel;
using PhiDos.Core.Machine;
using PhiDos.Core.Text;

namespace PhiDos.Core.Shell;

/// <summary>
/// 命令解释器，相当于 COMMAND.COM。负责显示提示符、读入命令、解析并执行。
/// </summary>
/// <remarks>
/// 代码按功能分在几个文件里：
/// CommandShell.cs（主循环和命令分派）、Commands.Files.cs（文件和目录命令）、
/// Commands.System.cs（日期时间、环境变量等）、Exec.cs（程序查找）、
/// Batch.cs（批处理）、Redirection.cs（重定向和管道）。
/// </remarks>
internal sealed partial class CommandShell
{
    private readonly DosKernel _kernel;
    private readonly DosProcess _process;
    private readonly bool _isPrimary;
    private readonly Dictionary<string, Action<ShellCommand>> _internal;

    public CommandShell(DosKernel kernel, DosProcess process, bool isPrimary)
    {
        _kernel = kernel;
        _process = process;
        _isPrimary = isPrimary;
        _internal = BuildCommandTable();
    }

    /// <summary>ECHO 开关。关闭时不显示提示符，批处理也不回显命令。</summary>
    public bool EchoOn { get; set; } = true;

    /// <summary>上一个程序的退出码，供 IF ERRORLEVEL 判断。</summary>
    public int ErrorLevel { get; set; }

    private DosEnvironment Environment => _process.Environment;

    // ───────── 主循环 ─────────

    public void Run()
    {
        if (_isPrimary) Startup();

        bool needNewLine = true;
        while (true)
        {
            if (_batch is not null)
            {
                RunBatchStep();
                needNewLine = true;
                continue;
            }

            if (needNewLine && EchoOn) Out("\r\n");
            if (EchoOn) PrintPrompt();

            string line;
            try
            {
                line = _kernel.StdReadLine(_process);
            }
            catch (DosBreakException)
            {
                needNewLine = true;
                continue;
            }
            if (_process.GetHandle(0).IsConsole) Out("\r\n");
            else if (line == "\x1a") return; // 标准输入被重定向且已读完

            if (line.Trim().Length == 0)
            {
                needNewLine = false;
                continue;
            }
            needNewLine = true;
            ExecuteLine(line);
        }
    }

    /// <summary>开机后的第一件事：有 AUTOEXEC.BAT 就运行它，没有就问日期和时间。</summary>
    private void Startup()
    {
        bool hasAutoexec = false;
        try
        {
            hasAutoexec = _kernel.Machine.IsMounted('A') &&
                          _kernel.FindFiles("A:\\AUTOEXEC.BAT", DosAttributes.None).Count > 0;
        }
        catch (Exception ex) when (ex is DosException or DosAbortException) { }

        if (hasAutoexec)
        {
            StartBatch("A:\\AUTOEXEC.BAT", "AUTOEXEC", []);
        }
        else if (_kernel.Machine.Options.PromptForDateTimeAtBoot)
        {
            try
            {
                Out("\r\n");
                DateCommand(ShellCommand.Parse("DATE"));
                TimeCommand(ShellCommand.Parse("TIME"));
            }
            catch (DosBreakException) { }
        }
    }

    /// <summary>执行一整行命令（可能含重定向和管道）。</summary>
    public void ExecuteLine(string line)
    {
        try
        {
            RunWithRedirection(line);
        }
        catch (DosBreakException)
        {
            OnBreakDuringBatch();
        }
        catch (DosAbortException)
        {
            // 严重错误里选了 Abort：放弃这条命令
        }
    }

    /// <summary>执行一条简单命令（重定向已经处理好了）。</summary>
    private void ExecuteSimple(string line)
    {
        var cmd = ShellCommand.Parse(line);
        if (cmd.Name.Length == 0) return;

        // 切换驱动器："B:"
        if (cmd.Name.Length == 2 && cmd.Name[1] == ':' && char.IsLetter(cmd.Name[0]) && cmd.Tail.Trim().Length == 0)
        {
            char d = char.ToUpperInvariant(cmd.Name[0]);
            if (_kernel.IsValidDrive(d)) _kernel.CurrentDrive = d;
            else Err(Messages.InvalidDrive);
            return;
        }

        try
        {
            if (_internal.TryGetValue(cmd.Name, out var action))
                action(cmd);
            else
                RunExternal(cmd);
        }
        catch (ShellError e)
        {
            Err(e.Message);
        }
        catch (DosException e)
        {
            Err(DescribeForShell(e));
        }
    }

    private static string DescribeForShell(DosException e) => e.Error switch
    {
        DosError.InvalidDrive => Messages.InvalidDrive,
        DosError.FileNotFound => Messages.FileNotFound,
        DosError.PathNotFound => Messages.InvalidDirectory,
        DosError.DiskFull => Messages.NoSpace,
        DosError.WriteProtected or DosError.DriveNotReady => "",
        _ => e.Message + "\r\n",
    };

    // ───────── 提示符 ─────────

    /// <summary>
    /// 显示提示符。按 $ 元字符展开（$P 当前路径、$G 大于号、$D 日期、$T 时间 ……）。
    /// 没有设置 PROMPT 时用 MachineOptions.DefaultPrompt（默认 $P$G，即 "A:\>"）；
    /// 它也为空时和 MS-DOS 2.0 一样显示 "A>"。
    /// </summary>
    private void PrintPrompt()
    {
        string? prompt = Environment.Get("PROMPT");
        if (string.IsNullOrEmpty(prompt)) prompt = _kernel.Machine.Options.DefaultPrompt;
        if (string.IsNullOrEmpty(prompt))
        {
            Out(_kernel.CurrentDrive + ">");
            return;
        }
        Out(ExpandPrompt(prompt));
    }

    internal string ExpandPrompt(string prompt)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < prompt.Length; i++)
        {
            char c = prompt[i];
            if (c != '$' || i + 1 >= prompt.Length) { sb.Append(c); continue; }
            char m = char.ToUpperInvariant(prompt[++i]);
            switch (m)
            {
                case 'T': sb.Append(DosFormat.LongTime(_kernel.Now)); break;
                case 'D': sb.Append(DosFormat.LongDate(_kernel.Now)); break;
                case 'P':
                    // 只用记住的当前目录，不去读盘：切到没插盘的驱动器时，提示符不会弹出 Not ready 错误
                    sb.Append(_kernel.CurrentDrive).Append(':').Append(_kernel.GetCurrentDirectoryText(_kernel.CurrentDrive));
                    break;
                case 'V': sb.Append(PhiDosInfo.Banner); break;
                case 'N': sb.Append(_kernel.CurrentDrive); break;
                case 'G': sb.Append('>'); break;
                case 'L': sb.Append('<'); break;
                case 'B': sb.Append('|'); break;
                case 'Q': sb.Append('='); break;
                case '$': sb.Append('$'); break;
                case 'H': sb.Append("\b \b"); break;
                case 'E': sb.Append('\x1b'); break;
                case '_': sb.Append("\r\n"); break;
                default: break; // 不认识的元字符什么也不显示
            }
        }
        return sb.ToString();
    }

    // ───────── 输出 ─────────

    private void Out(string text) => _kernel.StdWrite(_process, text);

    private void Err(string text)
    {
        if (text.Length > 0) _kernel.ErrWrite(_process, text);
    }

    /// <summary>命令执行失败时抛出，消息直接显示给用户。</summary>
    private sealed class ShellError(string message) : Exception(message);

    private static ShellError Fail(string message) => new(message);
}

/// <summary>
/// 解析好的一条命令：命令名（大写）、原始参数串，以及拆好的参数和开关。
/// </summary>
internal sealed class ShellCommand
{
    // DOS 的参数分隔符
    private const string Delimiters = " \t,;=";

    public string Name { get; private init; } = "";
    public string Tail { get; private init; } = "";

    /// <summary>去掉开关（/X）之后的参数。</summary>
    public List<string> Args { get; } = [];

    /// <summary>出现过的开关字母（大写）。</summary>
    public HashSet<char> Switches { get; } = [];

    // 这些命令允许和参数连写，例如 "CD.."、"CD\GAMES"
    private static readonly string[] Joinable = ["CHDIR", "MKDIR", "RMDIR", "CD", "MD", "RD"];

    public static ShellCommand Parse(string line)
    {
        int i = 0;
        while (i < line.Length && Delimiters.Contains(line[i])) i++;
        int start = i;
        while (i < line.Length && !Delimiters.Contains(line[i]) && line[i] != '/') i++;
        string word = line[start..i];
        string tail = line[i..];

        string upper = word.ToUpperInvariant();
        foreach (string j in Joinable)
        {
            if (upper.Length > j.Length && upper.StartsWith(j, StringComparison.Ordinal) &&
                (upper[j.Length] == '.' || upper[j.Length] == '\\'))
            {
                tail = word[j.Length..] + tail;
                upper = j;
                break;
            }
        }

        var cmd = new ShellCommand { Name = upper, Tail = tail };
        cmd.SplitArgs();
        return cmd;
    }

    private void SplitArgs()
    {
        foreach (string piece in Tail.Split(Delimiters.ToCharArray(), StringSplitOptions.RemoveEmptyEntries))
        {
            // "A:/W" 拆成参数 "A:" 和开关 W
            string[] parts = piece.Split('/');
            if (parts[0].Length > 0) Args.Add(parts[0]);
            for (int k = 1; k < parts.Length; k++)
                if (parts[k].Length > 0) Switches.Add(char.ToUpperInvariant(parts[k][0]));
        }
    }

    /// <summary>参数按空白拆开（不处理开关），用于传给外部程序。</summary>
    public string[] RawArgs => Tail.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
}
