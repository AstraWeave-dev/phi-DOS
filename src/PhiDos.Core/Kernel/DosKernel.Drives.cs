using PhiDos.Abi;
using PhiDos.Core.Machine;
using PhiDos.Core.Storage;
using PhiDos.Core.Text;

namespace PhiDos.Core.Kernel;

internal sealed partial class DosKernel
{
    public enum CriticalAction { Retry, Ignore }

    /// <summary>
    /// 在驱动器上执行一个操作。处理三种情况：
    /// 没插盘（Not ready error）、写保护（Write protect error）、换过盘（重置当前目录）。
    /// 前两种会像 DOS 一样提示 Abort, Retry, Ignore?。
    /// </summary>
    public T DriveOp<T>(char drive, bool write, Func<IDrive, T> op)
    {
        var slot = Machine.Slot(drive);
        while (true)
        {
            Gate.ThrowIfHalted();
            bool notReady;
            lock (slot)
            {
                SyncDriveState(drive, slot);
                var device = slot.Drive;
                notReady = device is null;
                if (device is not null && !(write && device.IsReadOnly))
                    return op(device);
            }
            var action = CriticalError(notReady ? "Not ready" : "Write protect", write, drive);
            if (action == CriticalAction.Ignore)
                throw new DosException(notReady ? DosError.DriveNotReady : DosError.WriteProtected);
        }
    }

    public void DriveOp(char drive, bool write, Action<IDrive> op)
        => DriveOp<bool>(drive, write, d => { op(d); return true; });

    /// <summary>已打开的文件写回磁盘时使用（文件已经拿到手，只需要检查写保护）。</summary>
    public void DriveWrite(char drive, Action op) => DriveOp(drive, write: true, _ => op());

    /// <summary>
    /// 显示 DOS 的严重错误提示并等待选择。选 A 时直接终止当前程序。
    /// </summary>
    public CriticalAction CriticalError(string kind, bool writing, char drive)
    {
        Console.Write($"\r\n{kind} error {(writing ? "writing" : "reading")} drive {drive}\r\n");
        while (true)
        {
            Console.Write("Abort, Retry, Ignore? ");
            KeyInput key = ReadKeyboard();
            char c = char.ToUpperInvariant(key.Char);
            if (key == KeyInput.CtrlC) c = 'A';
            if (c is 'A' or 'R' or 'I')
            {
                Console.Write(c + "\r\n");
                if (c == 'A') throw new DosAbortException();
                if (c == 'I') return CriticalAction.Ignore;
                Console.Write("\r\n");
                return CriticalAction.Retry;
            }
            Console.Write("\r\n");
        }
    }

    private void SyncDriveState(char drive, DriveSlot slot)
    {
        var state = _driveStates[char.ToUpperInvariant(drive) - 'A'];
        if (state.Generation != slot.Generation)
        {
            // 换了盘：回到根目录
            state.CurrentDirectory.Clear();
            state.Generation = slot.Generation;
        }
    }

    private DriveState StateOf(char drive)
    {
        var state = _driveStates[char.ToUpperInvariant(drive) - 'A'];
        var slot = Machine.Slot(drive);
        lock (slot) SyncDriveState(drive, slot);
        return state;
    }

    public bool IsValidDrive(char drive) => Machine.DriveExists(drive);

    // ───────── 路径解析 ─────────

    /// <summary>
    /// 把用户给的路径解析成绝对路径。处理盘符、\、.、..，名字规范化为大写 8.3。
    /// 只有最后一段允许通配符（并且要 allowWildcards 为 true）。
    /// </summary>
    public DosPath Resolve(string path, bool allowWildcards = false)
    {
        path = path.Trim();
        char drive = CurrentDrive;
        if (path.Length >= 2 && path[1] == ':')
        {
            drive = char.ToUpperInvariant(path[0]);
            if (!IsValidDrive(drive)) throw new DosException(DosError.InvalidDrive);
            path = path[2..];
        }

        var parts = new List<string>();
        if (path.StartsWith('\\'))
            path = path[1..];
        else
            parts.AddRange(StateOf(drive).CurrentDirectory);

        if (path.Length == 0) return new DosPath(drive, [.. parts]);

        string[] pieces = path.Split('\\');
        for (int i = 0; i < pieces.Length; i++)
        {
            string piece = pieces[i];
            bool last = i == pieces.Length - 1;
            if (piece.Length == 0)
            {
                if (last) break; // 结尾的 \，例如 "GAMES\"
                throw new DosException(DosError.PathNotFound);
            }
            if (piece == ".") continue;
            if (piece == "..")
            {
                // 根目录没有上一级：和原版一样报错，所以永远跑不出这个驱动器
                if (parts.Count == 0) throw new DosException(DosError.PathNotFound);
                parts.RemoveAt(parts.Count - 1);
                continue;
            }
            string? name = DosNames.Normalize(piece, allowWildcards && last);
            if (name is null)
                throw new DosException(last ? DosError.FileNotFound : DosError.PathNotFound);
            parts.Add(name);
        }
        return new DosPath(drive, [.. parts]);
    }

    public string GetCurrentDirectoryText(char drive)
    {
        drive = char.ToUpperInvariant(drive);
        if (!IsValidDrive(drive)) throw new DosException(DosError.InvalidDrive);
        return "\\" + string.Join("\\", StateOf(drive).CurrentDirectory);
    }

    public IReadOnlyList<string> GetCurrentDirectoryParts(char drive) => StateOf(drive).CurrentDirectory;
}
