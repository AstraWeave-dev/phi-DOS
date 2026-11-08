using PhiDos.Abi;
using PhiDos.Core.Kernel;
using PhiDos.Core.Machine;
using PhiDos.Core.Storage;
using PhiDos.Core.Text;

namespace PhiDos.Tests;

public class PathTests
{
    private static DosKernel NewKernel()
    {
        var machine = new DosMachine();
        machine.MountImage('A', Fat12Image.CreateInMemory());
        return new DosKernel(machine, new ExecutionGate(machine.ConsoleDevice));
    }

    [Theory]
    [InlineData("readme.txt", "A:\\README.TXT")]
    [InlineData("A:\\GAMES\\SAVE.DAT", "A:\\GAMES\\SAVE.DAT")]
    [InlineData("b:foo", "B:\\FOO")]
    [InlineData("\\a\\b\\..\\c", "A:\\A\\C")]
    [InlineData(".\\x\\.\\y", "A:\\X\\Y")]
    [InlineData("verylongname.text", "A:\\VERYLONG.TEX")]
    [InlineData("A:", "A:\\")]
    [InlineData("A:\\", "A:\\")]
    [InlineData("GAMES\\", "A:\\GAMES")]
    public void Resolve_NormalizesPaths(string input, string expected)
    {
        Assert.Equal(expected, NewKernel().Resolve(input).ToString());
    }

    [Fact]
    public void Resolve_UsesCurrentDirectoryOfEachDrive()
    {
        var k = NewKernel();
        k.GetCurrentDirectoryParts('A');
        k.Machine.MountImage('B', Fat12Image.CreateInMemory());
        k.DriveOp('A', true, d => d.MakeDirectory(["WORK"], DateTime.Now));
        k.ChangeDirectory("\\WORK");
        Assert.Equal("A:\\WORK\\FILE.TXT", k.Resolve("file.txt").ToString());
        Assert.Equal("B:\\FILE.TXT", k.Resolve("B:file.txt").ToString());
        Assert.Equal("A:\\", k.Resolve("..").ToString());
    }

    [Fact]
    public void DotDotAtRoot_IsAnError()
    {
        var ex = Assert.Throws<DosException>(() => NewKernel().Resolve("..\\..\\secret"));
        Assert.Equal(DosError.PathNotFound, ex.Error);
    }

    [Fact]
    public void InvalidDrive_IsReported()
    {
        var ex = Assert.Throws<DosException>(() => NewKernel().Resolve("Z:\\x"));
        Assert.Equal(DosError.InvalidDrive, ex.Error);
    }

    [Theory]
    [InlineData("*.*", "FOO.TXT", true)]
    [InlineData("*.TXT", "FOO.TXT", true)]
    [InlineData("*.TXT", "FOO.DAT", false)]
    [InlineData("F??.TXT", "FOO.TXT", true)]
    [InlineData("A?.TXT", "A.TXT", true)]
    [InlineData("*", "FOO", true)]
    [InlineData("*", "FOO.TXT", false)]
    [InlineData("FO*.*", "FOO.BAR", true)]
    [InlineData("*X.*", "FOO.BAR", true)]
    [InlineData("存*.*", "存档.TXT", true)]
    public void Wildcards_FollowDosRules(string pattern, string name, bool expected)
    {
        Assert.Equal(expected, DosNames.Matches(pattern, name));
    }

    [Theory]
    [InlineData("hello.txt", "HELLO.TXT")]
    [InlineData("我的存档.txt", "我的.TXT")]
    [InlineData("a+b.txt", null)]
    [InlineData("x.y.z", null)]
    public void Names_AreNormalizedTo83(string input, string? expected)
    {
        Assert.Equal(expected, DosNames.Normalize(input, allowWildcards: false));
    }
}
