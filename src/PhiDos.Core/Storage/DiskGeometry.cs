namespace PhiDos.Core.Storage;

/// <summary>
/// 软盘规格，即引导扇区里 BPB（BIOS 参数块）记录的那些数字。
/// </summary>
public sealed record DiskGeometry(
    string Name,
    int BytesPerSector,
    int SectorsPerCluster,
    int ReservedSectors,
    int FatCount,
    int RootEntries,
    int TotalSectors,
    byte MediaDescriptor,
    int SectorsPerFat,
    int SectorsPerTrack,
    int Heads)
{
    public static readonly DiskGeometry Floppy160K = new("160K", 512, 1, 1, 2, 64, 320, 0xFE, 1, 8, 1);
    public static readonly DiskGeometry Floppy180K = new("180K", 512, 1, 1, 2, 64, 360, 0xFC, 2, 9, 1);
    public static readonly DiskGeometry Floppy320K = new("320K", 512, 2, 1, 2, 112, 640, 0xFF, 1, 8, 2);
    public static readonly DiskGeometry Floppy360K = new("360K", 512, 2, 1, 2, 112, 720, 0xFD, 2, 9, 2);
    public static readonly DiskGeometry Floppy720K = new("720K", 512, 2, 1, 2, 112, 1440, 0xF9, 3, 9, 2);
    public static readonly DiskGeometry Floppy1200K = new("1.2M", 512, 1, 1, 2, 224, 2400, 0xF9, 7, 15, 2);
    public static readonly DiskGeometry Floppy1440K = new("1.44M", 512, 1, 1, 2, 224, 2880, 0xF0, 9, 18, 2);

    public static IReadOnlyList<DiskGeometry> All { get; } =
        [Floppy160K, Floppy180K, Floppy320K, Floppy360K, Floppy720K, Floppy1200K, Floppy1440K];

    /// <summary>按名字查找，例如 "360K"、"1.44M"、"1440"。找不到返回 null。</summary>
    public static DiskGeometry? Find(string name)
    {
        string n = name.Trim().ToUpperInvariant().TrimEnd('K', 'B');
        foreach (var g in All)
        {
            string gn = g.Name.ToUpperInvariant().TrimEnd('K');
            int kb = g.TotalSectors * g.BytesPerSector / 1024;
            if (n == gn || n == kb.ToString()) return g;
        }
        return null;
    }

    public long ImageBytes => (long)TotalSectors * BytesPerSector;
}
