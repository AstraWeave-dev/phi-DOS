using System;
using System.Text;
using PhiDos.Abi;

namespace PhiDos.Programs;

/// <summary>
/// MORE：从标准输入读文字，一屏一屏显示，每屏之后显示 -- More -- 并等待按键。
/// 用法：MORE &lt; 文件，或者 命令 | MORE。
/// </summary>
public sealed class More : IPhiProgram
{
    private const int Rows = 25;
    private const int Columns = 80;

    public int Run(IDos dos, string[] args)
    {
        // 把标准输入复制一份用来读数据，再让标准输入改为指向标准错误（键盘），
        // 这样即使数据来自管道，"按任意键继续"也能读到真正的键盘。
        int data = dos.Duplicate(0);
        dos.Close(0);
        dos.Duplicate(2);

        var decoder = new UTF8Encoding(false).GetDecoder();
        var buffer = new byte[4096];
        var chars = new char[4096];
        int row = 0, column = 0;
        while (true)
        {
            int n = dos.Read(data, buffer);
            if (n == 0) break;
            int z = Array.IndexOf(buffer, (byte)0x1A, 0, n);
            if (z >= 0) n = z;
            int count = decoder.GetChars(buffer, 0, n, chars, 0);
            for (int i = 0; i < count; i++)
            {
                char c = chars[i];
                dos.Write(c.ToString());
                if (c == '\n') { row++; column = 0; }
                else if (c == '\r') column = 0;
                else if (c == '\t') column = (column / 8 + 1) * 8;
                else if (c >= ' ') column++;
                if (column >= Columns) { row++; column = 0; }
                if (row >= Rows - 1)
                {
                    dos.Write("\r-- More --");
                    dos.ReadKey(echo: false);
                    dos.Write("\r\n");
                    row = 0;
                }
            }
            if (z >= 0) break;
        }
        return 0;
    }
}
