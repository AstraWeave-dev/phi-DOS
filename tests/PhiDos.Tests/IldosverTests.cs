using PhiDos.SystemDisk;

namespace PhiDos.Tests;

public class IldosverTests
{
    [Fact]
    public void ShowsColouredLogoAndInfo()
    {
        using var m = new TestMachine(mm => mm.MountImage('A', SystemPrograms.CreateSystemDisk()));
        m.Run("SET USER=astro");
        m.Run("CLS");
        m.Run("ILDOSVER");
        var screen = m.Machine.Console.Screen;
        var lines = screen.GetLines();

        int top = Array.FindIndex(lines, l => l.Contains("astro@phi-DOS"));
        Assert.True(top >= 0, string.Join("\n", lines));
        Assert.Contains("OS        phi-DOS X.XX", lines[top + 2]);
        Assert.Contains("CPU       .NET", lines[top + 4]);
        Assert.Contains("Language  IL", lines[top + 5]);
        Assert.Contains("A: virtual disk", lines[top + 8]);
        Assert.Contains("UTF-8 中文", lines[top + 10]);

        // 图标第一行是亮青色（0x0B），字段名是亮黄色（0x0E），值是默认的灰色（0x07）
        Assert.Equal(0x0B, screen.GetCell(8, top).Attribute);
        Assert.Equal((int)'█', screen.GetCell(8, top).Rune.Value);
        Assert.Equal(0x0E, screen.GetCell(26, top + 2).Attribute);
        Assert.Equal(0x07, screen.GetCell(36, top + 2).Attribute);
        // 色板：第一行的红色块（0x04），第二行的亮红色块（0x0C）
        Assert.Equal(0x04, screen.GetCell(29, top + 12).Attribute);
        Assert.Equal(0x0C, screen.GetCell(29, top + 13).Attribute);
        // 结束后颜色恢复默认
        Assert.Equal(0x07, screen.GetCell(0, screen.CursorY).Attribute);
    }
}
