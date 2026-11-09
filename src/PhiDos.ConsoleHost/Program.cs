using System.Text;
using PhiDos.ConsoleHost;

// 终端版宿主：在系统终端里运行一台 phi-DOS 虚拟机。
Console.OutputEncoding = new UTF8Encoding(false);
Console.InputEncoding = new UTF8Encoding(false);
return HostApp.Run(args);
