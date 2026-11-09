namespace PhiDos.Core.Machine;

/// <summary>
/// 虚拟机被重启或关机时，在虚拟机线程上抛出，用来让正在运行的代码尽快退出。
/// 程序不应该捕获它；即使捕获了，之后的系统调用也会把线程冻结。
/// </summary>
public sealed class MachineHaltException : Exception
{
    public MachineHaltException() : base("The machine was halted.") { }
}

/// <summary>用户按下 Ctrl+C，当前程序需要终止。</summary>
internal sealed class DosBreakException : Exception
{
    public DosBreakException() : base("^C") { }
}

/// <summary>严重错误提示里选了 Abort：终止当前程序（在 COMMAND 里则放弃当前命令）。</summary>
internal sealed class DosAbortException : Exception
{
    public DosAbortException() : base("Aborted") { }
}
