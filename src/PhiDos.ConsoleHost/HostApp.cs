using PhiDos.Core.Machine;

namespace PhiDos.ConsoleHost;

/// <summary>终端版宿主的主体：解析命令行、开机、在终端和虚拟机之间搬运按键和输出。</summary>
internal static class HostApp
{
    public static int Run(string[] args)
    {
        var options = new MachineOptions
        {
            ExitPowersOff = true,
        };

        using var machine = new DosMachine(options);
        machine.Start();
        Pump(machine);
        return 0;
    }

    /// <summary>主循环：把虚拟机的输出写到终端，把终端的按键送进虚拟机。</summary>
    private static void Pump(DosMachine machine)
    {
        var outputReady = new AutoResetEvent(false);
        machine.Console.OutputWritten += () => outputReady.Set();

        var inputThread = new Thread(() => ReadInput(machine)) { IsBackground = true, Name = "stdin" };
        inputThread.Start();

        while (machine.State == MachineState.Running)
        {
            outputReady.WaitOne(20);
            WriteOutput(machine.Console.TakeOutput());
        }
        WriteOutput(machine.Console.TakeOutput());
        Console.Out.Flush();
    }

    private static void WriteOutput(string text)
    {
        if (text.Length == 0) return;
        if (!Console.IsOutputRedirected && text.Contains("\x1b[2J"))
        {
            // CLS 发出的是 ANSI 清屏序列，终端里直接换成 Console.Clear()
            var pieces = text.Split("\x1b[2J");
            for (int i = 0; i < pieces.Length; i++)
            {
                if (i > 0) Console.Clear();
                Console.Write(pieces[i]);
            }
        }
        else
        {
            Console.Write(text);
        }
        Console.Out.Flush();
    }

    private static void ReadInput(DosMachine machine)
    {
        if (Console.IsInputRedirected)
        {
            // 标准输入来自文件或管道：整段读进去，读完后告诉虚拟机没有更多输入了
            var buffer = new char[4096];
            int n;
            while ((n = Console.In.Read(buffer, 0, buffer.Length)) > 0)
                machine.Console.PushText(new string(buffer, 0, n));
            machine.Console.EndOfInput();
            return;
        }

        Console.TreatControlCAsInput = true;
        while (machine.State != MachineState.Stopped)
        {
            ConsoleKeyInfo info = Console.ReadKey(intercept: true);
            if (info.Key == ConsoleKey.F12)
            {
                machine.Reset();
                if (!Console.IsOutputRedirected) Console.Clear();
                continue;
            }
            if (KeyMap.Translate(info) is KeyInput key) machine.Console.PushKey(key);
        }
    }
}
