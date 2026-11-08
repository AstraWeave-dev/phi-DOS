using System.Runtime.CompilerServices;
using PhiDos.Abi;

namespace PhiDos.Core.Machine;

/// <summary>
/// 一次开机对应一个闸门。重启或关机时闸门关闭，
/// 此后这次开机的线程只要调用系统或经过注入的检查点，就会被中断或冻结。
/// </summary>
internal sealed class ExecutionGate : IExecutionGate
{
    // 允许抛出 MachineHaltException 的次数。超过后说明程序在吞异常，直接冻结线程。
    private const int MaxHaltThrows = 16;

    private readonly ConsoleDevice _console;
    private volatile bool _halted;
    private int _haltThrows;

    public ExecutionGate(ConsoleDevice console) => _console = console;

    public bool IsHalted => _halted;

    /// <summary>为 true 时，注入的检查点也响应 Ctrl+C（对应 BREAK ON）。</summary>
    public volatile bool PollChecksBreak;

    public void Halt() => _halted = true;

    public void ThrowIfHalted()
    {
        if (!_halted) return;
        if (Interlocked.Increment(ref _haltThrows) <= MaxHaltThrows)
            throw new MachineHaltException();
        Park();
    }

    /// <summary>
    /// 由注入的代码调用。.NET 不能强制杀死线程，所以对于不肯退出的线程，
    /// 让它永远睡下去：不占 CPU，只占一点内存。
    /// </summary>
    public void Poll()
    {
        ThrowIfHalted();
        if (PollChecksBreak && _console.TakeBreak())
            throw new DosBreakException();
        // 递归太深时抛出可以捕获的异常，避免栈溢出直接让整个游戏崩溃。
        RuntimeHelpers.EnsureSufficientExecutionStack();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Park()
    {
        while (true) Thread.Sleep(Timeout.Infinite);
    }
}
