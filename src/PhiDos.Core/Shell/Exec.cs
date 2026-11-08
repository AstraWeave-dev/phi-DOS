using PhiDos.Abi;

namespace PhiDos.Core.Shell;

internal sealed partial class CommandShell
{
    /// <summary>
    /// 不是内置命令时，按 DOS 2.0 的顺序找程序：当前目录，再到 PATH 里的每个目录；
    /// 每个目录里依次找 .COM、.EXE、.BAT。都找不到就是 "Bad command or file name"。
    /// </summary>
    private void RunExternal(ShellCommand cmd)
    {
        var path = _kernel.FindProgram(cmd.Name, Environment, allowBatch: true,
            badPathDrive: _ => Err(Messages.BadPathDrive));
        if (path is null)
        {
            Err(Messages.BadCommand);
            return;
        }

        if (path.Value.Leaf!.EndsWith(".BAT", StringComparison.Ordinal))
        {
            StartBatch(path.Value.ToString(), cmd.RawArgs);
            return;
        }

        try
        {
            ErrorLevel = _kernel.RunProgram(_process, path.Value, cmd.Tail.TrimStart(' ', '\t'));
        }
        catch (DosException e) when (e.Error == DosError.BadProgram)
        {
            Err("Error in EXE file\r\n");
            if (e.Message != DosException.DescribeError(DosError.BadProgram)) Err("  " + e.Message + "\r\n");
        }
        catch (DosException e) when (e.Error == DosError.InsufficientMemory)
        {
            Err("Program too big to fit in memory\r\n");
        }
    }
}
