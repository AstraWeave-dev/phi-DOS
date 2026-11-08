using PhiDos.Core.Machine;

namespace PhiDos.Tests;

/// <summary>
/// 测试用的虚拟机包装：开机、输入命令、拿到输出。
/// 时钟固定在 1983-10-20 12:00，开机不问日期时间，保证输出可以逐字比较。
/// </summary>
internal sealed class TestMachine : IDisposable
{
    public static readonly DateTime FixedTime = new(1983, 10, 20, 12, 0, 0);

    public TestMachine(Action<DosMachine>? setup = null, MachineOptions? options = null, bool start = true)
    {
        Machine = new DosMachine(options ?? new MachineOptions
        {
            PromptForDateTimeAtBoot = false,
            Clock = () => FixedTime,
        });
        setup?.Invoke(Machine);
        if (start) Boot();
    }

    public DosMachine Machine { get; }

    /// <summary>开机后的全部输出。</summary>
    public string BootOutput { get; private set; } = "";

    public void Boot()
    {
        Machine.Start();
        WaitIdle();
        BootOutput = Machine.Console.TakeOutput();
    }

    /// <summary>输入一行命令，等它执行完，返回这期间的全部输出（CR LF 统一成 \n）。</summary>
    public string Run(string line)
    {
        Machine.Console.TakeOutput();
        Machine.Console.PushText(line + "\n");
        WaitIdle();
        return Normalize(Machine.Console.TakeOutput());
    }

    /// <summary>送入按键但不加回车。</summary>
    public string Type(string text)
    {
        Machine.Console.TakeOutput();
        Machine.Console.PushText(text);
        WaitIdle();
        return Normalize(Machine.Console.TakeOutput());
    }

    public void WaitIdle()
    {
        if (!Machine.Console.WaitForInputIdle(TimeSpan.FromSeconds(20)))
            throw new TimeoutException("The machine did not return to waiting for input.\n" + Machine.Console.TakeOutput());
    }

    public static string Normalize(string s) => s.Replace("\r\n", "\n").Replace("\r", "");

    public void Dispose() => Machine.Dispose();
}
