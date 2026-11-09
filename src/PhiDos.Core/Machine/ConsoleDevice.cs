using System.Text;

namespace PhiDos.Core.Machine;

/// <summary>
/// 宿主一侧看到的控制台：送入按键，取出输出。
/// 宿主（终端、Godot）只需要和这个接口打交道。
/// </summary>
public interface IConsoleDevice
{
    /// <summary>送入一次按键。可以在任意线程调用。</summary>
    void PushKey(KeyInput key);

    /// <summary>把一串文字当作按键依次送入，'\n' 视为回车。方便脚本和测试。</summary>
    void PushText(string text);

    /// <summary>
    /// 声明不会再有输入了（例如标准输入被重定向且已读完）。
    /// 之后虚拟机在等待按键时会自动关机。
    /// </summary>
    void EndOfInput();

    /// <summary>取出自上次调用以来的全部输出文字（原始字符流，含控制字符）。</summary>
    string TakeOutput();

    /// <summary>字符屏幕。图形宿主按格子绘制它即可。</summary>
    TextScreen Screen { get; }

    /// <summary>虚拟机正在等待按键，并且缓冲区已空。</summary>
    bool IsWaitingForInput { get; }

    /// <summary>
    /// 阻塞直到虚拟机进入"等待按键"状态，超时返回 false。主要用于测试和脚本。
    /// </summary>
    bool WaitForInputIdle(TimeSpan timeout);

    /// <summary>有新输出时触发（在虚拟机线程上触发，处理时注意线程）。</summary>
    event Action? OutputWritten;
}

/// <summary>
/// 控制台设备的实现。虚拟机线程和宿主线程同时访问它，所以内部用锁保护。
/// </summary>
/// <remarks>
/// lock 配合 Monitor.Wait / Monitor.PulseAll 是 .NET 最基础的"等待-通知"写法：
/// Wait 会暂时释放锁并睡眠，直到别的线程 PulseAll 把它叫醒。
/// </remarks>
internal sealed class ConsoleDevice : IConsoleDevice
{
    private readonly object _lock = new();
    private readonly Queue<KeyInput> _keys = new();
    private readonly StringBuilder _output = new();
    private readonly object _outputLock = new();
    private bool _breakPending;
    private bool _endOfInput;
    private int _waiters;

    public TextScreen Screen { get; } = new();

    public event Action? OutputWritten;

    public void PushKey(KeyInput key)
    {
        lock (_lock)
        {
            if (key == KeyInput.CtrlC)
            {
                // 和 DOS 一样：Ctrl+C 会清空键盘缓冲区
                _breakPending = true;
                _keys.Clear();
            }
            else
            {
                _keys.Enqueue(key);
            }
            Monitor.PulseAll(_lock);
        }
    }

    public void PushText(string text)
    {
        // 整串一次性放进队列，避免虚拟机在中途被误判为"空闲"
        lock (_lock)
        {
            foreach (char c in text)
            {
                if (c == '\r') continue;
                var key = c == '\n' ? KeyInput.Enter : new KeyInput(c);
                if (key == KeyInput.CtrlC)
                {
                    _breakPending = true;
                    _keys.Clear();
                }
                else
                {
                    _keys.Enqueue(key);
                }
            }
            Monitor.PulseAll(_lock);
        }
    }

    public void EndOfInput()
    {
        lock (_lock)
        {
            _endOfInput = true;
            Monitor.PulseAll(_lock);
        }
    }

    public string TakeOutput()
    {
        lock (_outputLock)
        {
            string s = _output.ToString();
            _output.Clear();
            return s;
        }
    }

    public bool IsWaitingForInput
    {
        get { lock (_lock) return _waiters > 0 && _keys.Count == 0 && !_breakPending; }
    }

    public bool WaitForInputIdle(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        lock (_lock)
        {
            while (!(_waiters > 0 && _keys.Count == 0 && !_breakPending))
            {
                var left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return false;
                Monitor.Wait(_lock, left);
            }
            return true;
        }
    }

    // ───────── 以下由虚拟机线程调用 ─────────

    internal void Write(string text)
    {
        if (text.Length == 0) return;
        lock (_outputLock)
        {
            _output.Append(text);
            Screen.Write(text);
        }
        OutputWritten?.Invoke();
    }

    internal bool KeyAvailable
    {
        get { lock (_lock) return _keys.Count > 0 || _breakPending; }
    }

    /// <summary>如果有未处理的 Ctrl+C，取走它并返回 true。</summary>
    internal bool TakeBreak()
    {
        lock (_lock)
        {
            if (!_breakPending) return false;
            _breakPending = false;
            return true;
        }
    }

    internal void FlushInput()
    {
        lock (_lock) _keys.Clear();
    }

    /// <summary>
    /// 等待并取出一个按键。有未处理的 Ctrl+C 时返回 <see cref="KeyInput.CtrlC"/>。
    /// </summary>
    internal KeyInput ReadKey(ExecutionGate gate)
    {
        lock (_lock)
        {
            _waiters++;
            Monitor.PulseAll(_lock); // 叫醒 WaitForInputIdle
            try
            {
                while (true)
                {
                    gate.ThrowIfHalted();
                    if (_breakPending)
                    {
                        _breakPending = false;
                        return KeyInput.CtrlC;
                    }
                    if (_keys.Count > 0) return _keys.Dequeue();
                    if (_endOfInput)
                    {
                        // 输入已经用完，没人会再按键了，关机。
                        gate.Halt();
                        gate.ThrowIfHalted();
                    }
                    Monitor.Wait(_lock);
                }
            }
            finally
            {
                _waiters--;
            }
        }
    }

    /// <summary>重启时叫醒所有在等按键的线程，让它们发现闸门已关闭。</summary>
    internal void WakeAll()
    {
        lock (_lock) Monitor.PulseAll(_lock);
    }

    /// <summary>开机时调用：清掉上次留下的输入状态。</summary>
    internal void ResetInput()
    {
        lock (_lock)
        {
            _keys.Clear();
            _breakPending = false;
        }
    }
}
