using PhiDos.Core.Storage;

namespace PhiDos.Core.Machine;

// DosMachine 的挂载相关方法。
public sealed partial class DosMachine
{
    /// <summary>
    /// 按路径挂载：是文件就当虚拟磁盘（.phidisk，标准 FAT12 镜像），是文件夹就映射文件夹。
    /// </summary>
    public void Mount(char drive, string path, bool readOnly = false)
    {
        if (Directory.Exists(path)) MountFolder(drive, path, readOnly);
        else if (File.Exists(path)) MountImage(drive, path, readOnly);
        else throw new FileNotFoundException("Disk image or folder not found.", path);
    }

    /// <summary>挂载一个虚拟磁盘文件。修改会直接写回该文件。</summary>
    public void MountImage(char drive, string imagePath, bool readOnly = false)
    {
        var access = readOnly ? FileAccess.Read : FileAccess.ReadWrite;
        var stream = new FileStream(imagePath, FileMode.Open, access, FileShare.Read);
        try
        {
            MountDrive(drive, new Fat12Drive(stream, readOnly, ownsStream: true));
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 用一个流挂载虚拟磁盘。适合在游戏里从资源包读取镜像：
    /// 先把内容复制到 MemoryStream，再挂上去，改动只留在内存里。
    /// </summary>
    public void MountImage(char drive, Stream image, bool readOnly = false, bool ownsStream = false)
        => MountDrive(drive, new Fat12Drive(image, readOnly || !image.CanWrite, ownsStream));

    /// <summary>
    /// 把电脑上的一个文件夹映射为驱动器。<paramref name="capacityBytes"/> 是假装的磁盘容量，
    /// 默认和 360KB 软盘一样；传 <see cref="FolderDrive.Unlimited"/> 表示不限容量。
    /// </summary>
    public void MountFolder(char drive, string folderPath, bool readOnly = false, long capacityBytes = 362_496)
        => MountDrive(drive, new FolderDrive(folderPath, readOnly, capacityBytes));

}

// 游戏代码直接访问虚拟磁盘内容。
public sealed partial class DosMachine
{
    /// <summary>
    /// 在驱动器上执行一段操作，例如游戏剧情往玩家的盘里放一个文件。
    /// 执行期间虚拟机线程不会同时访问这个驱动器。路径用规范化的大写 8.3 名字，
    /// 可以先用 <see cref="Text.DosNames.Normalize"/> 转换。
    /// </summary>
    /// <remarks>
    /// 如果 DOS 里的程序正打开着同一个文件，它不会看到这里做的修改，最好在程序不运行时调用。
    /// </remarks>
    public T WithDrive<T>(char drive, Func<IDrive, T> action)
    {
        var slot = Slot(drive);
        lock (slot)
        {
            if (slot.Drive is null) throw new InvalidOperationException($"No disk in drive {drive}:.");
            return action(slot.Drive);
        }
    }

    public void WithDrive(char drive, Action<IDrive> action) => WithDrive<bool>(drive, d => { action(d); return true; });

    /// <summary>
    /// 把虚拟磁盘的当前内容完整导出（标准 FAT12 镜像），用于游戏存档。
    /// 可以在虚拟机运行时调用。驱动器挂的是文件夹时抛出 InvalidOperationException。
    /// </summary>
    public byte[] ExportImage(char drive) => WithDrive(drive, d => d is Fat12Drive f
        ? f.ToArray()
        : throw new InvalidOperationException($"Drive {drive}: is not a virtual disk."));
}
