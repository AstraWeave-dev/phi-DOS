using PhiDos.Core;
using PhiDos.Core.Machine;

namespace PhiDos.Tests;

public class BootTests
{
    [Fact]
    public void Boot_ShowsBannerAndPrompt()
    {
        using var m = new TestMachine();
        var lines = m.Machine.Console.Screen.GetLines();
        Assert.Equal("phi-DOS Version X.XX", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Equal("A>", lines[2]);
        Assert.Equal(PhiDosInfo.Banner + "\r\n\r\nA>", m.BootOutput);
    }

    [Fact]
    public void EmptyLine_PrintsPromptAgainWithoutBlankLine()
    {
        using var m = new TestMachine();
        Assert.Equal("A>", m.Run(""));
    }

    [Fact]
    public void Exit_PowersOffWhenConfigured()
    {
        using var m = new TestMachine(options: new() { PromptForDateTimeAtBoot = false, ExitPowersOff = true });
        m.Machine.Console.PushText("EXIT\n");
        Assert.True(m.Machine.WaitForExit(TimeSpan.FromSeconds(5)));
        Assert.Equal(Core.Machine.MachineState.Stopped, m.Machine.State);
    }

    [Fact]
    public void DefaultPrompt_ShowsDriveAndDirectory()
    {
        using var m = new TestMachine(
            mm => mm.MountImage('A', ShellReadTests.SampleDisk()),
            new MachineOptions { PromptForDateTimeAtBoot = false, Clock = () => TestMachine.FixedTime });
        Assert.EndsWith("\r\n\r\nA:\\>", m.BootOutput);
        m.Run("CD GAMES");
        Assert.Equal("\nA:\\GAMES>", m.Run("REM"));
        // 切到没插盘的 B: 时提示符不会去读盘，所以不会弹出 Not ready
        Assert.Equal("\nB:\\>", m.Run("B:"));
        // 自己设置的 PROMPT 优先；清空后回到默认值
        m.Run("PROMPT $N$G");
        Assert.Equal("\nB>", m.Run("REM"));
        m.Run("PROMPT");
        Assert.Equal("\nB:\\>", m.Run("REM"));
    }

    [Fact]
    public void EmptyDefaultPrompt_IsMsDos20Style()
    {
        using var m = new TestMachine(options: new() { PromptForDateTimeAtBoot = false, DefaultPrompt = "" });
        Assert.EndsWith("\r\n\r\nA>", m.BootOutput);
    }

    [Fact]
    public void Reset_RebootsTheMachine()
    {
        using var m = new TestMachine();
        m.Machine.Reset();
        m.WaitIdle();
        Assert.Equal("phi-DOS Version X.XX", m.Machine.Console.Screen.GetLine(0));
    }
}
