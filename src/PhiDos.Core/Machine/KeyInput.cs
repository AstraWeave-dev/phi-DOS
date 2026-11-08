namespace PhiDos.Core.Machine;

/// <summary>非字符按键。</summary>
public enum DosKey
{
    None,
    F1, F2, F3, F4, F5, F6, F7, F8, F9, F10,
    Up, Down, Left, Right,
    Home, End, PageUp, PageDown,
    Insert, Delete,
}

/// <summary>
/// 宿主送进虚拟机的一次按键。普通字符键只填 <see cref="Char"/>；
/// 功能键、方向键填 <see cref="Key"/>，此时 Char 为 '\0'。
/// </summary>
/// <remarks>
/// readonly record struct 是不可变的值类型，比较两个按键时会逐字段比较，
/// 适合这种"小而简单的数据"。
/// </remarks>
public readonly record struct KeyInput(char Char, DosKey Key = DosKey.None)
{
    public static readonly KeyInput Enter = new('\r');
    public static readonly KeyInput Backspace = new('\b');
    public static readonly KeyInput Escape = new('\x1b');
    public static readonly KeyInput Tab = new('\t');
    public static readonly KeyInput CtrlC = new('\x03');
    public static readonly KeyInput CtrlZ = new('\x1a');

    public static KeyInput Special(DosKey key) => new('\0', key);

    public bool IsSpecial => Key != DosKey.None;

    /// <summary>IBM PC 键盘扫描码，ReadKey 读到功能键时第二次返回它。</summary>
    public byte ScanCode => Key switch
    {
        >= DosKey.F1 and <= DosKey.F10 => (byte)(59 + (Key - DosKey.F1)),
        DosKey.Home => 71,
        DosKey.Up => 72,
        DosKey.PageUp => 73,
        DosKey.Left => 75,
        DosKey.Right => 77,
        DosKey.End => 79,
        DosKey.Down => 80,
        DosKey.PageDown => 81,
        DosKey.Insert => 82,
        DosKey.Delete => 83,
        _ => 0,
    };
}
