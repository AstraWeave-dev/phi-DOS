using PhiDos.Abi;
using PhiDos.Core.Text;

namespace PhiDos.Core.Storage;

/// <summary>
/// 把电脑上的一个文件夹映射成驱动器。
/// </summary>
/// <remarks>
/// 规则：
/// 1. 不符合 8.3 的文件和目录一律隐藏、不可访问。
/// 2. 查找不区分大小写；DOS 新建的文件和目录用大写名字写入。
/// 3. 容量是设定值，已用空间按文件实际大小累计（按 1KB 簇向上取整）。
/// 4. 永远不会访问到文件夹以外：符号链接一律忽略，每个路径都检查是否仍在根目录之下。
/// </remarks>
public sealed class FolderDrive : IDrive
{
    private const int ClusterBytes = 1024;
    private readonly string _root;
    private readonly long _capacity;

    public FolderDrive(string folder, bool readOnly = false, long capacityBytes = 362_496, string? volumeLabel = null)
    {
        _root = Path.GetFullPath(folder);
        if (!Directory.Exists(_root)) throw new DirectoryNotFoundException(_root);
        IsReadOnly = readOnly;
        _capacity = capacityBytes;
        VolumeLabel = volumeLabel;
    }

    public bool IsFolder => true;
    public bool IsReadOnly { get; }
    public string? VolumeLabel { get; }
    public string RootPath => _root;

    // ───────── 名字映射 ─────────

    /// <summary>列出一个电脑目录里所有"DOS 看得见"的条目：名字 → 电脑上的路径。</summary>
    private List<(string Name, FileSystemInfo Info)> Visible(string hostDir)
    {
        var result = new List<(string, FileSystemInfo)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<FileSystemInfo> infos;
        try
        {
            infos = new DirectoryInfo(hostDir).EnumerateFileSystemInfos()
                .OrderBy(i => i.Name, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DosException(DosError.AccessDenied);
        }

        foreach (var info in infos)
        {
            if (info.LinkTarget is not null) continue; // 符号链接可能指向文件夹外面，不碰
            if (!DosNames.IsValidShortName(info.Name)) continue;
            string name = info.Name.ToUpperInvariant();
            // Linux 上 a.txt 和 A.TXT 可以同时存在，只认第一个
            if (seen.Add(name)) result.Add((name, info));
        }
        return result;
    }

    /// <summary>把 DOS 路径翻译成电脑上的路径。任何一段找不到就返回 null。</summary>
    private FileSystemInfo? Find(IReadOnlyList<string> path)
    {
        FileSystemInfo current = new DirectoryInfo(_root);
        foreach (string part in path)
        {
            if (current is not DirectoryInfo dir) return null;
            var match = Visible(dir.FullName).FirstOrDefault(v => v.Name == part.ToUpperInvariant());
            if (match.Info is null) return null;
            current = match.Info;
        }
        EnsureInside(current.FullName);
        return current;
    }

    private string DirPath(IReadOnlyList<string> path)
    {
        if (path.Count == 0) return _root;
        return Find(path) is DirectoryInfo d ? d.FullName : throw new DosException(DosError.PathNotFound);
    }

    /// <summary>新建条目时用：上一级目录必须存在，新名字用大写。</summary>
    private string NewPath(IReadOnlyList<string> path)
    {
        string parent = DirPath(path.Take(path.Count - 1).ToArray());
        string full = Path.Combine(parent, path[^1].ToUpperInvariant());
        EnsureInside(full);
        return full;
    }

    private void EnsureInside(string full)
    {
        string f = Path.GetFullPath(full);
        if (f != _root && !f.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new DosException(DosError.AccessDenied);
    }

    private static DriveEntry ToEntry(string name, FileSystemInfo info)
    {
        var attrs = DosAttributes.None;
        if (info is DirectoryInfo) attrs |= DosAttributes.Directory;
        else attrs |= DosAttributes.Archive;
        if ((info.Attributes & FileAttributes.ReadOnly) != 0 && info is FileInfo) attrs |= DosAttributes.ReadOnly;
        if ((info.Attributes & FileAttributes.Hidden) != 0) attrs |= DosAttributes.Hidden;
        if ((info.Attributes & FileAttributes.System) != 0) attrs |= DosAttributes.System;
        long size = info is FileInfo f ? f.Length : 0;
        return new DriveEntry(name, attrs, size, info.LastWriteTime);
    }

    private void RequireWritable()
    {
        if (IsReadOnly) throw new DosException(DosError.WriteProtected);
    }

    /// <summary>把电脑上的 IO 异常翻译成 DOS 错误。</summary>
    private static T Host<T>(Func<T> action)
    {
        try { return action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DosException(ex is FileNotFoundException ? DosError.FileNotFound
                : ex is DirectoryNotFoundException ? DosError.PathNotFound
                : DosError.AccessDenied);
        }
    }

    private static void Host(Action action) => Host(() => { action(); return 0; });

    // ───────── 容量 ─────────

    private long UsedBytes()
    {
        long used = 0;
        void Walk(string dir, int depth)
        {
            if (depth > 32) return;
            foreach (var (_, info) in Visible(dir))
            {
                if (info is FileInfo f) used += (f.Length + ClusterBytes - 1) / ClusterBytes * ClusterBytes;
                else if (info is DirectoryInfo d) { used += ClusterBytes; Walk(d.FullName, depth + 1); }
            }
        }
        Walk(_root, 0);
        return used;
    }

    private void RequireSpace(long extraBytes)
    {
        if (extraBytes <= 0) return;
        if (UsedBytes() + extraBytes > _capacity) throw new DosException(DosError.DiskFull);
    }

    private static long Rounded(long n) => (n + ClusterBytes - 1) / ClusterBytes * ClusterBytes;

    // ───────── IDrive ─────────

    public DriveSpace GetSpace() => new(_capacity, Math.Max(0, _capacity - UsedBytes()), ClusterBytes);

    public IReadOnlyList<DriveEntry> List(IReadOnlyList<string> directory)
        => Visible(DirPath(directory)).Select(v => ToEntry(v.Name, v.Info)).ToList();

    public DriveEntry? GetEntry(IReadOnlyList<string> path)
    {
        if (path.Count == 0) return new DriveEntry("\\", DosAttributes.Directory, 0, DateTime.MinValue);
        var info = Find(path);
        return info is null ? null : ToEntry(path[^1].ToUpperInvariant(), info);
    }

    public IDriveFile OpenFile(IReadOnlyList<string> path, bool writable)
    {
        if (writable) RequireWritable();
        if (Find(path) is not FileInfo f) throw new DosException(DosError.FileNotFound);
        var stream = Host(() => new FileStream(f.FullName, FileMode.Open,
            writable ? FileAccess.ReadWrite : FileAccess.Read, FileShare.ReadWrite));
        return new FolderFile(this, f.FullName, stream);
    }

    public IDriveFile CreateFile(IReadOnlyList<string> path, DateTime now)
    {
        RequireWritable();
        string full = Find(path) is FileInfo existing ? existing.FullName : NewPath(path);
        if (Directory.Exists(full)) throw new DosException(DosError.AccessDenied);
        if (!File.Exists(full)) RequireSpace(0);
        var stream = Host(() => new FileStream(full, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite));
        return new FolderFile(this, full, stream);
    }

    public void Delete(IReadOnlyList<string> path)
    {
        RequireWritable();
        if (Find(path) is not FileInfo f) throw new DosException(DosError.FileNotFound);
        Host(() => f.Delete());
    }

    public void Rename(IReadOnlyList<string> from, IReadOnlyList<string> to)
    {
        RequireWritable();
        var src = Find(from) ?? throw new DosException(DosError.FileNotFound);
        if (Find(to) is not null) throw new DosException(DosError.FileExists);
        string dest = NewPath(to);
        Host(() =>
        {
            if (src is DirectoryInfo d) d.MoveTo(dest);
            else ((FileInfo)src).MoveTo(dest);
        });
    }

    public void MakeDirectory(IReadOnlyList<string> path, DateTime now)
    {
        RequireWritable();
        if (Find(path) is not null) throw new DosException(DosError.AccessDenied);
        string full = NewPath(path);
        RequireSpace(ClusterBytes);
        Host(() => Directory.CreateDirectory(full));
    }

    public void RemoveDirectory(IReadOnlyList<string> path)
    {
        RequireWritable();
        if (Find(path) is not DirectoryInfo d) throw new DosException(DosError.PathNotFound);
        // 文件夹里如果有 DOS 看不见的文件（长文件名等），也不允许删除，免得误删电脑上的东西
        if (Host(() => d.EnumerateFileSystemInfos().Any())) throw new DosException(DosError.DirectoryNotEmpty);
        Host(() => d.Delete());
    }

    public void Format(string? volumeLabel, DateTime now) => throw new DosException(DosError.NotSupported);

    public void Flush() { }

    private sealed class FolderFile(FolderDrive drive, string path, FileStream stream) : IDriveFile
    {
        public long Length => stream.Length;

        public int Read(long position, Span<byte> buffer)
        {
            stream.Position = position;
            int total = 0;
            while (total < buffer.Length)
            {
                int n = ReadSome(buffer[total..]);
                if (n == 0) break;
                total += n;
            }
            return total;
        }

        private int ReadSome(Span<byte> buffer)
        {
            try { return stream.Read(buffer); }
            catch (IOException) { throw new DosException(DosError.AccessDenied); }
        }

        public void Write(long position, ReadOnlySpan<byte> data)
        {
            drive.RequireWritable();
            long end = position + data.Length;
            if (end > stream.Length) drive.RequireSpace(Rounded(end) - Rounded(stream.Length));
            stream.Position = position;
            try { stream.Write(data); }
            catch (IOException) { throw new DosException(DosError.DiskFull); }
        }

        public void SetLength(long length)
        {
            drive.RequireWritable();
            if (length > stream.Length) drive.RequireSpace(Rounded(length) - Rounded(stream.Length));
            Host(() => stream.SetLength(length));
        }

        public void Close(DateTime? modifiedTime)
        {
            Host(() => stream.Dispose());
            if (modifiedTime is DateTime t)
                Host(() => File.SetLastWriteTime(path, t));
        }
    }
}
