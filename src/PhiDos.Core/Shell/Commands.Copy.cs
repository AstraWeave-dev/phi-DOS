namespace PhiDos.Core.Shell;

internal sealed partial class CommandShell
{
    // M3 实现
    private void CopyCommand(ShellCommand cmd) => throw Fail(Messages.BadCommand);
    private void EraseCommand(ShellCommand cmd) => throw Fail(Messages.BadCommand);
    private void RenameCommand(ShellCommand cmd) => throw Fail(Messages.BadCommand);
}
