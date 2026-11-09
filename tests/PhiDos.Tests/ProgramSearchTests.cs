using PhiDos.Abi;
using PhiDos.Core.Machine;

namespace PhiDos.Tests;

/// <summary>M4：程序查找与内置程序表。</summary>
public class ProgramSearchTests
{
    private sealed class Hello : IPhiProgram
    {
        public int Run(IDos dos, string[] args)
        {
            dos.WriteLine("Hello from HELLO, args=[" + string.Join("|", args) + "]");
            return args.Length;
        }
    }

    private sealed class Other : IPhiProgram
    {
        public int Run(IDos dos, string[] args)
        {
            dos.WriteLine("OTHER sees GREETING=" + (dos.GetEnv("greeting") ?? "(none)"));
            return 0;
        }
    }

    private sealed class Caller : IPhiProgram
    {
        public int Run(IDos dos, string[] args)
        {
            int code = dos.Exec("HELLO", "x y z");
            dos.WriteLine("HELLO returned " + code);
            return 0;
        }
    }

    private sealed class Crash : IPhiProgram
    {
        public int Run(IDos dos, string[] args) => throw new InvalidOperationException("boom");
    }

    private sealed class WaitKey : IPhiProgram
    {
        public int Run(IDos dos, string[] args)
        {
            dos.Write("press a key: ");
            char c = dos.ReadKey();
            dos.WriteLine($" got {c}");
            return 0;
        }
    }

    private static TestMachine NewMachine() => new(
        m => m.MountImage('A', ShellReadTests.SampleDisk()),
        new MachineOptions
        {
            PromptForDateTimeAtBoot = false,
            Clock = () => TestMachine.FixedTime,
            DefaultPrompt = "",
            BuiltinPrograms = new Dictionary<string, Func<IPhiProgram>>(StringComparer.OrdinalIgnoreCase)
            {
                ["HELLO"] = () => new Hello(),
                ["OTHER"] = () => new Other(),
                ["CALLER"] = () => new Caller(),
                ["CRASH"] = () => new Crash(),
                ["WAITKEY"] = () => new WaitKey(),
            },
        });

    /// <summary>在盘上放一个占位文件，内置程序表要求文件必须存在。</summary>
    private static void Touch(TestMachine m, string name) => m.Run($"COPY NOTES {name}");

    [Fact]
    public void BuiltinProgram_NeedsAFileOnDisk()
    {
        using var m = NewMachine();
        Assert.Equal("Bad command or file name\n\nA>", m.Run("HELLO"));
        Touch(m, "HELLO.COM");
        Assert.Equal("Hello from HELLO, args=[a|b]\n\nA>", m.Run("hello a b"));
        m.Run("DEL HELLO.COM");
        Assert.Equal("Bad command or file name\n\nA>", m.Run("HELLO"));
    }

    [Fact]
    public void ExtensionsAndOrder()
    {
        using var m = NewMachine();
        Touch(m, "HELLO.EXE");
        Assert.StartsWith("Hello from HELLO", m.Run("HELLO"));
        Assert.StartsWith("Hello from HELLO", m.Run("HELLO.EXE"));
        Assert.Equal("Bad command or file name\n\nA>", m.Run("HELLO.COM"));
        Assert.Equal("Bad command or file name\n\nA>", m.Run("NOTES"));       // 没有可执行的扩展名
        Assert.Equal("Bad command or file name\n\nA>", m.Run("README.TXT"));
    }

    [Fact]
    public void PathSearch()
    {
        using var m = NewMachine();
        m.Run("MD BIN");
        Touch(m, "BIN\\HELLO.COM");
        Assert.Equal("Bad command or file name\n\nA>", m.Run("HELLO"));
        m.Run("PATH \\BIN");
        Assert.Equal("PATH=\\BIN\n\nA>", m.Run("PATH"));
        Assert.StartsWith("Hello from HELLO", m.Run("HELLO"));
        m.Run("CD GAMES");
        Assert.StartsWith("Hello from HELLO", m.Run("HELLO"));
        Assert.StartsWith("Hello from HELLO", m.Run("\\BIN\\HELLO"));
        m.Run("PATH ;");
        Assert.Equal("No Path\n\nA>", m.Run("PATH"));
        Assert.Equal("Bad command or file name\n\nA>", m.Run("HELLO"));
    }

    [Fact]
    public void PathWithBadDrive_Warns()
    {
        using var m = NewMachine();
        m.Run("PATH Q:\\BIN");
        Assert.Equal("Invalid drive in search path\nBad command or file name\n\nA>", m.Run("HELLO"));
    }

    [Fact]
    public void ChildGetsCopyOfEnvironment()
    {
        using var m = NewMachine();
        Touch(m, "OTHER.COM");
        Assert.StartsWith("OTHER sees GREETING=(none)", m.Run("OTHER"));
        m.Run("SET GREETING=hi there");
        Assert.StartsWith("OTHER sees GREETING=hi there", m.Run("OTHER"));
        Assert.Contains("GREETING=hi there", m.Run("SET"));
    }

    [Fact]
    public void Set_OutOfEnvironmentSpace()
    {
        using var m = NewMachine();
        Assert.Equal("\nA>", m.Run("SET A=" + new string('x', 100)));
        Assert.Equal("Out of environment space\n\nA>", m.Run("SET B=" + new string('x', 100)));
    }

    [Fact]
    public void ProgramCanRunAnotherProgram()
    {
        using var m = NewMachine();
        Touch(m, "HELLO.COM");
        Touch(m, "CALLER.COM");
        Assert.Equal("Hello from HELLO, args=[x|y|z]\nHELLO returned 3\n\nA>", m.Run("CALLER"));
    }

    [Fact]
    public void UnhandledException_IsReported()
    {
        using var m = NewMachine();
        Touch(m, "CRASH.COM");
        Assert.Equal("\nAbnormal program termination\n  InvalidOperationException: boom\n\nA>", m.Run("CRASH"));
    }

    [Fact]
    public void CtrlC_TerminatesProgram()
    {
        using var m = NewMachine();
        Touch(m, "WAITKEY.COM");
        Assert.Equal("press a key: ", m.Run("WAITKEY"));
        m.Machine.Console.PushKey(KeyInput.CtrlC);
        m.WaitIdle();
        Assert.Equal("^C\r\n\r\nA>", m.Machine.Console.TakeOutput());
        m.Run("WAITKEY");
        Assert.Equal("q got q\n\nA>", m.Type("q"));
    }

    [Fact]
    public void Prompt_MetaStrings()
    {
        using var m = NewMachine();
        m.Run("PROMPT $p$g");
        Assert.Equal("\nA:\\>", m.Run("REM"));
        m.Run("CD GAMES");
        Assert.Equal("\nA:\\GAMES>", m.Run("REM"));
        m.Run("PROMPT [$n$q$d]$_$t $$");
        Assert.Equal("\n[A=Thu 10-20-1983]\n12:00:00.00 $", m.Run("REM"));
        m.Run("PROMPT");
        Assert.Equal("\nA>", m.Run("REM"));
    }
}
