// 行为照 MS-DOS 2.0 的 SORT.ASM；提示文字取自 SORTMES.ASM。
// MS-DOS v1.25 and v2.0 Source Code, Copyright (c) Microsoft Corporation. Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using PhiDos.Abi;

namespace PhiDos.Programs;

/// <summary>
/// SORT [/R] [/+n]：读入标准输入的所有行，排序后写到标准输出。
/// /R 倒序；/+n 从第 n 列开始比较。比较时不区分大小写。
/// </summary>
public sealed class Sort : IPhiProgram
{
    private const int MaxInput = 63 * 1024; // 原版一次最多排序 63KB

    public int Run(IDos dos, string[] args)
    {
        bool reverse = false;
        int column = 1;
        foreach (string a in args)
        {
            string s = a.ToUpperInvariant();
            if (s == "/R") reverse = true;
            else if (s.StartsWith("/+") && int.TryParse(s.AsSpan(2), out int c) && c > 0) column = c;
            else
            {
                dos.Write("SORT: Invalid parameter\r\n");
                return 1;
            }
        }

        var bytes = new List<byte>();
        var buffer = new byte[4096];
        while (true)
        {
            int n = dos.Read(0, buffer);
            if (n == 0) break;
            int z = Array.IndexOf(buffer, (byte)0x1A, 0, n);
            for (int i = 0; i < (z >= 0 ? z : n); i++) bytes.Add(buffer[i]);
            if (bytes.Count > MaxInput)
            {
                dos.Write("SORT: Insufficient memory\r\n");
                return 1;
            }
            if (z >= 0) break;
        }

        string text = Encoding.UTF8.GetString(bytes.ToArray());
        var lines = new List<string>(text.Replace("\r\n", "\n").Split('\n'));
        if (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

        string Key(string line) => (column - 1 < line.Length ? line[(column - 1)..] : "").ToUpperInvariant();
        // List.Sort 不保证相等元素的顺序，这里带上原始序号保证结果稳定
        var indexed = new List<(string Line, int Index)>();
        for (int i = 0; i < lines.Count; i++) indexed.Add((lines[i], i));
        indexed.Sort((a, b) =>
        {
            int c = string.CompareOrdinal(Key(a.Line), Key(b.Line));
            if (reverse) c = -c;
            return c != 0 ? c : a.Index.CompareTo(b.Index);
        });

        foreach (var (line, _) in indexed)
            dos.Write(1, Encoding.UTF8.GetBytes(line + "\r\n"));
        return 0;
    }
}
