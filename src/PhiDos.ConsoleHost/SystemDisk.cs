using PhiDos.Core.Storage;
using PhiDos.SystemDisk;

namespace PhiDos.ConsoleHost;

/// <summary>新建磁盘：默认是带系统命令的系统盘，--blank 时是空白盘。</summary>
internal static class SystemDisk
{
    public static MemoryStream CreateInMemory() => SystemPrograms.CreateSystemDisk();

    public static void CreateFile(string path, DiskGeometry geometry, string? label, bool withPrograms)
    {
        if (withPrograms) SystemPrograms.CreateSystemDiskFile(path, geometry, label);
        else Fat12Image.Create(path, geometry, label);
    }
}
