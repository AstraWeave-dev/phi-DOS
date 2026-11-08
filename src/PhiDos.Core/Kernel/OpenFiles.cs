using System.Text;
using PhiDos.Abi;
using PhiDos.Core.Storage;

namespace PhiDos.Core.Kernel;

/// <summary>
/// 系统文件表里的一项，即一个"打开的文件或设备"。
/// 多个句柄可以指向同一项（比如 0、1、2 都指向 CON），用引用计数决定何时真正关闭。
/// </summary>
internal abstract class OpenFile
{
    public int RefCount = 1;
    public FileAccessMode Mode { get; init; } = FileAccessMode.ReadWrite;
    public abstract bool IsDevice { get; }
    public virtual bool IsConsole => false;
    public abstract int Read(Span<byte> buffer);
    public abstract int Write(ReadOnlySpan<byte> data);
    public virtual long Seek(long offset, SeekOrigin origin) => 0;
    public virtual long Length => 0;
    public virtual void Close() { }

    public bool CanRead => Mode != FileAccessMode.Write;
    public bool CanWrite => Mode != FileAccessMode.Read;
}

/// <summary>NUL、PRN、AUX 等设备：读到的永远是文件结尾，写入的内容被丢弃。</summary>
internal sealed class NulFile : OpenFile
{
    public override bool IsDevice => true;
    public override int Read(Span<byte> buffer) => 0;
    public override int Write(ReadOnlySpan<byte> data) => data.Length;
}

/// <summary>
/// CON 设备。写入时按 UTF-8 解码后显示；读取时是"整行输入"模式：
/// 用户输入一行并按回车后，这一行（带 CR LF）才会交给读取者。行首的 Ctrl+Z 表示文件结尾。
/// </summary>
internal sealed class ConsoleFile(DosKernel kernel) : OpenFile
{
    // Decoder 会记住被拆开的半个 UTF-8 字符，下一次写入时接着解码，
    // 所以一个汉字的三个字节分两次写进来也能正确显示。
    private readonly Decoder _decoder = new UTF8Encoding(false).GetDecoder();
    private byte[] _pending = [];
    private int _pendingPos;
    private bool _eof;

    public override bool IsDevice => true;
    public override bool IsConsole => true;

    public override int Write(ReadOnlySpan<byte> data)
    {
        int count = _decoder.GetCharCount(data, flush: false);
        if (count > 0)
        {
            var chars = new char[count];
            _decoder.GetChars(data, chars, flush: false);
            kernel.ConsoleOut(new string(chars), checkBreak: true);
        }
        else
        {
            kernel.CheckBreak(console: true);
        }
        return data.Length;
    }

    public override int Read(Span<byte> buffer)
    {
        if (_pendingPos >= _pending.Length)
        {
            if (_eof) { _eof = false; return 0; }
            string line = kernel.ReadConsoleLine();
            kernel.ConsoleOut("\n", checkBreak: false);
            int ctrlZ = line.IndexOf('\x1a');
            if (ctrlZ >= 0)
            {
                line = line[..ctrlZ];
                _eof = true;
                _pending = Encoding.UTF8.GetBytes(line);
            }
            else
            {
                _pending = Encoding.UTF8.GetBytes(line + "\r\n");
            }
            _pendingPos = 0;
            if (_pending.Length == 0) { _eof = false; return 0; }
        }
        int n = Math.Min(buffer.Length, _pending.Length - _pendingPos);
        _pending.AsSpan(_pendingPos, n).CopyTo(buffer);
        _pendingPos += n;
        return n;
    }
}

/// <summary>磁盘上的一个打开的文件，自己维护读写位置。</summary>
internal sealed class DiskFile(IDriveFile file, DosKernel kernel, char drive) : OpenFile
{
    private long _position;

    public override bool IsDevice => false;
    public override long Length => file.Length;
    public char Drive => drive;

    public override int Read(Span<byte> buffer)
    {
        int n = file.Read(_position, buffer);
        _position += n;
        return n;
    }

    public override int Write(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
        {
            // DOS 的惯例：写 0 字节表示把文件截断到当前位置
            kernel.DriveWrite(drive, () => file.SetLength(_position));
            return 0;
        }
        // Span 不能被 lambda 捕获（它只能活在栈上），所以先复制成数组。
        byte[] copy = data.ToArray();
        long position = _position;
        kernel.DriveWrite(drive, () => file.Write(position, copy));
        _position += data.Length;
        return data.Length;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => file.Length + offset,
            _ => throw new DosException(DosError.InvalidFunction),
        };
        if (target < 0) throw new DosException(DosError.InvalidFunction);
        _position = target;
        return _position;
    }

    public override void Close() => kernel.DriveWrite(drive, file.Close);
}
