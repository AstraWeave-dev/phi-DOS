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

    /// <summary>把电脑上的一个文件夹映射为驱动器。</summary>
    public void MountFolder(char drive, string folderPath, bool readOnly = false, long capacityBytes = 362_496)
        => MountDrive(drive, new FolderDrive(folderPath, readOnly, capacityBytes));

}
