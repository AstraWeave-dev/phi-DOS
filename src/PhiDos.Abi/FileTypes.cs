namespace PhiDos.Abi;

/// <summary>打开文件时的访问方式（INT 21h 3Dh 的 AL）。</summary>
public enum FileAccessMode
{
    Read = 0,
    Write = 1,
    ReadWrite = 2,
}

/// <summary>
/// 目录项属性，数值与 FAT 磁盘上的属性字节完全一致。
/// </summary>
/// <remarks>
/// [Flags] 表示这些值可以按位组合，例如 Hidden | System。
/// </remarks>
[Flags]
public enum DosAttributes : byte
{
    None = 0x00,
    ReadOnly = 0x01,
    Hidden = 0x02,
    System = 0x04,
    VolumeLabel = 0x08,
    Directory = 0x10,
    Archive = 0x20,
}

/// <summary>FindFiles 返回的一个目录项。</summary>
/// <param name="Name">8.3 形式的名字，例如 "COMMAND.COM"、"GAMES"。</param>
/// <param name="Attributes">属性。</param>
/// <param name="Size">文件大小（字节），目录为 0。</param>
/// <param name="LastWriteTime">最后修改时间。</param>
public sealed record DirEntry(string Name, DosAttributes Attributes, long Size, DateTime LastWriteTime)
{
    public bool IsDirectory => (Attributes & DosAttributes.Directory) != 0;
}

/// <summary>GetDiskInfo 的结果。</summary>
/// <param name="TotalBytes">总容量。</param>
/// <param name="FreeBytes">剩余空间。</param>
/// <param name="BytesPerCluster">每簇字节数（文件夹驱动器为 1024）。</param>
/// <param name="VolumeLabel">卷标，没有时为 null。</param>
/// <param name="IsFolder">是否是映射的文件夹。</param>
public sealed record DiskInfo(long TotalBytes, long FreeBytes, int BytesPerCluster, string? VolumeLabel, bool IsFolder);
