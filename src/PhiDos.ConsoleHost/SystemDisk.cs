using PhiDos.Core.Storage;

namespace PhiDos.ConsoleHost;

/// <summary>新建磁盘。M5 之后会把系统自带的外部命令（FORMAT 等）一起放进去。</summary>
internal static class SystemDisk
{
    public static MemoryStream CreateInMemory()
    {
        var ms = Fat12Image.CreateInMemory();
        return ms;
    }

    public static void CreateFile(string path, DiskGeometry geometry, string? label, bool withPrograms)
    {
        Fat12Image.Create(path, geometry, label);
    }
}
