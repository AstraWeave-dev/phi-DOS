using PhiDos.Abi;
using PhiDos.Core.Machine;
using PhiDos.Core.Text;

namespace PhiDos.Core.Kernel;

internal sealed partial class DosKernel
{
    /// <summary>程序结束时最多保留的嵌套层数，防止程序无限递归地运行自己。</summary>
    private const int MaxNesting = 16;
    private int _nesting;

    /// <summary>
    /// IDos.Exec：运行程序并等待它结束。没写扩展名时依次找 .COM、.EXE；
    /// 没写路径时先找当前目录，再按 PATH 找。批处理文件不能用它运行（和 DOS 的 EXEC 一样）。
    /// </summary>
    public int Exec(DosProcess parent, string program, string args)
    {
        EnterCall();
        var path = FindProgram(program, parent.Environment, allowBatch: false)
                   ?? throw new DosException(DosError.FileNotFound);
        return RunProgram(parent, path, args);
    }

    /// <summary>
    /// 按规则找程序文件。返回 null 表示找不到。
    /// 名字里带了盘符或路径时只在那里找，不再搜索 PATH。
    /// </summary>
    public DosPath? FindProgram(string name, DosEnvironment environment, bool allowBatch, Action<string>? badPathDrive = null)
    {
        name = name.Trim();
        if (name.Length == 0 || DosNames.HasWildcards(name)) return null;

        string[] extensions = allowBatch ? ["COM", "EXE", "BAT"] : ["COM", "EXE"];
        DosPath requested;
        try { requested = Resolve(name); }
        catch (DosException) { return null; }
        if (requested.Leaf is null) return null;

        var (baseName, ext) = DosNames.Split(requested.Leaf);
        if (ext.Length > 0)
        {
            if (!extensions.Contains(ext)) return null;
            extensions = [ext];
        }

        bool hasPath = name.Contains('\\') || name.Contains(':');
        var directories = new List<DosPath> { requested.Parent };
        if (!hasPath)
        {
            string path = environment.Get("PATH") ?? "";
            foreach (string entry in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                try { directories.Add(Resolve(entry.Trim())); }
                catch (DosException e) when (e.Error == DosError.InvalidDrive) { badPathDrive?.Invoke(entry); }
                catch (DosException) { }
            }
        }

        foreach (var dir in directories)
        {
            foreach (string e in extensions)
            {
                var candidate = dir.Child(baseName + "." + e);
                try
                {
                    var entry = DriveOp(candidate.Drive, write: false, d => d.GetEntry(candidate.Parts));
                    if (entry is { IsDirectory: false, IsVolumeLabel: false }) return candidate;
                }
                catch (DosException) { }
            }
        }
        return null;
    }

    /// <summary>
    /// 运行一个 .COM / .EXE。内置程序表里有同名项时直接调用它的 C# 实现，
    /// 否则从磁盘加载 IL 程序集。子进程继承父进程的句柄和一份环境变量副本。
    /// </summary>
    public int RunProgram(DosProcess parent, DosPath path, string tail)
    {
        string name = DosNames.Split(path.Leaf!).Name;
        if (_nesting >= MaxNesting) throw new DosException(DosError.InsufficientMemory);

        IPhiProgram program;
        IDisposable? unload = null;
        if (Machine.Options.BuiltinPrograms.TryGetValue(name, out var factory))
        {
            program = factory();
        }
        else
        {
            var loaded = Loader.Load(path);
            program = loaded.Program;
            unload = loaded;
        }

        string[] args = tail.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var child = new DosProcess(this, parent, parent.Environment.Clone(), name, args, tail);
        child.InheritHandles(parent);

        var previous = CurrentProcess;
        CurrentProcess = child;
        _nesting++;
        try
        {
            return program.Run(child, args) & 0xFF;
        }
        catch (DosAbortException)
        {
            // 严重错误时选了 Abort：程序被终止，回到调用它的地方
            return 0;
        }
        catch (Exception ex) when (ex is not (MachineHaltException or DosBreakException))
        {
            // 程序自己抛出了没有处理的异常
            Console.Write("\r\nAbnormal program termination\r\n");
            Console.Write($"  {ex.GetType().Name}: {ex.Message}\r\n");
            return 255;
        }
        finally
        {
            _nesting--;
            CurrentProcess = previous;
            child.CloseAllHandles();
            unload?.Dispose();
        }
    }
}
