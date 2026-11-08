using System.Text;

namespace PhiDos.Core.Text;

/// <summary>
/// 计算文字在字符屏幕上占几格。中日韩文字和全角符号占两格，其余占一格。
/// </summary>
public static class DisplayWidth
{
    public static int Of(Rune rune)
    {
        int v = rune.Value;
        if (v < 0x20) return 0;
        if (v < 0x1100) return 1;
        bool wide =
            v <= 0x115F ||
            (v >= 0x2E80 && v <= 0xA4CF && v != 0x303F) ||
            (v >= 0xAC00 && v <= 0xD7A3) ||
            (v >= 0xF900 && v <= 0xFAFF) ||
            (v >= 0xFE30 && v <= 0xFE4F) ||
            (v >= 0xFF00 && v <= 0xFF60) ||
            (v >= 0xFFE0 && v <= 0xFFE6) ||
            (v >= 0x1F300 && v <= 0x1F64F) ||
            (v >= 0x1F900 && v <= 0x1F9FF) ||
            (v >= 0x20000 && v <= 0x3FFFD);
        return wide ? 2 : 1;
    }

    public static int Of(string text)
    {
        int width = 0;
        foreach (Rune r in text.EnumerateRunes()) width += Of(r);
        return width;
    }

    /// <summary>右侧补空格到指定显示宽度（DIR 列对齐用）。</summary>
    public static string PadRight(string text, int width)
    {
        int w = Of(text);
        return w >= width ? text : text + new string(' ', width - w);
    }
}
