namespace PhiDos.Core.Kernel;

/// <summary>
/// 解析后的绝对路径：驱动器字母 + 从根目录开始的规范化名字列表。
/// 例如 "A:\GAMES\SAVE.DAT" 为 Drive='A'，Parts=["GAMES","SAVE.DAT"]。
/// </summary>
internal readonly record struct DosPath(char Drive, string[] Parts)
{
    public bool IsRoot => Parts.Length == 0;

    /// <summary>最后一段名字；根目录为 null。</summary>
    public string? Leaf => Parts.Length > 0 ? Parts[^1] : null;

    /// <summary>上一级目录的名字列表。</summary>
    public string[] ParentParts => Parts.Length > 0 ? Parts[..^1] : [];

    public DosPath Parent => new(Drive, ParentParts);

    public DosPath Child(string name) => new(Drive, [.. Parts, name]);

    /// <summary>不带驱动器的目录形式，例如 "\GAMES"、"\"。</summary>
    public string DirectoryText => "\\" + string.Join("\\", Parts);

    public override string ToString() => $"{Drive}:{DirectoryText}";
}
