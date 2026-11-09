using System.Text;
using PhiDos.Core.Machine;
using PhiDos.Core.Storage;

namespace PhiDos.Tests;

/// <summary>M3：写操作。</summary>
public sealed class ShellWriteTests : IDisposable
{
    private readonly TestMachine _m = new(m => m.MountImage('A', ShellReadTests.SampleDisk()));

    public void Dispose() => _m.Dispose();

    private string Type(string file) => _m.Run("TYPE " + file).Replace("\nA>", "").TrimEnd('\n');

    [Fact]
    public void Copy_SingleFile_KeepsTimestamp()
    {
        Assert.Equal("        1 File(s) copied\n\nA>", _m.Run("COPY README.TXT COPY.TXT"));
        Assert.Equal("Welcome to phi-DOS.\n第二行是中文。", Type("COPY.TXT"));
        Assert.Contains("COPY     TXT       54   1-05-84   9:05a", _m.Run("DIR COPY.TXT"));
    }

    [Fact]
    public void Copy_ToDirectory()
    {
        _m.Run("COPY README.TXT GAMES");
        Assert.Contains("README   TXT", _m.Run("DIR GAMES"));
        _m.Run("COPY NOTES GAMES\\");
        Assert.Contains("NOTES", _m.Run("DIR GAMES"));
    }

    [Fact]
    public void Copy_Wildcards_ShowEachName()
    {
        _m.Run("MD BACKUP");
        Assert.Equal("README.TXT\nNOTES\n        2 File(s) copied\n\nA>", _m.Run("COPY *.* BACKUP"));
        Assert.Equal("GAMES\\SAVE.DAT\n        1 File(s) copied\n\nA>", _m.Run("COPY GAMES\\*.DAT BACKUP"));
    }

    [Fact]
    public void Copy_Template()
    {
        _m.Run("COPY README.TXT A.TXT");
        _m.Run("COPY *.TXT *.BAK");
        Assert.Contains("        2 File(s)", _m.Run("DIR *.BAK"));
    }

    [Fact]
    public void Copy_OntoItself()
    {
        Assert.Equal("File cannot be copied onto itself\n        0 File(s) copied\n\nA>", _m.Run("COPY README.TXT"));
    }

    [Fact]
    public void Copy_SourceNotFound()
    {
        Assert.Equal("NOSUCH.TXT File not found\n        0 File(s) copied\n\nA>", _m.Run("COPY nosuch.txt x.txt"));
    }

    [Fact]
    public void Copy_Concatenation()
    {
        _m.Run("COPY CON A.TXT");
        _m.Type("first\n\u001A\n");
        _m.Run("COPY CON B.TXT");
        _m.Type("second\n\u001A\n");
        Assert.Equal("A.TXT\nB.TXT\n        1 File(s) copied\n\nA>", _m.Run("COPY A.TXT+B.TXT C.TXT"));
        Assert.Equal("first\nsecond", Type("C.TXT"));
        // A+B 不写目标时，追加到 A 后面
        _m.Run("COPY A.TXT+B.TXT");
        Assert.Equal("first\nsecond", Type("A.TXT"));
    }

    [Fact]
    public void CopyCon_ReadsUntilCtrlZ()
    {
        Assert.Equal("", _m.Run("COPY CON NEW.TXT"));
        _m.Type("hello\nworld\n");
        string end = _m.Type("\u001A\n");
        Assert.Equal("^Z\n        1 File(s) copied\n\nA>", end);
        Assert.Equal("hello\nworld", Type("NEW.TXT"));
    }

    [Fact]
    public void Copy_ToConsole()
    {
        Assert.Equal("no extension        1 File(s) copied\n\nA>", _m.Run("COPY NOTES CON"));
    }

    [Fact]
    public void Copy_DiskFull_RemovesPartialFile()
    {
        var a = ShellReadTests.SampleDisk();
        var d = new Fat12Drive(a);
        var f = d.CreateFile(["BIG.DAT"], TestMachine.FixedTime);
        f.Write(0, new byte[200_000]); // 放不进 160KB 的 B 盘
        f.Close(TestMachine.FixedTime);
        d.Flush();
        a.Position = 0;

        using var m = new TestMachine(mm =>
        {
            mm.MountImage('A', a);
            mm.MountImage('B', Fat12Image.CreateInMemory(DiskGeometry.Floppy160K));
        });
        Assert.Equal("Insufficient disk space\n        0 File(s) copied\n\nA>", m.Run("COPY BIG.DAT B:"));
        Assert.EndsWith("File not found\n\nA>", m.Run("DIR B:BIG.DAT"));
    }

    [Fact]
    public void Copy_SameFileTwiceInConcatenation_LosesContent()
    {
        Assert.Equal("NOTES\nNOTES\nContent of destination lost before copy\n        1 File(s) copied\n\nA>",
            _m.Run("COPY NOTES+NOTES"));
    }

    [Fact]
    public void Del_SingleAndWildcard()
    {
        Assert.Equal("\nA>", _m.Run("DEL NOTES"));
        Assert.Equal("File not found\n\nA>", _m.Run("DEL NOTES"));
        _m.Run("COPY README.TXT X.TXT");
        _m.Run("ERASE *.TXT");
        Assert.EndsWith("File not found\n\nA>", _m.Run("DIR *.TXT"));
    }

    [Fact]
    public void Del_Everything_AsksFirst()
    {
        Assert.Equal("Are you sure (Y/N)? ", _m.Run("DEL *.*"));
        Assert.Equal("\nA>", _m.Run("N"));
        Assert.Contains("README", _m.Run("DIR"));
        _m.Run("DEL *.*");
        _m.Run("Y");
        string dir = _m.Run("DIR");
        Assert.DoesNotContain("README", dir);
        Assert.Contains("GAMES", dir); // 目录不会被删除
    }

    [Fact]
    public void Ren_SingleAndWildcard()
    {
        Assert.Equal("\nA>", _m.Run("REN README.TXT READ.ME"));
        Assert.Contains("READ     ME", _m.Run("DIR"));
        Assert.Equal("Duplicate file name or File not found\n\nA>", _m.Run("REN NOSUCH X"));
        Assert.Equal("Duplicate file name or File not found\n\nA>", _m.Run("REN READ.ME NOTES"));
        _m.Run("COPY READ.ME A.TXT");
        _m.Run("COPY READ.ME B.TXT");
        _m.Run("REN *.TXT *.OLD");
        Assert.Contains("        2 File(s)", _m.Run("DIR *.OLD"));
    }

    [Fact]
    public void MdAndRd()
    {
        Assert.Equal("\nA>", _m.Run("MD WORK"));
        Assert.Equal("Unable to create directory\n\nA>", _m.Run("MD WORK"));
        _m.Run("COPY NOTES WORK");
        Assert.Equal("Invalid path, not directory,\nor directory not empty\n\nA>", _m.Run("RD WORK"));
        _m.Run("DEL WORK\\NOTES");
        _m.Run("CD WORK");
        Assert.Equal("Invalid path, not directory,\nor directory not empty\n\nA>", _m.Run("RD \\WORK"));
        _m.Run("CD \\");
        Assert.Equal("\nA>", _m.Run("RD WORK"));
        Assert.Equal("Invalid directory\n\nA>", _m.Run("CD WORK"));
    }

    [Fact]
    public void DirectoryPathLimit_Is64Characters()
    {
        string path = "";
        for (int i = 0; i < 7; i++)
        {
            path += "\\DIRNAME" + i;
            Assert.Equal("\nA>", _m.Run("MD " + path));
        }
        // 再加一层就超过 63 个字符了
        Assert.Equal("Unable to create directory\n\nA>", _m.Run("MD " + path + "\\TOOLONG1"));
    }

    [Fact]
    public void WriteProtect_ShowsCriticalError()
    {
        using var m = new TestMachine(mm => mm.MountImage('A', ShellReadTests.SampleDisk(), readOnly: true));
        Assert.Equal("\nWrite protect error writing drive A\nAbort, Retry, Ignore? ", m.Run("COPY NOTES X"));
        Assert.Equal("I\n        0 File(s) copied\n\nA>", m.Type("I"));
    }

    [Fact]
    public void FolderDrive_WorksThroughTheShell()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"phidos-shell-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "hello.txt"), "from the host");
            using var m = new TestMachine(mm =>
            {
                mm.MountImage('A', ShellReadTests.SampleDisk());
                mm.MountFolder('B', dir);
            });
            Assert.Contains("HELLO    TXT       13", m.Run("DIR B:"));
            m.Run("COPY README.TXT B:");
            m.Run("MD B:SUB");
            Assert.True(File.Exists(Path.Combine(dir, "README.TXT")));
            Assert.True(Directory.Exists(Path.Combine(dir, "SUB")));
            Assert.Equal("Welcome to phi-DOS.\r\n第二行是中文。\r\n", File.ReadAllText(Path.Combine(dir, "README.TXT")).Split('\u001A')[0]);
            m.Run("B:");
            Assert.Equal("Invalid directory\n\nB>", m.Run("CD .."));
        }
        finally { Directory.Delete(dir, true); }
    }
}
