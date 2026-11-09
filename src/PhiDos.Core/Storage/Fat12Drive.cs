using System.Buffers.Binary;
using System.Text;
using PhiDos.Abi;
using PhiDos.Core.Text;

namespace PhiDos.Core.Storage;

/// <summary>
/// 标准 FAT12 磁盘镜像（.phidisk）。支持读写和子目录。
/// </summary>
/// <remarks>
/// 磁盘从头到尾依次是：引导扇区（含 BPB）、FAT 表（通常两份）、根目录区、数据区。
/// 根目录区大小固定；子目录和文件一样存放在数据区，由簇链串起来。
/// FAT 表常驻内存，修改后在 Flush 时写回所有副本；目录和文件数据直接写入镜像。
/// 布局与 MS-DOS 2.0 的 FAT.ASM、DIR.ASM 描述的一致，可以用 DOSBox、mtools 直接打开。
/// </remarks>
public sealed class Fat12Drive : IDrive, IDisposable
{
    private const int EntrySize = 32;
    private const int EndOfChain = 0xFFF;
    private const byte DeletedMark = 0xE5;

    private readonly Stream _stream;
    private readonly bool _ownsStream;
    private DiskGeometry _geometry;
    private byte[] _fat = [];
    private bool _fatDirty;

    public Fat12Drive(Stream stream, bool readOnly = false, bool ownsStream = false)
    {
        if (!stream.CanSeek) throw new ArgumentException("The disk image stream must be seekable.", nameof(stream));
        _stream = stream;
        _ownsStream = ownsStream;
        IsReadOnly = readOnly || !stream.CanWrite;
        _geometry = ReadGeometry();
        LoadFat();
    }

    public bool IsFolder => false;
    public bool IsReadOnly { get; }
    public DiskGeometry Geometry => _geometry;

    // ───────── 磁盘布局 ─────────

    private int BytesPerSector => _geometry.BytesPerSector;
    private int ClusterBytes => _geometry.BytesPerSector * _geometry.SectorsPerCluster;
    private int FatStart => _geometry.ReservedSectors;
    private int RootStart => FatStart + _geometry.FatCount * _geometry.SectorsPerFat;
    private int RootSectors => (_geometry.RootEntries * EntrySize + BytesPerSector - 1) / BytesPerSector;
    private int DataStart => RootStart + RootSectors;
    private int ClusterCount => (_geometry.TotalSectors - DataStart) / _geometry.SectorsPerCluster;

    private long ClusterOffset(int cluster) => ((long)DataStart + (long)(cluster - 2) * _geometry.SectorsPerCluster) * BytesPerSector;

    private DiskGeometry ReadGeometry()
    {
        var boot = new byte[512];
        ReadAt(0, boot);
        if (boot[0] != 0xEB && boot[0] != 0xE9)
            throw new InvalidDataException("Not a FAT12 disk image: the boot sector has no BPB.");
        var g = new DiskGeometry(
            "custom",
            BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(0x0B)),
            boot[0x0D],
            BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(0x0E)),
            boot[0x10],
            BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(0x11)),
            BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(0x13)),
            boot[0x15],
            BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(0x16)),
            BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(0x18)),
            BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(0x1A)));

        bool sane = g.BytesPerSector is 512 or 1024 or 2048 or 4096
                    && g.SectorsPerCluster is 1 or 2 or 4 or 8 or 16 or 32 or 64 or 128
                    && g.ReservedSectors >= 1 && g.FatCount >= 1 && g.RootEntries > 0
                    && g.SectorsPerFat > 0 && g.TotalSectors > 0;
        if (!sane) throw new InvalidDataException("Not a FAT12 disk image: the BPB is invalid.");
        // 和标准软盘规格一样时用它的名字（360K 之类），方便显示
        var known = DiskGeometry.All.FirstOrDefault(k => k with { Name = "custom" } == g);
        if (known is not null) g = known;
        _geometry = g;
        if (ClusterCount < 1 || ClusterCount >= 4085)
            throw new InvalidDataException("Not a FAT12 disk image: wrong cluster count for FAT12.");
        if (_stream.Length < g.ImageBytes)
            throw new InvalidDataException("The disk image is shorter than its BPB says.");
        return g;
    }

    private void LoadFat()
    {
        _fat = new byte[_geometry.SectorsPerFat * BytesPerSector];
        ReadAt((long)FatStart * BytesPerSector, _fat);
        _fatDirty = false;
    }

    // ───────── FAT 表 ─────────

    /// <summary>
    /// 读一个 FAT 项。FAT12 每项 12 位，两项挤在 3 个字节里：
    /// 偶数号簇取低 12 位，奇数号簇取高 12 位。
    /// </summary>
    private int GetFat(int cluster)
    {
        int offset = cluster + cluster / 2;
        int value = _fat[offset] | (_fat[offset + 1] << 8);
        return (cluster & 1) == 0 ? value & 0xFFF : value >> 4;
    }

    private void SetFat(int cluster, int value)
    {
        int offset = cluster + cluster / 2;
        if ((cluster & 1) == 0)
        {
            _fat[offset] = (byte)value;
            _fat[offset + 1] = (byte)((_fat[offset + 1] & 0xF0) | ((value >> 8) & 0x0F));
        }
        else
        {
            _fat[offset] = (byte)((_fat[offset] & 0x0F) | ((value << 4) & 0xF0));
            _fat[offset + 1] = (byte)(value >> 4);
        }
        _fatDirty = true;
    }

    private static bool IsEnd(int value) => value >= 0xFF8 || value < 2;

    private bool IsValidCluster(int c) => c >= 2 && c < ClusterCount + 2;

    /// <summary>从起始簇出发，列出整条簇链。遇到损坏的链（越界、成环）就停下。</summary>
    private List<int> Chain(int start)
    {
        var chain = new List<int>();
        int c = start;
        while (IsValidCluster(c) && chain.Count <= ClusterCount)
        {
            chain.Add(c);
            c = GetFat(c);
            if (IsEnd(c)) break;
        }
        return chain;
    }

    private int FreeClusterCount()
    {
        int free = 0;
        for (int c = 2; c < ClusterCount + 2; c++)
            if (GetFat(c) == 0) free++;
        return free;
    }

    /// <summary>分配 count 个簇并接在 last 后面（last 为 0 表示新链）。返回新分配的第一个簇。空间不够时不做任何修改。</summary>
    private int Allocate(int count, int last, bool zero)
    {
        if (count <= 0) return 0;
        var found = new List<int>(count);
        for (int c = 2; c < ClusterCount + 2 && found.Count < count; c++)
            if (GetFat(c) == 0) found.Add(c);
        if (found.Count < count) throw new DosException(DosError.DiskFull);

        for (int i = 0; i < found.Count; i++)
            SetFat(found[i], i + 1 < found.Count ? found[i + 1] : EndOfChain);
        if (last != 0) SetFat(last, found[0]);
        if (zero)
        {
            var empty = new byte[ClusterBytes];
            foreach (int c in found) WriteAt(ClusterOffset(c), empty);
        }
        return found[0];
    }

    private void FreeChain(int start)
    {
        foreach (int c in Chain(start)) SetFat(c, 0);
    }

    // ───────── 目录 ─────────

    /// <summary>一个目录：根目录（Cluster = 0）或子目录（Cluster = 起始簇）。</summary>
    private readonly record struct Dir(int Cluster)
    {
        public bool IsRoot => Cluster == 0;
    }

    /// <summary>目录里的一个原始目录项和它的位置。</summary>
    private sealed class RawEntry
    {
        public required Dir Dir;
        public required int Index;
        public required byte[] Bytes;

        public byte First => Bytes[0];
        public DosAttributes Attributes => (DosAttributes)Bytes[11];
        public int StartCluster
        {
            get => BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(26));
            set => BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(26), (ushort)value);
        }
        public uint Size
        {
            get => BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(28));
            set => BinaryPrimitives.WriteUInt32LittleEndian(Bytes.AsSpan(28), value);
        }
        public bool IsLongNameFragment => Bytes[11] == 0x0F;
        public bool IsDotEntry => Bytes[0] == '.';
    }

    /// <summary>目录占用的字节位置列表（每个目录项一个位置）。</summary>
    private List<long> EntryOffsets(Dir dir)
    {
        var offsets = new List<long>();
        if (dir.IsRoot)
        {
            long baseOffset = (long)RootStart * BytesPerSector;
            for (int i = 0; i < _geometry.RootEntries; i++) offsets.Add(baseOffset + i * EntrySize);
        }
        else
        {
            int perCluster = ClusterBytes / EntrySize;
            foreach (int c in Chain(dir.Cluster))
            {
                long baseOffset = ClusterOffset(c);
                for (int i = 0; i < perCluster; i++) offsets.Add(baseOffset + i * EntrySize);
            }
        }
        return offsets;
    }

    /// <summary>读出目录里所有在用的目录项（到 0x00 结束标记为止）。</summary>
    private List<RawEntry> ReadDir(Dir dir, bool includeFree = false)
    {
        var result = new List<RawEntry>();
        var offsets = EntryOffsets(dir);
        for (int i = 0; i < offsets.Count; i++)
        {
            var bytes = new byte[EntrySize];
            ReadAt(offsets[i], bytes);
            if (bytes[0] == 0x00)
            {
                if (!includeFree) break;
            }
            else if (bytes[0] == DeletedMark && !includeFree)
            {
                continue;
            }
            result.Add(new RawEntry { Dir = dir, Index = i, Bytes = bytes });
        }
        return result;
    }

    private void WriteEntry(RawEntry entry)
    {
        var offsets = EntryOffsets(entry.Dir);
        WriteAt(offsets[entry.Index], entry.Bytes);
    }

    /// <summary>在目录里找一个空位；子目录满了就再接一个簇。根目录满了报错。</summary>
    private RawEntry NewEntry(Dir dir)
    {
        var offsets = EntryOffsets(dir);
        var bytes = new byte[EntrySize];
        for (int i = 0; i < offsets.Count; i++)
        {
            ReadAt(offsets[i], bytes);
            if (bytes[0] == 0x00 || bytes[0] == DeletedMark)
                return new RawEntry { Dir = dir, Index = i, Bytes = new byte[EntrySize] };
        }
        if (dir.IsRoot) throw new DosException(DosError.AccessDenied); // 根目录满
        var chain = Chain(dir.Cluster);
        Allocate(1, chain[^1], zero: true);
        return new RawEntry { Dir = dir, Index = offsets.Count, Bytes = new byte[EntrySize] };
    }

    private static string DecodeName(byte[] b)
    {
        var name = (byte[])b[..8].Clone();
        if (name[0] == 0x05) name[0] = DeletedMark; // 0x05 代表真正的首字节 0xE5
        string n = Decode(name).TrimEnd(' ');
        string e = Decode(b[8..11]).TrimEnd(' ');
        return e.Length == 0 ? n : n + "." + e;
    }

    private static string DecodeLabel(byte[] b) => Decode(b[..11]).TrimEnd(' ');

    private static string Decode(byte[] bytes)
    {
        // 原版 DOS 写入的名字是单字节代码页，不是合法 UTF-8 时按 Latin-1 显示，至少不会出错
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    private static void EncodeName(string shortName, byte[] entry)
    {
        var (name, ext) = DosNames.Split(shortName);
        Fill(entry.AsSpan(0, 8), name);
        Fill(entry.AsSpan(8, 3), ext);
        if (entry[0] == DeletedMark) entry[0] = 0x05;
    }

    private static void Fill(Span<byte> target, string text)
    {
        target.Fill((byte)' ');
        Encoding.UTF8.GetBytes(text).AsSpan(0, Math.Min(target.Length, Encoding.UTF8.GetByteCount(text))).CopyTo(target);
    }

    private static DriveEntry ToDriveEntry(RawEntry e)
    {
        var date = BinaryPrimitives.ReadUInt16LittleEndian(e.Bytes.AsSpan(24));
        var time = BinaryPrimitives.ReadUInt16LittleEndian(e.Bytes.AsSpan(22));
        string name = (e.Attributes & DosAttributes.VolumeLabel) != 0 ? DecodeLabel(e.Bytes) : DecodeName(e.Bytes);
        return new DriveEntry(name, e.Attributes, e.Size, FatTime.ToDateTime(date, time));
    }

    private static bool IsListable(RawEntry e)
        => !e.IsLongNameFragment && !e.IsDotEntry && (e.Attributes & DosAttributes.VolumeLabel) == 0;

    /// <summary>沿路径找到目录项。空路径返回 null（根目录没有目录项）。</summary>
    private RawEntry? Lookup(IReadOnlyList<string> path)
    {
        var dir = new Dir(0);
        RawEntry? found = null;
        for (int i = 0; i < path.Count; i++)
        {
            found = null;
            foreach (var e in ReadDir(dir))
            {
                if (IsListable(e) && string.Equals(DecodeName(e.Bytes), path[i], StringComparison.OrdinalIgnoreCase))
                {
                    found = e;
                    break;
                }
            }
            if (found is null) return null;
            if (i < path.Count - 1)
            {
                if ((found.Attributes & DosAttributes.Directory) == 0) return null;
                dir = new Dir(found.StartCluster);
            }
        }
        return found;
    }

    private Dir DirOf(IReadOnlyList<string> path)
    {
        if (path.Count == 0) return new Dir(0);
        var e = Lookup(path);
        if (e is null || (e.Attributes & DosAttributes.Directory) == 0) throw new DosException(DosError.PathNotFound);
        return new Dir(e.StartCluster);
    }

    private static IReadOnlyList<string> ParentOf(IReadOnlyList<string> path) => path.Take(path.Count - 1).ToArray();

    private void RequireWritable()
    {
        if (IsReadOnly) throw new DosException(DosError.WriteProtected);
    }

    // ───────── IDrive ─────────

    public string? VolumeLabel
    {
        get
        {
            foreach (var e in ReadDir(new Dir(0)))
                if ((e.Attributes & DosAttributes.VolumeLabel) != 0 && !e.IsLongNameFragment)
                    return DecodeLabel(e.Bytes);
            return null;
        }
    }

    public DriveSpace GetSpace()
        => new((long)ClusterCount * ClusterBytes, (long)FreeClusterCount() * ClusterBytes, ClusterBytes);

    public IReadOnlyList<DriveEntry> List(IReadOnlyList<string> directory)
        => ReadDir(DirOf(directory)).Where(IsListable).Select(ToDriveEntry).ToList();

    public DriveEntry? GetEntry(IReadOnlyList<string> path)
    {
        if (path.Count == 0) return new DriveEntry("\\", DosAttributes.Directory, 0, DateTime.MinValue);
        var e = Lookup(path);
        return e is null ? null : ToDriveEntry(e);
    }

    public IDriveFile OpenFile(IReadOnlyList<string> path, bool writable)
    {
        if (writable) RequireWritable();
        var e = Lookup(path) ?? throw new DosException(DosError.FileNotFound);
        if ((e.Attributes & (DosAttributes.Directory | DosAttributes.VolumeLabel)) != 0)
            throw new DosException(DosError.AccessDenied);
        return new Fat12File(this, e);
    }

    public IDriveFile CreateFile(IReadOnlyList<string> path, DateTime now)
    {
        RequireWritable();
        var e = Lookup(path);
        if (e is not null)
        {
            if ((e.Attributes & (DosAttributes.Directory | DosAttributes.VolumeLabel)) != 0)
                throw new DosException(DosError.AccessDenied);
            if (e.StartCluster != 0) FreeChain(e.StartCluster);
            e.StartCluster = 0;
            e.Size = 0;
        }
        else
        {
            var dir = DirOf(ParentOf(path));
            e = NewEntry(dir);
            EncodeName(path[^1], e.Bytes);
        }
        e.Bytes[11] = (byte)DosAttributes.Archive;
        FatTime.Write(now, e.Bytes);
        WriteEntry(e);
        Flush();
        return new Fat12File(this, e);
    }

    public void Delete(IReadOnlyList<string> path)
    {
        RequireWritable();
        var e = Lookup(path) ?? throw new DosException(DosError.FileNotFound);
        if ((e.Attributes & DosAttributes.Directory) != 0) throw new DosException(DosError.AccessDenied);
        if (e.StartCluster != 0) FreeChain(e.StartCluster);
        e.Bytes[0] = DeletedMark;
        WriteEntry(e);
        Flush();
    }

    public void Rename(IReadOnlyList<string> from, IReadOnlyList<string> to)
    {
        RequireWritable();
        var e = Lookup(from) ?? throw new DosException(DosError.FileNotFound);
        if (Lookup(to) is not null) throw new DosException(DosError.FileExists);
        var targetDir = DirOf(ParentOf(to));
        if (targetDir == e.Dir)
        {
            EncodeName(to[^1], e.Bytes);
            WriteEntry(e);
        }
        else
        {
            var moved = NewEntry(targetDir);
            e.Bytes.CopyTo(moved.Bytes, 0);
            EncodeName(to[^1], moved.Bytes);
            WriteEntry(moved);
            e.Bytes[0] = DeletedMark;
            WriteEntry(e);
            if ((moved.Attributes & DosAttributes.Directory) != 0)
                FixDotDot(moved.StartCluster, targetDir.Cluster);
        }
        Flush();
    }

    private void FixDotDot(int dirCluster, int parentCluster)
    {
        foreach (var e in ReadDir(new Dir(dirCluster)))
        {
            if (e.Bytes[0] == '.' && e.Bytes[1] == '.')
            {
                e.StartCluster = parentCluster;
                WriteEntry(e);
                return;
            }
        }
    }

    public void MakeDirectory(IReadOnlyList<string> path, DateTime now)
    {
        RequireWritable();
        if (Lookup(path) is not null) throw new DosException(DosError.AccessDenied);
        var parent = DirOf(ParentOf(path));
        var entry = NewEntry(parent);
        int cluster = Allocate(1, 0, zero: true);

        // 新目录里必须先写 . 和 ..，分别指向自己和上一级（上一级是根目录时写 0）
        var dot = new byte[EntrySize];
        Fill(dot.AsSpan(0, 11), ".");
        dot[11] = (byte)DosAttributes.Directory;
        FatTime.Write(now, dot);
        BinaryPrimitives.WriteUInt16LittleEndian(dot.AsSpan(26), (ushort)cluster);
        var dotdot = (byte[])dot.Clone();
        Fill(dotdot.AsSpan(0, 11), "..");
        BinaryPrimitives.WriteUInt16LittleEndian(dotdot.AsSpan(26), (ushort)parent.Cluster);
        WriteAt(ClusterOffset(cluster), dot);
        WriteAt(ClusterOffset(cluster) + EntrySize, dotdot);

        EncodeName(path[^1], entry.Bytes);
        entry.Bytes[11] = (byte)DosAttributes.Directory;
        FatTime.Write(now, entry.Bytes);
        entry.StartCluster = cluster;
        entry.Size = 0;
        WriteEntry(entry);
        Flush();
    }

    public void RemoveDirectory(IReadOnlyList<string> path)
    {
        RequireWritable();
        var e = Lookup(path);
        if (e is null || (e.Attributes & DosAttributes.Directory) == 0) throw new DosException(DosError.PathNotFound);
        if (ReadDir(new Dir(e.StartCluster)).Any(IsListable)) throw new DosException(DosError.DirectoryNotEmpty);
        if (e.StartCluster != 0) FreeChain(e.StartCluster);
        e.Bytes[0] = DeletedMark;
        WriteEntry(e);
        Flush();
    }

    public void Format(string? volumeLabel, DateTime now)
    {
        RequireWritable();
        Fat12Image.WriteSystemArea(_stream, _geometry, volumeLabel, now);
        LoadFat();
        _stream.Flush();
    }

    public void Flush()
    {
        if (_fatDirty && !IsReadOnly)
        {
            for (int i = 0; i < _geometry.FatCount; i++)
                WriteAt((long)(FatStart + i * _geometry.SectorsPerFat) * BytesPerSector, _fat);
            _fatDirty = false;
        }
        if (_stream.CanWrite) _stream.Flush();
    }

    /// <summary>把整张盘的内容复制出来（先把 FAT 写回）。</summary>
    public byte[] ToArray()
    {
        Flush();
        var bytes = new byte[_geometry.ImageBytes];
        ReadAt(0, bytes);
        return bytes;
    }

    public void Dispose()
    {
        try { Flush(); } catch (IOException) { }
        if (_ownsStream) _stream.Dispose();
    }

    // ───────── 底层读写 ─────────

    private void ReadAt(long offset, Span<byte> buffer)
    {
        _stream.Position = offset;
        int total = 0;
        while (total < buffer.Length)
        {
            int n = _stream.Read(buffer[total..]);
            if (n == 0) { buffer[total..].Clear(); break; }
            total += n;
        }
    }

    private void WriteAt(long offset, ReadOnlySpan<byte> data)
    {
        RequireWritable();
        _stream.Position = offset;
        _stream.Write(data);
    }

    /// <summary>FAT12 上的一个打开的文件。</summary>
    private sealed class Fat12File(Fat12Drive drive, RawEntry entry) : IDriveFile
    {
        private List<int> _chain = drive.Chain(entry.StartCluster);
        private long _length = entry.Size;
        private bool _dirty;

        public long Length => _length;

        public int Read(long position, Span<byte> buffer)
        {
            if (position >= _length) return 0;
            int count = (int)Math.Min(buffer.Length, _length - position);
            int done = 0;
            int cb = drive.ClusterBytes;
            while (done < count)
            {
                long pos = position + done;
                int index = (int)(pos / cb);
                if (index >= _chain.Count) break; // 簇链比文件大小短：磁盘损坏，读到哪算哪
                int inCluster = (int)(pos % cb);
                int n = Math.Min(count - done, cb - inCluster);
                drive.ReadAt(drive.ClusterOffset(_chain[index]) + inCluster, buffer.Slice(done, n));
                done += n;
            }
            return done;
        }

        public void Write(long position, ReadOnlySpan<byte> data)
        {
            drive.RequireWritable();
            long end = position + data.Length;
            if (end > uint.MaxValue) throw new DosException(DosError.DiskFull);
            EnsureClusters(end);
            if (position > _length)
            {
                // 在文件末尾之后写：中间的空洞补零
                var zeros = new byte[(int)Math.Min(position - _length, int.MaxValue)];
                WriteRaw(_length, zeros);
            }
            WriteRaw(position, data);
            if (end > _length) _length = end;
            _dirty = true;
        }

        public void SetLength(long length)
        {
            drive.RequireWritable();
            int cb = drive.ClusterBytes;
            int needed = (int)((length + cb - 1) / cb);
            if (needed < _chain.Count)
            {
                if (needed == 0)
                {
                    drive.FreeChain(_chain[0]);
                    entry.StartCluster = 0;
                }
                else
                {
                    drive.FreeChain(_chain[needed]);
                    drive.SetFat(_chain[needed - 1], EndOfChain);
                }
                _chain = _chain.Take(needed).ToList();
            }
            else
            {
                EnsureClusters(length);
            }
            if (length > _length)
            {
                var zeros = new byte[(int)(length - _length)];
                WriteRaw(_length, zeros);
            }
            _length = length;
            _dirty = true;
        }

        public void Close(DateTime? modifiedTime)
        {
            if (!_dirty && modifiedTime is null) return;
            entry.Size = (uint)_length;
            entry.StartCluster = _chain.Count > 0 ? _chain[0] : 0;
            if (modifiedTime is DateTime t)
            {
                FatTime.Write(t, entry.Bytes);
                entry.Bytes[11] |= (byte)DosAttributes.Archive;
            }
            drive.WriteEntry(entry);
            drive.Flush();
            _dirty = false;
        }

        private void EnsureClusters(long length)
        {
            int cb = drive.ClusterBytes;
            int needed = (int)((length + cb - 1) / cb);
            int extra = needed - _chain.Count;
            if (extra <= 0) return;
            int last = _chain.Count > 0 ? _chain[^1] : 0;
            int first = drive.Allocate(extra, last, zero: false);
            if (_chain.Count == 0)
            {
                entry.StartCluster = first;
                // 新文件的起始簇要马上记进目录项，否则异常中断后这些簇就丢了
                drive.WriteEntry(entry);
            }
            _chain = drive.Chain(_chain.Count > 0 ? _chain[0] : first);
        }

        private void WriteRaw(long position, ReadOnlySpan<byte> data)
        {
            int cb = drive.ClusterBytes;
            int done = 0;
            while (done < data.Length)
            {
                long pos = position + done;
                int index = (int)(pos / cb);
                int inCluster = (int)(pos % cb);
                int n = Math.Min(data.Length - done, cb - inCluster);
                drive.WriteAt(drive.ClusterOffset(_chain[index]) + inCluster, data.Slice(done, n));
                done += n;
            }
        }
    }
}

/// <summary>DOS 日期时间格式与 DateTime 之间的换算。</summary>
internal static class FatTime
{
    /// <summary>
    /// 日期：bit 15-9 年（从 1980 起），8-5 月，4-0 日。
    /// 时间：bit 15-11 时，10-5 分，4-0 秒的一半（所以只精确到 2 秒）。
    /// </summary>
    public static DateTime ToDateTime(ushort date, ushort time)
    {
        if (date == 0) return DateTime.MinValue;
        int year = 1980 + (date >> 9);
        int month = Math.Clamp((date >> 5) & 0x0F, 1, 12);
        int day = Math.Clamp(date & 0x1F, 1, DateTime.DaysInMonth(year, month));
        int hour = Math.Min(time >> 11, 23);
        int minute = Math.Min((time >> 5) & 0x3F, 59);
        int second = Math.Min((time & 0x1F) * 2, 59);
        return new DateTime(year, month, day, hour, minute, second);
    }

    public static (ushort Date, ushort Time) FromDateTime(DateTime t)
    {
        if (t.Year < 1980) t = new DateTime(1980, 1, 1);
        if (t.Year > 2107) t = new DateTime(2107, 12, 31, 23, 59, 58);
        ushort date = (ushort)(((t.Year - 1980) << 9) | (t.Month << 5) | t.Day);
        ushort time = (ushort)((t.Hour << 11) | (t.Minute << 5) | (t.Second / 2));
        return (date, time);
    }

    public static void Write(DateTime t, byte[] entry)
    {
        var (date, time) = FromDateTime(t);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(22), time);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(24), date);
    }
}
