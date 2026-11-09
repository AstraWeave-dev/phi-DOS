using System;
using System.Collections.Generic;
using System.Text;
using PhiDos.Abi;

namespace PhiDos.Programs;

/// <summary>
/// ILDOSVER：彩色的系统信息展示，左边是 phi-DOS 的 φ 图标，右边是这台 IL DOS 的信息。
/// 系统名和版本号直接写在程序里。
/// </summary>
public sealed class Ildosver : IPhiProgram
{
    private const string Version = "X.XX"; // 和 PhiDosInfo.Version 保持一致

    // ANSI 颜色（phi-DOS 屏幕支持 ESC[...m 改颜色）
    private const string Reset = "\x1b[0m";
    private const string Cyan = "\x1b[0;36m";
    private const string BrightCyan = "\x1b[1;36m";
    private const string Blue = "\x1b[0;34m";
    private const string BrightBlue = "\x1b[1;34m";
    private const string BrightWhite = "\x1b[1;37m";
    private const string BrightYellow = "\x1b[1;33m";
    private const string Gray = "\x1b[0;37m";

    // φ 图标，每行 22 格宽，从上到下由亮青色渐变到蓝色
    private static readonly (string Color, string Art)[] Logo =
    [
        (BrightCyan, "       ████████       "),
        (BrightCyan, "          ██          "),
        (BrightCyan, "    ▄████████████▄    "),
        (Cyan,       "  ▄██▀    ██    ▀██▄  "),
        (Cyan,       "  ██      ██      ██  "),
        (Cyan,       "  ██      ██      ██  "),
        (BrightBlue, "  ▀██▄    ██    ▄██▀  "),
        (BrightBlue, "    ▀████████████▀    "),
        (Blue,       "          ██          "),
        (Blue,       "       ████████       "),
    ];

    public int Run(IDos dos, string[] args)
    {
        if (args.Length > 0)
        {
            dos.WriteLine("Shows information about this phi-DOS system.");
            dos.WriteLine();
            dos.WriteLine("ILDOSVER");
            return args[0] == "/?" ? 0 : 1;
        }

        string user = dos.GetEnv("USER") ?? "guest";
        string title = $"{user}@phi-DOS";

        var info = new List<string>
        {
            BrightWhite + title + Reset,
            Gray + new string('-', title.Length) + Reset,
            Field("OS", $"phi-DOS {Version}"),
            Field("Kernel", "IL-DOS, MS-DOS 2.0 behaviour"),
            Field("CPU", ".NET"),
            Field("Language", "IL"),
            Field("Shell", "COMMAND.COM"),
            Field("Programs", ".COM / .EXE as IL assemblies"),
            Field("Drive", DriveText(dos)),
            Field("Date", dos.Now.ToString("MM-dd-yyyy  HH:mm")),
            Field("Charset", "UTF-8 中文 ✓"),
        };

        var sb = new StringBuilder("\r\n");
        int rows = Math.Max(Logo.Length, info.Count);
        for (int i = 0; i < rows; i++)
        {
            string art = i < Logo.Length ? Logo[i].Color + Logo[i].Art + Reset : new string(' ', 22);
            string text = i < info.Count ? info[i] : "";
            sb.Append(' ').Append(art).Append("   ").Append(text).Append("\r\n");
        }

        // 最后是 16 色色板，和 neofetch 一样
        sb.Append("\r\n").Append(new string(' ', 26));
        for (int c = 0; c < 8; c++) sb.Append($"\x1b[0;3{c}m███");
        sb.Append(Reset).Append("\r\n").Append(new string(' ', 26));
        for (int c = 0; c < 8; c++) sb.Append($"\x1b[1;3{c}m███");
        sb.Append(Reset).Append("\r\n");

        dos.Write(sb.ToString());
        return 0;
    }

    private static string Field(string name, string value)
        => BrightYellow + name.PadRight(10) + Reset + value;

    private static string DriveText(IDos dos)
    {
        char drive = dos.CurrentDrive;
        try
        {
            var disk = dos.GetDiskInfo(drive);
            string kind = disk.IsFolder ? "folder" : "virtual disk";
            return $"{drive}: {kind}, {disk.FreeBytes / 1024}K free of {disk.TotalBytes / 1024}K";
        }
        catch (DosException)
        {
            return $"{drive}: not ready";
        }
    }
}
