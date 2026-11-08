using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("PhiDos.Core")]
[assembly: InternalsVisibleTo("PhiDos.Tests")]

namespace PhiDos.Abi;

/// <summary>
/// 程序加载时，系统会在每个循环的回跳处和每个方法的开头自动插入一次
/// <see cref="Poll"/> 调用。它负责响应 Ctrl+C 和"重启"，让死循环也能被打断。
/// 程序本身不需要、也不应该手动调用它。
/// </summary>
public static class DosRuntime
{
    // [ThreadStatic] 让每个线程拥有自己独立的一份字段值。
    // 每台虚拟机跑在自己的线程上，所以多台机器同时运行时互不干扰。
    [ThreadStatic]
    private static IExecutionGate? t_gate;

    internal static IExecutionGate? CurrentGate
    {
        get => t_gate;
        set => t_gate = value;
    }

    public static void Poll()
    {
        // ?. 是"空值条件运算符"：t_gate 为 null 时什么都不做。
        t_gate?.Poll();
    }
}

/// <summary>由 Core 实现，决定 Poll 时该继续、中断还是冻结线程。</summary>
internal interface IExecutionGate
{
    void Poll();
}
