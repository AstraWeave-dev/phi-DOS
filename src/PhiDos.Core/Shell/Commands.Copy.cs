using PhiDos.Abi;
using PhiDos.Core.Kernel;
using PhiDos.Core.Text;

namespace PhiDos.Core.Shell;

internal sealed partial class CommandShell
{
    // ───────── COPY ─────────

    /// <summary>COPY 的一个参数：路径文字加上它后面跟着的 /A /B 开关。</summary>
    private sealed record CopyItem(string Spec, bool? Ascii);

    /// <summary>
    /// COPY [/A|/B] 源[/A|/B] [+ 源[/A|/B] ...] [目标[/A|/B]] [/V]
    /// 行为：
    /// 源里有通配符或在连接复制时逐个显示源文件名；多个源写进同一个文件即连接复制，
    /// 连接时默认按文本方式（读到 Ctrl+Z 为止，结尾补 Ctrl+Z）；
    /// 出错时删除写了一半的目标文件，最后显示复制了几个文件。
    /// </summary>
    private void CopyCommand(ShellCommand cmd)
    {
        var (sources, dest, plus) = ParseCopyArgs(cmd.Tail);
        if (sources.Count == 0) throw Fail(Messages.InvalidNumberOfParameters);

        int copied = 0;
        try
        {
            bool concat = plus || (sources.Count == 1 && DosNames.HasWildcards(sources[0].Spec) &&
                                   dest is not null && !IsDirectoryTarget(dest.Spec) &&
                                   !DosNames.HasWildcards(RawLeaf(dest.Spec)));
            copied = concat ? CopyConcatenate(sources, dest) : CopySeparately(sources[0], dest);
        }
        catch (CopyStop stop)
        {
            if (stop.Message.Length > 0) Err(stop.Message);
            copied = stop.Copied;
        }
        Out(DosFormat.Number9(copied) + Messages.FilesCopied);
    }

    /// <summary>COPY 中途出错：显示消息，然后照常显示已复制的数量。</summary>
    private sealed class CopyStop(string message, int copied) : Exception(message)
    {
        public int Copied { get; } = copied;
    }

    private static (List<CopyItem> Sources, CopyItem? Dest, bool Plus) ParseCopyArgs(string tail)
    {
        // 切成单词和 "+" 两种记号
        var tokens = new List<string>();
        var word = new System.Text.StringBuilder();
        void Flush() { if (word.Length > 0) { tokens.Add(word.ToString()); word.Clear(); } }
        foreach (char c in tail)
        {
            if (c is ' ' or '\t' or ',' or ';' or '=') Flush();
            else if (c == '+') { Flush(); tokens.Add("+"); }
            else word.Append(c);
        }
        Flush();

        var sources = new List<CopyItem>();
        CopyItem? dest = null;
        bool plus = false;
        bool expectSource = true;
        bool? mode = null; // 最近一次出现的 /A 或 /B，影响后面的参数
        foreach (string t in tokens)
        {
            if (t == "+") { plus = true; expectSource = true; continue; }
            string[] parts = t.Split('/');
            foreach (string sw in parts.Skip(1))
            {
                if (sw.StartsWith('A') || sw.StartsWith('a')) mode = true;
                else if (sw.StartsWith('B') || sw.StartsWith('b')) mode = false;
            }
            if (parts[0].Length == 0) continue; // 单独的开关
            var item = new CopyItem(parts[0], mode);
            if (expectSource) { sources.Add(item); expectSource = false; }
            else if (dest is null) dest = item;
            else throw Fail(Messages.InvalidNumberOfParameters);
        }
        return (sources, dest, plus);
    }

    /// <summary>目标是不是一个目录（"B:"、以 \ 结尾、或者确实存在的目录）。</summary>
    private bool IsDirectoryTarget(string spec)
    {
        if (spec.Length == 2 && spec[1] == ':') return true;
        if (spec.EndsWith('\\')) return true;
        try
        {
            var dp = _kernel.Resolve(spec);
            if (dp.IsRoot) return true;
            return _kernel.DriveOp(dp.Drive, false, d => d.GetEntry(dp.Parts)) is { IsDirectory: true };
        }
        catch (DosException) { return false; }
    }

    /// <summary>源文件的一个匹配：完整路径、显示用的名字、修改时间。</summary>
    private sealed record CopySource(DosPath Path, string Display, DateTime Time, bool IsDevice);

    /// <summary>展开一个源（可能含通配符）。是设备时只返回它自己。</summary>
    private List<CopySource> ExpandSource(string spec)
    {
        DosPath dp;
        try { dp = _kernel.Resolve(spec, allowWildcards: true); }
        catch (DosException) { return []; }
        if (dp.Leaf is string leaf && DosNames.IsDeviceName(leaf, out _))
            return [new CopySource(dp, leaf, DateTime.MinValue, true)];

        // 源是目录时复制目录里的全部文件
        string pattern = spec;
        if (dp.IsRoot || IsDirectoryTarget(spec))
            pattern = spec.TrimEnd('\\') + (spec.EndsWith(':') ? "" : "\\") + "*.*";

        IReadOnlyList<DirEntry> found;
        try { found = _kernel.FindFiles(pattern, DosAttributes.None); }
        catch (DosException) { return []; }

        var basePath = _kernel.Resolve(pattern, allowWildcards: true).Parent;
        string prefix = DisplayPrefix(pattern);
        return found.Select(e => new CopySource(basePath.Child(e.Name), prefix + e.Name, e.LastWriteTime, false)).ToList();
    }

    /// <summary>参数里文件名前面的部分（盘符和路径），显示源文件名时原样保留。</summary>
    private static string DisplayPrefix(string spec)
    {
        int cut = Math.Max(spec.LastIndexOf('\\'), spec.Length >= 2 && spec[1] == ':' ? 1 : -1);
        return spec[..(cut + 1)].ToUpperInvariant();
    }

    /// <summary>根据目标参数算出某个源文件应该复制到哪里。</summary>
    private DosPath DestinationFor(CopyItem? dest, string sourceName)
    {
        if (dest is null)
            return new DosPath(_kernel.CurrentDrive, [.. _kernel.GetCurrentDirectoryParts(_kernel.CurrentDrive), sourceName]);
        if (IsDirectoryTarget(dest.Spec))
            return _kernel.Resolve(dest.Spec).Child(sourceName);
        var dp = _kernel.Resolve(dest.Spec, allowWildcards: true);
        if (dp.Leaf is string leaf && DosNames.HasWildcards(leaf))
            return dp.Parent.Child(ApplyTemplate(leaf, sourceName));
        return dp;
    }

    private int CopySeparately(CopyItem source, CopyItem? dest)
    {
        var matches = ExpandSource(source.Spec);
        if (matches.Count == 0)
            throw new CopyStop(source.Spec.ToUpperInvariant() + " " + Messages.FileNotFound, 0);

        bool showNames = DosNames.HasWildcards(source.Spec) || matches.Count > 1;
        int copied = 0;
        foreach (var src in matches)
        {
            if (showNames) Out(src.Display + "\r\n");
            var target = DestinationFor(dest, src.Path.Leaf!);
            if (!src.IsDevice && SamePath(target, src.Path))
                throw new CopyStop(Messages.CopyOntoItself, copied);

            bool srcAscii = source.Ascii ?? src.IsDevice;
            bool destAscii = dest?.Ascii ?? false;
            int input = OpenSource(src, copied);
            try
            {
                int output = CreateDestination(target, copied);
                try
                {
                    CopyData(input, output, srcAscii, copied, target);
                    if (destAscii) WriteBytes(output, [0x1A], copied, target);
                    if (!src.IsDevice && _process.GetHandle(output) is DiskFile df) df.KeepTimestamp = src.Time;
                }
                finally { SafeClose(output); }
            }
            finally { SafeClose(input); }
            copied++;
        }
        return copied;
    }

    private int CopyConcatenate(List<CopyItem> sources, CopyItem? dest)
    {
        var first = ExpandSource(sources[0].Spec);
        DosPath target;
        if (dest is null || IsDirectoryTarget(dest.Spec))
        {
            if (first.Count == 0) throw new CopyStop(sources[0].Spec.ToUpperInvariant() + " " + Messages.FileNotFound, 0);
            target = DestinationFor(dest, first[0].Path.Leaf!);
        }
        else
        {
            target = _kernel.Resolve(dest.Spec);
        }

        // "COPY A+B" 的目标就是 A：在 A 后面追加，而不是先把 A 清空
        bool append = first.Count > 0 && !first[0].IsDevice && SamePath(first[0].Path, target);
        bool destAscii = dest?.Ascii ?? true;

        int output;
        if (append)
        {
            output = _kernel.Open(_process, target.ToString(), FileAccessMode.ReadWrite);
            long end = _kernel.Seek(_process, output, 0, SeekOrigin.End);
            if (sources[0].Ascii ?? true) TruncateAtCtrlZ(output, end);
        }
        else
        {
            output = CreateDestination(target, 0);
        }

        try
        {
            bool firstFile = true;
            foreach (var item in sources)
            {
                foreach (var src in ExpandSource(item.Spec))
                {
                    Out(src.Display + "\r\n");
                    if (!src.IsDevice && SamePath(src.Path, target))
                    {
                        if (!(append && firstFile)) Err(Messages.ContentLost);
                        firstFile = false;
                        continue;
                    }
                    firstFile = false;
                    int input = OpenSource(src, 0);
                    try { CopyData(input, output, item.Ascii ?? true, 0, target); }
                    finally { SafeClose(input); }
                }
            }
            if (destAscii) WriteBytes(output, [0x1A], 0, target);
        }
        finally { SafeClose(output); }
        return 1;
    }

    /// <summary>关闭句柄；句柄已经关过（例如写满时提前关闭）就忽略。</summary>
    private void SafeClose(int handle)
    {
        if (handle < 0 || handle >= _process.Handles.Length || _process.Handles[handle] is null) return;
        _kernel.Close(_process, handle);
    }

    private int OpenSource(CopySource src, int copied)
    {
        try { return _kernel.Open(_process, src.Path.ToString(), FileAccessMode.Read); }
        catch (DosException) { throw new CopyStop(src.Display + " " + Messages.FileNotFound, copied); }
    }

    private int CreateDestination(DosPath target, int copied)
    {
        try { return _kernel.Create(_process, target.ToString()); }
        catch (DosException e)
        {
            // 写保护、未就绪时用户已经在严重错误提示里选了 Ignore，不再额外报错
            string message = e.Error switch
            {
                DosError.WriteProtected or DosError.DriveNotReady => "",
                DosError.DiskFull => Messages.NoSpace,
                _ => Messages.FileCreationError,
            };
            throw new CopyStop(message, copied);
        }
    }

    private void CopyData(int input, int output, bool ascii, int copied, DosPath target)
    {
        var buf = new byte[4096];
        while (true)
        {
            int n = _kernel.Read(_process, input, buf);
            if (n == 0) return;
            if (ascii)
            {
                int z = Array.IndexOf(buf, (byte)0x1A, 0, n);
                if (z >= 0)
                {
                    WriteBytes(output, buf.AsSpan(0, z), copied, target);
                    return;
                }
            }
            WriteBytes(output, buf.AsSpan(0, n), copied, target);
        }
    }

    private void WriteBytes(int output, ReadOnlySpan<byte> data, int copied, DosPath target)
    {
        if (data.Length == 0) return;
        try
        {
            _kernel.Write(_process, output, data);
        }
        catch (DosException e) when (e.Error == DosError.DiskFull)
        {
            // 写满了：关掉并删除这个不完整的目标文件
            try { _kernel.Close(_process, output); } catch (DosException) { }
            try { _kernel.Delete(target.ToString()); } catch (DosException) { }
            throw new CopyStop(Messages.NoSpace, copied);
        }
    }

    /// <summary>文本方式追加时，先把原文件截到第一个 Ctrl+Z 处。</summary>
    private void TruncateAtCtrlZ(int handle, long length)
    {
        var buf = new byte[4096];
        _kernel.Seek(_process, handle, 0, SeekOrigin.Begin);
        long pos = 0;
        while (pos < length)
        {
            int n = _kernel.Read(_process, handle, buf);
            if (n == 0) break;
            int z = Array.IndexOf(buf, (byte)0x1A, 0, n);
            if (z >= 0)
            {
                _kernel.Seek(_process, handle, pos + z, SeekOrigin.Begin);
                _kernel.Write(_process, handle, ReadOnlySpan<byte>.Empty); // 写 0 字节 = 截断
                return;
            }
            pos += n;
        }
        _kernel.Seek(_process, handle, 0, SeekOrigin.End);
    }

    private static bool SamePath(DosPath a, DosPath b) => a.ToString() == b.ToString();

    /// <summary>
    /// 用通配符模板生成新名字（COPY *.TXT *.BAK、REN *.TXT *.OLD）：
    /// ? 取源名字同一位置的字符，* 取源名字剩下的部分，其他字符照抄。
    /// </summary>
    internal static string ApplyTemplate(string template, string name)
    {
        var (tn, te) = DosNames.Split(template);
        var (nn, ne) = DosNames.Split(name);
        string n = ApplyPart(tn, nn);
        string e = ApplyPart(te, ne);
        return e.Length == 0 ? n : n + "." + e;
    }

    private static string ApplyPart(string template, string source)
    {
        var src = source.EnumerateRunes().ToList();
        var sb = new System.Text.StringBuilder();
        int i = 0;
        foreach (var r in template.EnumerateRunes())
        {
            if (r.Value == '*')
            {
                for (int k = i; k < src.Count; k++) sb.Append(src[k].ToString());
                return sb.ToString();
            }
            if (r.Value == '?') { if (i < src.Count) sb.Append(src[i].ToString()); }
            else sb.Append(r.ToString());
            i++;
        }
        return sb.ToString().TrimEnd(' ');
    }

    // ───────── DEL / ERASE ─────────

    private void EraseCommand(ShellCommand cmd)
    {
        if (cmd.Args.Count == 0) throw Fail(Messages.InvalidNumberOfParameters);
        string spec = cmd.Args[0];
        if (IsDirectoryTarget(spec))
            spec = spec.TrimEnd('\\') + (spec.EndsWith(':') ? "" : "\\") + "*.*";

        DosPath dp;
        try { dp = _kernel.Resolve(spec, allowWildcards: true); }
        catch (DosException e) when (e.Error == DosError.InvalidDrive) { throw Fail(Messages.InvalidDrive); }
        catch (DosException) { throw Fail(Messages.FileNotFound); }

        if (dp.Leaf is string leaf && IsEverything(leaf) && !AskYesNo(Messages.AreYouSure)) return;

        IReadOnlyList<DirEntry> found;
        try { found = _kernel.FindFiles(spec, DosAttributes.None); }
        catch (DosException e) when (e.Error == DosError.PathNotFound) { throw Fail(Messages.InvalidDirectory); }
        if (found.Count == 0) throw Fail(Messages.FileNotFound);

        foreach (var e in found)
        {
            if ((e.Attributes & DosAttributes.ReadOnly) != 0) continue; // 只读文件跳过，和 FCB 删除一样
            _kernel.Delete(dp.Parent.Child(e.Name).ToString());
        }
    }

    /// <summary>"*.*" 或 "????????.???" 这种会删光整个目录的模式。</summary>
    private static bool IsEverything(string pattern)
    {
        var (n, e) = DosNames.Split(pattern);
        static bool All(string s, int width) => s.StartsWith('*') || (s.Length >= width && s.All(c => c == '?'));
        return All(n, 8) && All(e, 3);
    }

    // ───────── REN / RENAME ─────────

    private void RenameCommand(ShellCommand cmd)
    {
        if (cmd.Args.Count < 2) throw Fail(Messages.InvalidNumberOfParameters);
        string spec = cmd.Args[0];
        // 新名字只取文件名部分，改名总是在原来的目录里进行
        string newName = RawLeaf(cmd.Args[1]);

        DosPath dp;
        try { dp = _kernel.Resolve(spec, allowWildcards: true); }
        catch (DosException e) when (e.Error == DosError.InvalidDrive) { throw Fail(Messages.InvalidDrive); }
        catch (DosException) { throw Fail(Messages.DuplicateOrNotFound); }
        string? template = DosNames.Normalize(newName, allowWildcards: true);
        if (template is null || dp.Leaf is null) throw Fail(Messages.DuplicateOrNotFound);

        IReadOnlyList<DirEntry> found;
        try { found = _kernel.FindFiles(spec, DosAttributes.None); }
        catch (DosException) { throw Fail(Messages.DuplicateOrNotFound); }
        if (found.Count == 0) throw Fail(Messages.DuplicateOrNotFound);

        bool failed = false;
        foreach (var e in found)
        {
            string target = ApplyTemplate(template, e.Name);
            try
            {
                _kernel.Rename(dp.Parent.Child(e.Name).ToString(), dp.Parent.Child(target).ToString());
            }
            catch (DosException ex) when (ex.Error is not (DosError.WriteProtected or DosError.DriveNotReady))
            {
                failed = true;
            }
        }
        if (failed) throw Fail(Messages.DuplicateOrNotFound);
    }
}
