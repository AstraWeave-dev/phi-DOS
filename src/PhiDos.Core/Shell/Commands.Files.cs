using PhiDos.Abi;
using PhiDos.Core.Kernel;
using PhiDos.Core.Text;

namespace PhiDos.Core.Shell;

internal sealed partial class CommandShell
{
    private const int LinesPerPage = 23;
    private const int WideColumns = 5;

    // ───────── DIR ─────────

    /// <summary>
    /// DIR [盘:][路径][文件名] [/P] [/W]
    /// 输出格式照 MS-DOS 2.0 的 TCODE4.ASM：名字 8 格、扩展名 3 格、大小 9 格右对齐，
    /// 然后是日期和 12 小时制时间；/W 每行五个名字，用制表符隔开。
    /// </summary>
    private void DirCommand(ShellCommand cmd)
    {
        bool wide = cmd.Switches.Contains('W');
        bool page = cmd.Switches.Contains('P');
        var (drive, dirParts, pattern) = ResolveListing(cmd.Args.FirstOrDefault() ?? "");

        Out("\r\n");
        PrintVolumeLine(drive);
        Out(Messages.DirectoryOf + drive + ":\\" + string.Join("\\", dirParts) + "\r\n\r\n");

        string full = $"{drive}:\\{string.Join("\\", dirParts)}{(dirParts.Length > 0 ? "\\" : "")}{pattern}";
        IReadOnlyList<DirEntry> entries;
        try
        {
            entries = _kernel.FindFiles(full, DosAttributes.Directory);
        }
        catch (DosException e) when (e.Error == DosError.PathNotFound)
        {
            throw Fail(Messages.InvalidDirectory);
        }
        if (entries.Count == 0) throw Fail(Messages.FileNotFound);

        int perLine = wide ? WideColumns : 1;
        int column = 0;
        int lines = 0;
        foreach (var e in entries)
        {
            Out(FormatName(e.Name));
            if (!wide)
            {
                Out(e.IsDirectory ? Messages.DirMarker : DosFormat.Number9(e.Size));
                if (e.LastWriteTime != DateTime.MinValue)
                    Out("  " + DosFormat.DirDate(e.LastWriteTime) + "  " + DosFormat.DirTime(e.LastWriteTime));
            }

            if (++column < perLine)
            {
                Out("\t");
                continue;
            }
            column = 0;
            Out("\r\n");
            if (page && ++lines == LinesPerPage)
            {
                lines = 0;
                Err(Messages.Pause);
                _kernel.StdReadKey(_process, echo: false);
                Out("\r\n");
            }
        }
        if (column != 0) Out("\r\n");

        var info = _kernel.GetDiskInfo(drive);
        Out(DosFormat.Number9(entries.Count) + Messages.DirFiles + DosFormat.Number9(info.FreeBytes) + Messages.BytesFree);
    }

    /// <summary>名字 8 格 + 空格 + 扩展名 3 格（按显示宽度对齐，汉字占两格）。</summary>
    private static string FormatName(string name)
    {
        var (n, e) = name is "." or ".." ? (name, "") : DosNames.Split(name);
        return DisplayWidth.PadRight(n, 8) + " " + DisplayWidth.PadRight(e, 3);
    }

    /// <summary>
    /// 把 DIR 的参数拆成 驱动器、目录、文件名模式。
    /// 规则和 DOS 一样：参数是目录就列整个目录；只写了主名就补 .*；只写了 .扩展名 就补 *。
    /// </summary>
    private (char Drive, string[] Dir, string Pattern) ResolveListing(string arg)
    {
        DosPath dp;
        string rawLeaf = RawLeaf(arg);
        string fixedArg = arg;
        if (rawLeaf.StartsWith('.') && rawLeaf is not "." and not "..")
            fixedArg = arg[..^rawLeaf.Length] + "*" + rawLeaf;

        try
        {
            dp = _kernel.Resolve(fixedArg, allowWildcards: true);
        }
        catch (DosException e)
        {
            throw Fail(e.Error == DosError.InvalidDrive ? Messages.InvalidDrive
                     : e.Error == DosError.PathNotFound ? Messages.InvalidDirectory
                     : Messages.FileNotFound);
        }

        if (dp.IsRoot || rawLeaf is "." or ".." || rawLeaf.Length == 0) return (dp.Drive, dp.Parts, "*.*");

        string leaf = dp.Leaf!;
        if (!DosNames.HasWildcards(leaf))
        {
            bool isDir = false;
            try
            {
                isDir = _kernel.DriveOp(dp.Drive, false, d => d.GetEntry(dp.Parts)) is { IsDirectory: true };
            }
            catch (DosException) { }
            if (isDir) return (dp.Drive, dp.Parts, "*.*");
        }
        string pattern = rawLeaf.Contains('.') || leaf.Contains('.') ? leaf : leaf + ".*";
        return (dp.Drive, dp.ParentParts, pattern);
    }

    /// <summary>参数里最后一段原始文字（盘符和目录之后的部分）。</summary>
    private static string RawLeaf(string arg)
    {
        int cut = Math.Max(arg.LastIndexOf('\\'), arg.Length >= 2 && arg[1] == ':' ? 1 : -1);
        return arg[(cut + 1)..];
    }

    private void PrintVolumeLine(char drive)
    {
        var info = _kernel.GetDiskInfo(drive);
        Out(Messages.VolumeIn + drive + (info.VolumeLabel is string l ? Messages.VolumeIs + l : Messages.NoLabel) + "\r\n");
    }

    // ───────── VOL ─────────

    private void VolCommand(ShellCommand cmd)
    {
        char drive = _kernel.CurrentDrive;
        if (cmd.Args.Count > 0)
        {
            string a = cmd.Args[0];
            if (a.Length != 2 || a[1] != ':' || !_kernel.IsValidDrive(char.ToUpperInvariant(a[0])))
                throw Fail(Messages.InvalidDrive);
            drive = char.ToUpperInvariant(a[0]);
        }
        Out("\r\n");
        PrintVolumeLine(drive);
    }

    // ───────── TYPE ─────────

    private void TypeCommand(ShellCommand cmd)
    {
        if (cmd.Args.Count == 0) throw Fail(Messages.InvalidNumberOfParameters);
        int handle;
        try
        {
            handle = _kernel.Open(_process, cmd.Args[0], FileAccessMode.Read);
        }
        catch (DosException e) when (e.Error is DosError.FileNotFound or DosError.PathNotFound or DosError.AccessDenied)
        {
            throw Fail(Messages.FileNotFound);
        }
        try
        {
            var buf = new byte[512];
            while (true)
            {
                int n = _kernel.Read(_process, handle, buf);
                if (n == 0) break;
                int z = Array.IndexOf(buf, (byte)0x1A, 0, n); // Ctrl+Z 是文本文件的结尾
                _kernel.Write(_process, 1, buf.AsSpan(0, z >= 0 ? z : n));
                if (z >= 0) break;
            }
        }
        finally
        {
            _process.CloseHandle(handle);
        }
    }

    // ───────── CD / MD / RD ─────────

    private void ChdirCommand(ShellCommand cmd)
    {
        string arg = cmd.Tail.Trim();
        if (arg.Length == 0 || (arg.Length == 2 && arg[1] == ':'))
        {
            char d = arg.Length == 0 ? _kernel.CurrentDrive : char.ToUpperInvariant(arg[0]);
            if (!_kernel.IsValidDrive(d)) throw Fail(Messages.InvalidDrive);
            string dir = _kernel.DriveOp(d, false, _ => _kernel.GetCurrentDirectoryText(d));
            Out($"{d}:{dir}\r\n");
            return;
        }
        try
        {
            _kernel.ChangeDirectory(cmd.Args.FirstOrDefault() ?? arg);
        }
        catch (DosException e) when (e.Error != DosError.InvalidDrive)
        {
            throw Fail(Messages.InvalidDirectory);
        }
    }

    private void MkdirCommand(ShellCommand cmd)
    {
        if (cmd.Args.Count == 0) throw Fail(Messages.InvalidNumberOfParameters);
        try
        {
            _kernel.MakeDirectory(cmd.Args[0]);
        }
        catch (DosException e) when (e.Error != DosError.InvalidDrive)
        {
            throw Fail(Messages.UnableToCreateDirectory);
        }
    }

    private void RmdirCommand(ShellCommand cmd)
    {
        if (cmd.Args.Count == 0) throw Fail(Messages.InvalidNumberOfParameters);
        try
        {
            _kernel.RemoveDirectory(cmd.Args[0]);
        }
        catch (DosException e) when (e.Error != DosError.InvalidDrive)
        {
            throw Fail(Messages.InvalidRmdir);
        }
    }
}
