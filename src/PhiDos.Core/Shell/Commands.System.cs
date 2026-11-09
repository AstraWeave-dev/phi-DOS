using PhiDos.Abi;

namespace PhiDos.Core.Shell;

internal sealed partial class CommandShell
{
    private Dictionary<string, Action<ShellCommand>> BuildCommandTable()
    {
        // StringComparer.Ordinal：命令名在解析时已经转成大写，这里按字节比较即可
        var t = new Dictionary<string, Action<ShellCommand>>(StringComparer.Ordinal)
        {
            ["DIR"] = DirCommand,
            ["RENAME"] = RenameCommand,
            ["REN"] = RenameCommand,
            ["ERASE"] = EraseCommand,
            ["DEL"] = EraseCommand,
            ["TYPE"] = TypeCommand,
            ["REM"] = _ => { },
            ["COPY"] = CopyCommand,
            ["PAUSE"] = PauseCommand,
            ["DATE"] = DateCommand,
            ["TIME"] = TimeCommand,
            ["VER"] = _ => Out("\r\n" + PhiDosInfo.Banner + "\r\n"),
            ["VOL"] = VolCommand,
            ["CD"] = ChdirCommand,
            ["CHDIR"] = ChdirCommand,
            ["MD"] = MkdirCommand,
            ["MKDIR"] = MkdirCommand,
            ["RD"] = RmdirCommand,
            ["RMDIR"] = RmdirCommand,
            ["BREAK"] = BreakCommand,
            ["VERIFY"] = VerifyCommand,
            ["SET"] = SetCommand,
            ["PROMPT"] = PromptCommand,
            ["PATH"] = PathCommand,
            ["EXIT"] = ExitCommand,
            ["ECHO"] = EchoCommand,
            ["GOTO"] = GotoCommand,
            ["SHIFT"] = ShiftCommand,
            ["IF"] = IfCommand,
            ["FOR"] = ForCommand,
            ["CLS"] = _ => Out(Messages.ClearScreen),
        };
        return t;
    }

    // ───────── DATE / TIME ─────────

    private void DateCommand(ShellCommand cmd)
    {
        string inline = cmd.Tail.Trim();
        if (inline.Length > 0)
        {
            if (TrySetDate(inline)) return;
            Out(Messages.InvalidDate);
        }
        else
        {
            Out(Messages.CurrentDate + DosFormat.LongDate(_kernel.Now));
        }
        while (true)
        {
            Err(Messages.NewDate);
            string input = _kernel.StdReadLine(_process).Trim();
            Out("\r\n");
            if (input.Length == 0 || input == "\x1a") return;
            if (TrySetDate(input)) return;
            Out(Messages.InvalidDate);
        }
    }

    /// <summary>
    /// 解析 月-日-年（分隔符可以是 - / .）。年份写两位时按 19xx 处理，
    /// 和 DOS 2.0 一样只接受 1980 到 2099 年。
    /// </summary>
    private bool TrySetDate(string text)
    {
        var parts = text.Split(['-', '/', '.']);
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out int month) || !int.TryParse(parts[1], out int day) ||
            !int.TryParse(parts[2], out int year))
            return false;
        if (parts[2].Length <= 2) year += 1900;
        if (year < 1980 || year > 2099 || month < 1 || month > 12) return false;
        if (day < 1 || day > DateTime.DaysInMonth(year, month)) return false;

        var now = _kernel.Now;
        var target = new DateTime(year, month, day) + now.TimeOfDay;
        _kernel.Machine.ClockOffset += target - now;
        return true;
    }

    private void TimeCommand(ShellCommand cmd)
    {
        string inline = cmd.Tail.Trim();
        if (inline.Length > 0)
        {
            if (TrySetTime(inline)) return;
            Out(Messages.InvalidTime);
        }
        else
        {
            Out(Messages.CurrentTime + DosFormat.LongTime(_kernel.Now));
        }
        while (true)
        {
            Err(Messages.NewTime);
            string input = _kernel.StdReadLine(_process).Trim();
            Out("\r\n");
            if (input.Length == 0 || input == "\x1a") return;
            if (TrySetTime(input)) return;
            Out(Messages.InvalidTime);
        }
    }

    /// <summary>解析 时[:分[:秒[.百分秒]]]，时分秒之间可以用 : 或 .。</summary>
    private bool TrySetTime(string text)
    {
        string[] fields = text.Split([':', '.']);
        if (fields.Length > 4) return false;
        var nums = new int[4];
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].Length == 0 || fields[i].Length > 2 || !int.TryParse(fields[i], out nums[i])) return false;
        }
        int hour = nums[0], minute = nums[1], second = nums[2], hundredths = nums[3];
        if (hour > 23 || minute > 59 || second > 59 || hundredths > 99) return false;

        var now = _kernel.Now;
        var target = now.Date + new TimeSpan(0, hour, minute, second, hundredths * 10);
        _kernel.Machine.ClockOffset += target - now;
        return true;
    }

    // ───────── 其他 ─────────

    private void PauseCommand(ShellCommand cmd)
    {
        Err(Messages.Pause);
        _kernel.StdReadKey(_process, echo: false);
        Out("\r\n");
    }

    private void BreakCommand(ShellCommand cmd)
    {
        if (OnOff(cmd, Messages.BreakIs, _kernel.BreakOn) is bool on) _kernel.BreakOn = on;
    }

    private void VerifyCommand(ShellCommand cmd)
    {
        if (OnOff(cmd, Messages.VerifyIs, _kernel.Verify) is bool on) _kernel.Verify = on;
    }

    /// <summary>BREAK、VERIFY 共用：无参数时显示状态，有参数时必须是 ON 或 OFF。</summary>
    private bool? OnOff(ShellCommand cmd, string label, bool current)
    {
        string arg = cmd.Tail.Trim().ToUpperInvariant();
        if (arg.Length == 0)
        {
            Out(label + (current ? Messages.On : Messages.Off));
            return null;
        }
        if (arg == "ON") return true;
        if (arg == "OFF") return false;
        throw Fail(Messages.MustSpecifyOnOff);
    }

    private void ExitCommand(ShellCommand cmd)
    {
        if (_isPrimary)
        {
            // 原版最外层的 EXIT 什么也不做；宿主可以让它关机
            if (_kernel.Machine.Options.ExitPowersOff) _kernel.Machine.PowerOffFromInside(_kernel.Gate);
            return;
        }
        throw new ShellExit();
    }

    /// <summary>次级 COMMAND 执行 EXIT 时抛出，回到调用它的程序。</summary>
    internal sealed class ShellExit : Exception;

    // ───────── 环境变量 ─────────

    private void SetCommand(ShellCommand cmd)
    {
        string tail = cmd.Tail.TrimStart(' ', '\t');
        if (tail.Length == 0)
        {
            foreach (var kv in Environment.Entries) Out($"{kv.Key}={kv.Value}\r\n");
            return;
        }
        int eq = tail.IndexOf('=');
        if (eq <= 0) throw Fail(Messages.SyntaxError);
        string name = tail[..eq].Trim();
        string value = tail[(eq + 1)..];
        if (!Environment.Set(name, value)) throw Fail(Messages.OutOfEnvironment);
    }

    private void PromptCommand(ShellCommand cmd)
    {
        string value = cmd.Tail.TrimStart(' ', '\t', ',', ';', '=');
        if (!Environment.Set("PROMPT", value)) throw Fail(Messages.OutOfEnvironment);
    }

    private void PathCommand(ShellCommand cmd)
    {
        string value = cmd.Tail.TrimStart(' ', '\t', '=').Trim();
        if (value.Length == 0)
        {
            string? path = Environment.Get("PATH");
            Out(string.IsNullOrEmpty(path) ? Messages.NoPath + "\r\n" : "PATH=" + path + "\r\n");
            return;
        }
        if (value == ";")
        {
            // "PATH ;" 清空搜索路径
            Environment.Set("PATH", null);
            Environment.Init("PATH", "");
            return;
        }
        if (!Environment.Set("PATH", value.ToUpperInvariant())) throw Fail(Messages.OutOfEnvironment);
    }

    /// <summary>环境变量 PATH 拆成目录列表。</summary>
    private IEnumerable<string> SearchPath()
    {
        string? path = Environment.Get("PATH");
        if (string.IsNullOrEmpty(path)) yield break;
        foreach (string dir in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
            yield return dir.Trim();
    }

    /// <summary>"Are you sure (Y/N)?" 之类的确认：读一行，以 Y 开头为是，N 开头为否，其他重问。</summary>
    private bool AskYesNo(string question)
    {
        while (true)
        {
            Err(question);
            string answer = _kernel.StdReadLine(_process).Trim();
            if (_process.GetHandle(0).IsConsole) Out("\r\n");
            if (answer == "\x1a") return false;
            if (answer.StartsWith('Y') || answer.StartsWith('y')) return true;
            if (answer.StartsWith('N') || answer.StartsWith('n')) return false;
        }
    }
}
