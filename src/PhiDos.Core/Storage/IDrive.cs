using PhiDos.Abi;

namespace PhiDos.Core.Storage;

/// <summary>
/// 驱动器里的一个目录项。<see cref="Name"/> 是规范化后的 8.3 名字（大写，形如 "FOO.TXT"）。
/// </summary>
public sealed record DriveEntry(string Name, DosAttributes Attributes, long Size, DateTime LastWriteTime)
{
    public bool IsDirectory => (Attributes & DosAttributes.Directory) != 0;
    public bool IsVolumeLabel => (Attributes & DosAttributes.VolumeLabel) != 0;
}

/// <summary>驱动器容量信息。</summary>
public sealed record DriveSpace(long TotalBytes, long FreeBytes, int BytesPerCluster);

/// <summary>
/// 驱动器上一个打开的文件。读写都用绝对位置，文件指针由内核维护。
/// </summary>
public interface IDriveFile
{
    long Length { get; }
    int Read(long position, Span<byte> buffer);
    void Write(long position, ReadOnlySpan<byte> data);
    void SetLength(long length);
    /// <summary>
    /// 把修改写回磁盘并更新目录项。<paramref name="modifiedTime"/> 不为 null 时
    /// 表示文件被写过，目录项的修改时间改成这个值。
    /// </summary>
    void Close(DateTime? modifiedTime);
}

/// <summary>
/// 存储设备接口。内核只通过它访问磁盘，不关心底下是 FAT12 镜像还是电脑上的文件夹。
/// </summary>
/// <remarks>
/// 路径参数一律是"已经拆好、已经规范化"的名字列表，从根目录开始，
/// 例如 \GAMES\SAVE.DAT 对应 ["GAMES", "SAVE.DAT"]，根目录对应空列表。
/// 出错时抛出 <see cref="DosException"/>。
/// </remarks>
public interface IDrive
{
    /// <summary>是否是映射的文件夹。</summary>
    bool IsFolder { get; }

    /// <summary>是否写保护。写保护的驱动器上任何写操作都会触发 "Write protect error"。</summary>
    bool IsReadOnly { get; }

    /// <summary>卷标，没有时为 null。</summary>
    string? VolumeLabel { get; }

    DriveSpace GetSpace();

    /// <summary>列出目录内容（不含 . 和 ..，不含卷标）。</summary>
    IReadOnlyList<DriveEntry> List(IReadOnlyList<string> directory);

    /// <summary>查找一个条目，不存在返回 null。空路径表示根目录本身。</summary>
    DriveEntry? GetEntry(IReadOnlyList<string> path);

    IDriveFile OpenFile(IReadOnlyList<string> path, bool writable);

    /// <summary>创建文件；已存在则清空。</summary>
    IDriveFile CreateFile(IReadOnlyList<string> path, DateTime now);

    void Delete(IReadOnlyList<string> path);

    /// <summary>改名或在同一驱动器内移动。目标已存在时抛出 FileExists。</summary>
    void Rename(IReadOnlyList<string> from, IReadOnlyList<string> to);

    void MakeDirectory(IReadOnlyList<string> path, DateTime now);

    void RemoveDirectory(IReadOnlyList<string> path);

    /// <summary>格式化：清空全部内容并设置卷标。文件夹驱动器抛出 NotSupported。</summary>
    void Format(string? volumeLabel, DateTime now);

    /// <summary>把缓存写回存储。</summary>
    void Flush();
}
