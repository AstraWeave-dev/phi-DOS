using System;
using PhiDos.Abi;

namespace Samples;

/// <summary>
/// 示例程序：问名字、打招呼、显示参数和当前目录，然后返回退出码。
/// 每个 phi-DOS 程序里都要有正好一个这样的公开类。
/// </summary>
public class Hello : IPhiProgram
{
    public int Run(IDos dos, string[] args)
    {
        // ANSI 颜色：ESC[1;33m 是亮黄色，ESC[0m 恢复默认
        dos.WriteLine("\x1b[1;33mHello from an IL program!\x1b[0m");

        if (args.Length > 0)
            dos.WriteLine($"You gave {args.Length} argument(s): {string.Join(", ", args)}");

        dos.Write("What is your name? ");
        string name = dos.ReadLine();
        dos.WriteLine(); // 行输入只回显回车，需要自己换行

        if (name.Length == 0 || name == "\x1a")
        {
            dos.WriteLine("Never mind.");
            return 1; // 退出码，批处理里可以用 IF ERRORLEVEL 1 判断
        }

        dos.WriteLine($"Nice to meet you, \x1b[1;36m{name}\x1b[0m!");
        dos.WriteLine($"You are in {dos.CurrentDrive}:{dos.CurrentDirectory}, it is {dos.Now:HH:mm}.");
        return 0;
    }
}
