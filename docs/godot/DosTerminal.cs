// phi-DOS 的 Godot 终端节点示例。
// 把这个文件放进 ProjectVN 的 C# 脚本目录，挂到一个 Control 节点上即可。
// 详细说明见 docs/GODOT-INTEGRATION.md。

using System;
using System.IO;
using Godot;
using PhiDos.Core.Machine;
using PhiDos.SystemDisk;

/// <summary>
/// 把一台 phi-DOS 虚拟机画在屏幕上（80×25 字符），并把键盘输入送进去。
/// </summary>
public partial class DosTerminal : Control
{
    /// <summary>等宽字体。要显示中文，请选一个带中文的等宽字体。</summary>
    [Export] public Font? TerminalFont { get; set; }

    [Export] public int FontSize { get; set; } = 16;

    /// <summary>A 盘的虚拟磁盘（res:// 或 user:// 路径）。留空时用一张临时系统盘。</summary>
    [Export(PropertyHint.File, "*.phidisk")] public string DiskPath { get; set; } = "";

    /// <summary>开机时是否像原版一样询问日期和时间。</summary>
    [Export] public bool PromptForDateTime { get; set; }

    /// <summary>剧情可以通过它操作虚拟机：换盘、重启、读写盘里的文件。</summary>
    public DosMachine Machine { get; private set; } = null!;

    // CGA 的 16 色调色板，下标就是 PC 文本模式的颜色编号
    private static readonly Color[] Palette =
    [
        new("000000"), new("0000aa"), new("00aa00"), new("00aaaa"),
        new("aa0000"), new("aa00aa"), new("aa5500"), new("aaaaaa"),
        new("555555"), new("5555ff"), new("55ff55"), new("55ffff"),
        new("ff5555"), new("ff55ff"), new("ffff55"), new("ffffff"),
    ];

    private Font _font = null!;
    private Vector2 _cell;
    private float _ascent;
    private long _drawnVersion = -1;
    private double _blinkTimer;
    private bool _cursorVisible = true;

    public override void _Ready()
    {
        _font = TerminalFont ?? ThemeDB.FallbackFont;
        _cell = new Vector2(_font.GetStringSize("M", HorizontalAlignment.Left, -1, FontSize).X, _font.GetHeight(FontSize));
        _ascent = _font.GetAscent(FontSize);
        CustomMinimumSize = new Vector2(_cell.X * TextScreen.Columns, _cell.Y * TextScreen.Rows);
        FocusMode = FocusModeEnum.All;

        Machine = new DosMachine(new MachineOptions { PromptForDateTimeAtBoot = PromptForDateTime });
        Machine.MountImage('A', LoadDisk(DiskPath), ownsStream: true);
        Machine.Start();
        GrabFocus();
    }

    public override void _ExitTree()
    {
        // 一定要关掉虚拟机，否则后台线程会一直留着（在编辑器里还会妨碍重新编译）
        Machine?.Dispose();
    }

    /// <summary>从 res:// 或 user:// 读出磁盘镜像，放进内存。改动留在内存里，需要时用 SaveDisk 存档。</summary>
    public static MemoryStream LoadDisk(string path)
    {
        if (string.IsNullOrEmpty(path)) return SystemPrograms.CreateSystemDisk();
        // 注意要写 Godot.FileAccess，System.IO 里也有一个同名的类型
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read)
                         ?? throw new FileNotFoundException("Cannot open disk image", path);
        byte[] bytes = file.GetBuffer((long)file.GetLength());
        return new MemoryStream(bytes); // 可写、大小固定，正好适合磁盘镜像
    }

    /// <summary>把某个驱动器的当前内容存到 user://（游戏存档用），虚拟机运行中也可以调用。</summary>
    public void SaveDisk(char drive, string userPath)
    {
        byte[] image = Machine.ExportImage(drive);
        using var file = Godot.FileAccess.Open(userPath, Godot.FileAccess.ModeFlags.Write);
        file.StoreBuffer(image);
    }

    public override void _Process(double delta)
    {
        _blinkTimer += delta;
        if (_blinkTimer >= 0.25)
        {
            _blinkTimer = 0;
            _cursorVisible = !_cursorVisible;
            QueueRedraw();
        }
        // 屏幕内容变了才重画
        if (Machine.Console.Screen.Version != _drawnVersion) QueueRedraw();
    }

    public override void _Draw()
    {
        var screen = Machine.Console.Screen;
        long version = screen.Version; // 先记版本号再取快照，保证不会漏掉更新
        ScreenCell[] cells = screen.Snapshot(out int cursorX, out int cursorY);
        _drawnVersion = version;

        DrawRect(new Rect2(Vector2.Zero, _cell * new Vector2(TextScreen.Columns, TextScreen.Rows)), Palette[0]);
        for (int y = 0; y < TextScreen.Rows; y++)
        {
            for (int x = 0; x < TextScreen.Columns; x++)
            {
                ScreenCell cell = cells[y * TextScreen.Columns + x];
                if (cell.IsWideTail) continue; // 宽字符的右半格，左半格已经画过了
                bool wide = x + 1 < TextScreen.Columns && cells[y * TextScreen.Columns + x + 1].IsWideTail;
                var pos = new Vector2(x * _cell.X, y * _cell.Y);

                Color background = Palette[(cell.Attribute >> 4) & 0x07];
                Color foreground = Palette[cell.Attribute & 0x0F];
                if (background != Palette[0])
                    DrawRect(new Rect2(pos, new Vector2(_cell.X * (wide ? 2 : 1), _cell.Y)), background);
                if (cell.Rune.Value != ' ')
                    DrawString(_font, pos + new Vector2(0, _ascent), cell.Rune.ToString(),
                               HorizontalAlignment.Left, -1, FontSize, foreground);
            }
        }

        if (_cursorVisible && HasFocus())
            DrawRect(new Rect2(cursorX * _cell.X, cursorY * _cell.Y + _cell.Y - 2, _cell.X, 2), Palette[7]);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true } key) return;
        if (DosKeyMap.Translate(key) is KeyInput dosKey)
        {
            Machine.Console.PushKey(dosKey);
            _cursorVisible = true;
            AcceptEvent();
        }
        else if (key.Unicode > 0xFFFF)
        {
            // 超出基本平面的字符（比如部分表情符号）由两个 char 组成
            Machine.Console.PushText(char.ConvertFromUtf32((int)key.Unicode));
            AcceptEvent();
        }
    }
}
