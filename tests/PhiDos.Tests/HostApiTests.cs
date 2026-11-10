using System.Text;
using PhiDos.Abi;
using PhiDos.Core.Machine;
using PhiDos.Core.Storage;
using PhiDos.SystemDisk;

namespace PhiDos.Tests;

/// <summary>给游戏宿主用的接口：存档导出、游戏代码直接读写虚拟磁盘、呼叫宿主。</summary>
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

    // ───────── 呼叫宿主（IDos.CallHost） ─────────

    /// <summary>
    /// 测试用的真程序：CALL 名字 [参数...] 调用宿主并显示回答。
    /// 第一个参数是 /C 时自己捕获 DosException 并显示错误码，否则不捕获。
    /// </summary>
    private const string CallProgram = """
        public class Call : IPhiProgram
        {
            public int Run(IDos dos, string[] args)
            {
                bool catchErrors = args.Length > 0 && args[0] == "/C";
                int first = catchErrors ? 1 : 0;
                string name = args.Length > first ? args[first] : "";
                string argument = args.Length > first + 1 ? string.Join(" ", args, first + 1, args.Length - first - 1) : "";
                if (!catchErrors)
                {
                    dos.WriteLine("[" + dos.CallHost(name, argument) + "]");
                    return 0;
                }
                try
                {
                    dos.WriteLine("[" + dos.CallHost(name, argument) + "]");
                    return 0;
                }
                catch (DosException e)
                {
                    dos.WriteLine("error " + e.Error + ": " + e.Message);
                    return 1;
                }
            }
        }
        """;

    /// <summary>开一台机器：A 盘是系统盘，上面放着编译好的 CALL.COM，登记给定的宿主功能。</summary>
    private static TestMachine HostMachine(Dictionary<string, Func<string, string?>> calls)
    {
        var disk = SystemPrograms.CreateSystemDisk(now: TestMachine.FixedTime);
        var d = new Fat12Drive(disk);
        var f = d.CreateFile(["CALL.COM"], TestMachine.FixedTime);
        f.Write(0, ProgramCompiler.Compile(CallProgram, "CALL"));
        f.Close(TestMachine.FixedTime);
        d.Flush();
        disk.Position = 0;
        return new TestMachine(m => m.MountImage('A', disk), new MachineOptions
        {
            PromptForDateTimeAtBoot = false,
            DefaultPrompt = "",
            Clock = () => TestMachine.FixedTime,
            HostCalls = calls,
            // VerifyPrograms 保持默认的 true：CALL.COM 要通过加载检查
        });
    }

    [Fact]
    public void CallHost_ReturnsTheAnswer()
    {
        using var m = HostMachine(new() { ["PING"] = _ => "pong" });
        Assert.Equal("[pong]\n\nA>", m.Run("CALL PING"));
    }

    [Fact]
    public void CallHost_PassesTheArgumentUnchanged()
    {
        string? received = null;
        using var m = HostMachine(new() { ["SAY"] = a => { received = a; return "ok"; } });
        Assert.Equal("[ok]\n\nA>", m.Run("CALL SAY 前往 第二章"));
        Assert.Equal("前往 第二章", received);
    }

    [Fact]
    public void CallHost_NameIsCaseInsensitive()
    {
        // 字典本身区分大小写也一样能找到
        using var m = HostMachine(new Dictionary<string, Func<string, string?>>(StringComparer.Ordinal)
        {
            ["SwitchScene"] = a => "to " + a,
        });
        Assert.Equal("[to ending]\n\nA>", m.Run("CALL switchscene ending"));
        Assert.Equal("[to ending]\n\nA>", m.Run("CALL SWITCHSCENE ending"));
    }

    [Fact]
    public void CallHost_NullAnswerBecomesEmpty()
    {
        using var m = HostMachine(new() { ["QUIET"] = _ => null });
        Assert.Equal("[]\n\nA>", m.Run("CALL QUIET"));
    }

    [Fact]
    public void CallHost_UnknownName_IsInvalidFunction()
    {
        using var m = HostMachine(new() { ["PING"] = _ => "pong" });
        Assert.Equal("error InvalidFunction: Invalid function\n\nA>", m.Run("CALL /C NOSUCH"));
        // 程序不捕获时，按现有规则显示 Abnormal program termination
        Assert.Equal("\nAbnormal program termination\n  DosException: Invalid function\n\nA>", m.Run("CALL NOSUCH"));
        // 空名字也一样
        Assert.Equal("error InvalidFunction: Invalid function\n\nA>", m.Run("CALL /C"));
    }

    [Fact]
    public void CallHost_WorksFromAProgramLoadedFromDisk()
    {
        using var m = HostMachine(new() { ["PING"] = _ => "pong" });
        Assert.Contains("CALL     COM", m.Run("DIR CALL.COM"));
        Assert.True(m.Machine.Options.VerifyPrograms);
        Assert.Equal("[pong]\n\nA>", m.Run("CALL PING"));
    }

    [Fact]
    public void CallHost_HandlerException_BecomesHostError()
    {
        using var m = HostMachine(new() { ["BOOM"] = _ => throw new InvalidOperationException("scene not found") });
        Assert.Equal("error HostError: Host function failed: scene not found\n\nA>", m.Run("CALL /C BOOM"));
        // 虚拟机没有受影响，下一条命令照常执行
        Assert.Equal("\nphi-DOS Version X.XX\n\nA>", m.Run("VER"));
        Assert.Equal("\nAbnormal program termination\n  DosException: Host function failed: scene not found\n\nA>", m.Run("CALL BOOM"));
        Assert.Equal("\nphi-DOS Version X.XX\n\nA>", m.Run("VER"));
    }

    [Fact]
    public void CallHost_HandlerDosException_IsPassedThrough()
    {
        using var m = HostMachine(new() { ["LOOKUP"] = _ => throw new DosException(DosError.FileNotFound) });
        Assert.Equal("error FileNotFound: File not found\n\nA>", m.Run("CALL /C LOOKUP"));
    }

    [Fact]
    public void CallHost_ResetDuringHandler_RebootsCleanly()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var m = HostMachine(new()
        {
            ["WAIT"] = _ =>
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(30));
                return "too late";
            },
        });

        m.Machine.Console.PushText("CALL WAIT\n");
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        m.Machine.Reset();
        m.WaitIdle();
        Assert.Equal("phi-DOS Version X.XX", m.Machine.Console.Screen.GetLine(0));
        Assert.Equal("\nphi-DOS Version X.XX\n\nA>", m.Run("VER"));

        // 旧线程里的处理函数这时才返回：它的回答不能再出现在新开机的屏幕上
        release.Set();
        Thread.Sleep(300);
        Assert.DoesNotContain("too late", string.Join("\n", m.Machine.Console.Screen.GetLines()));
        Assert.Equal("\nphi-DOS Version X.XX\n\nA>", m.Run("VER"));
    }

    [Fact]
    public void OldProgramsWithoutCallHost_StillRun()
    {
        // 不用 CallHost 的程序（比如系统自带的 ILDOSVER）照常运行
        using var m = HostMachine(new());
        Assert.Contains("phi-DOS X.XX", m.Run("ILDOSVER"));
    }
}
