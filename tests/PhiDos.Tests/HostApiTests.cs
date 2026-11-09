using System.Text;
using PhiDos.Core.Storage;
using PhiDos.SystemDisk;

namespace PhiDos.Tests;

/// <summary>给游戏宿主用的接口：存档导出、游戏代码直接读写虚拟磁盘。</summary>
public class HostApiTests
{
    [Fact]
    public void ExportImage_CanBeMountedAgain()
    {
        byte[] saved;
        using (var m = new TestMachine(mm => mm.MountImage('A', SystemPrograms.CreateSystemDisk())))
        {
            m.Run("MD SAVES");
            m.Run("ECHO progress=42 > SAVES\\GAME.SAV");
            saved = m.Machine.ExportImage('A');
        }
        Assert.Equal(368_640, saved.Length);

        using var again = new TestMachine(mm => mm.MountImage('A', new MemoryStream(saved)));
        Assert.Equal("progress=42 \n\nA>", again.Run("TYPE SAVES\\GAME.SAV"));
    }

    [Fact]
    public void WithDrive_LetsGameCodePlaceFiles()
    {
        using var m = new TestMachine(mm => mm.MountImage('A', SystemPrograms.CreateSystemDisk()));
        m.Machine.WithDrive('A', d =>
        {
            var f = d.CreateFile(["LETTER.TXT"], TestMachine.FixedTime);
            f.Write(0, Encoding.UTF8.GetBytes("给你的一封信\r\n"));
            f.Close(TestMachine.FixedTime);
        });
        Assert.Equal("给你的一封信\n\nA>", m.Run("TYPE LETTER.TXT"));

        m.Run("ECHO reply> REPLY.TXT");
        string reply = m.Machine.WithDrive('A', d =>
        {
            var f = d.OpenFile(["REPLY.TXT"], writable: false);
            var buf = new byte[f.Length];
            f.Read(0, buf);
            f.Close(null);
            return Encoding.UTF8.GetString(buf);
        });
        Assert.Equal("reply\r\n", reply);
    }

    [Fact]
    public void ExportImage_RefusesFolderDrives()
    {
        string dir = Directory.CreateTempSubdirectory("phidos-export-").FullName;
        try
        {
            using var m = new TestMachine(mm =>
            {
                mm.MountImage('A', SystemPrograms.CreateSystemDisk());
                mm.MountFolder('B', dir);
            });
            Assert.Throws<InvalidOperationException>(() => m.Machine.ExportImage('B'));
            m.Machine.Unmount('B');
            Assert.Throws<InvalidOperationException>(() => m.Machine.WithDrive('B', _ => { })); // 没插盘
        }
        finally { Directory.Delete(dir, true); }
    }
}
