namespace PhiDos.Core;

/// <summary>系统名称与版本号。</summary>
public static class PhiDosInfo
{
    /// <summary>版本号占位符，正式版本号确定后在这里统一替换。</summary>
    public const string Version = "X.XX";

    /// <summary>开机信息，同时也是 VER 命令的输出。</summary>
    public const string Banner = "phi-DOS Version " + Version;
}
