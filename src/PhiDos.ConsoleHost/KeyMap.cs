using PhiDos.Core.Machine;

namespace PhiDos.ConsoleHost;

/// <summary>把 .NET 的按键信息翻译成 phi-DOS 的按键。</summary>
internal static class KeyMap
{
    public static KeyInput? Translate(ConsoleKeyInfo info)
    {
        bool ctrl = (info.Modifiers & ConsoleModifiers.Control) != 0;
        if (ctrl && info.Key == ConsoleKey.C) return KeyInput.CtrlC;

        switch (info.Key)
        {
            case ConsoleKey.Enter: return KeyInput.Enter;
            case ConsoleKey.Backspace: return KeyInput.Backspace;
            case ConsoleKey.Escape: return KeyInput.Escape;
            case ConsoleKey.Tab: return KeyInput.Tab;
            case >= ConsoleKey.F1 and <= ConsoleKey.F10:
                return KeyInput.Special(DosKey.F1 + (info.Key - ConsoleKey.F1));
            case ConsoleKey.UpArrow: return KeyInput.Special(DosKey.Up);
            case ConsoleKey.DownArrow: return KeyInput.Special(DosKey.Down);
            case ConsoleKey.LeftArrow: return KeyInput.Special(DosKey.Left);
            case ConsoleKey.RightArrow: return KeyInput.Special(DosKey.Right);
            case ConsoleKey.Home: return KeyInput.Special(DosKey.Home);
            case ConsoleKey.End: return KeyInput.Special(DosKey.End);
            case ConsoleKey.PageUp: return KeyInput.Special(DosKey.PageUp);
            case ConsoleKey.PageDown: return KeyInput.Special(DosKey.PageDown);
            case ConsoleKey.Insert: return KeyInput.Special(DosKey.Insert);
            case ConsoleKey.Delete: return KeyInput.Special(DosKey.Delete);
        }
        return info.KeyChar != '\0' ? new KeyInput(info.KeyChar) : null;
    }
}
