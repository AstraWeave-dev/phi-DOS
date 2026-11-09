using System.Text;
using PhiDos.Core.Machine;
using PhiDos.Core.Storage;

namespace PhiDos.Tests;

/// <summary>M2：只读命令，逐字检查输出格式。</summary>
public class ShellReadTests
{
    private static readonly DateTime T = TestMachine.FixedTime;

    /// <summary>一张预先放好文件的盘：根目录两个文件和一个子目录。</summary>
    internal static MemoryStream SampleDisk(string? label = null)
    {
        var ms = Fat12Image.CreateInMemory(volumeLabel: label, now: T);
        var d = new Fat12Drive(ms);
        void Put(string[] path, string text, DateTime t)
        {
            var f = d.CreateFile(path, t);
            f.Write(0, Encoding.UTF8.GetBytes(text));
            f.Close(t);
        }
        Put(["README.TXT"], "Welcome to phi-DOS.\r\n第二行是中文。\r\n\u001Aafter eof", new DateTime(1984, 1, 5, 9, 5, 0));
        Put(["NOTES"], "no extension", new DateTime(1983, 3, 8, 0, 30, 0));
        d.MakeDirectory(["GAMES"], T);
        Put(["GAMES", "SAVE.DAT"], new string('x', 1500), new DateTime(1983, 12, 31, 23, 59, 0));
        d.Flush();
        ms.Position = 0;
        return ms;
    }

    private static TestMachine WithSample(string? label = null)
        => new(m => m.MountImage('A', SampleDisk(label)));

    [Fact]
    public void Dir_MatchesClassicFormat()
    {
        using var m = WithSample();
        string expected =
            "\n" +
            " Volume in drive A has no label\n" +
            " Directory of  A:\\\n" +
            "\n" +
            "README   TXT       54   1-05-84   9:05a\n" +
            "NOTES              12   3-08-83  12:30a\n" +
            "GAMES        <DIR>     10-20-83  12:00p\n" +
            "        3 File(s)    357376 bytes free\n" +
            "\n" +
            "A>";
        Assert.Equal(expected, m.Run("dir"));
    }

    [Fact]
    public void Dir_ShowsVolumeLabel()
    {
        using var m = WithSample("STORY");
        Assert.Contains(" Volume in drive A is STORY\n", m.Run("DIR"));
    }

    [Fact]
    public void Dir_Subdirectory_ShowsDotEntries()
    {
        using var m = WithSample();
        string output = m.Run("DIR GAMES");
        Assert.Contains(" Directory of  A:\\GAMES\n", output);
        Assert.Contains(".            <DIR>     10-20-83  12:00p\n", output);
        Assert.Contains("..           <DIR>     10-20-83  12:00p\n", output);
        Assert.Contains("SAVE     DAT     1500  12-31-83  11:59p\n", output);
        Assert.Contains("        3 File(s)", output);
    }

    [Fact]
    public void Dir_Patterns()
    {
        using var m = WithSample();
        Assert.Contains("        1 File(s)", m.Run("DIR *.TXT"));
        Assert.Contains("README   TXT", m.Run("DIR README"));      // 只写主名 = README.*
        Assert.Contains("README   TXT", m.Run("DIR .TXT"));        // 只写扩展名 = *.TXT
        Assert.EndsWith("File not found\n\nA>", m.Run("DIR *.XYZ"));
    }

    [Fact]
    public void Dir_Wide()
    {
        using var m = WithSample();
        Assert.Contains("README   TXT\tNOTES       \tGAMES       \t\n        3 File(s)", m.Run("DIR/W"));
    }

    [Fact]
    public void Type_ShowsTextUntilCtrlZ()
    {
        using var m = WithSample();
        Assert.Equal("Welcome to phi-DOS.\n第二行是中文。\n\nA>", m.Run("TYPE README.TXT"));
        Assert.Equal("File not found\n\nA>", m.Run("TYPE NOSUCH.TXT"));
    }

    [Fact]
    public void Cd_ChangesAndShowsDirectory()
    {
        using var m = WithSample();
        Assert.Equal("A:\\\n\nA>", m.Run("CD"));
        m.Run("CD GAMES");
        Assert.Equal("A:\\GAMES\n\nA>", m.Run("CD"));
        Assert.Contains("SAVE     DAT", m.Run("DIR"));
        m.Run("CD..");
        Assert.Equal("A:\\\n\nA>", m.Run("CD"));
        Assert.Equal("Invalid directory\n\nA>", m.Run("CD NOWHERE"));
        Assert.Equal("Invalid directory\n\nA>", m.Run("CD .."));
        m.Run("CD\\GAMES");
        Assert.Equal("A:\\GAMES\n\nA>", m.Run("CD"));
    }

    [Fact]
    public void Ver_ShowsBanner()
    {
        using var m = WithSample();
        Assert.Equal("\nphi-DOS Version X.XX\n\nA>", m.Run("VER"));
    }

    [Fact]
    public void Vol_ShowsLabel()
    {
        using var m = WithSample("STORY");
        Assert.Equal("\n Volume in drive A is STORY\n\nA>", m.Run("VOL"));
        Assert.Equal("Invalid drive specification\n\nA>", m.Run("VOL Q:"));
    }

    [Fact]
    public void Rem_DoesNothing()
    {
        using var m = WithSample();
        Assert.Equal("\nA>", m.Run("REM anything at all"));
    }

    [Fact]
    public void UnknownCommand()
    {
        using var m = WithSample();
        Assert.Equal("Bad command or file name\n\nA>", m.Run("FROBNICATE"));
    }

    [Fact]
    public void DriveSwitch_AndNotReady()
    {
        using var m = WithSample();
        Assert.Equal("\nB>", m.Run("B:"));
        string output = m.Run("DIR");
        Assert.Equal("\n\nNot ready error reading drive B\nAbort, Retry, Ignore? ", output);
        // 插上一张盘再选 Retry
        m.Machine.MountImage('B', SampleDisk("SECOND"));
        Assert.Contains(" Volume in drive B is SECOND", m.Type("R"));
        Assert.Equal("Invalid drive specification\n\nB>", m.Run("Q:"));
    }

    [Fact]
    public void NotReady_Abort_ReturnsToPrompt()
    {
        using var m = WithSample();
        m.Run("DIR B:");
        Assert.Equal("A\n\nA>", m.Type("A"));
    }

    [Fact]
    public void Pause_WaitsForAKey()
    {
        using var m = WithSample();
        Assert.Equal("Strike a key when ready . . . ", m.Run("PAUSE"));
        Assert.Equal("\n\nA>", m.Type("x"));
    }

    [Fact]
    public void Date_ShowsAndSetsDate()
    {
        using var m = WithSample();
        Assert.Equal("Current date is Thu 10-20-1983\nEnter new date: ", m.Run("DATE"));
        Assert.Equal("\nA>", m.Run("12-25-85"));
        Assert.StartsWith("Current date is Wed 12-25-1985", m.Run("DATE"));
        Assert.Equal("\nInvalid date\nEnter new date: ", m.Run("13-01-85"));
        Assert.Equal("\nA>", m.Run(""));
    }

    [Fact]
    public void Date_TwoDigitYearsAreNineteenHundreds()
    {
        using var m = WithSample();
        m.Run("DATE 1-1-80");
        Assert.StartsWith("Current date is Tue  1-01-1980", m.Run("DATE"));
        m.Run("");
        Assert.Contains("Invalid date", m.Run("DATE 1-1-26")); // 1926 年，超出范围
    }

    [Fact]
    public void Time_ShowsAndSetsTime()
    {
        using var m = WithSample();
        Assert.Equal("Current time is 12:00:00.00\nEnter new time: ", m.Run("TIME"));
        Assert.Equal("\nA>", m.Run("9:05"));
        Assert.StartsWith("Current time is  9:05:00.00", m.Run("TIME"));
        Assert.Equal("\nInvalid time\nEnter new time: ", m.Run("25:00"));
    }

    [Fact]
    public void Boot_AsksForDateAndTimeWithoutAutoexec()
    {
        using var m = new TestMachine(
            mm => mm.MountImage('A', SampleDisk()),
            new MachineOptions { Clock = () => T, DefaultPrompt = "" });
        Assert.Equal(
            "phi-DOS Version X.XX\r\n\r\nCurrent date is Thu 10-20-1983\r\nEnter new date: ",
            m.BootOutput);
        Assert.Equal("Current time is 12:00:00.00\nEnter new time: ", m.Run(""));
        Assert.Equal("\nA>", m.Run(""));
    }

    [Fact]
    public void BreakAndVerify()
    {
        using var m = WithSample();
        Assert.Equal("BREAK is off\n\nA>", m.Run("BREAK"));
        m.Run("BREAK ON");
        Assert.Equal("BREAK is on\n\nA>", m.Run("BREAK"));
        Assert.Equal("Must specify ON or OFF\n\nA>", m.Run("VERIFY MAYBE"));
    }

    [Fact]
    public void CtrlC_AtPrompt()
    {
        using var m = WithSample();
        m.Machine.Console.PushText("DIR");
        m.Machine.Console.PushKey(KeyInput.CtrlC);
        m.WaitIdle();
        Assert.EndsWith("^C\r\n\r\nA>", m.Machine.Console.TakeOutput());
    }

    [Fact]
    public void LineEditing_F3RepeatsLastCommand()
    {
        using var m = WithSample();
        m.Run("VER");
        m.Machine.Console.PushKey(KeyInput.Special(DosKey.F3));
        m.Machine.Console.PushKey(KeyInput.Enter);
        m.WaitIdle();
        Assert.Equal("VER\r\r\n\r\nphi-DOS Version X.XX\r\n\r\nA>", m.Machine.Console.TakeOutput());
    }
}
