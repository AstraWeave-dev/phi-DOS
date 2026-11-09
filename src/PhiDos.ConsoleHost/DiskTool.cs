using PhiDos.Abi;
using PhiDos.Core.Storage;
using PhiDos.Core.Text;

namespace PhiDos.ConsoleHost;

/// <summary>
/// 镜像工具 phidisk：把文件夹打包成 .phidisk、把 .phidisk 解包成文件夹、列出镜像内容。
/// 发布包里的 phidisk.bat（Linux 上是 phidisk）就是调用 phidos --phidisk。
/// </summary>
internal static class DiskTool
{
    private const string Usage = """
        phidisk - make and unpack phi-DOS disk images (.phidisk, standard FAT12)

        Usage:
          phidisk <folder> <image>     Pack the folder into a new disk image
          phidisk <image> <folder>     Unpack the disk image into the folder
          phidisk <image>              List the contents of the disk image

        Options:
          --size <size>    Disk size when packing: 160K, 180K, 320K, 360K, 720K, 1.2M, 1.44M.
                           Default: the smallest size that fits.
          --label <name>   Volume label when packing (up to 11 characters)
          --force          Overwrite an existing image, or existing files when unpacking

        Names that are not valid 8.3 names (for example "LongFileName.txt") cannot be
        stored on a DOS disk. They are skipped and listed as warnings.
        Subfolders are packed too. File dates are kept in both directions.
        """;

    public static int Run(string[] args)
    {
        var positional = new List<string>();
        string? size = null, label = null;
        bool force = false;
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value.");
                switch (a)
                {
                    case "-h" or "--help" or "/?": Console.WriteLine(Usage); return 0;
                    case "--size": size = Next(); break;
                    case "--label": label = Next(); break;
                    case "--force" or "-f": force = true; break;
                    default:
                        if (a.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unknown option {a}");
                        positional.Add(a);
                        break;
                }
            }

            DiskGeometry? geometry = null;
            if (size is not null)
                geometry = DiskGeometry.Find(size) ?? throw new ArgumentException(
                    $"Unknown disk size {size}. Use one of: {string.Join(", ", DiskGeometry.All.Select(g => g.Name))}");

            switch (positional.Count)
            {
                case 1 when File.Exists(positional[0]):
                    return List(positional[0]);
                case 2 when Directory.Exists(positional[0]):
                    return Pack(positional[0], positional[1], geometry, label, force);
                case 2 when File.Exists(positional[0]):
                    return Unpack(positional[0], positional[1], force);
                case 1 or 2:
                    throw new ArgumentException($"{positional[0]} is neither a folder nor a disk image.");
                default:
                    Console.WriteLine(Usage);
                    return 2;
            }
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine("phidisk: " + ex.Message);
            return 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine("phidisk: " + ex.Message);
            return 1;
        }
    }

    // ───────── 打包 ─────────

    /// <summary>文件夹里要放进镜像的一项。Parts 是盘上的路径，例如 ["GAMES", "SAVE.DAT"]。</summary>
    private sealed record Item(string[] Parts, FileSystemInfo Info);

    private static int Pack(string folder, string image, DiskGeometry? geometry, string? label, bool force)
    {
        if (File.Exists(image) && !force)
            throw new ArgumentException($"{image} already exists. Use --force to overwrite it.");
        if (label is not null && DosNames.Truncate(label.Trim(), 11) != label.Trim())
            Console.Error.WriteLine($"Warning: the volume label is cut to 11 bytes: {DosNames.Truncate(label.Trim(), 11)}");

        var warnings = new List<string>();
        var items = new List<Item>();
        Collect(new DirectoryInfo(folder), [], "", items, warnings);
        long totalBytes = items.Select(i => i.Info).OfType<FileInfo>().Sum(f => f.Length);
        int fileCount = items.Count(i => i.Info is FileInfo);
        int dirCount = items.Count - fileCount;

        var candidates = geometry is not null ? [geometry] : DiskGeometry.All.OrderBy(g => g.ImageBytes).ToList();
        string? lastProblem = null;
        foreach (var g in candidates)
        {
            var result = TryBuild(g, items, label);
            if (result.Image is null)
            {
                lastProblem = result.Problem;
                continue;
            }

            File.WriteAllBytes(image, result.Image);
            foreach (string w in warnings) Console.Error.WriteLine("Skipped: " + w);
            using var check = new Fat12Drive(new MemoryStream(result.Image), readOnly: true);
            Console.WriteLine($"Packed {fileCount} file(s) and {dirCount} folder(s), {totalBytes} bytes, into {image}");
            Console.WriteLine($"Disk size {g.Name}, {check.GetSpace().FreeBytes} bytes free" +
                              (label is not null ? $", label {check.VolumeLabel}" : ""));
            return warnings.Count > 0 ? 3 : 0;
        }

        foreach (string w in warnings) Console.Error.WriteLine("Skipped: " + w);
        string largest = (geometry ?? DiskGeometry.All.MaxBy(x => x.ImageBytes)!).Name;
        Console.Error.WriteLine($"phidisk: the folder does not fit on a {largest} disk: {lastProblem}");
        Console.Error.WriteLine($"         ({fileCount} file(s), {dirCount} folder(s), {totalBytes} bytes)");
        return 1;
    }

    /// <summary>
    /// 递归收集文件夹内容。不符合 8.3 的名字、符号链接、只是大小写不同的重名都跳过并记下原因。
    /// 目录排在它里面的东西前面，这样建立镜像时上一级目录总是已经存在。
    /// </summary>
    private static void Collect(DirectoryInfo dir, string[] parts, string relative, List<Item> items, List<string> warnings)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var info in dir.EnumerateFileSystemInfos().OrderBy(i => i.Name, StringComparer.Ordinal))
        {
            string shown = relative.Length == 0 ? info.Name : relative + Path.DirectorySeparatorChar + info.Name;
            if (info.LinkTarget is not null)
            {
                warnings.Add($"{shown} (symbolic link)");
                continue;
            }
            if (!DosNames.IsValidShortName(info.Name))
            {
                warnings.Add($"{shown} (not a valid 8.3 name)");
                continue;
            }
            string name = info.Name.ToUpperInvariant();
            if (!seen.Add(name))
            {
                warnings.Add($"{shown} (same name as another file, apart from letter case)");
                continue;
            }
            string[] path = [.. parts, name];
            items.Add(new Item(path, info));
            if (info is DirectoryInfo sub) Collect(sub, path, shown, items, warnings);
        }
    }

    private static (byte[]? Image, string? Problem) TryBuild(DiskGeometry g, List<Item> items, string? label)
    {
        var stream = Fat12Image.CreateInMemory(g, string.IsNullOrWhiteSpace(label) ? null : label.Trim(), DateTime.Now);
        var drive = new Fat12Drive(stream);
        try
        {
            foreach (var item in items)
            {
                if (item.Info is DirectoryInfo d)
                {
                    drive.MakeDirectory(item.Parts, d.LastWriteTime);
                    continue;
                }
                var f = (FileInfo)item.Info;
                var file = drive.CreateFile(item.Parts, f.LastWriteTime);
                using (var source = f.OpenRead())
                {
                    var buffer = new byte[64 * 1024];
                    long position = 0;
                    int n;
                    while ((n = source.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        file.Write(position, buffer.AsSpan(0, n));
                        position += n;
                    }
                }
                file.Close(f.LastWriteTime);
            }
            return (drive.ToArray(), null);
        }
        catch (DosException e) when (e.Error == DosError.DiskFull)
        {
            return (null, "not enough space");
        }
        catch (DosException e) when (e.Error == DosError.AccessDenied)
        {
            return (null, $"too many entries in the root folder (at most {g.RootEntries})");
        }
    }

    // ───────── 解包 ─────────

    private static int Unpack(string image, string folder, bool force)
    {
        using var drive = OpenImage(image);
        var entries = new List<(string[] Parts, DriveEntry Entry)>();
        Walk(drive, [], entries);

        if (!force)
        {
            var conflicts = entries.Where(e => !e.Entry.IsDirectory && File.Exists(HostPath(folder, e.Parts))).ToList();
            if (conflicts.Count > 0)
            {
                foreach (var c in conflicts.Take(10)) Console.Error.WriteLine("Exists: " + HostPath(folder, c.Parts));
                if (conflicts.Count > 10) Console.Error.WriteLine($"... and {conflicts.Count - 10} more");
                throw new ArgumentException("Some files already exist. Use --force to overwrite them.");
            }
        }

        Directory.CreateDirectory(folder);
        long bytes = 0;
        foreach (var (parts, entry) in entries)
        {
            string target = HostPath(folder, parts);
            if (entry.IsDirectory)
            {
                Directory.CreateDirectory(target);
                continue;
            }
            var file = drive.OpenFile(parts, writable: false);
            var data = new byte[file.Length];
            file.Read(0, data);
            file.Close(null);
            File.WriteAllBytes(target, data);
            if (entry.LastWriteTime != DateTime.MinValue) File.SetLastWriteTime(target, entry.LastWriteTime);
            bytes += data.Length;
        }
        // 目录的时间最后再设，否则往里面写文件时又会被改掉
        foreach (var (parts, entry) in entries.Where(e => e.Entry.IsDirectory && e.Entry.LastWriteTime != DateTime.MinValue))
            Directory.SetLastWriteTime(HostPath(folder, parts), entry.LastWriteTime);

        int files = entries.Count(e => !e.Entry.IsDirectory);
        Console.WriteLine($"Unpacked {files} file(s) and {entries.Count - files} folder(s), {bytes} bytes, into {folder}");
        return 0;
    }

    private static void Walk(IDrive drive, string[] parts, List<(string[], DriveEntry)> result)
    {
        foreach (var e in drive.List(parts))
        {
            string[] path = [.. parts, e.Name];
            result.Add((path, e));
            if (e.IsDirectory) Walk(drive, path, result);
        }
    }

    private static string HostPath(string folder, string[] parts) => Path.Combine([folder, .. parts]);

    // ───────── 列出内容 ─────────

    private static int List(string image)
    {
        using var drive = OpenImage(image);
        var space = drive.GetSpace();
        Console.WriteLine($"{image}: {drive.Geometry.Name} disk, label {drive.VolumeLabel ?? "(none)"}");
        Console.WriteLine();
        var entries = new List<(string[] Parts, DriveEntry Entry)>();
        Walk(drive, [], entries);
        foreach (var (parts, e) in entries)
        {
            string name = string.Join("\\", parts) + (e.IsDirectory ? "\\" : "");
            string size = e.IsDirectory ? "<DIR>" : e.Size.ToString();
            string date = e.LastWriteTime == DateTime.MinValue ? "" : e.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            Console.WriteLine($"  {DisplayWidth.PadRight(name, 40)} {size,9}  {date}");
        }
        Console.WriteLine();
        Console.WriteLine($"  {entries.Count(x => !x.Entry.IsDirectory)} file(s), {space.FreeBytes} bytes free of {space.TotalBytes}");
        return 0;
    }

    private static Fat12Drive OpenImage(string image)
    {
        var stream = new FileStream(image, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return new Fat12Drive(stream, readOnly: true, ownsStream: true);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
