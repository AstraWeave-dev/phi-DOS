namespace PhiDos.Core.Shell;

internal sealed partial class CommandShell
{
    // M6 实现批处理
    private object? _batch;
    private void RunBatchStep() => _batch = null;
    private void StartBatch(string path, string[] args) { }
    private void OnBreakDuringBatch() { }
    private void EchoCommand(ShellCommand cmd) { }
    private void GotoCommand(ShellCommand cmd) { }
    private void ShiftCommand(ShellCommand cmd) { }
    private void IfCommand(ShellCommand cmd) { }
    private void ForCommand(ShellCommand cmd) { }
}
