// 行为照 MS-DOS 2.0 的 FIND.ASM；提示文字取自 FINDMES.ASM。
// MS-DOS v1.25 and v2.0 Source Code, Copyright (c) Microsoft Corporation. Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using PhiDos.Abi;

namespace PhiDos.Programs;

/// <summary>
/// FIND [/V] [/C] [/N] "字符串" [文件 ...]
/// 显示包含该字符串的行（区分大小写）。/V 显示不包含的行，/C 只显示行数，/N 显示行号。
/// 不给文件时从标准输入读。
/// </summary>
public sealed class Find : IPhiProgram
{
    public int Run(IDos dos, string[] args)
    {
        string tail = dos.CommandTail;
        bool invert = false, countOnly = false, numbers = false;

        int i = 0;
        // 开关在字符串之前
        while (true)
        {
            while (i < tail.Length && tail[i] is ' ' or '\t') i++;
            if (i < tail.Length && tail[i] == '/')
            {
                if (i + 1 >= tail.Length) return Error(dos, "FIND: Invalid number of parameters\r\n");
                char sw = char.ToUpperInvariant(tail[i + 1]);
                if (sw == 'V') invert = true;
                else if (sw == 'C') countOnly = true;
                else if (sw == 'N') numbers = true;
                else return Error(dos, "FIND: Invalid Parameter " + tail[i + 1] + "\r\n");
                i += 2;
                continue;
            }
            break;
        }

        if (i >= tail.Length) return Error(dos, "FIND: Invalid number of parameters\r\n");
        if (tail[i] != '"') return Error(dos, "FIND: Syntax error\r\n");
        // 两个连续的双引号表示字符串里的一个双引号
        var sb = new StringBuilder();
        int j = i + 1;
        while (true)
        {
            if (j >= tail.Length) return Error(dos, "FIND: Syntax error\r\n");
            if (tail[j] == '"')
            {
                if (j + 1 < tail.Length && tail[j + 1] == '"') { sb.Append('"'); j += 2; continue; }
                break;
            }
            sb.Append(tail[j++]);
        }
        string needle = sb.ToString();
        string rest = tail[(j + 1)..];

        var files = new List<string>();
        foreach (string f in rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (f.StartsWith('/'))
            {
                char sw = f.Length > 1 ? char.ToUpperInvariant(f[1]) : ' ';
                if (sw == 'V') invert = true;
                else if (sw == 'C') countOnly = true;
                else if (sw == 'N') numbers = true;
                else return Error(dos, "FIND: Invalid Parameter " + (f.Length > 1 ? f[1] : ' ') + "\r\n");
            }
            else files.Add(f);
        }

        if (files.Count == 0)
        {
            Search(dos, 0, needle, invert, countOnly, numbers, header: null);
            return 0;
        }

        foreach (string f in files)
        {
            int h;
            try
            {
                h = dos.Open(f, FileAccessMode.Read);
            }
            catch (DosException)
            {
                dos.Write("FIND: File not found " + f.ToUpperInvariant() + "\r\n");
                continue;
            }
            try
            {
                Search(dos, h, needle, invert, countOnly, numbers, header: "\r\n---------- " + f.ToUpperInvariant());
            }
            finally
            {
                dos.Close(h);
            }
        }
        return 0;
    }

    private static void Search(IDos dos, int handle, string needle, bool invert, bool countOnly, bool numbers, string? header)
    {
        int count = 0, lineNo = 0;
        if (header is not null && !countOnly) dos.Write(header + "\r\n");
        foreach (string line in ReadLines(dos, handle))
        {
            lineNo++;
            bool hit = line.Contains(needle, StringComparison.Ordinal);
            if (hit == invert) continue;
            count++;
            if (!countOnly) dos.Write((numbers ? $"[{lineNo}]" : "") + line + "\r\n");
        }
        if (countOnly) dos.Write((header is not null ? header + ": " : "") + count + "\r\n");
    }

    private static IEnumerable<string> ReadLines(IDos dos, int handle)
    {
        var bytes = new List<byte>();
        var buffer = new byte[4096];
        bool eof = false;
        while (!eof)
        {
            int n = dos.Read(handle, buffer);
            if (n == 0) break;
            for (int k = 0; k < n; k++)
            {
                byte b = buffer[k];
                if (b == 0x1A) { eof = true; break; }
                if (b == '\n')
                {
                    yield return Decode(bytes);
                    bytes.Clear();
                }
                else if (b != '\r') bytes.Add(b);
            }
        }
        if (bytes.Count > 0) yield return Decode(bytes);
    }

    private static string Decode(List<byte> bytes) => Encoding.UTF8.GetString(bytes.ToArray());

    private static int Error(IDos dos, string message)
    {
        dos.Write(message);
        return 2;
    }
}
