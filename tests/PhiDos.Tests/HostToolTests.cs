using System.Diagnostics;
using System.Text;
using PhiDos.ConsoleHost;
using PhiDos.Core.Machine;
using PhiDos.Core.Storage;

namespace PhiDos.Tests;

/// <summary>终端版的命令行解析、镜像工具 phidisk、不限容量的文件夹驱动器。</summary>
public sealed class HostToolTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("phidos-tool-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string P(string name) => Path.Combine(_root, name);

    // ───────── 命令行 ─────────

    [Fact]
    public void CommandLine_PositionalArgumentsFillDrivesInOrder()
    {
        var o = CommandLine.Parse(["sys.phidisk", "games", "save.phidisk"]);
        Assert.Equal("ABC", new string(o.Mounts.Select(m => m.Drive).ToArray()));
    }

    [Fact]
    public void CommandLine_DriveAssignments()
    {
        var o = CommandLine.Parse(["C:=D:\\games:ro:1.44M", "Z:=/home/me/shared:unlimited", "-r", "e:=disk.phidisk"]);
        var c = o.Mounts.Single(m => m.Drive == 'C');
        Assert.Equal("D:\\games", c.Path);
        Assert.True(c.ReadOnly);
        Assert.Equal(1_457_664, c.Capacity);  // 1.44M 软盘真正能放的字节数
        var z = o.Mounts.Single(m => m.Drive == 'Z');
        Assert.Equal("/home/me/shared", z.Path);
        Assert.False(z.ReadOnly);
        Assert.Equal(FolderDrive.Unlimited, z.Capacity);
        Assert.True(o.Mounts.Single(m => m.Drive == 'E').ReadOnly); // -r 影响后面的盘
    }

    [Fact]
    public void CommandLine_MixedStyles_AndErrors()
    {
        var o = CommandLine.Parse(["A:=sys.phidisk", "-b", "folder", "third"]);
        Assert.Equal("ABC", new string(o.Mounts.Select(m => m.Drive).ToArray()));
        Assert.Throws<ArgumentException>(() => CommandLine.Parse(["a.phidisk", "A:=b.phidisk"]));
        Assert.Throws<ArgumentException>(() => CommandLine.Parse(["--frob"]));
        Assert.Equal(["x", "y"], CommandLine.Parse(["--phidisk", "x", "y"]).DiskToolArgs!);
    }

    [Theory]
    [InlineData("360K", 362_496)]
    [InlineData("1.44M", 1_457_664)]
    [InlineData("10M", 10_485_760)]
    [InlineData("512K", 524_288)]
    [InlineData("2000", 2000)]
    public void CommandLine_Sizes(string text, long bytes)
    {
        Assert.True(CommandLine.TryParseSize(text, out long parsed));
        Assert.Equal(bytes, parsed);
    }

    // ───────── 不限容量、Z 盘 ─────────

    [Fact]
    public void UnlimitedFolder_AndDriveZ()
    {
        Directory.CreateDirectory(P("big"));
        using var m = new TestMachine(mm =>
        {
            mm.MountImage('A', ShellReadTests.SampleDisk());
            mm.MountFolder('Z', P("big"), capacityBytes: FolderDrive.Unlimited);
        }, new MachineOptions { PromptForDateTimeAtBoot = false, DefaultPrompt = "", DriveCount = 26 });
        m.Run("COPY CON Z:BIG.TXT");
        m.Type(new string('x', 100) + "\n\u001A\n");
        for (int i = 0; i < 13; i++) m.Run("COPY Z:BIG.TXT+Z:BIG.TXT Z:BIG2.TXT > NUL");
        string dir = m.Run("DIR Z:");
        Assert.Contains("BIG      TXT", dir);
        Assert.DoesNotContain("Insufficient", dir);
        Assert.Equal("\nZ>", m.Run("Z:"));
    }

    // ───────── phidisk ─────────

    private void MakeSampleFolder()
    {
        Directory.CreateDirectory(P("src/GAMES"));
        File.WriteAllText(P("src/readme.txt"), "hello\r\n");
        File.WriteAllText(P("src/GAMES/save.dat"), new string('s', 3000));
        File.WriteAllText(P("src/存档.TXT"), "中文\r\n");
        File.WriteAllText(P("src/A very long name.txt"), "skipped");
        File.SetLastWriteTime(P("src/readme.txt"), new DateTime(1984, 1, 5, 9, 4, 0));
    }

    [Fact]
    public void Pack_Unpack_RoundTrip()
    {
        MakeSampleFolder();
        Assert.Equal(3, DiskTool.Run([P("src"), P("disk.phidisk"), "--label", "games"])); // 3 = 有文件被跳过
        Assert.Equal(163_840, new FileInfo(P("disk.phidisk")).Length); // 自动挑最小的 160K

        using (var d = new Fat12Drive(File.OpenRead(P("disk.phidisk")), readOnly: true, ownsStream: true))
        {
            Assert.Equal("GAMES", d.VolumeLabel);
            Assert.Equal(["GAMES", "README.TXT", "存档.TXT"], d.List([]).Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal));
            Assert.Equal(3000, d.GetEntry(["GAMES", "SAVE.DAT"])!.Size);
            Assert.Equal(new DateTime(1984, 1, 5, 9, 4, 0), d.GetEntry(["README.TXT"])!.LastWriteTime);
        }

        Assert.Equal(0, DiskTool.Run([P("disk.phidisk"), P("out")]));
        Assert.Equal("hello\r\n", File.ReadAllText(P("out/README.TXT")));
        Assert.Equal(new string('s', 3000), File.ReadAllText(P("out/GAMES/SAVE.DAT")));
        Assert.Equal("中文\r\n", File.ReadAllText(P("out/存档.TXT")));
        Assert.Equal(new DateTime(1984, 1, 5, 9, 4, 0), File.GetLastWriteTime(P("out/README.TXT")));

        // 再解包一次：文件已存在，不加 --force 就拒绝
        Assert.Equal(2, DiskTool.Run([P("disk.phidisk"), P("out")]));
        Assert.Equal(0, DiskTool.Run([P("disk.phidisk"), P("out"), "--force"]));
        Assert.Equal(0, DiskTool.Run([P("disk.phidisk")])); // 列出内容
    }

    [Fact]
    public void Pack_RefusesToOverwrite_AndHonoursSize()
    {
        Directory.CreateDirectory(P("small"));
        File.WriteAllText(P("small/A.TXT"), "a");
        Assert.Equal(0, DiskTool.Run([P("small"), P("x.phidisk"), "--size", "1.44M"]));
        Assert.Equal(1_474_560, new FileInfo(P("x.phidisk")).Length);
        Assert.Equal(2, DiskTool.Run([P("small"), P("x.phidisk")]));
        Assert.Equal(0, DiskTool.Run([P("small"), P("x.phidisk"), "--force"]));
        Assert.Equal(163_840, new FileInfo(P("x.phidisk")).Length);
    }

    [Fact]
    public void Pack_TooBig()
    {
        Directory.CreateDirectory(P("huge"));
        File.WriteAllBytes(P("huge/BIG.BIN"), new byte[2_000_000]);
        Assert.Equal(1, DiskTool.Run([P("huge"), P("h.phidisk")]));
        Assert.False(File.Exists(P("h.phidisk")));
    }

    [Fact]
    public void Pack_TooManyRootEntries_PicksALargerDisk()
    {
        Directory.CreateDirectory(P("many"));
        for (int i = 0; i < 100; i++) File.WriteAllText(P($"many/F{i}.TXT"), "x");
        Assert.Equal(0, DiskTool.Run([P("many"), P("m.phidisk")]));
        // 160K 和 180K 的根目录只能放 64 项，所以选了 320K
        Assert.Equal(327_680, new FileInfo(P("m.phidisk")).Length);
    }

    [Fact]
    public void PackedImage_IsReadableByMtools()
    {
        if (!File.Exists("/usr/bin/mtype")) return;
        MakeSampleFolder();
        DiskTool.Run([P("src"), P("mt.phidisk")]);
        var psi = new ProcessStartInfo("mtype", $"-i {P("mt.phidisk")} ::GAMES/SAVE.DAT")
        {
            RedirectStandardOutput = true,
            Environment = { ["MTOOLS_SKIP_CHECK"] = "1" },
        };
        using var p = Process.Start(psi)!;
        Assert.Equal(new string('s', 3000), p.StandardOutput.ReadToEnd());
    }

    [Fact]
    public void PackedImage_BootsInPhiDos()
    {
        MakeSampleFolder();
        DiskTool.Run([P("src"), P("boot.phidisk")]);
        using var m = new TestMachine(mm => mm.MountImage('A', P("boot.phidisk")));
        Assert.Equal("hello\n\nA>", m.Run("TYPE README.TXT"));
    }
}
