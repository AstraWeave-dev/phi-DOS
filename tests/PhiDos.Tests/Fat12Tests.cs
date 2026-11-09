using System.Diagnostics;
using System.Text;
using PhiDos.Abi;
using PhiDos.Core.Storage;

namespace PhiDos.Tests;

public class Fat12Tests
{
    private static readonly DateTime T = new(1983, 10, 20, 12, 0, 0);

    private static Fat12Drive NewDisk(DiskGeometry? g = null, string? label = null)
        => new(Fat12Image.CreateInMemory(g, label, T));

    private static void WriteFile(IDrive d, string[] path, string text)
    {
        var f = d.CreateFile(path, T);
        f.Write(0, Encoding.UTF8.GetBytes(text));
        f.Close(T);
    }

    private static string ReadFile(IDrive d, params string[] path)
    {
        var f = d.OpenFile(path, false);
        var buf = new byte[f.Length];
        f.Read(0, buf);
        f.Close(null);
        return Encoding.UTF8.GetString(buf);
    }

    [Fact]
    public void NewDisk_Has360KGeometry()
    {
        using var d = NewDisk();
        var space = d.GetSpace();
        Assert.Equal(362_496, space.TotalBytes); // 和 DOS 2.0 FORMAT 报告的一致
        Assert.Equal(362_496, space.FreeBytes);
        Assert.Equal(1024, space.BytesPerCluster);
        Assert.Null(d.VolumeLabel);
        Assert.Empty(d.List([]));
    }

    [Fact]
    public void VolumeLabel_IsStoredInRootDirectory()
    {
        using var d = NewDisk(label: "phidisk");
        Assert.Equal("PHIDISK", d.VolumeLabel);
        Assert.Empty(d.List([]));
    }

    [Fact]
    public void Files_CanBeWrittenAndReadBack()
    {
        using var d = NewDisk();
        WriteFile(d, ["HELLO.TXT"], "Hello, phi-DOS!");
        Assert.Equal("Hello, phi-DOS!", ReadFile(d, "HELLO.TXT"));
        var e = Assert.Single(d.List([]));
        Assert.Equal("HELLO.TXT", e.Name);
        Assert.Equal(15, e.Size);
        Assert.Equal(T, e.LastWriteTime);
        Assert.Equal(362_496 - 1024, d.GetSpace().FreeBytes);
    }

    [Fact]
    public void LargeFile_SpansManyClusters()
    {
        using var d = NewDisk();
        var data = new byte[100_000];
        new Random(42).NextBytes(data);
        var f = d.CreateFile(["BIG.DAT"], T);
        f.Write(0, data.AsSpan(0, 50_000));
        f.Write(50_000, data.AsSpan(50_000));
        f.Close(T);

        var r = d.OpenFile(["BIG.DAT"], false);
        var back = new byte[r.Length];
        Assert.Equal(100_000, r.Read(0, back));
        Assert.Equal(data, back);
        Assert.Equal(362_496 - 98 * 1024, d.GetSpace().FreeBytes);
    }

    [Fact]
    public void SetLength_ShrinksAndFreesClusters()
    {
        using var d = NewDisk();
        var f = d.CreateFile(["A.DAT"], T);
        f.Write(0, new byte[10_000]);
        f.SetLength(1000);
        f.Close(T);
        Assert.Equal(1000, d.GetEntry(["A.DAT"])!.Size);
        Assert.Equal(362_496 - 1024, d.GetSpace().FreeBytes);
    }

    [Fact]
    public void Subdirectories_WorkAndContainDotEntries()
    {
        using var d = NewDisk();
        d.MakeDirectory(["GAMES"], T);
        d.MakeDirectory(["GAMES", "SAVE"], T);
        WriteFile(d, ["GAMES", "SAVE", "SLOT1.DAT"], "level 3");
        Assert.True(d.GetEntry(["GAMES"])!.IsDirectory);
        Assert.Equal("level 3", ReadFile(d, "GAMES", "SAVE", "SLOT1.DAT"));
        Assert.Equal(["SAVE"], d.List(["GAMES"]).Select(e => e.Name));
        Assert.Throws<DosException>(() => d.RemoveDirectory(["GAMES", "SAVE"]));
        d.Delete(["GAMES", "SAVE", "SLOT1.DAT"]);
        d.RemoveDirectory(["GAMES", "SAVE"]);
        Assert.Empty(d.List(["GAMES"]));
    }

    [Fact]
    public void Subdirectory_GrowsBeyondOneCluster()
    {
        using var d = NewDisk();
        d.MakeDirectory(["MANY"], T);
        for (int i = 0; i < 100; i++) WriteFile(d, ["MANY", $"F{i}.TXT"], i.ToString());
        Assert.Equal(100, d.List(["MANY"]).Count);
        Assert.Equal("57", ReadFile(d, "MANY", "F57.TXT"));
    }

    [Fact]
    public void RootDirectory_IsLimitedTo112Entries()
    {
        using var d = NewDisk();
        for (int i = 0; i < 112; i++) WriteFile(d, [$"F{i}.TXT"], "");
        var ex = Assert.Throws<DosException>(() => d.CreateFile(["ONEMORE.TXT"], T));
        Assert.Equal(DosError.AccessDenied, ex.Error);
    }

    [Fact]
    public void Rename_MovesBetweenDirectories()
    {
        using var d = NewDisk();
        d.MakeDirectory(["DOCS"], T);
        WriteFile(d, ["A.TXT"], "moved");
        d.Rename(["A.TXT"], ["DOCS", "B.TXT"]);
        Assert.Null(d.GetEntry(["A.TXT"]));
        Assert.Equal("moved", ReadFile(d, "DOCS", "B.TXT"));
    }

    [Fact]
    public void DiskFull_IsReported()
    {
        using var d = NewDisk(DiskGeometry.Floppy160K);
        var f = d.CreateFile(["HUGE.DAT"], T);
        var ex = Assert.Throws<DosException>(() => f.Write(0, new byte[200_000]));
        Assert.Equal(DosError.DiskFull, ex.Error);
    }

    [Fact]
    public void ChineseNames_RoundTrip_EvenWithLeadByteE5()
    {
        // "存" 的 UTF-8 编码以 0xE5 开头，正好是 FAT 的"已删除"标记，需要特殊处理
        using var d = NewDisk();
        WriteFile(d, ["存档.TXT"], "中文内容");
        Assert.Equal("存档.TXT", Assert.Single(d.List([])).Name);
        Assert.Equal("中文内容", ReadFile(d, "存档.TXT"));
    }

    [Fact]
    public void ReadOnlyDisk_RejectsWrites()
    {
        using var d = new Fat12Drive(Fat12Image.CreateInMemory(now: T), readOnly: true);
        var ex = Assert.Throws<DosException>(() => d.CreateFile(["X.TXT"], T));
        Assert.Equal(DosError.WriteProtected, ex.Error);
    }

    [Fact]
    public void Format_ClearsEverything()
    {
        using var d = NewDisk();
        WriteFile(d, ["A.TXT"], "data");
        d.Format("NEWDISK", T);
        Assert.Empty(d.List([]));
        Assert.Equal("NEWDISK", d.VolumeLabel);
        Assert.Equal(362_496, d.GetSpace().FreeBytes);
    }

    [Fact]
    public void AllStandardGeometries_AreValidFat12()
    {
        foreach (var g in DiskGeometry.All)
        {
            using var d = NewDisk(g);
            WriteFile(d, ["T.TXT"], "x");
            Assert.Equal("x", ReadFile(d, "T.TXT"));
        }
    }

    [Fact]
    public void NonFatData_IsRejected()
    {
        var ms = new MemoryStream(new byte[368_640]);
        Assert.Throws<InvalidDataException>(() => new Fat12Drive(ms));
    }

    // ───────── 与 mtools 互通（没装 mtools 时跳过）─────────

    private static bool HasMtools => File.Exists("/usr/bin/mdir");

    private static string Mtools(string tool, string args)
    {
        var psi = new ProcessStartInfo(tool, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Environment = { ["MTOOLS_SKIP_CHECK"] = "1", ["MTOOLS_NO_VFAT"] = "1" },
        };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return output;
    }

    [Fact]
    public void ImagesWeWrite_AreReadableByMtools()
    {
        if (!HasMtools) return;
        string img = Path.Combine(Path.GetTempPath(), $"phidos-{Guid.NewGuid():N}.phidisk");
        try
        {
            Fat12Image.Create(img, volumeLabel: "TESTDISK", now: T);
            using (var d = new Fat12Drive(File.Open(img, FileMode.Open), ownsStream: true))
            {
                d.MakeDirectory(["GAMES"], T);
                WriteFile(d, ["GAMES", "README.TXT"], "inside a subdirectory");
                var big = new byte[30_000];
                new Random(1).NextBytes(big);
                var f = d.CreateFile(["BIG.BIN"], T);
                f.Write(0, big);
                f.Close(T);
            }
            Assert.Contains("inside a subdirectory", Mtools("mtype", $"-i {img} ::GAMES/README.TXT"));
            string dir = Mtools("mdir", $"-i {img} ::");
            Assert.Contains("TESTDISK", dir);
            Assert.Contains("GAMES", dir);
            Assert.Contains("BIG", dir);
            string check = Mtools("minfo", $"-i {img} ::");
            Assert.Contains("cluster size: 2 sectors", check);
            Assert.Contains("media descriptor byte: 0xfd", check);
        }
        finally { File.Delete(img); }
    }

    [Fact]
    public void ImagesMtoolsWrites_AreReadableByUs()
    {
        if (!HasMtools) return;
        string img = Path.Combine(Path.GetTempPath(), $"phidos-{Guid.NewGuid():N}.img");
        string src = Path.Combine(Path.GetTempPath(), $"phidos-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(src, "written by mtools");
            Mtools("mformat", $"-i {img} -C -f 360 -v MTDISK ::");
            Mtools("mmd", $"-i {img} ::SUB");
            Mtools("mcopy", $"-i {img} {src} ::SUB/NOTE.TXT");
            using var d = new Fat12Drive(File.Open(img, FileMode.Open), ownsStream: true);
            Assert.Equal("MTDISK", d.VolumeLabel);
            Assert.Equal("written by mtools", ReadFile(d, "SUB", "NOTE.TXT"));
        }
        finally
        {
            File.Delete(img);
            File.Delete(src);
        }
    }
}
