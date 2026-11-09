using System.Reflection;
using PhiDos.Core.Storage;

namespace PhiDos.SystemDisk;

/// <summary>
/// 系统自带的外部命令，以及带着这些命令的"系统盘"。
/// </summary>
public static class SystemPrograms
{
    private static readonly Lazy<IReadOnlyDictionary<string, byte[]>> s_files = new(LoadFiles);

    /// <summary>文件名（如 "FORMAT.COM"）到文件内容（IL 程序集）的映射。</summary>
    public static IReadOnlyDictionary<string, byte[]> Files => s_files.Value;

    private static IReadOnlyDictionary<string, byte[]> LoadFiles()
    {
        var asm = typeof(SystemPrograms).Assembly;
        var result = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string resource in asm.GetManifestResourceNames())
        {
            if (!resource.StartsWith("programs/", StringComparison.Ordinal)) continue;
            // 资源名形如 programs/FORMAT.COM，文件名在 src/PhiDos.Programs/SystemPrograms.props 里定义
            string name = resource["programs/".Length..].ToUpperInvariant();
            using var stream = asm.GetManifestResourceStream(resource)!;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            result[name] = ms.ToArray();
        }
        return result;
    }

    /// <summary>把系统命令写进驱动器的根目录（已存在的同名文件会被覆盖）。</summary>
    public static void InstallTo(IDrive drive, DateTime now)
    {
        foreach (var (name, bytes) in Files)
        {
            var file = drive.CreateFile([name], now);
            file.Write(0, bytes);
            file.Close(now);
        }
        drive.Flush();
    }

    /// <summary>在内存里新建一张系统盘。</summary>
    public static MemoryStream CreateSystemDisk(DiskGeometry? geometry = null, string? volumeLabel = null, DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        var ms = Fat12Image.CreateInMemory(geometry, volumeLabel, t);
        var drive = new Fat12Drive(ms);
        InstallTo(drive, t);
        drive.Flush();
        ms.Position = 0;
        return ms;
    }

    /// <summary>新建一个系统盘文件（.phidisk）。</summary>
    public static void CreateSystemDiskFile(string path, DiskGeometry? geometry = null, string? volumeLabel = null, DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        Fat12Image.Create(path, geometry, volumeLabel, t);
        using var drive = new Fat12Drive(File.Open(path, FileMode.Open, FileAccess.ReadWrite), ownsStream: true);
        InstallTo(drive, t);
    }
}
