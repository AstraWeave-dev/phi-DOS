using System.Text;
using PhiDos.Core.Machine;
using PhiDos.Core.Storage;
using PhiDos.SystemDisk;

namespace PhiDos.Tests;

/// <summary>VI 编辑器。通过 80×25 屏幕上显示的内容和保存下来的文件来检查。</summary>
public sealed class ViTests : IDisposable
{
    private readonly MemoryStream _disk;
    private readonly TestMachine _m;

    public ViTests()
    {
        _disk = SystemPrograms.CreateSystemDisk(now: TestMachine.FixedTime);
        var d = new Fat12Drive(_disk);
        var f = d.CreateFile(["POEM.TXT"], TestMachine.FixedTime);
        f.Write(0, Encoding.UTF8.GetBytes("roses are red\r\nviolets are blue\r\n星星在天上\r\n"));
        f.Close(TestMachine.FixedTime);
        d.Flush();
        _disk.Position = 0;
        _m = new TestMachine(m => m.MountImage('A', _disk));
    }

    public void Dispose() => _m.Dispose();

    private TextScreen Screen => _m.Machine.Console.Screen;
    private string Line(int y) => Screen.GetLine(y);
    private string Status => Screen.GetLine(24);

    /// <summary>送入按键，等 vi 处理完。</summary>
    private void Keys(string text) => _m.Type(text);

    private void Key(DosKey key)
    {
        _m.Machine.Console.PushKey(KeyInput.Special(key));
        _m.WaitIdle();
    }

    private string FileText(string name)
    {
        string output = _m.Run("TYPE " + name);
        return output[..output.LastIndexOf("\n\nA>", StringComparison.Ordinal)];
    }

    [Fact]
    public void OpensFile_AndShowsTildesForEmptyLines()
    {
        _m.Run("VI POEM.TXT");
        Assert.Equal("roses are red", Line(0));
        Assert.Equal("violets are blue", Line(1));
        Assert.Equal("星星在天上", Line(2));
        Assert.Equal("~", Line(3));
        Assert.Equal("~", Line(23));
        Assert.Equal("\"POEM.TXT\" 3 lines, 37 characters", Status);
        Assert.Equal((0, 0), (Screen.CursorX, Screen.CursorY));
    }

    [Fact]
    public void NewFile_InsertAndSave()
    {
        _m.Run("VI NOTE.TXT");
        Assert.Equal("\"NOTE.TXT\" [New file]", Status);
        Keys("iHello\n中文 too");
        Assert.Equal("-- INSERT --", Status);
        Keys("\u001b:wq\n");
        Assert.Equal("Hello\n中文 too", FileText("NOTE.TXT"));
    }

    [Fact]
    public void QuitWithUnsavedChanges_IsRefused()
    {
        _m.Run("VI POEM.TXT");
        Keys("x:q\n");
        Assert.Equal("No write since last change (use ! to override)", Status);
        Keys(":q!\n");
        Assert.StartsWith("roses", FileText("POEM.TXT")); // 没有保存
    }

    [Fact]
    public void Movement_HjklWordAndLineEnds()
    {
        _m.Run("VI POEM.TXT");
        Keys("w");
        Assert.Equal((6, 0), (Screen.CursorX, Screen.CursorY)); // "are"
        Keys("$");
        Assert.Equal(12, Screen.CursorX);
        Keys("j");
        Assert.Equal((15, 1), (Screen.CursorX, Screen.CursorY)); // 行尾一直保持在行尾
        Keys("0b");
        Assert.Equal((10, 0), (Screen.CursorX, Screen.CursorY)); // 上一行的 "red"
        Keys("jj");
        Assert.Equal((8, 2), (Screen.CursorX, Screen.CursorY)); // 想保持第 10 列，这一行只有 5 个字，停在最后一个字上
        Keys("0ll");
        Assert.Equal(4, Screen.CursorX); // 每个汉字占两格
        Key(DosKey.Up);
        Assert.Equal(1, Screen.CursorY);
        Keys("G");
        Assert.Equal(2, Screen.CursorY);
        Keys("gg");
        Assert.Equal(0, Screen.CursorY);
        Keys("2G");
        Assert.Equal(1, Screen.CursorY);
    }

    [Fact]
    public void DeleteYankPutAndUndo()
    {
        _m.Run("VI POEM.TXT");
        Keys("dd");
        Assert.Equal("violets are blue", Line(0));
        Keys("p");
        Assert.Equal("violets are blue", Line(0));
        Assert.Equal("roses are red", Line(1));
        Keys("yyP");
        Assert.Equal("roses are red", Line(1));
        Assert.Equal("roses are red", Line(2));
        Keys("u");
        Assert.Equal("星星在天上", Line(2));
        Keys("u"); // 再按一次就重做，和原版 vi 一样
        Assert.Equal("roses are red", Line(2));
        Keys("xx");
        Assert.Equal("ses are red", Line(1));
    }

    [Fact]
    public void OpenLineAboveAndBelow_AndAppend()
    {
        _m.Run("VI POEM.TXT");
        Keys("oline two\u001bOline one\u001bAX\u001bIY\u001b:x\n");
        Assert.Equal("roses are red\nYline oneX\nline two\nviolets are blue\n星星在天上", FileText("POEM.TXT"));
    }

    [Fact]
    public void Search_ForwardBackwardAndWrap()
    {
        _m.Run("VI POEM.TXT");
        Keys("/are\n");
        Assert.Equal((6, 0), (Screen.CursorX, Screen.CursorY));
        Keys("n");
        Assert.Equal((8, 1), (Screen.CursorX, Screen.CursorY));
        Keys("n");
        Assert.Equal((6, 0), (Screen.CursorX, Screen.CursorY));
        Assert.Equal("search hit BOTTOM, continuing at TOP", Status);
        Keys("N");
        Assert.Equal((8, 1), (Screen.CursorX, Screen.CursorY));
        Keys("/天\n");
        Assert.Equal((6, 2), (Screen.CursorX, Screen.CursorY));
        Keys("/zebra\n");
        Assert.Equal("Pattern not found: zebra", Status);
    }

    [Fact]
    public void ColonCommands()
    {
        _m.Run("VI POEM.TXT");
        Keys(":3\n");
        Assert.Equal(2, Screen.CursorY);
        Keys(":frob\n");
        Assert.Equal("Not an editor command: frob", Status);
        Keys(":w COPY.TXT\n");
        Assert.Equal("\"COPY.TXT\" 3 lines, 37 characters written", Status);
        Keys("ZZ");
        Assert.Equal("roses are red\nviolets are blue\n星星在天上", FileText("COPY.TXT"));
    }

    [Fact]
    public void BackspaceAtLineStart_JoinsLines()
    {
        _m.Run("VI POEM.TXT");
        Keys("ji\b\u001b:wq\n");
        Assert.StartsWith("roses are redviolets are blue\n", FileText("POEM.TXT"));
    }

    [Fact]
    public void LongFile_Scrolls()
    {
        _m.Run("VI LONG.TXT");
        var sb = new StringBuilder("i");
        for (int i = 1; i <= 40; i++) sb.Append("line ").Append(i).Append('\n');
        Keys(sb.ToString() + "\u001b");
        Assert.Equal("line 40", Line(22));
        Keys("gg");
        Assert.Equal("line 1", Line(0));
        Assert.Equal("line 24", Line(23));
        Key(DosKey.PageDown);
        Assert.Equal("line 23", Line(0));
    }

    [Fact]
    public void LongLine_Wraps()
    {
        _m.Run("VI WIDE.TXT");
        Keys("i" + new string('a', 85) + "\u001b");
        Assert.Equal(new string('a', 80), Line(0));
        Assert.Equal("aaaaa", Line(1));
        Assert.Equal((4, 1), (Screen.CursorX, Screen.CursorY));
    }

    [Fact]
    public void Exit_ClearsTheScreen()
    {
        _m.Run("VI POEM.TXT");
        Keys(":q\n");
        Assert.Equal("", Line(0).Trim());
        Assert.Equal("A>", Screen.GetLines().First(l => l.Length > 0));
    }

    [Fact]
    public void CtrlC_LeavesTheEditorCleanly()
    {
        _m.Run("VI POEM.TXT");
        _m.Machine.Console.PushKey(KeyInput.CtrlC);
        _m.WaitIdle();
        Assert.Equal("\nphi-DOS Version X.XX\n\nA>", _m.Run("VER"));
    }
}
