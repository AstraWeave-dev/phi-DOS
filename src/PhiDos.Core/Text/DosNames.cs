using System.Text;

namespace PhiDos.Core.Text;

/// <summary>
/// 8.3 文件名的规范化、校验与通配符匹配。
/// </summary>
/// <remarks>
/// phi-DOS 用 UTF-8 存文件名，所以 8.3 的限制按字节算：
/// 主名最多 8 字节、扩展名最多 3 字节。一个汉字占 3 字节，所以主名最多放 2 个汉字。
/// 和 DOS 一样，超长的部分会被直接截掉。
/// </remarks>
public static class DosNames
{
    public const int NameBytes = 8;
    public const int ExtBytes = 3;

    // DOS 2.0 文件名里不允许出现的字符
    private const string InvalidChars = "\"/\\[]:|<>+=;, ";

    /// <summary>
    /// 把用户输入的一个路径片段规范化为 8.3 名字：转大写、截断超长部分。
    /// 名字非法时返回 null。允许通配符时 * 和 ? 会保留。
    /// </summary>
    public static string? Normalize(string component, bool allowWildcards)
    {
        if (component.Length == 0) return null;
        if (component is "." or "..") return component;

        int dot = component.IndexOf('.');
        string name = dot < 0 ? component : component[..dot];
        string ext = dot < 0 ? "" : component[(dot + 1)..];
        if (ext.Contains('.')) return null;

        name = Truncate(name.ToUpperInvariant(), NameBytes);
        ext = Truncate(ext.ToUpperInvariant(), ExtBytes);
        if (name.Length == 0) return null;

        foreach (char c in name + ext)
        {
            if (c < 0x20 || InvalidChars.Contains(c)) return null;
            if (!allowWildcards && (c == '*' || c == '?')) return null;
        }
        return ext.Length == 0 ? name : name + "." + ext;
    }

    /// <summary>名字是否已经是合法的规范 8.3 形式（文件夹驱动器用来过滤电脑上的文件）。</summary>
    public static bool IsValidShortName(string name)
    {
        if (name is "." or "..") return false;
        string? normalized = Normalize(name, allowWildcards: false);
        return normalized is not null && string.Equals(normalized, name, StringComparison.OrdinalIgnoreCase)
            && Encoding.UTF8.GetByteCount(name) == Encoding.UTF8.GetByteCount(normalized);
    }

    public static bool HasWildcards(string s) => s.Contains('*') || s.Contains('?');

    /// <summary>把 "FOO.TXT" 拆成 ("FOO", "TXT")。</summary>
    public static (string Name, string Ext) Split(string shortName)
    {
        int dot = shortName.IndexOf('.');
        return dot < 0 ? (shortName, "") : (shortName[..dot], shortName[(dot + 1)..]);
    }

    /// <summary>
    /// 按 DOS 规则做通配符匹配。* 表示"本部分剩下的全部"，? 匹配任意一个字符，
    /// 也可以匹配"没有字符"，所以 A?.TXT 能匹配 A.TXT。
    /// </summary>
    public static bool Matches(string pattern, string shortName)
    {
        var (pn, pe) = Split(pattern);
        var (nn, ne) = Split(shortName);
        return PartMatches(pn, nn, NameBytes) && PartMatches(pe, ne, ExtBytes);
    }

    private static bool PartMatches(string pattern, string name, int width)
    {
        var p = Expand(pattern, width);
        var n = name.EnumerateRunes().ToList();
        int len = Math.Max(p.Count, n.Count);
        for (int i = 0; i < len; i++)
        {
            Rune pc = i < p.Count ? p[i] : new Rune(' ');
            Rune nc = i < n.Count ? n[i] : new Rune(' ');
            if (pc.Value == '?') continue;
            if (pc != nc) return false;
        }
        return true;
    }

    private static List<Rune> Expand(string pattern, int width)
    {
        var result = new List<Rune>();
        foreach (Rune r in pattern.EnumerateRunes())
        {
            if (r.Value == '*')
            {
                // * 之后的字符被忽略（DOS 的行为），本部分剩下的位置全部视为 ?
                while (result.Count < width + 8) result.Add(new Rune('?'));
                return result;
            }
            result.Add(r);
        }
        return result;
    }

    /// <summary>按 UTF-8 字节数截断，不会把一个字符切成两半。</summary>
    public static string Truncate(string s, int maxBytes)
    {
        int bytes = 0;
        var sb = new StringBuilder();
        foreach (Rune r in s.EnumerateRunes())
        {
            int n = r.Utf8SequenceLength;
            if (bytes + n > maxBytes) break;
            bytes += n;
            sb.Append(r.ToString());
        }
        return sb.ToString();
    }

    /// <summary>DOS 设备名。路径的最后一段是这些名字时（不论扩展名）指向设备而不是文件。</summary>
    public static bool IsDeviceName(string shortName, out string device)
    {
        string baseName = Split(shortName).Name;
        device = baseName;
        return baseName is "CON" or "NUL" or "PRN" or "AUX" or "LPT1" or "COM1";
    }
}
