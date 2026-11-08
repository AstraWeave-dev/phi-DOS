// 提示文字取自 MS-DOS 2.0 源码（v2.0/source/FORMES.ASM）。
// MS-DOS v1.25 and v2.0 Source Code, Copyright (c) Microsoft Corporation. Licensed under the MIT License.

using System;
using PhiDos.Abi;

namespace PhiDos.Programs;

/// <summary>
/// FORMAT [盘:] [/V]
/// 格式化虚拟磁盘：清空全部内容，可以设置卷标。文件夹驱动器不能格式化。
/// </summary>
public sealed class Format : IPhiProgram
{
    private const string InvalidLabelChars = "\"*+,./:;<=>?[\\]|";

    public int Run(IDos dos, string[] args)
    {
        char drive = dos.CurrentDrive;
        bool askLabel = false;

        foreach (string raw in args)
        {
            // "A:/V" 这种写法：第一段是盘符，后面每段是一个开关
            string[] pieces = raw.Split('/');
            if (pieces[0].Length > 0)
            {
                if (pieces[0].Length != 2 || pieces[0][1] != ':') return InvalidParameter(dos);
                drive = char.ToUpperInvariant(pieces[0][0]);
            }
            for (int k = 1; k < pieces.Length; k++)
            {
                if (pieces[k].Equals("V", StringComparison.OrdinalIgnoreCase)) askLabel = true;
                else return InvalidParameter(dos);
            }
        }

        DiskInfo info;
        try
        {
            info = dos.GetDiskInfo(drive);
        }
        catch (DosException e) when (e.Error == DosError.InvalidDrive)
        {
            dos.WriteLine("Invalid drive specification");
            return 1;
        }

        while (true)
        {
            dos.Write($"Insert new diskette for drive {drive}:\r\nand strike any key when ready");
            dos.ReadKey(echo: false);
            dos.WriteLine();
            dos.WriteLine();
            dos.Write("Formatting...");

            string? label = null;
            try
            {
                info = dos.GetDiskInfo(drive);
                if (info.IsFolder) throw new DosException(DosError.NotSupported);
                dos.FormatDisk(drive, null);
                dos.WriteLine("Format complete");
                if (askLabel)
                {
                    label = AskLabel(dos);
                    if (label is not null) dos.FormatDisk(drive, label);
                }
                info = dos.GetDiskInfo(drive);
            }
            catch (DosException e) when (e.Error is DosError.NotSupported or DosError.WriteProtected or DosError.DriveNotReady)
            {
                dos.WriteLine();
                dos.Write("Format failure\r\n\r\n");
                return 1;
            }

            dos.WriteLine();
            dos.WriteLine(Pad(info.TotalBytes) + " bytes total disk space");
            dos.Write(Pad(info.FreeBytes) + " bytes available on disk\r\n\r\n");

            dos.Write("Format another (Y/N)?");
            char answer = char.ToUpperInvariant(dos.ReadKey(echo: true));
            dos.WriteLine();
            if (answer != 'Y') return 0;
        }
    }

    private static string? AskLabel(IDos dos)
    {
        while (true)
        {
            dos.Write("Volume label (11 characters, ENTER for none)? ");
            string label = dos.ReadLine().Trim();
            dos.WriteLine();
            if (label.Length == 0 || label == "\x1a") return null;
            if (label.IndexOfAny(InvalidLabelChars.ToCharArray()) < 0) return label.ToUpperInvariant();
            dos.WriteLine("Invalid characters in volume label");
        }
    }

    private static int InvalidParameter(IDos dos)
    {
        dos.WriteLine("Invalid parameter");
        return 1;
    }

    private static string Pad(long n) => n.ToString().PadLeft(9);
}
