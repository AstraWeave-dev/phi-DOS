using PhiDos.Core.Machine;
using PhiDos.Core.Storage;

namespace PhiDos.ConsoleHost;

/// <summary>终端版宿主的主体：解析命令行、开机、在终端和虚拟机之间搬运按键和输出。</summary>
internal static class HostApp
{
    private const string Usage = """
        phi-DOS terminal host

        Usage:
          phidos [options] [disk ...]
          phidos --new <file> [--size 360K] [--label NAME] [--blank]
          phidos --phidisk <arguments>        (same as the phidisk tool, see phidisk --help)

        Mounting drives (A disk is a .phidisk file, i.e. a FAT12 image, or a folder):
          phidos disk1 disk2 ...               Mount in order on A:, B:, C: ...
          phidos -a <disk> -b <disk> ...       Mount on A: to D:
          phidos X:=<disk>[:options]           Mount on any drive A: to Z:
            options (append with colons, e.g. C:=D:\games:ro:1.44M):
              ro          write-protected
              rw          writable (default)
              360K, 1.44M, 10M, 1G ...
                          capacity of a folder drive (default 360K)
              unlimited   folder drive without a capacity limit

          If nothing is mounted on A:, it gets a temporary in-memory system disk
          (with FORMAT, MORE, SORT, FIND, VI, ILDOSVER). Changes to it are lost on exit.

        Options:
          -r, --readonly        Mount all following disks write-protected
          --no-date             Do not ask for date and time at boot
          -h, --help            Show this help

        Creating a disk:
          --new <file>          Create a new .phidisk with the system programs and exit
          --size <size>         160K, 180K, 320K, 360K (default), 720K, 1.2M, 1.44M
          --label <name>        Volume label
          --blank               Leave the new disk empty

        Keys: F12 resets the machine. Type EXIT at the prompt to quit.
        Standard input may be redirected from a file to run a script.
        Set PHIDOS_TRACE=1 to see stack traces of internal errors.
        """;

    public static int Run(string[] args)
    {
        HostOptions o;
        try
        {
            o = CommandLine.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Try phidos --help.");
            return 2;
        }

        if (o.ShowHelp)
        {
            Console.WriteLine(Usage);
            return 0;
        }
        if (o.DiskToolArgs is not null) return DiskTool.Run(o.DiskToolArgs);
        if (o.NewDisk is not null) return CreateDisk(o.NewDisk, o.Size, o.Label, o.Blank);

        var options = new MachineOptions
        {
            ExitPowersOff = true,
            PromptForDateTimeAtBoot = o.AskDate,
            DriveCount = Math.Max(2, o.Mounts.Count == 0 ? 2 : o.Mounts.Max(m => m.Drive) - 'A' + 1),
        };

        using var machine = new DosMachine(options);
        try
        {
            if (!o.Mounts.Any(m => m.Drive == 'A'))
                machine.MountImage('A', SystemDisk.CreateInMemory(), ownsStream: true);
            foreach (var m in o.Mounts) Mount(machine, m);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cannot mount: {ex.Message}");
            return 1;
        }

        machine.Start();
        Pump(machine);
        return 0;
    }

    private static void Mount(DosMachine machine, MountSpec m)
    {
        if (Directory.Exists(m.Path))
        {
            machine.MountFolder(m.Drive, m.Path, m.ReadOnly, m.Capacity ?? 362_496);
            return;
        }
        if (!File.Exists(m.Path)) throw new FileNotFoundException($"{m.Drive}: {m.Path} is neither a disk image nor a folder.");
        if (m.Capacity is not null)
            Console.Error.WriteLine($"Note: the capacity option for {m.Drive}: is ignored, a disk image has its own size.");
        machine.MountImage(m.Drive, m.Path, m.ReadOnly);
    }

    private static int CreateDisk(string path, string size, string? label, bool blank)
    {
        var geometry = DiskGeometry.Find(size);
        if (geometry is null)
        {
            Console.Error.WriteLine($"Unknown disk size {size}. Use one of: " +
                                   string.Join(", ", DiskGeometry.All.Select(g => g.Name)));
            return 2;
        }
        SystemDisk.CreateFile(path, geometry, label, withPrograms: !blank);
        Console.WriteLine($"Created {path} ({geometry.Name}{(blank ? ", blank" : "")}).");
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
                if (!Console.IsOutputRedirected) Console.Clear();
                machine.Reset();
                continue;
            }
            if (KeyMap.Translate(info) is KeyInput key) machine.Console.PushKey(key);
        }
    }
}
