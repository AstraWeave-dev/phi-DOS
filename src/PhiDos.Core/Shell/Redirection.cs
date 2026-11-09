using PhiDos.Abi;
using PhiDos.Core.Kernel;

namespace PhiDos.Core.Shell;

internal sealed partial class CommandShell
{
    /// <summary>
    /// 处理一行里的管道（|）和重定向（&lt;、&gt;、&gt;&gt;），然后执行。
    /// 管道照 DOS 2.0 的做法：前一个命令的输出先写进当前驱动器根目录的临时文件
    /// %PIPE1.$$$ / %PIPE2.$$$，后一个命令再从这个文件读，用完删除。
    /// </summary>
    private void RunWithRedirection(string line)
    {
        var segments = SplitPipes(line);
        if (segments.Count == 1)
        {
            RunSegment(segments[0], pipeIn: null, pipeOut: null);
            return;
        }

        char drive = _kernel.CurrentDrive;
        string[] pipes = [$"{drive}:\\%PIPE1.$$$", $"{drive}:\\%PIPE2.$$$"];
        try
        {
            for (int i = 0; i < segments.Count; i++)
            {
                string? input = i > 0 ? pipes[(i - 1) % 2] : null;
                string? output = i < segments.Count - 1 ? pipes[i % 2] : null;
                if (!RunSegment(segments[i], input, output)) break;
            }
        }
        finally
        {
            foreach (string p in pipes)
            {
                try { _kernel.Delete(p); } catch (DosException) { }
            }
        }
    }

    /// <summary>按 | 拆开。双引号里的 | 不算（方便 FIND "a|b" 这种用法）。</summary>
    private static List<string> SplitPipes(string line)
    {
        var parts = new List<string>();
        int start = 0;
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == '|' && !quoted)
            {
                parts.Add(line[start..i]);
                start = i + 1;
            }
        }
        parts.Add(line[start..]);
        return parts;
    }

    /// <summary>
    /// 执行管道中的一段：解析它自己的重定向，替换 COMMAND 的标准输入输出句柄，
    /// 执行完再换回来。子程序继承句柄，所以重定向会自动传给它。返回 false 表示管道要中止。
    /// </summary>
    private bool RunSegment(string text, string? pipeIn, string? pipeOut)
    {
        var (command, inFile, outFile, append) = ParseRedirection(text);
        inFile ??= pipeIn;
        outFile ??= pipeOut;

        OpenFile? newIn = null, newOut = null;
        try
        {
            if (inFile is not null)
            {
                try { newIn = OpenRedirect(inFile, FileAccessMode.Read, false, false); }
                catch (DosException)
                {
                    Err(pipeIn is not null && inFile == pipeIn ? Messages.PipeError : Messages.FileNotFound);
                    return false;
                }
            }
            if (outFile is not null)
            {
                try { newOut = OpenRedirect(outFile, FileAccessMode.Write, create: true, append); }
                catch (DosException)
                {
                    Err(pipeOut is not null && outFile == pipeOut ? Messages.PipeError : Messages.FileCreationError);
                    return false;
                }
            }
        }
        catch
        {
            newIn?.Close();
            throw;
        }

        OpenFile? oldIn = newIn is not null ? _process.Replace(0, newIn) : null;
        OpenFile? oldOut = newOut is not null ? _process.Replace(1, newOut) : null;
        try
        {
            ExecuteSimple(command);
        }
        finally
        {
            if (newIn is not null) { _process.Replace(0, oldIn); CloseQuietly(newIn); }
            if (newOut is not null) { _process.Replace(1, oldOut); CloseQuietly(newOut); }
        }
        return true;
    }

    private static void CloseQuietly(OpenFile file)
    {
        if (--file.RefCount > 0) return;
        try { file.Close(); } catch (DosException) { }
    }

    /// <summary>打开重定向用的文件，得到一个不属于任何句柄的"打开的文件"。</summary>
    private OpenFile OpenRedirect(string path, FileAccessMode mode, bool create, bool append)
    {
        int handle;
        if (create && append)
        {
            try
            {
                handle = _kernel.Open(_process, path, FileAccessMode.ReadWrite);
                _kernel.Seek(_process, handle, 0, SeekOrigin.End);
            }
            catch (DosException e) when (e.Error == DosError.FileNotFound)
            {
                handle = _kernel.Create(_process, path);
            }
        }
        else if (create)
        {
            handle = _kernel.Create(_process, path);
        }
        else
        {
            handle = _kernel.Open(_process, path, mode);
        }
        // 从句柄表里拿出来，交给调用者管理
        var file = _process.Handles[handle]!;
        _process.Handles[handle] = null;
        return file;
    }

    /// <summary>从命令里摘出 &lt; 文件、&gt; 文件、&gt;&gt; 文件。</summary>
    private static (string Command, string? In, string? Out, bool Append) ParseRedirection(string text)
    {
        var command = new System.Text.StringBuilder();
        string? input = null, output = null;
        bool append = false;
        bool quoted = false;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '"') quoted = !quoted;
            if (!quoted && (c == '<' || c == '>'))
            {
                bool isAppend = c == '>' && i + 1 < text.Length && text[i + 1] == '>';
                i += isAppend ? 2 : 1;
                while (i < text.Length && text[i] is ' ' or '\t') i++;
                int start = i;
                while (i < text.Length && text[i] is not (' ' or '\t' or '<' or '>')) i++;
                string target = text[start..i];
                if (c == '<') input = target;
                else { output = target; append = isAppend; }
                // 重定向符号前面的空格会留在命令里，所以 ECHO hi > X 写进文件的是 "hi "，和原版一样
                continue;
            }
            command.Append(c);
            i++;
        }
        return (command.ToString(), input, output, append);
    }
}
