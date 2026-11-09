using System.Text;
using PhiDos.Core.Machine;
using PhiDos.Core.Storage;
using PhiDos.SystemDisk;

namespace PhiDos.Tests;

/// <summary>M6：重定向、管道、批处理。</summary>
public class BatchTests
{
    private static readonly DateTime T = TestMachine.FixedTime;

    private static MemoryStream Disk(params (string Name, string Text)[] files)
    {
        var ms = SystemPrograms.CreateSystemDisk(now: T);
        var d = new Fat12Drive(ms);
        foreach (var (name, text) in files)
        {
            var f = d.CreateFile([name], T);
            f.Write(0, Encoding.UTF8.GetBytes(text.Replace("\n", "\r\n")));
            f.Close(T);
        }
        d.Flush();
        ms.Position = 0;
        return ms;
    }

    private static TestMachine Boot(params (string, string)[] files) => new(m => m.MountImage('A', Disk(files)));

    private static string Type(TestMachine m, string file) => m.Run("TYPE " + file).Replace("\n\nA>", "").TrimEnd('\n');

    [Fact]
    public void OutputRedirection()
    {
        using var m = Boot();
        Assert.Equal("\nA>", m.Run("VER > V.TXT"));
        Assert.Equal("\nphi-DOS Version X.XX", Type(m, "V.TXT"));
        m.Run("ECHO second >> V.TXT");
        Assert.Equal("\nphi-DOS Version X.XX\nsecond ", Type(m, "V.TXT")); // 原版也会留下 > 前面的空格
        m.Run("ECHO replaced>V.TXT");
        Assert.Equal("replaced", Type(m, "V.TXT"));
    }

    [Fact]
    public void InputRedirection()
    {
        using var m = Boot(("WORDS.TXT", "pear\napple\nfig\n"));
        Assert.Equal("apple\nfig\npear\n\nA>", m.Run("SORT < WORDS.TXT"));
        Assert.Equal("File not found\n\nA>", m.Run("SORT < NOSUCH.TXT"));
    }

    [Fact]
    public void Pipes()
    {
        using var m = Boot(("WORDS.TXT", "pear\napple\nfig\n"));
        Assert.Equal("pear\nfig\napple\n\nA>", m.Run("TYPE WORDS.TXT | SORT /R"));
        Assert.Equal("apple\n\nA>", m.Run("TYPE WORDS.TXT | SORT | FIND \"pp\""));
        // 和原版一样：管道用的临时文件会出现在 DIR 的结果里，用完就删掉
        Assert.Contains("%PIPE1   $$$", m.Run("DIR | SORT"));
        Assert.DoesNotContain("%PIPE", m.Run("DIR"));
    }

    [Fact]
    public void Pipe_IntoMore_StillReadsKeyboard()
    {
        var lines = string.Join("\n", Enumerable.Range(1, 30).Select(i => "line " + i));
        using var m = Boot(("LONG.TXT", lines + "\n"));
        string first = m.Run("TYPE LONG.TXT | MORE");
        Assert.Contains("line 24\n", first);
        Assert.DoesNotContain("line 25", first);
        Assert.EndsWith("-- More --", first);
        string rest = m.Type(" ");
        Assert.Contains("line 30", rest);
        Assert.EndsWith("\nA>", rest);
    }

    [Fact]
    public void Batch_EchoAndParameters()
    {
        using var m = Boot(("GREET.BAT", "ECHO Hello, %1!\nREM %0 was called with %1 and %2\n"));
        Assert.Equal(
            "\nA>ECHO Hello, World!\nHello, World!\n" +
            "\nA>REM GREET was called with World and again\n" +
            "\nA>",
            m.Run("GREET World again"));
    }

    [Fact]
    public void Batch_EchoOff()
    {
        using var m = Boot(("QUIET.BAT", "ECHO OFF\nECHO only this\nREM hidden\n"));
        Assert.Equal("\nA>ECHO OFF\nonly this\n\nA>", m.Run("QUIET"));
        Assert.Equal("ECHO is on\n\nA>", m.Run("ECHO")); // 批处理结束后 ECHO 恢复
    }

    [Fact]
    public void Batch_GotoAndIf()
    {
        const string bat = """
            ECHO OFF
            IF "%1"=="" GOTO usage
            IF EXIST %1 GOTO found
            ECHO %1 is missing
            GOTO end
            :found
            ECHO %1 exists
            GOTO end
            :usage
            ECHO usage: CHECK file
            :end
            """;
        using var m = Boot(("CHECK.BAT", bat));
        Assert.Contains("usage: CHECK file", m.Run("CHECK"));
        Assert.Contains("FORMAT.COM exists", m.Run("CHECK FORMAT.COM"));
        Assert.Contains("NOPE.TXT is missing", m.Run("CHECK NOPE.TXT"));
    }

    [Fact]
    public void Batch_ErrorLevel()
    {
        const string bat = """
            ECHO OFF
            FIND "zzz" CHECK.BAT > NUL
            ECHO done
            FIND /X "zzz" CHECK.BAT
            IF ERRORLEVEL 2 ECHO bad switch
            IF NOT ERRORLEVEL 1 ECHO all fine
            """;
        using var m = Boot(("CHECK.BAT", bat));
        string output = m.Run("CHECK");
        Assert.Contains("done", output);
        Assert.Contains("FIND: Invalid Parameter X", output);
        Assert.Contains("bad switch", output);
        Assert.DoesNotContain("all fine", output);
    }

    [Fact]
    public void Batch_Shift()
    {
        using var m = Boot(("ARGS.BAT", "ECHO OFF\n:again\nIF \"%1\"==\"\" GOTO end\nECHO arg %1\nSHIFT\nGOTO again\n:end\n"));
        Assert.Equal("\nA>ECHO OFF\narg a\narg b\narg c\n\nA>", m.Run("ARGS a b c"));
    }

    [Fact]
    public void Batch_LabelNotFound()
    {
        using var m = Boot(("BAD.BAT", "GOTO nowhere\nECHO never\n"));
        Assert.Equal("\nA>GOTO nowhere\nLabel not found\n\nA>", m.Run("BAD"));
    }

    [Fact]
    public void Batch_EnvironmentVariables()
    {
        using var m = Boot(("ENV.BAT", "ECHO OFF\nECHO name=%NAME% pct=100%%\n"));
        m.Run("SET NAME=phi");
        Assert.Contains("name=phi pct=100%", m.Run("ENV"));
    }

    [Fact]
    public void Batch_ChainsToAnotherBatch()
    {
        using var m = Boot(("ONE.BAT", "ECHO OFF\nECHO in one\nTWO\nECHO never printed\n"), ("TWO.BAT", "ECHO in two\n"));
        string output = m.Run("ONE");
        Assert.Contains("in one", output);
        Assert.Contains("in two", output);
        Assert.DoesNotContain("never printed", output);
    }

    [Fact]
    public void For_AtCommandLineAndInBatch()
    {
        using var m = Boot(("A.TXT", "a"), ("B.TXT", "b"), ("LIST.BAT", "ECHO OFF\nFOR %%f IN (*.TXT) DO TYPE %%f\n"));
        Assert.Equal("x\ny\n\nA>", m.Run("FOR %c IN (x y) DO ECHO %c"));
        Assert.Contains("a", m.Run("LIST"));
        Assert.Equal("Syntax error\n\nA>", m.Run("FOR %c IN x DO ECHO"));
    }

    [Fact]
    public void Batch_CtrlCAsksToTerminate()
    {
        using var m = Boot(("WAIT.BAT", "ECHO OFF\nPAUSE\nECHO after pause\n"));
        Assert.EndsWith("Strike a key when ready . . . ", m.Run("WAIT"));
        m.Machine.Console.PushKey(KeyInput.CtrlC);
        m.WaitIdle();
        Assert.EndsWith("^C\r\nTerminate batch job (Y/N)? ", m.Machine.Console.TakeOutput());
        Assert.Equal("Y\n\nA>", m.Type("y"));
    }

    [Fact]
    public void Batch_CtrlC_No_Continues()
    {
        using var m = Boot(("WAIT.BAT", "ECHO OFF\nPAUSE\nECHO after pause\n"));
        m.Run("WAIT");
        m.Machine.Console.PushKey(KeyInput.CtrlC);
        m.WaitIdle();
        Assert.Contains("after pause", m.Type("n"));
    }

    [Fact]
    public void Autoexec_RunsAtBootInsteadOfDatePrompt()
    {
        using var m = new TestMachine(
            mm => mm.MountImage('A', Disk(("AUTOEXEC.BAT", "ECHO OFF\nPROMPT $p$g\nECHO Welcome!\n"))),
            new MachineOptions { Clock = () => T, DefaultPrompt = "" });
        Assert.Equal("phi-DOS Version X.XX\r\n\r\nA>ECHO OFF\r\nWelcome!\r\n\r\nA:\\>", m.BootOutput);
    }

    [Fact]
    public void BatchFile_RemovedWhileRunning()
    {
        using var m = Boot(("SELF.BAT", "ECHO OFF\nDEL SELF.BAT\nECHO still here?\n"));
        Assert.Equal("\nA>ECHO OFF\n\nBatch file missing\n\nA>", m.Run("SELF"));
    }
}
