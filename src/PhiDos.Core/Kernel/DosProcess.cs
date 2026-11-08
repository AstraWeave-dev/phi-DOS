using PhiDos.Abi;

namespace PhiDos.Core.Kernel;

/// <summary>
/// 一个正在运行的程序，相当于 DOS 的 PSP（程序段前缀）。
/// 它持有自己的句柄表和环境变量，并把 <see cref="IDos"/> 的每个调用转给内核。
/// 程序拿到的 IDos 就是它自己的 DosProcess。
/// </summary>
internal sealed class DosProcess : IDos
{
    private readonly DosKernel _kernel;

    public DosProcess(DosKernel kernel, DosProcess? parent, DosEnvironment environment, string name, string[] args)
    {
        _kernel = kernel;
        Parent = parent;
        Environment = environment;
        Name = name;
        Args = args;
    }

    public DosProcess? Parent { get; }
    public DosEnvironment Environment { get; }
    public string Name { get; }
    public string[] Args { get; }
    public OpenFile?[] Handles { get; } = new OpenFile?[DosKernel.MaxHandles];

    public OpenFile GetHandle(int handle)
    {
        if (handle < 0 || handle >= Handles.Length || Handles[handle] is null)
            throw new DosException(DosError.InvalidHandle);
        return Handles[handle]!;
    }

    public int AllocHandle(OpenFile file)
    {
        for (int i = 0; i < Handles.Length; i++)
        {
            if (Handles[i] is null)
            {
                Handles[i] = file;
                return i;
            }
        }
        if (--file.RefCount == 0) file.Close();
        throw new DosException(DosError.TooManyOpenFiles);
    }

    public void CloseHandle(int handle)
    {
        var file = GetHandle(handle);
        Handles[handle] = null;
        if (--file.RefCount == 0) file.Close();
    }

    /// <summary>把句柄 <paramref name="handle"/> 换成另一个文件（重定向用），返回原来的文件。</summary>
    public OpenFile? Replace(int handle, OpenFile? file)
    {
        var old = Handles[handle];
        Handles[handle] = file;
        return old;
    }

    /// <summary>子进程继承父进程的全部句柄（DOS 2.0 的行为，重定向正是靠它传给子程序）。</summary>
    public void InheritHandles(DosProcess parent)
    {
        for (int i = 0; i < Handles.Length; i++)
        {
            var f = parent.Handles[i];
            if (f is null) continue;
            f.RefCount++;
            Handles[i] = f;
        }
    }

    /// <summary>程序结束时关闭它的全部句柄。</summary>
    public void CloseAllHandles()
    {
        for (int i = 0; i < Handles.Length; i++)
        {
            var f = Handles[i];
            if (f is null) continue;
            Handles[i] = null;
            try
            {
                if (--f.RefCount == 0) f.Close();
            }
            catch (DosException) { /* 程序退出时的关闭失败不再报告 */ }
        }
    }

    // ───────── IDos ─────────
    // => 后面跟表达式的写法叫"表达式主体成员"，等价于 { return ...; }，适合这种一行转发。

    public void Write(string text) { _kernel.EnterCall(true); _kernel.StdWrite(this, text); }
    public void WriteLine(string text = "") { _kernel.EnterCall(true); _kernel.StdWrite(this, text + "\r\n"); }
    public char ReadKey(bool echo = true) { _kernel.EnterCall(true); return _kernel.StdReadKey(this, echo); }
    public string ReadLine() { _kernel.EnterCall(true); return _kernel.StdReadLine(this); }
    public bool KeyAvailable { get { _kernel.EnterCall(true); return _kernel.StdKeyAvailable(this); } }

    public int Create(string path) => _kernel.Create(this, path);
    public int Open(string path, FileAccessMode mode) => _kernel.Open(this, path, mode);
    public int Read(int handle, Span<byte> buffer) => _kernel.Read(this, handle, buffer);
    public int Write(int handle, ReadOnlySpan<byte> data) => _kernel.Write(this, handle, data);
    public long Seek(int handle, long offset, SeekOrigin origin) => _kernel.Seek(this, handle, offset, origin);
    public void Close(int handle) => _kernel.Close(this, handle);
    public void Delete(string path) => _kernel.Delete(path);
    public void Rename(string from, string to) => _kernel.Rename(from, to);
    public IReadOnlyList<DirEntry> FindFiles(string pattern) => _kernel.FindFiles(pattern, DosAttributes.None);
    public IReadOnlyList<DirEntry> FindFiles(string pattern, DosAttributes include) => _kernel.FindFiles(pattern, include);

    public char CurrentDrive
    {
        get { _kernel.EnterCall(); return _kernel.CurrentDrive; }
        set
        {
            _kernel.EnterCall();
            char d = char.ToUpperInvariant(value);
            if (!_kernel.IsValidDrive(d)) throw new DosException(DosError.InvalidDrive);
            _kernel.CurrentDrive = d;
        }
    }

    public string CurrentDirectory { get { _kernel.EnterCall(); return _kernel.GetCurrentDirectoryText(_kernel.CurrentDrive); } }
    public string GetCurrentDirectory(char drive) { _kernel.EnterCall(); return _kernel.GetCurrentDirectoryText(drive); }
    public void ChangeDirectory(string path) => _kernel.ChangeDirectory(path);
    public void MakeDirectory(string path) => _kernel.MakeDirectory(path);
    public void RemoveDirectory(string path) => _kernel.RemoveDirectory(path);
    public DiskInfo GetDiskInfo(char drive) => _kernel.GetDiskInfo(drive);
    public void FormatDisk(char drive, string? volumeLabel) => _kernel.FormatDisk(drive, volumeLabel);

    public int Exec(string program, string args) => _kernel.Exec(this, program, args);
    public string? GetEnv(string name) { _kernel.EnterCall(); return Environment.Get(name); }
    public DateTime Now { get { _kernel.EnterCall(); return _kernel.Now; } }
}
