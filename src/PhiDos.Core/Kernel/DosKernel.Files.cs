using PhiDos.Abi;
using PhiDos.Core.Storage;
using PhiDos.Core.Text;

namespace PhiDos.Core.Kernel;

internal sealed partial class DosKernel
{
    public OpenFile OpenDevice(string device) => device switch
    {
        "CON" => new ConsoleFile(this),
        _ => new NulFile(),
    };

    public int Create(DosProcess process, string path)
    {
        EnterCall();
        var dp = Resolve(path);
        if (dp.Leaf is null) throw new DosException(DosError.AccessDenied);
        if (DosNames.IsDeviceName(dp.Leaf, out string device))
            return process.AllocHandle(OpenDevice(device));

        var file = DriveOp(dp.Drive, write: true, d =>
        {
            var existing = d.GetEntry(dp.Parts);
            if (existing is not null &&
                (existing.IsDirectory || existing.IsVolumeLabel || (existing.Attributes & DosAttributes.ReadOnly) != 0))
                throw new DosException(DosError.AccessDenied);
            return d.CreateFile(dp.Parts, Now);
        });
        return process.AllocHandle(new DiskFile(file, this, dp.Drive) { Mode = FileAccessMode.ReadWrite });
    }

    public int Open(DosProcess process, string path, FileAccessMode mode)
    {
        EnterCall();
        if (mode is < FileAccessMode.Read or > FileAccessMode.ReadWrite)
            throw new DosException(DosError.InvalidAccess);
        var dp = Resolve(path);
        if (dp.Leaf is null) throw new DosException(DosError.AccessDenied);
        if (DosNames.IsDeviceName(dp.Leaf, out string device))
            return process.AllocHandle(OpenDevice(device));

        bool writable = mode != FileAccessMode.Read;
        var file = DriveOp(dp.Drive, write: false, d =>
        {
            var entry = d.GetEntry(dp.Parts) ?? throw NotFound(d, dp);
            if (entry.IsDirectory || entry.IsVolumeLabel) throw new DosException(DosError.AccessDenied);
            if (writable && (entry.Attributes & DosAttributes.ReadOnly) != 0) throw new DosException(DosError.AccessDenied);
            return d.OpenFile(dp.Parts, writable);
        });
        return process.AllocHandle(new DiskFile(file, this, dp.Drive) { Mode = mode });
    }

    public int Read(DosProcess process, int handle, Span<byte> buffer)
    {
        EnterCall();
        var file = process.GetHandle(handle);
        if (!file.CanRead) throw new DosException(DosError.AccessDenied);
        return file.Read(buffer);
    }

    public int Write(DosProcess process, int handle, ReadOnlySpan<byte> data)
    {
        EnterCall();
        var file = process.GetHandle(handle);
        if (!file.CanWrite) throw new DosException(DosError.AccessDenied);
        return file.Write(data);
    }

    public long Seek(DosProcess process, int handle, long offset, SeekOrigin origin)
    {
        EnterCall();
        return process.GetHandle(handle).Seek(offset, origin);
    }

    public void Close(DosProcess process, int handle)
    {
        EnterCall();
        process.CloseHandle(handle);
    }

    public void Delete(string path)
    {
        EnterCall();
        var dp = Resolve(path);
        if (dp.Leaf is null || DosNames.IsDeviceName(dp.Leaf, out _)) throw new DosException(DosError.AccessDenied);
        DriveOp(dp.Drive, write: true, d =>
        {
            var entry = d.GetEntry(dp.Parts) ?? throw NotFound(d, dp);
            if (entry.IsDirectory || entry.IsVolumeLabel || (entry.Attributes & DosAttributes.ReadOnly) != 0)
                throw new DosException(DosError.AccessDenied);
            d.Delete(dp.Parts);
        });
    }

    public void Rename(string from, string to)
    {
        EnterCall();
        var src = Resolve(from);
        var dst = Resolve(to);
        if (src.Drive != dst.Drive) throw new DosException(DosError.NotSameDevice);
        if (src.Leaf is null || dst.Leaf is null) throw new DosException(DosError.AccessDenied);
        if (DosNames.IsDeviceName(dst.Leaf, out _)) throw new DosException(DosError.AccessDenied);
        DriveOp(src.Drive, write: true, d =>
        {
            var entry = d.GetEntry(src.Parts) ?? throw NotFound(d, src);
            // DOS 2.0 的改名不能用在目录上
            if (entry.IsDirectory || entry.IsVolumeLabel) throw new DosException(DosError.AccessDenied);
            if (d.GetEntry(dst.ParentParts) is not { IsDirectory: true } && dst.ParentParts.Length > 0)
                throw new DosException(DosError.PathNotFound);
            if (d.GetEntry(dst.Parts) is not null) throw new DosException(DosError.AccessDenied);
            d.Rename(src.Parts, dst.Parts);
        });
    }

    /// <summary>
    /// 查找条目。普通文件总是包含；include 里的位决定是否额外包含目录、隐藏、系统文件。
    /// 只给 VolumeLabel 时只返回卷标。子目录里查找时，会像 DOS 一样带上 . 和 .. 两项。
    /// </summary>
    public IReadOnlyList<DirEntry> FindFiles(string pattern, DosAttributes include)
    {
        EnterCall();
        if (string.IsNullOrWhiteSpace(pattern)) pattern = "*.*";
        var dp = Resolve(pattern, allowWildcards: true);
        string leaf = dp.Leaf ?? "*.*";
        string[] dirParts = dp.Leaf is null ? dp.Parts : dp.ParentParts;

        return DriveOp(dp.Drive, write: false, d =>
        {
            var result = new List<DirEntry>();
            if (include == DosAttributes.VolumeLabel)
            {
                if (d.VolumeLabel is string label && dirParts.Length == 0)
                    result.Add(new DirEntry(label, DosAttributes.VolumeLabel, 0, DateTime.MinValue));
                return result;
            }

            if (dirParts.Length > 0 && d.GetEntry(dirParts) is not { IsDirectory: true })
                throw new DosException(DosError.PathNotFound);

            bool wantDirs = (include & DosAttributes.Directory) != 0;
            if (wantDirs && dirParts.Length > 0 && MatchesDotEntries(leaf))
            {
                var self = d.GetEntry(dirParts)!;
                result.Add(new DirEntry(".", DosAttributes.Directory, 0, self.LastWriteTime));
                result.Add(new DirEntry("..", DosAttributes.Directory, 0, self.LastWriteTime));
            }

            foreach (var e in d.List(dirParts))
            {
                if (e.IsVolumeLabel) continue;
                if (e.IsDirectory && !wantDirs) continue;
                if ((e.Attributes & DosAttributes.Hidden) != 0 && (include & DosAttributes.Hidden) == 0) continue;
                if ((e.Attributes & DosAttributes.System) != 0 && (include & DosAttributes.System) == 0) continue;
                if (!DosNames.Matches(leaf, e.Name)) continue;
                result.Add(new DirEntry(e.Name, e.Attributes, e.IsDirectory ? 0 : e.Size, e.LastWriteTime));
            }
            return result;
        });
    }

    /// <summary>"*.*"、"*"、"????????.???" 这类模式会匹配 . 和 ..。</summary>
    private static bool MatchesDotEntries(string pattern)
    {
        var (name, ext) = DosNames.Split(pattern);
        bool NameAny(string s) => s.Length > 0 && (s[0] == '*' || s.All(c => c == '?'));
        return NameAny(name) && (ext.Length == 0 || NameAny(ext));
    }

    public void ChangeDirectory(string path)
    {
        EnterCall();
        var dp = Resolve(path);
        if (DirectoryLength(dp) > MaxCurrentDirectoryLength - 1) throw new DosException(DosError.PathNotFound);
        DriveOp(dp.Drive, write: false, d =>
        {
            if (!dp.IsRoot && d.GetEntry(dp.Parts) is not { IsDirectory: true })
                throw new DosException(DosError.PathNotFound);
        });
        var state = StateOf(dp.Drive);
        state.CurrentDirectory.Clear();
        state.CurrentDirectory.AddRange(dp.Parts);
    }

    public void MakeDirectory(string path)
    {
        EnterCall();
        var dp = Resolve(path);
        if (dp.Leaf is null || DosNames.IsDeviceName(dp.Leaf, out _)) throw new DosException(DosError.AccessDenied);
        if (DirectoryLength(dp) > MaxCurrentDirectoryLength - 1) throw new DosException(DosError.AccessDenied);
        DriveOp(dp.Drive, write: true, d =>
        {
            if (dp.ParentParts.Length > 0 && d.GetEntry(dp.ParentParts) is not { IsDirectory: true })
                throw new DosException(DosError.PathNotFound);
            if (d.GetEntry(dp.Parts) is not null) throw new DosException(DosError.AccessDenied);
            d.MakeDirectory(dp.Parts, Now);
        });
    }

    public void RemoveDirectory(string path)
    {
        EnterCall();
        var dp = Resolve(path);
        if (dp.Leaf is null) throw new DosException(DosError.AccessDenied);
        var current = StateOf(dp.Drive).CurrentDirectory;
        bool isCurrentOrAbove = dp.Parts.Length <= current.Count &&
                                dp.Parts.SequenceEqual(current.Take(dp.Parts.Length));
        if (isCurrentOrAbove) throw new DosException(DosError.CurrentDirectory);
        DriveOp(dp.Drive, write: true, d =>
        {
            if (d.GetEntry(dp.Parts) is not { IsDirectory: true }) throw new DosException(DosError.PathNotFound);
            if (d.List(dp.Parts).Count > 0) throw new DosException(DosError.AccessDenied);
            d.RemoveDirectory(dp.Parts);
        });
    }

    public DiskInfo GetDiskInfo(char drive)
    {
        EnterCall();
        drive = char.ToUpperInvariant(drive);
        if (!IsValidDrive(drive)) throw new DosException(DosError.InvalidDrive);
        return DriveOp(drive, write: false, d =>
        {
            var space = d.GetSpace();
            return new DiskInfo(space.TotalBytes, space.FreeBytes, space.BytesPerCluster, d.VolumeLabel, d.IsFolder);
        });
    }

    public void FormatDisk(char drive, string? volumeLabel)
    {
        EnterCall();
        drive = char.ToUpperInvariant(drive);
        if (!IsValidDrive(drive)) throw new DosException(DosError.InvalidDrive);
        if (volumeLabel is not null)
        {
            volumeLabel = DosNames.Truncate(volumeLabel.Trim().ToUpperInvariant(), 11);
            if (volumeLabel.Length == 0) volumeLabel = null;
        }
        DriveOp(drive, write: true, d =>
        {
            if (d.IsFolder) throw new DosException(DosError.NotSupported);
            d.Format(volumeLabel, Now);
        });
        StateOf(drive).CurrentDirectory.Clear();
    }

    /// <summary>"\A\B" 这种形式的长度（UTF-8 字节）。</summary>
    private static int DirectoryLength(DosPath dp)
        => System.Text.Encoding.UTF8.GetByteCount(dp.DirectoryText);

    /// <summary>区分"文件不存在"和"路径不存在"。</summary>
    private static DosException NotFound(IDrive d, DosPath dp)
    {
        if (dp.ParentParts.Length > 0 && d.GetEntry(dp.ParentParts) is not { IsDirectory: true })
            return new DosException(DosError.PathNotFound);
        return new DosException(DosError.FileNotFound);
    }
}
