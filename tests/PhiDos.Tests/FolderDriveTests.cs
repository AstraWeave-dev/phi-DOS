using System.Text;
using PhiDos.Abi;
using PhiDos.Core.Storage;

namespace PhiDos.Tests;

public sealed class FolderDriveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"phidos-folder-{Guid.NewGuid():N}");
    private static readonly DateTime T = new(1983, 10, 20, 12, 0, 0);

    public FolderDriveTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "readme.txt"), "lower case on the host");
        File.WriteAllText(Path.Combine(_dir, "LongFileName.txt"), "too long");
        File.WriteAllText(Path.Combine(_dir, "存档.TXT"), "中文");
        File.WriteAllText(Path.Combine(_dir, "我的存档.txt"), "12 bytes, too long");
        Directory.CreateDirectory(Path.Combine(_dir, "games"));
        File.WriteAllText(Path.Combine(_dir, "games", "a.dat"), "aaa");
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void OnlyValidShortNamesAreVisible()
    {
        var d = new FolderDrive(_dir);
        var names = d.List([]).Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(["GAMES", "README.TXT", "存档.TXT"], names);
    }

    [Fact]
    public void Lookup_IsCaseInsensitive()
    {
        var d = new FolderDrive(_dir);
        var f = d.OpenFile(["GAMES", "A.DAT"], false);
        var buf = new byte[f.Length];
        f.Read(0, buf);
        f.Close(null);
        Assert.Equal("aaa", Encoding.UTF8.GetString(buf));
    }

    [Fact]
    public void NewFiles_AreCreatedInUpperCase()
    {
        var d = new FolderDrive(_dir);
        var f = d.CreateFile(["new.txt"], T);
        f.Write(0, "x"u8);
        f.Close(T);
        Assert.True(File.Exists(Path.Combine(_dir, "NEW.TXT")));
        Assert.Equal(T, File.GetLastWriteTime(Path.Combine(_dir, "NEW.TXT")));
    }

    [Fact]
    public void Capacity_IsEnforced()
    {
        var d = new FolderDrive(_dir, capacityBytes: 10 * 1024);
        var f = d.CreateFile(["BIG.DAT"], T);
        var ex = Assert.Throws<DosException>(() => f.Write(0, new byte[20 * 1024]));
        Assert.Equal(DosError.DiskFull, ex.Error);
        f.Close(null);
    }

    [Fact]
    public void SymbolicLinks_AreIgnored()
    {
        string outside = Path.Combine(Path.GetTempPath(), $"phidos-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(_dir, "ESCAPE"), outside);
            var d = new FolderDrive(_dir);
            Assert.DoesNotContain(d.List([]), e => e.Name == "ESCAPE");
            Assert.Null(d.GetEntry(["ESCAPE"]));
        }
        finally { Directory.Delete(outside); }
    }

    [Fact]
    public void Format_IsNotSupported()
    {
        var d = new FolderDrive(_dir);
        var ex = Assert.Throws<DosException>(() => d.Format(null, T));
        Assert.Equal(DosError.NotSupported, ex.Error);
    }

    [Fact]
    public void RemoveDirectory_RefusesWhenHostHasHiddenFiles()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "KEEP"));
        File.WriteAllText(Path.Combine(_dir, "KEEP", "a very long name.txt"), "precious");
        var d = new FolderDrive(_dir);
        Assert.Empty(d.List(["KEEP"]));
        Assert.Throws<DosException>(() => d.RemoveDirectory(["KEEP"]));
        Assert.True(File.Exists(Path.Combine(_dir, "KEEP", "a very long name.txt")));
    }
}
