namespace PhiDos.Core;

/// <summary>系统名称与版本号。</summary>
public static class PhiDosInfo
{
    /// <summary>
    /// 编译时传入的原始版本号（-p:PhiDosVersion=...），没传时是占位符 "X.XX"。
    /// Release 工作流会把发布时填的版本号传进来。
    /// </summary>
    public const string RawVersion = Build.BuildVersion.Raw;

    /// <summary>显示用的版本号，DOS 风格的两段式，见 <see cref="FormatVersion"/>。</summary>
    public static readonly string Version = FormatVersion(RawVersion);

    /// <summary>开机信息，同时也是 VER 命令的输出。</summary>
    public static readonly string Banner = "phi-DOS Version " + Version;

    /// <summary>
    /// 把版本号换算成 DOS 风格的"主版本.两位次版本"：
    /// 第一段是主版本；后面各段的数字依次接起来，取前两位，不足两位补 0。
    /// 例如 0.1.0 → 0.10，5.2.10 → 5.21，2.0 → 2.00，3.3 → 3.30，6.22 → 6.22。
    /// 横线后面的部分（如 1.0-beta 的 -beta）不显示。不是数字的版本号（如 X.XX）原样返回。
    /// </summary>
    public static string FormatVersion(string raw)
    {
        string core = raw.Split('-', '+')[0];
        string[] parts = core.Split('.');
        if (parts.Length < 2) return raw;
        foreach (string p in parts)
        {
            if (p.Length == 0) return raw;
            foreach (char c in p)
                if (c is < '0' or > '9') return raw;
        }
        string minor = string.Concat(parts[1..]);
        minor = minor.Length >= 2 ? minor[..2] : minor.PadRight(2, '0');
        string major = parts[0].TrimStart('0');
        return (major.Length == 0 ? "0" : major) + "." + minor;
    }
}
