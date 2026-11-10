using PhiDos.Abi;

namespace PhiDos.Core.Machine;

/// <summary>虚拟机的配置。创建 <see cref="DosMachine"/> 时传入。</summary>
public sealed class MachineOptions
{
    /// <summary>驱动器数量，1 到 26，对应 A: 到 Z:。默认 2（A: 和 B:），和当年双软驱的 PC 一样。</summary>
    public int DriveCount { get; init; } = 2;

    /// <summary>
    /// 启动盘上没有 AUTOEXEC.BAT 时，是否在开机时询问日期和时间（经典 DOS 的习惯）。
    /// </summary>
    public bool PromptForDateTimeAtBoot { get; init; } = true;

    /// <summary>
    /// 在最外层的 COMMAND 里执行 EXIT 时是否关机。
    /// 经典 DOS 的 EXIT 在最外层什么都不做；终端版宿主把它设为 true，方便退出。
    /// </summary>
    public bool ExitPowersOff { get; init; }

    /// <summary>
    /// 没有设置 PROMPT 环境变量时使用的提示符。默认 "$P$G"，显示为 "A:\>"、"A:\GAMES>"。
    /// 设为空字符串则只显示 "A>"，也就是早期 DOS 的样子。
    /// </summary>
    public string DefaultPrompt { get; init; } = "$P$G";

    /// <summary>宿主的时钟，默认是电脑的本地时间。测试时可以换成固定时间。</summary>
    public Func<DateTime> Clock { get; init; } = () => DateTime.Now;

    /// <summary>
    /// 内置程序表：把程序名（如 "FORMAT"）直接映射到 C# 实现。
    /// 磁盘上仍必须存在同名的 .COM 或 .EXE 文件，系统才会运行它；
    /// 存在时直接调用这里的实现，不再从文件加载。主要用于调试。
    /// </summary>
    public IReadOnlyDictionary<string, Func<IPhiProgram>> BuiltinPrograms { get; init; }
        = new Dictionary<string, Func<IPhiProgram>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 宿主功能表：程序用 IDos.CallHost(名字, 参数) 呼叫时，调用这里登记的函数。
    /// 函数的参数是程序传来的字符串，返回值交还给程序，返回 null 当作空字符串。
    /// 名字不区分大小写。函数在虚拟机线程上执行，碰 Godot 节点要用 CallDeferred 回到主线程。
    /// </summary>
    /// <remarks>
    /// 和 <see cref="BuiltinPrograms"/> 的区别：那里是"盘上的占位文件 → 游戏里的 C# 类"，
    /// 这里是盘上的真程序自己决定什么时候、用什么参数呼叫宿主。
    /// 函数抛出的 DosException 原样交给程序；其他异常转成 DosError.HostError。
    /// </remarks>
    public IReadOnlyDictionary<string, Func<string, string?>> HostCalls { get; init; }
        = new Dictionary<string, Func<string, string?>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>加载程序时是否插入 Ctrl+C / 重启检查点。关掉后死循环只能靠重启解决，而且线程会一直占 CPU。</summary>
    public bool InjectInterruptChecks { get; init; } = true;

    /// <summary>加载程序时是否拒绝调用危险 API（直接访问电脑文件、退出进程等）的程序。</summary>
    public bool VerifyPrograms { get; init; } = true;

    /// <summary>环境变量区大小（字节）。默认 160 字节，超出时报 "Out of environment space"。</summary>
    public int EnvironmentSize { get; init; } = 160;

    /// <summary>虚拟机线程的栈大小。</summary>
    public int ThreadStackSize { get; init; } = 16 * 1024 * 1024;
}
