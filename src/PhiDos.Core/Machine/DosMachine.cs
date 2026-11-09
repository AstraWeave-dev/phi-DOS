using PhiDos.Core.Kernel;
using PhiDos.Core.Storage;

namespace PhiDos.Core.Machine;

public enum MachineState
{
    /// <summary>还没开机。</summary>
    Off,
    /// <summary>正在运行。</summary>
    Running,
    /// <summary>已关机（调用了 Stop、输入耗尽，或在最外层执行了 EXIT）。</summary>
    Stopped,
}

/// <summary>
/// 一台虚拟电脑。插上磁盘、开机，它就会自己运行 DOS。
/// 宿主只需要把 <see cref="Console"/> 接到屏幕和键盘上。
/// </summary>
/// <example>
/// <code>
/// using var machine = new DosMachine();
/// machine.Mount('A', "game.phidisk");
/// machine.Start();
/// // 每帧：machine.Console.TakeOutput() 或绘制 machine.Console.Screen
/// // 按键：machine.Console.PushKey(...)
/// </code>
/// </example>
public sealed partial class DosMachine : IDisposable
{
    private readonly ConsoleDevice _console = new();
    private readonly object _lock = new();
    private readonly DriveSlot[] _drives;
    private Session? _session;

    public DosMachine(MachineOptions? options = null)
    {
        Options = options ?? new MachineOptions();
        if (Options.DriveCount is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(options), "DriveCount must be 1 to 4.");
        _drives = new DriveSlot[Options.DriveCount];
        for (int i = 0; i < _drives.Length; i++) _drives[i] = new DriveSlot();
    }

    public MachineOptions Options { get; }

    /// <summary>控制台：屏幕输出和键盘输入。</summary>
    public IConsoleDevice Console => _console;

    internal ConsoleDevice ConsoleDevice => _console;

    public MachineState State { get; private set; } = MachineState.Off;

    /// <summary>状态变化时触发（可能在虚拟机线程上触发）。</summary>
    public event Action<MachineState>? StateChanged;

    /// <summary>DATE、TIME 命令设置的时间与宿主时间的差。重启后保留。</summary>
    internal TimeSpan ClockOffset { get; set; }

    internal DateTime Now => Options.Clock() + ClockOffset;

    // ───────── 驱动器 ─────────

    /// <summary>挂载任意实现了 <see cref="IDrive"/> 的驱动器。</summary>
    public void MountDrive(char drive, IDrive device)
    {
        var slot = Slot(drive);
        lock (slot)
        {
            (slot.Drive as IDisposable)?.Dispose();
            slot.Drive = device;
            slot.Generation++;
        }
    }

    /// <summary>取出磁盘。之后访问该驱动器会出现 "Not ready error"。</summary>
    public void Unmount(char drive)
    {
        var slot = Slot(drive);
        lock (slot)
        {
            if (slot.Drive is null) return;
            try { slot.Drive.Flush(); } catch (Exception) { /* 取出时尽量写回，失败也不阻止取出 */ }
            (slot.Drive as IDisposable)?.Dispose();
            slot.Drive = null;
            slot.Generation++;
        }
    }

    public bool IsMounted(char drive) => Slot(drive).Drive is not null;

    internal int DriveCount => _drives.Length;

    internal DriveSlot Slot(char drive)
    {
        int i = char.ToUpperInvariant(drive) - 'A';
        if (i < 0 || i >= _drives.Length)
            throw new ArgumentOutOfRangeException(nameof(drive), $"Drive {drive}: does not exist on this machine.");
        return _drives[i];
    }

    internal bool DriveExists(char drive)
    {
        int i = char.ToUpperInvariant(drive) - 'A';
        return i >= 0 && i < _drives.Length;
    }

    // ───────── 电源 ─────────

    /// <summary>开机。DOS 在后台线程上运行，本方法立即返回。</summary>
    public void Start()
    {
        lock (_lock)
        {
            if (State == MachineState.Running) throw new InvalidOperationException("The machine is already running.");
            StartSession();
        }
    }

    /// <summary>
    /// 重启，相当于按机箱上的 Reset 键：清屏并重新开机，磁盘保持插着。
    /// 正在运行的程序会被中断；实在不肯退出的线程会被冻结并放弃。
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            HaltSession();
            _console.Screen.Clear();
            _console.TakeOutput();
            StartSession();
        }
    }

    /// <summary>关机。最多等待 <paramref name="timeout"/>（默认 2 秒）让线程退出。</summary>
    public void Stop(TimeSpan? timeout = null)
    {
        Session? old;
        lock (_lock)
        {
            old = _session;
            HaltSession();
            SetState(MachineState.Stopped);
        }
        if (old is not null && old.Thread != Thread.CurrentThread)
            old.Thread.Join(timeout ?? TimeSpan.FromSeconds(2));
        FlushDrives();
    }

    /// <summary>等待虚拟机自行关机（例如输入耗尽），超时返回 false。</summary>
    public bool WaitForExit(TimeSpan timeout)
    {
        Session? s;
        lock (_lock) s = _session;
        return s is null || s.Thread.Join(timeout);
    }

    public void Dispose()
    {
        if (State == MachineState.Running) Stop();
        for (int i = 0; i < _drives.Length; i++)
            (_drives[i].Drive as IDisposable)?.Dispose();
    }

    private void StartSession()
    {
        var gate = new ExecutionGate(_console);
        var session = new Session(gate);
        // 第二个参数指定线程的栈大小。IsBackground = true 让这个线程不会阻止整个程序退出。
        session.Thread = new Thread(() => ThreadMain(session), Options.ThreadStackSize)
        {
            IsBackground = true,
            Name = "phi-DOS machine",
        };
        _session = session;
        _console.ResetInput();
        SetState(MachineState.Running);
        session.Thread.Start();
    }

    private void HaltSession()
    {
        if (_session is null) return;
        _session.Gate.Halt();
        _console.WakeAll();
        // 被放弃的线程如果还在跑，就把它降到最低优先级，尽量别和新线程抢 CPU。
        try { _session.Thread.Priority = ThreadPriority.Lowest; } catch (Exception) { }
    }

    private void ThreadMain(Session session)
    {
        Abi.DosRuntime.CurrentGate = session.Gate;
        try
        {
            var kernel = new DosKernel(this, session.Gate);
            kernel.Boot();
        }
        catch (MachineHaltException)
        {
            // 正常的关机或重启
        }
        catch (Exception ex)
        {
            // 内核自身的 bug，不应发生。显示出来方便排查，然后关机。
            _console.Write($"\r\nphi-DOS internal error: {ex.GetType().Name}: {ex.Message}\r\n");
            // 设置环境变量 PHIDOS_TRACE=1 时显示完整的调用栈，方便排查
            if (System.Environment.GetEnvironmentVariable("PHIDOS_TRACE") == "1")
                _console.Write(ex.ToString().Replace("\n", "\r\n") + "\r\n");
        }
        finally
        {
            Abi.DosRuntime.CurrentGate = null;
            bool current;
            lock (_lock) current = ReferenceEquals(_session, session);
            if (current)
            {
                FlushDrives();
                lock (_lock) SetState(MachineState.Stopped);
            }
        }
    }

    /// <summary>由内核调用：最外层 EXIT 且 ExitPowersOff 为 true 时关机。</summary>
    internal void PowerOffFromInside(ExecutionGate gate)
    {
        gate.Halt();
        gate.ThrowIfHalted();
    }

    private void FlushDrives()
    {
        foreach (var slot in _drives)
        {
            lock (slot)
            {
                try { slot.Drive?.Flush(); } catch (Exception) { }
            }
        }
    }

    private void SetState(MachineState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(state);
    }

    private sealed class Session(ExecutionGate gate)
    {
        public ExecutionGate Gate { get; } = gate;
        public Thread Thread { get; set; } = null!;
    }
}

/// <summary>一个驱动器插槽。Generation 在换盘时加一，内核据此重置该盘的当前目录。</summary>
internal sealed class DriveSlot
{
    public IDrive? Drive;
    public int Generation;
}
