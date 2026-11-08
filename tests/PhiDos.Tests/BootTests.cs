using PhiDos.Core;

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
        Assert.Equal("\nA>", m.Run(""));
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
    public void Reset_RebootsTheMachine()
    {
        using var m = new TestMachine();
        m.Machine.Reset();
        m.WaitIdle();
        Assert.Equal("phi-DOS Version X.XX", m.Machine.Console.Screen.GetLine(0));
    }
}
