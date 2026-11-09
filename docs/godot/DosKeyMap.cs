// 把 Godot 的按键事件翻译成 phi-DOS 的按键。详细说明见 docs/GODOT-INTEGRATION.md。

using Godot;
using PhiDos.Core.Machine;

public static class DosKeyMap
{
    public static KeyInput? Translate(InputEventKey e)
    {
        Key k = e.Keycode;

        if (e.CtrlPressed && k == Key.C) return KeyInput.CtrlC;

        switch (k)
        {
            case Key.Enter or Key.KpEnter: return KeyInput.Enter;
            case Key.Backspace: return KeyInput.Backspace;
            case Key.Escape: return KeyInput.Escape;
            case Key.Tab: return KeyInput.Tab;
            case >= Key.F1 and <= Key.F10: return KeyInput.Special(DosKey.F1 + (int)(k - Key.F1));
            case Key.Up: return KeyInput.Special(DosKey.Up);
            case Key.Down: return KeyInput.Special(DosKey.Down);
            case Key.Left: return KeyInput.Special(DosKey.Left);
            case Key.Right: return KeyInput.Special(DosKey.Right);
            case Key.Home: return KeyInput.Special(DosKey.Home);
            case Key.End: return KeyInput.Special(DosKey.End);
            case Key.Pageup: return KeyInput.Special(DosKey.PageUp);
            case Key.Pagedown: return KeyInput.Special(DosKey.PageDown);
            case Key.Insert: return KeyInput.Special(DosKey.Insert);
            case Key.Delete: return KeyInput.Special(DosKey.Delete);
        }

        // Ctrl+字母 是控制字符，例如 Ctrl+Z 是文件结束符 0x1A
        if (e.CtrlPressed && k >= Key.A && k <= Key.Z) return new KeyInput((char)(k - Key.A + 1));

        if (e.Unicode is > 0 and <= 0xFFFF) return new KeyInput((char)e.Unicode);
        return null;
    }
}
