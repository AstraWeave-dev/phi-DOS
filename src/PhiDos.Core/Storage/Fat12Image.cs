using System.Buffers.Binary;
using System.Text;
using PhiDos.Abi;

namespace PhiDos.Core.Storage;

/// <summary>新建和格式化 FAT12 镜像。</summary>
public static class Fat12Image
{
    /// <summary>
    /// 新建一张空白虚拟磁盘文件（标准 FAT12 镜像，默认 360KB）。文件已存在时覆盖。
    /// </summary>
    public static void Create(string path, DiskGeometry? geometry = null, string? volumeLabel = null, DateTime? now = null)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        Create(fs, geometry, volumeLabel, now);
    }

    /// <summary>在流里写出一张空白镜像。</summary>
    public static void Create(Stream stream, DiskGeometry? geometry = null, string? volumeLabel = null, DateTime? now = null)
    {
        var g = geometry ?? DiskGeometry.Floppy360K;
        stream.SetLength(g.ImageBytes);
        // 先把整张盘写成 0xF6，这是 DOS FORMAT 填充数据区的值
        stream.Position = 0;
        var fill = new byte[g.BytesPerSector];
        Array.Fill(fill, (byte)0xF6);
        for (int i = 0; i < g.TotalSectors; i++) stream.Write(fill);
        WriteSystemArea(stream, g, volumeLabel, now ?? DateTime.Now);
        stream.Flush();
    }

    /// <summary>新建一张空白镜像并放进内存，适合测试或临时盘。</summary>
    public static MemoryStream CreateInMemory(DiskGeometry? geometry = null, string? volumeLabel = null, DateTime? now = null)
    {
        var ms = new MemoryStream();
        Create(ms, geometry, volumeLabel, now);
        ms.Position = 0;
        return ms;
    }

    /// <summary>重写引导扇区、FAT 表和根目录区，数据区不动（与 DOS 的快速格式化效果相同）。</summary>
    internal static void WriteSystemArea(Stream stream, DiskGeometry g, string? volumeLabel, DateTime now)
    {
        int bps = g.BytesPerSector;
        stream.Position = 0;
        stream.Write(BuildBootSector(g, volumeLabel, now));
        // 保留扇区的其余部分清零
        for (int i = 1; i < g.ReservedSectors; i++) stream.Write(new byte[bps]);

        // FAT：第 0 项是介质描述符，第 1 项是结束标记，其余为 0（空闲）
        var fat = new byte[g.SectorsPerFat * bps];
        fat[0] = g.MediaDescriptor;
        fat[1] = 0xFF;
        fat[2] = 0xFF;
        for (int i = 0; i < g.FatCount; i++) stream.Write(fat);

        int rootBytes = ((g.RootEntries * 32 + bps - 1) / bps) * bps;
        var root = new byte[rootBytes];
        if (volumeLabel is not null)
        {
            // 卷标是根目录里一个属性为 0x08 的特殊目录项
            WriteLabelBytes(root.AsSpan(0, 11), volumeLabel);
            root[11] = (byte)DosAttributes.VolumeLabel;
            FatTime.Write(now, root);
        }
        stream.Write(root);
        stream.Flush();
    }

    private static byte[] BuildBootSector(DiskGeometry g, string? volumeLabel, DateTime now)
    {
        var b = new byte[g.BytesPerSector];
        b[0] = 0xEB; b[1] = 0x3C; b[2] = 0x90;        // JMP SHORT 3E / NOP
        Encoding.ASCII.GetBytes("PHIDOS  ").CopyTo(b, 3); // OEM 名称，8 字节
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x0B), (ushort)g.BytesPerSector);
        b[0x0D] = (byte)g.SectorsPerCluster;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x0E), (ushort)g.ReservedSectors);
        b[0x10] = (byte)g.FatCount;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x11), (ushort)g.RootEntries);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x13), (ushort)g.TotalSectors);
        b[0x15] = g.MediaDescriptor;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x16), (ushort)g.SectorsPerFat);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x18), (ushort)g.SectorsPerTrack);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x1A), (ushort)g.Heads);
        // 0x1C 起是隐藏扇区数和大扇区数，软盘为 0

        // 扩展 BPB（DOS 4.0 才有，DOS 2.0 会忽略；写上它 mtools 和 Linux 能显示卷标）
        b[0x26] = 0x29;
        uint serial = (uint)((now.Month << 24) | (now.Day << 16) | (now.Hour << 8) | now.Minute) ^ (uint)now.Year;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x27), serial);
        WriteLabelBytes(b.AsSpan(0x2B, 11), volumeLabel ?? "NO NAME");
        Encoding.ASCII.GetBytes("FAT12   ").CopyTo(b, 0x36);

        // 一段很短的引导代码：用 BIOS 显示提示，按任意键后重启。
        // 用 DOSBox 之类从这张盘启动时会看到它。
        byte[] code =
        [
            0xFA,             // cli
            0x31, 0xC0,       // xor ax,ax
            0x8E, 0xD8,       // mov ds,ax
            0xBE, 0x58, 0x7C, // mov si,7C58h（下面消息的地址）
            0xFB,             // sti
            0xAC,             // lodsb
            0x08, 0xC0,       // or al,al
            0x74, 0x06,       // jz 结束
            0xB4, 0x0E,       // mov ah,0Eh
            0xCD, 0x10,       // int 10h  显示一个字符
            0xEB, 0xF5,       // jmp 回到 lodsb
            0x31, 0xC0,       // xor ax,ax
            0xCD, 0x16,       // int 16h  等待按键
            0xCD, 0x19,       // int 19h  重新引导
        ];
        code.CopyTo(b, 0x3E);
        byte[] msg = Encoding.ASCII.GetBytes("\r\nNon-System disk or disk error\r\nReplace and strike any key when ready\r\n\0");
        msg.CopyTo(b, 0x58);

        b[510] = 0x55;
        b[511] = 0xAA;
        return b;
    }

    private static void WriteLabelBytes(Span<byte> target, string label)
    {
        target.Fill((byte)' ');
        var bytes = Encoding.UTF8.GetBytes(Text.DosNames.Truncate(label.ToUpperInvariant(), 11));
        bytes.CopyTo(target);
    }
}
