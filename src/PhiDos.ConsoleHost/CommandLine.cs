using System.Globalization;
using System.Text.RegularExpressions;
using PhiDos.Core.Storage;

namespace PhiDos.ConsoleHost;

/// <summary>一个要挂载的驱动器。</summary>
/// <param name="Drive">盘符，'A' 到 'Z'。</param>
/// <param name="Path">虚拟磁盘文件或文件夹的路径。</param>
/// <param name="ReadOnly">是否写保护。</param>
/// <param name="Capacity">文件夹驱动器的容量（字节），null 表示默认 360KB，<see cref="FolderDrive.Unlimited"/> 表示不限。</param>
internal sealed record MountSpec(char Drive, string Path, bool ReadOnly, long? Capacity);

/// <summary>解析好的命令行。</summary>
internal sealed class HostOptions
{
    public List<MountSpec> Mounts { get; } = [];
    public bool AskDate { get; set; } = true;
    public bool ShowHelp { get; set; }

    // --new：新建磁盘后退出
    public string? NewDisk { get; set; }
    public string Size { get; set; } = "360K";
    public string? Label { get; set; }
    public bool Blank { get; set; }

    // --phidisk：镜像工具，后面的参数原样交给它
    public string[]? DiskToolArgs { get; set; }

    public List<string> Warnings { get; } = [];
}

/// <summary>
/// 终端版的命令行解析。
/// </summary>
/// <remarks>
/// 挂载驱动器有三种写法，可以混用：
///   phidos disk.phidisk folder        按顺序依次挂到 A:、B: ……
///   phidos -a disk.phidisk -b folder   -a 到 -d 指定盘符（旧写法）
///   phidos C:=folder:ro:1.44M          任意盘符 A 到 Z，冒号后面可以跟选项
/// 选项：ro（只读）、rw（可写）、unlimited（文件夹不限容量）、容量（如 360K、1.44M、10M）。
/// </remarks>
internal static class CommandLine
{
    private static readonly Regex DriveAssign = new(@"^([A-Za-z]):=(.+)$");

    public static HostOptions Parse(string[] args)
    {
        var o = new HostOptions();
        bool readOnly = false;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value.");

            switch (a)
            {
                case "-h" or "--help" or "/?":
                    o.ShowHelp = true;
                    continue;
                case "-r" or "--readonly":
                    readOnly = true;
                    continue;
                case "--no-date":
                    o.AskDate = false;
                    continue;
                case "--new":
                    o.NewDisk = Next();
                    continue;
                case "--size":
                    o.Size = Next();
                    continue;
                case "--label":
                    o.Label = Next();
                    continue;
                case "--blank":
                    o.Blank = true;
                    continue;
                case "--phidisk":
                    o.DiskToolArgs = args[(i + 1)..];
                    return o;
                case "-a" or "-b" or "-c" or "-d":
                    Add(o, new MountSpec(char.ToUpperInvariant(a[1]), Next(), readOnly, null));
                    continue;
            }

            var m = DriveAssign.Match(a);
            if (m.Success)
            {
                Add(o, ParseAssignment(char.ToUpperInvariant(m.Groups[1].Value[0]), m.Groups[2].Value, readOnly));
                continue;
            }

            if (a.StartsWith('-')) throw new ArgumentException($"Unknown option {a}");

            // 按顺序：挂到下一个还没用的盘符
            char next = 'A';
            while (o.Mounts.Any(x => x.Drive == next)) next++;
            if (next > 'Z') throw new ArgumentException("Too many drives.");
            Add(o, new MountSpec(next, a, readOnly, null));
        }
        return o;
    }

    private static void Add(HostOptions o, MountSpec spec)
    {
        if (o.Mounts.Any(x => x.Drive == spec.Drive))
            throw new ArgumentException($"Drive {spec.Drive}: is given more than once.");
        o.Mounts.Add(spec);
    }

    /// <summary>
    /// 解析 "路径[:选项...]"。Windows 路径里本身就有冒号（D:\games），
    /// 所以从右往左一段一段看，认得出的才当作选项。
    /// </summary>
    private static MountSpec ParseAssignment(char drive, string text, bool readOnly)
    {
        long? capacity = null;
        string path = text;
        while (true)
        {
            int colon = path.LastIndexOf(':');
            if (colon <= 0) break;
            string option = path[(colon + 1)..].ToLowerInvariant();
            if (option == "ro") readOnly = true;
            else if (option == "rw") readOnly = false;
            else if (option is "unlimited" or "max") capacity = FolderDrive.Unlimited;
            else if (TryParseSize(option, out long bytes)) capacity = bytes;
            else break;
            path = path[..colon];
        }
        if (path.Length == 0) throw new ArgumentException($"Drive {drive}: has no path.");
        return new MountSpec(drive, path, readOnly, capacity);
    }

    /// <summary>
    /// 容量："360K"、"1.44M"、"10M"、"1G"、纯数字（字节）。
    /// 正好是软盘规格名时，用那种软盘真正能放的字节数（1.44M 是 1457664，不是 1.44×1024×1024）。
    /// </summary>
    public static bool TryParseSize(string text, out long bytes)
    {
        bytes = 0;
        text = text.Trim().ToUpperInvariant();
        if (text.Length == 0) return false;

        if (DiskGeometry.Find(text) is DiskGeometry g && text.TrimEnd('B').Any(char.IsLetter))
        {
            int dataSectors = g.TotalSectors - g.ReservedSectors - g.FatCount * g.SectorsPerFat - g.RootEntries * 32 / g.BytesPerSector;
            bytes = (long)(dataSectors / g.SectorsPerCluster) * g.SectorsPerCluster * g.BytesPerSector;
            return true;
        }

        long unit = 1;
        string number = text.TrimEnd('B');
        if (number.EndsWith('K')) { unit = 1024; number = number[..^1]; }
        else if (number.EndsWith('M')) { unit = 1024 * 1024; number = number[..^1]; }
        else if (number.EndsWith('G')) { unit = 1024L * 1024 * 1024; number = number[..^1]; }
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value <= 0)
            return false;
        bytes = (long)(value * unit);
        return bytes > 0;
    }
}
