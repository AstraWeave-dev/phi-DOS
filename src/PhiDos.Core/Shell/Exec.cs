namespace PhiDos.Core.Shell;

internal sealed partial class CommandShell
{
    // M4 实现程序查找
    private void RunExternal(ShellCommand cmd) => Err(Messages.BadCommand);
}
