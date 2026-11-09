using PhiDos.Abi;

namespace PhiDos.Core.Machine;

/// <summary>虚拟机的配置。创建 <see cref="DosMachine"/> 时传入。</summary>
public sealed class MachineOptions
{
    /// <summary>驱动器数量，1 到 26，对应 A: 到 Z:。默认 2（A: 和 B:），和当年双软驱的 PC 一样。</summary>
    public int DriveCount { get; init; } = 2;

    /// <summary>
    /// 启动盘上没有 AUTOEXEC.BAT 时，是否像 DOS 2.0 一样在开机时询问日期和时间。
    /// </summary>
    public bool PromptForDateTimeAtBoot { get; init; } = true;

    /// <summary>
    /// 在最外层的 COMMAND 里执行 EXIT 时是否关机。
    /// 原版 DOS 的 EXIT 在最外层什么都不做；终端版宿主把它设为 true，方便退出。
    /// </summary>
    public bool ExitPowersOff { get; init; }

    /// <summary>
    /// 没有设置 PROMPT 环境变量时使用的提示符。默认 "$P$G"，显示为 "A:\>"、"A:\GAMES>"（MS-DOS 6.0 起的默认值）。
    /// 设为空字符串则和 MS-DOS 2.0 一样只显示 "A>"。
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

    /// <summary>加载程序时是否插入 Ctrl+C / 重启检查点。关掉后死循环只能靠重启解决，而且线程会一直占 CPU。</summary>
    public bool InjectInterruptChecks { get; init; } = true;

    /// <summary>加载程序时是否拒绝调用危险 API（直接访问电脑文件、退出进程等）的程序。</summary>
    public bool VerifyPrograms { get; init; } = true;

    /// <summary>环境变量区大小（字节）。DOS 2.0 默认 160 字节，超出时报 "Out of environment space"。</summary>
    public int EnvironmentSize { get; init; } = 160;

    /// <summary>虚拟机线程的栈大小。</summary>
    public int ThreadStackSize { get; init; } = 16 * 1024 * 1024;
}
