namespace PhiDos.Core.Shell;

internal sealed partial class CommandShell
{
    // M6 实现重定向和管道
    private void RunWithRedirection(string line) => ExecuteSimple(line);
}
