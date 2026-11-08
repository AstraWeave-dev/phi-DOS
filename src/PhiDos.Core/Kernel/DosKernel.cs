using PhiDos.Abi;
using PhiDos.Core.Machine;
using PhiDos.Core.Shell;

namespace PhiDos.Core.Kernel;

/// <summary>
/// 内核。一次开机创建一个，保存这次开机的全部系统状态：
/// 当前驱动器、每个驱动器的当前目录、BREAK/VERIFY 开关、进程等。
/// </summary>
/// <remarks>
/// partial 表示这个类的代码分散在多个文件里（DosKernel.Console.cs、DosKernel.Files.cs ……），
/// 编译时会合并成一个类。这样每个文件只关心一类功能，读起来更轻松。
/// </remarks>
internal sealed partial class DosKernel
{
    public const int MaxHandles = 20;
    public const int MaxCurrentDirectoryLength = 64;

    private readonly DriveState[] _driveStates;

    public DosKernel(DosMachine machine, ExecutionGate gate)
    {
        Machine = machine;
        Console = machine.ConsoleDevice;
        Gate = gate;
        _driveStates = new DriveState[machine.DriveCount];
        for (int i = 0; i < _driveStates.Length; i++) _driveStates[i] = new DriveState();
        Loader = new Loader.ProgramLoader(this);
    }

    public DosMachine Machine { get; }
    public ConsoleDevice Console { get; }
    public ExecutionGate Gate { get; }
    public Loader.ProgramLoader Loader { get; }

    /// <summary>当前驱动器字母，例如 'A'。</summary>
    public char CurrentDrive { get; set; } = 'A';

    /// <summary>BREAK ON 时，所有系统调用都检查 Ctrl+C；OFF 时只有控制台调用检查。</summary>
    public bool BreakOn
    {
        get => Gate.PollChecksBreak;
        set => Gate.PollChecksBreak = value;
    }

    public bool Verify { get; set; }

    public DateTime Now => Machine.Now;

    /// <summary>最外层的进程，即开机时启动的 COMMAND。</summary>
    public DosProcess RootProcess { get; private set; } = null!;

    /// <summary>正在运行的进程（程序里再运行程序时会形成一串）。</summary>
    public DosProcess CurrentProcess { get; set; } = null!;

    /// <summary>开机：显示开机信息，启动 COMMAND。</summary>
    public void Boot()
    {
        Console.Write(PhiDosInfo.Banner + "\r\n");

        var env = new DosEnvironment(Machine.Options.EnvironmentSize);
        env.Init("PATH", "");
        env.Init("COMSPEC", "A:\\COMMAND.COM");

        var con = new ConsoleFile(this) { RefCount = 3 };
        var nul = new NulFile { RefCount = 2 };
        RootProcess = new DosProcess(this, parent: null, env, "COMMAND", []);
        RootProcess.Handles[0] = con;
        RootProcess.Handles[1] = con;
        RootProcess.Handles[2] = con;
        RootProcess.Handles[3] = nul; // AUX
        RootProcess.Handles[4] = nul; // PRN
        CurrentProcess = RootProcess;

        var shell = new CommandShell(this, RootProcess, isPrimary: true);
        shell.Run();
    }

    /// <summary>每个系统调用开头都要经过这里：检查重启和 Ctrl+C。</summary>
    public void EnterCall(bool consoleCall = false)
    {
        Gate.ThrowIfHalted();
        CheckBreak(consoleCall);
    }

    private sealed class DriveState
    {
        /// <summary>当前目录，从根开始的名字列表。空列表表示根目录。</summary>
        public List<string> CurrentDirectory = [];
        public int Generation = -1;
    }
}
