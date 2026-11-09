using PhiDos.Core.Machine;
using PhiDos.Core.Storage;

namespace PhiDos.ConsoleHost;

/// <summary>终端版宿主的主体：解析命令行、开机、在终端和虚拟机之间搬运按键和输出。</summary>
internal static class HostApp
{
    private const string Usage = """
        phi-DOS terminal host

        Usage:
          phidos [disk]                          Mount disk on A: and boot
          phidos -a <disk> [-b <disk>] ...       Mount disks on A: to D:
          phidos --new <file> [--size 360K] [--label NAME] [--blank]
                                                 Create a new .phidisk and exit

        A disk is either a .phidisk file (standard FAT12 image) or a folder.
        If nothing is mounted on A:, it gets a temporary in-memory system disk
        (with FORMAT, MORE, SORT and FIND). Changes to it are lost on exit.

        Options:
          -r, --readonly        Mount the following disks write-protected
          --no-date             Do not ask for date and time at boot
          -h, --help            Show this help

        Keys: F12 resets the machine. Type EXIT at the prompt to quit.
        Standard input may be redirected from a file to run a script.
        """;

    public static int Run(string[] args)
    {
        var mounts = new List<(char Drive, string Path, bool ReadOnly)>();
        bool readOnly = false;
        bool askDate = true;
        string? newDisk = null;
        string size = "360K";
        string? label = null;
        bool blank = false;
        char nextDrive = 'A';

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value.");
            try
            {
                switch (a)
                {
                    case "-h" or "--help" or "/?":
                        Console.WriteLine(Usage);
                        return 0;
                    case "-r" or "--readonly": readOnly = true; break;
                    case "--no-date": askDate = false; break;
                    case "--new": newDisk = Next(); break;
                    case "--size": size = Next(); break;
                    case "--label": label = Next(); break;
                    case "--blank": blank = true; break;
                    case "-a" or "-b" or "-c" or "-d":
                        mounts.Add((char.ToUpperInvariant(a[1]), Next(), readOnly));
                        break;
                    default:
                        if (a.StartsWith('-')) throw new ArgumentException($"Unknown option {a}");
                        mounts.Add((nextDrive, a, readOnly));
                        break;
                }
                if (mounts.Count > 0) nextDrive = (char)(mounts.Max(m => m.Drive) + 1);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
        }

        if (newDisk is not null) return CreateDisk(newDisk, size, label, blank);

        var options = new MachineOptions
        {
            ExitPowersOff = true,
            PromptForDateTimeAtBoot = askDate,
            DriveCount = Math.Max(2, mounts.Count == 0 ? 2 : mounts.Max(m => m.Drive) - 'A' + 1),
        };

        using var machine = new DosMachine(options);
        try
        {
            if (!mounts.Any(m => m.Drive == 'A'))
                machine.MountImage('A', SystemDisk.CreateInMemory(), ownsStream: true);
            foreach (var (drive, path, ro) in mounts)
                machine.Mount(drive, path, ro);
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
