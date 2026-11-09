using PhiDos.Core.Machine;
using PhiDos.Core.Storage;
using PhiDos.SystemDisk;

namespace PhiDos.Tests;

/// <summary>M5：IL 程序加载器。</summary>
public class LoaderTests
{
    private static readonly DateTime T = TestMachine.FixedTime;

    /// <summary>新建一张系统盘，并放进额外的程序。</summary>
    private static MemoryStream Disk(params (string Name, byte[] Bytes)[] programs)
    {
        var ms = SystemPrograms.CreateSystemDisk(now: T);
        var d = new Fat12Drive(ms);
        foreach (var (name, bytes) in programs)
        {
            var f = d.CreateFile([name], T);
            f.Write(0, bytes);
            f.Close(T);
        }
        d.Flush();
        ms.Position = 0;
        return ms;
    }

    private static TestMachine Boot(params (string, byte[])[] programs)
        => new(m => m.MountImage('A', Disk(programs)));

    private const string Hello = """
        public class Hello : IPhiProgram
        {
            public int Run(IDos dos, string[] args)
            {
                dos.WriteLine("Hello from an IL program!");
                dos.WriteLine("args=" + string.Join(",", args) + " tail=[" + dos.CommandTail + "]");
                return 7;
            }
        }
        """;

    [Fact]
    public void ProgramOnDisk_Runs()
    {
        using var m = Boot(("HELLO.COM", ProgramCompiler.Compile(Hello)));
        Assert.Equal("Hello from an IL program!\nargs=a,b tail=[a  b]\n\nA>", m.Run("HELLO a  b"));
    }

    [Fact]
    public void SystemDisk_ContainsPrograms()
    {
        using var m = Boot();
        string dir = m.Run("DIR");
        Assert.Contains("FORMAT   COM", dir);
        Assert.Contains("MORE     COM", dir);
        Assert.Contains("SORT     EXE", dir);
        Assert.Contains("FIND     EXE", dir);
    }

    [Fact]
    public void NonIlFile_IsRejected()
    {
        using var m = Boot(("JUNK.COM", [0xCD, 0x20, 0x90, 0x90]));
        Assert.Equal("Error in EXE file\n  not a .NET assembly\n\nA>", m.Run("JUNK"));
    }

    [Fact]
    public void CorruptAssembly_DoesNotBreakTheMachine()
    {
        byte[] good = ProgramCompiler.Compile(Hello);
        using var m = Boot(("BROKEN.COM", good[..(good.Length / 2)]));
        Assert.StartsWith("Error in EXE file\n", m.Run("BROKEN"));
        Assert.Equal("\nphi-DOS Version X.XX\n\nA>", m.Run("VER"));
    }

    [Fact]
    public void ForbiddenApi_IsRejected()
    {
        const string evil = """
            public class Evil : IPhiProgram
            {
                public int Run(IDos dos, string[] args)
                {
                    System.IO.File.Delete("/etc/passwd");
                    return 0;
                }
            }
            """;
        using var m = Boot(("EVIL.COM", ProgramCompiler.Compile(evil)));
        Assert.Equal("Error in EXE file\n  forbidden API: System.IO.File\n\nA>", m.Run("EVIL"));
    }

    [Fact]
    public void EnvironmentExit_IsRejected()
    {
        const string quit = """
            public class Quit : IPhiProgram
            {
                public int Run(IDos dos, string[] args) { Environment.Exit(1); return 0; }
            }
            """;
        using var m = Boot(("QUIT.COM", ProgramCompiler.Compile(quit)));
        Assert.Equal("Error in EXE file\n  forbidden API: System.Environment.Exit\n\nA>", m.Run("QUIT"));
    }

    [Fact]
    public void Iterators_AreAllowed()
    {
        // yield 生成的代码会用到 Environment.CurrentManagedThreadId，不应被误伤
        const string iter = """
            public class Iter : IPhiProgram
            {
                static System.Collections.Generic.IEnumerable<int> Numbers() { yield return 1; yield return 2; }
                public int Run(IDos dos, string[] args)
                {
                    foreach (int n in Numbers()) dos.Write(n.ToString());
                    dos.WriteLine();
                    return 0;
                }
            }
            """;
        using var m = Boot(("ITER.COM", ProgramCompiler.Compile(iter)));
        Assert.Equal("12\n\nA>", m.Run("ITER"));
    }

    [Fact]
    public void MissingEntryClass_IsRejected()
    {
        using var m = Boot(("NONE.COM", ProgramCompiler.Compile("public class Nothing { }")));
        Assert.Equal("Error in EXE file\n  no public class implements IPhiProgram\n\nA>", m.Run("NONE"));
    }

    private const string Loop = """
        public class Loop : IPhiProgram
        {
            public int Run(IDos dos, string[] args)
            {
                dos.WriteLine("looping");
                long n = 0;
                while (true) { n++; }
            }
        }
        """;

    [Fact]
    public void InfiniteLoop_CtrlCStopsItWhenBreakIsOn()
    {
        using var m = Boot(("LOOP.COM", ProgramCompiler.Compile(Loop)));
        m.Run("BREAK ON");
        m.Machine.Console.TakeOutput();
        m.Machine.Console.PushText("LOOP\n");
        Thread.Sleep(300);
        Assert.False(m.Machine.Console.IsWaitingForInput);
        m.Machine.Console.PushKey(KeyInput.CtrlC);
        m.WaitIdle();
        Assert.EndsWith("looping\r\n\r\nA>", m.Machine.Console.TakeOutput());
    }

    [Fact]
    public void InfiniteLoop_ResetAlwaysWorks()
    {
        using var m = Boot(("LOOP.COM", ProgramCompiler.Compile(Loop)));
        m.Machine.Console.PushText("LOOP\n");
        Thread.Sleep(300);
        m.Machine.Console.PushKey(KeyInput.CtrlC); // BREAK OFF：纯计算的循环不理 Ctrl+C
        Thread.Sleep(300);
        Assert.False(m.Machine.Console.IsWaitingForInput);
        m.Machine.Reset();
        m.WaitIdle();
        Assert.Equal("phi-DOS Version X.XX", m.Machine.Console.Screen.GetLine(0));
        Assert.Equal("\nphi-DOS Version X.XX\n\nA>", m.Run("VER"));
    }

    [Fact]
    public void ProgramThatSwallowsExceptions_IsFrozenOnReset()
    {
        const string stubborn = """
            public class Stubborn : IPhiProgram
            {
                public int Run(IDos dos, string[] args)
                {
                    while (true)
                    {
                        try { while (true) { } }
                        catch { }
                    }
                }
            }
            """;
        using var m = Boot(("STUBBORN.COM", ProgramCompiler.Compile(stubborn)));
        m.Machine.Console.PushText("STUBBORN\n");
        Thread.Sleep(300);
        m.Machine.Reset();
        m.WaitIdle();
        Assert.Equal("\nphi-DOS Version X.XX\n\nA>", m.Run("VER"));
    }

    [Fact]
    public void InfiniteRecursion_DoesNotCrashTheHost()
    {
        const string recurse = """
            public class Recurse : IPhiProgram
            {
                static int Down(int n) => Down(n + 1) + 1;
                public int Run(IDos dos, string[] args) => Down(0);
            }
            """;
        using var m = Boot(("RECURSE.COM", ProgramCompiler.Compile(recurse)));
        string output = m.Run("RECURSE");
        Assert.StartsWith("\nAbnormal program termination\n  InsufficientExecutionStackException: ", output);
        Assert.EndsWith("\n\nA>", output);
        Assert.Equal("\nphi-DOS Version X.XX\n\nA>", m.Run("VER")); // 机器还活着
    }

    [Fact]
    public void ProgramsAreUnloadedAfterRunning()
    {
        using var m = Boot(("HELLO.COM", ProgramCompiler.Compile(Hello)));
        for (int i = 0; i < 30; i++) Assert.StartsWith("Hello from", m.Run("HELLO"));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        int loaded = System.Runtime.Loader.AssemblyLoadContext.All.Count(c => c.Name?.StartsWith("phi-DOS ") == true);
        Assert.True(loaded <= 2, $"{loaded} program load contexts are still alive");
    }

    [Fact]
    public void Format_ErasesAVirtualDisk()
    {
        using var m = new TestMachine(mm =>
        {
            mm.MountImage('A', Disk());
            mm.MountImage('B', ShellReadTests.SampleDisk("OLD"));
        });
        Assert.Equal("Insert new diskette for drive B:\nand strike any key when ready", m.Run("FORMAT B: /V"));
        Assert.Equal("\n\nFormatting...Format complete\nVolume label (11 characters, ENTER for none)? ", m.Type("x"));
        Assert.Equal("\n   362496 bytes total disk space\n   362496 bytes available on disk\n\nFormat another (Y/N)?",
            m.Run("STORY DISK"));
        Assert.Equal("n\n\nA>", m.Type("n"));
        Assert.Equal("\n Volume in drive B is STORY DISK\n\nA>", m.Run("VOL B:"));
        Assert.EndsWith("File not found\n\nA>", m.Run("DIR B:"));
    }

    [Fact]
    public void Format_RefusesFolderDrives()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"phidos-fmt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "KEEP.TXT"), "do not erase");
        try
        {
            using var m = new TestMachine(mm =>
            {
                mm.MountImage('A', Disk());
                mm.MountFolder('B', dir);
            });
            m.Run("FORMAT B:");
            Assert.Equal("\n\nFormatting...\nFormat failure\n\n\nA>", m.Type("x"));
            Assert.True(File.Exists(Path.Combine(dir, "KEEP.TXT")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Chkdsk_IsNotProvided()
    {
        using var m = Boot();
        Assert.Equal("Bad command or file name\n\nA>", m.Run("CHKDSK"));
    }
}
