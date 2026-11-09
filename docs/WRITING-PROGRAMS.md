# 编写 phi-DOS 程序

这份文档讲怎样给 phi-DOS 写自己的 .COM / .EXE 程序：从环境准备、第一个程序，到文件读写、管道、彩色和全屏界面、调试和发布。

目录

1. phi-DOS 的程序是什么
2. 准备
3. 五分钟跑起第一个程序
4. 从零新建一个程序项目
5. 程序的骨架
6. 控制台：输出、输入、按键
7. 颜色与全屏界面
8. 文件与目录
9. 标准输入输出、重定向和管道
10. 运行其他程序、环境变量、磁盘信息
11. 规则与限制
12. 调试
13. 把程序放到盘上
14. 可以参考的现成程序
15. 速查

---

## 1. phi-DOS 的程序是什么

在原版 DOS 里，.COM 和 .EXE 是 x86 机器码。在 phi-DOS 里，它们是 **.NET 程序集**（编译 C# 得到的那个 DLL），里面装的是 IL（中间语言）。

运行一个程序时，系统会：

1. 从磁盘读出文件内容。
2. 检查它有没有使用被禁止的 API（见第 11 节）。
3. 在每个循环和每个方法开头插入检查点，这样死循环也能被打断。
4. 把它加载进一个独立的、用完就能卸载的空间。
5. 找到里面实现了 `IPhiProgram` 的那个类，创建它，调用 `Run`。
6. 程序返回后，卸载它。

所以写一个 phi-DOS 程序，就是写一个普通的 C# 类库，里面有一个类实现 `IPhiProgram`。

.COM 和 .EXE 在 phi-DOS 里没有区别，只影响查找顺序：同一个目录里同名的 .COM 比 .EXE 先被找到。

## 2. 准备

- .NET 10 SDK。
- phi-DOS 的仓库。程序要引用其中的 `src/PhiDos.Abi`，它很小，只有接口定义。
- 一个能跑 phi-DOS 的地方。最方便的是发布包：`phidos.exe` 旁边有个 `A` 文件夹，双击 `run-phidos.bat` 就会把它挂成 A 盘开机。

## 3. 五分钟跑起第一个程序

仓库里有一个现成的示例 `samples/Hello`。

```
dotnet build samples/Hello -p:PhiDosDrive=C:\Users\你\Downloads\phi-DOS-win-x64-small\A
```

`PhiDosDrive` 指向你发布包里的 `A` 文件夹。编译完成后，`HELLO.dll` 会被自动改名成 `HELLO.COM` 拷进去。

然后双击 `run-phidos.bat`，在 DOS 里输入：

```
A:\>HELLO one two
Hello from an IL program!
You gave 2 argument(s): one, two
What is your name? Astro
Nice to meet you, Astro!
You are in A:\, it is 02:41.
```

之后每次改完代码，重新执行那条 `dotnet build`，再回到 DOS 里运行 HELLO 就是新版本，不用重启 phi-DOS。程序每次运行都会从盘上重新加载。

## 4. 从零新建一个程序项目

```
dotnet new classlib -n Guess -f net10.0
```

把 `Guess/Guess.csproj` 改成：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>GUESS</AssemblyName>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <DebugType>none</DebugType>
    <DosName>GUESS.COM</DosName>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\phi-DOS\src\PhiDos.Abi\PhiDos.Abi.csproj" Private="false" />
  </ItemGroup>

  <Target Name="CopyToPhiDosDrive" AfterTargets="Build" Condition="'$(PhiDosDrive)' != ''">
    <Copy SourceFiles="$(TargetPath)" DestinationFiles="$(PhiDosDrive)\$(DosName)" />
  </Target>

</Project>
```

每一项的作用：

| 设置 | 为什么 |
|---|---|
| `AssemblyName` | 程序集名，也就是 DLL 的文件名。建议和盘上的主文件名一致 |
| `ImplicitUsings` 关掉 | 只 using 真正用到的命名空间，读代码时一眼能看出用了什么 |
| `DebugType` none | 不生成 .pdb，盘上只需要 DLL |
| `DosName` | 拷到盘上时的文件名，必须符合 8.3 |
| `Private="false"` | 不把 PhiDos.Abi.dll 复制出来，运行时由系统提供 |
| `CopyToPhiDosDrive` | 和示例一样，`-p:PhiDosDrive=...` 时自动拷进去 |

`ProjectReference` 的路径按你自己放 phi-DOS 仓库的位置改。

删掉 `Class1.cs`，写你自己的程序。

## 5. 程序的骨架

```csharp
using System;
using PhiDos.Abi;

public class Guess : IPhiProgram
{
    public int Run(IDos dos, string[] args)
    {
        int answer = new Random().Next(1, 101);
        dos.WriteLine("I'm thinking of a number from 1 to 100.");
        for (int tries = 1; ; tries++)
        {
            dos.Write("Your guess? ");
            string line = dos.ReadLine();
            dos.WriteLine();
            if (line == "\x1a") return 1;                    // 输入结束
            if (!int.TryParse(line, out int guess)) continue;
            if (guess < answer) dos.WriteLine("Higher.");
            else if (guess > answer) dos.WriteLine("Lower.");
            else
            {
                dos.WriteLine($"Yes! {tries} tries.");
                return 0;
            }
        }
    }
}
```

要点：

- **只能有一个**公开的、非抽象的类实现 `IPhiProgram`，并且有无参构造函数。别的辅助类随便写，设成 `internal` 或者不实现这个接口就行。
- `dos` 是你和系统打交道的唯一渠道，所有输入输出都通过它。
- `args` 是按空格拆开的参数，不含程序名。需要原样的命令行（比如带空格的引号字符串），用 `dos.CommandTail`。
- 返回值是**退出码**，只取低 8 位（0 到 255）。批处理里可以用 `IF ERRORLEVEL n` 判断：退出码大于等于 n 时成立。惯例是 0 表示成功。
- 出错时系统会抛出 `DosException`，`Error` 属性是 DOS 的错误码。没有捕获的异常会让程序结束，屏幕显示 `Abnormal program termination` 和异常信息，退出码 255。

## 6. 控制台：输出、输入、按键

### 输出

```csharp
dos.Write("no newline");
dos.WriteLine("with newline");   // 行尾是 CR LF
dos.WriteLine();                 // 空行
```

文字按 UTF-8 处理，中文直接写。屏幕是 80 列 × 25 行，中文字占两格。

### 整行输入

```csharp
string line = dos.ReadLine();
dos.WriteLine();
```

- 用户可以用退格、Esc（作废重输）、F1、F3（复制上一次的输入）编辑。
- 用户按回车时**只回显回车，不换行**，这是原版 DOS 的行为，所以读完一般要自己 `WriteLine()`。
- 一行最多 127 字节。
- 标准输入被重定向到文件、并且已经读完时，返回 `"\x1a"`（Ctrl+Z）。

### 单个按键

```csharp
char c = dos.ReadKey(echo: false);
if (c == '\0')
{
    char scan = dos.ReadKey(echo: false);   // 功能键：再读一次得到扫描码
    if (scan == 72) { /* 上箭头 */ }
}
```

`echo` 为 true 时按下的字符会显示在屏幕上。

功能键按 DOS 的规矩分两次返回：第一次是 `'\0'`，第二次是扫描码。

| 键 | 扫描码 | 键 | 扫描码 |
|---|---|---|---|
| F1 到 F10 | 59 到 68 | Home | 71 |
| 上 | 72 | PageUp | 73 |
| 左 | 75 | 右 | 77 |
| End | 79 | 下 | 80 |
| PageDown | 81 | Insert | 82 |
| Delete | 83 | | |

普通键直接返回字符：回车是 `'\r'`，退格是 `'\b'`，Esc 是 `'\x1b'`，Tab 是 `'\t'`，Ctrl+字母是 1 到 26（Ctrl+Z 就是 `'\x1a'`）。

### 不等待地检查按键

```csharp
while (!dos.KeyAvailable)
{
    // 做别的事，比如游戏的一帧
}
char c = dos.ReadKey(echo: false);
```

### Ctrl+C

用户按 Ctrl+C 时，程序会在下一次控制台调用时被终止，屏幕显示 `^C`。程序不需要处理它。

纯计算的循环里不调用 IDos 时，和原版一样不理 Ctrl+C；用户可以先执行 `BREAK ON`，这样任何循环都能被 Ctrl+C 打断。无论如何，宿主的"重启"（终端版的 F12）总是有效的。

## 7. 颜色与全屏界面

phi-DOS 内置了 ANSI.SYS 的功能，往屏幕上写转义序列就能控制颜色和光标。

### 颜色

```csharp
dos.Write("\x1b[1;33m");   // 亮黄色
dos.Write("Warning");
dos.Write("\x1b[0m");      // 恢复默认
```

| 序列 | 效果 |
|---|---|
| `ESC[0m` | 恢复默认（灰字黑底） |
| `ESC[1m` | 高亮（前景变成亮色） |
| `ESC[5m` | 闪烁 |
| `ESC[7m` | 反色 |
| `ESC[30m` 到 `ESC[37m` | 前景：黑 红 绿 黄 蓝 品红 青 白 |
| `ESC[40m` 到 `ESC[47m` | 背景，顺序同上 |

可以用分号连写，例如 `ESC[1;37;44m` 是蓝底亮白字。C# 里 ESC 写作 `\x1b`。

注意：`\x1b` 后面紧跟的字符如果也是十六进制数字，C# 会把它们连在一起。例如 `"\x1bA"` 是一个字符 U+01BA。保险的写法是 `"\u001b"`，或者像上面那样把颜色序列单独写一个字符串。

程序结束前记得写 `ESC[0m`，否则之后的提示符也会带着你的颜色。

### 光标和清屏

| 序列 | 效果 |
|---|---|
| `ESC[2J` | 清屏，光标回到左上角 |
| `ESC[行;列H` | 把光标移到指定位置，从 1 开始数 |
| `ESC[K` | 清除从光标到行尾 |
| `ESC[nA` / `B` / `C` / `D` | 光标上 / 下 / 右 / 左移 n 格 |

### 写全屏程序的经验

VI 就是这样做的，可以参考 `src/PhiDos.Programs/Vi/Vi.cs`：

- 每次按键后把整个屏幕重画一遍。80×25 很小，足够快，代码也最简单。
- 用 `ESC[行;1H` 定位到每一行再写，不要靠换行往下走。
- **不要写到第 25 行第 80 列。** 那会让屏幕向上滚一行，整个画面就错位了。最后一行最多写 79 格。
- 一行写满 80 格时，光标已经跳到下一行开头，这时再写 `ESC[K` 会把下一行清掉。写满时就不要再清。
- 自己计算中文的宽度：中日韩文字和全角符号占两格。
- 退出前 `ESC[2J` 清屏，把干净的屏幕还给 COMMAND。用 `try ... finally` 保证 Ctrl+C 退出时也会清屏。

## 8. 文件与目录

### 路径

- 写法和 DOS 一样：`A:\GAMES\SAVE.DAT`、`..\README.TXT`、`B:NOTES`。
- 名字是 8.3 的，不区分大小写，太长的部分会被截掉。8.3 按 UTF-8 字节算，一个汉字 3 字节，所以主名最多两个汉字。
- 每个驱动器有自己的当前目录。

### 读文件

```csharp
int h = dos.Open("README.TXT", FileAccessMode.Read);
try
{
    var buffer = new byte[512];
    int n;
    while ((n = dos.Read(h, buffer)) > 0)
    {
        // 处理 buffer[0..n]
    }
}
finally
{
    dos.Close(h);
}
```

句柄是一个 int，用完一定要 `Close`。每个程序最多同时打开 20 个句柄（0 到 4 已经被标准设备占用）。程序结束时，系统会把没关的句柄关掉。

### 写文件

```csharp
int h = dos.Create("OUT.TXT");          // 不存在就新建，存在就清空
dos.Write(h, Encoding.UTF8.GetBytes("line one\r\n"));
dos.Close(h);
```

要在已有文件后面追加：

```csharp
int h = dos.Open("LOG.TXT", FileAccessMode.ReadWrite);
dos.Seek(h, 0, SeekOrigin.End);
dos.Write(h, data);
dos.Close(h);
```

写 0 个字节表示把文件截断到当前位置。

文本文件请遵守 DOS 的习惯：每行用 CR LF（`\r\n`）结尾；读的时候遇到 Ctrl+Z（0x1A）就当作文件结束。

### 目录和查找

```csharp
foreach (DirEntry e in dos.FindFiles("*.TXT"))
    dos.WriteLine($"{e.Name} {e.Size} {e.LastWriteTime}");

// 连子目录一起找
foreach (DirEntry e in dos.FindFiles("*.*", DosAttributes.Directory))
    if (e.IsDirectory && e.Name is not "." and not "..") dos.WriteLine(e.Name);

dos.MakeDirectory("SAVES");
dos.ChangeDirectory("SAVES");
dos.RemoveDirectory("\\OLD");
dos.Delete("TEMP.TXT");
dos.Rename("A.TXT", "B.TXT");          // 也可以在同一个盘里移到别的目录
```

通配符的规则和 DOS 一样：`*` 匹配这一部分剩下的全部，`?` 匹配一个字符（也可以匹配"没有字符"）。注意 `*` 只管主名或扩展名中的一部分，`*` 本身只匹配没有扩展名的文件，要所有文件请写 `*.*`。

### 错误处理

```csharp
try
{
    int h = dos.Open(name, FileAccessMode.Read);
    ...
}
catch (DosException e) when (e.Error == DosError.FileNotFound)
{
    dos.WriteLine("File not found");
}
```

常见的错误码：

| DosError | 含义 |
|---|---|
| FileNotFound | 文件不存在 |
| PathNotFound | 路径里的目录不存在 |
| AccessDenied | 不允许（比如把目录当文件打开、目录不空、只读文件） |
| InvalidHandle | 句柄无效 |
| TooManyOpenFiles | 句柄用完了 |
| InvalidDrive | 没有这个驱动器 |
| DiskFull | 磁盘满了 |
| WriteProtected / DriveNotReady | 写保护 / 没插盘，用户在提示里选了 Ignore |

写保护和没插盘时，屏幕会先出现原版的 `Abort, Retry, Ignore?`。用户选 Abort 时程序直接结束；选 Retry 会重试；选 Ignore 时你的程序才会收到 `DosException`。

## 9. 标准输入输出、重定向和管道

每个程序一开始都有这几个句柄：

| 句柄 | 是什么 | 默认指向 |
|---|---|---|
| 0 | 标准输入 | 键盘 |
| 1 | 标准输出 | 屏幕 |
| 2 | 标准错误 | 屏幕 |
| 3 | AUX | NUL |
| 4 | PRN | NUL |

`Write`、`WriteLine`、`ReadKey`、`ReadLine` 都走句柄 0 和 1，所以用户写 `HELLO > OUT.TXT` 时，你的输出会自动进文件，程序什么都不用改。

### 写一个过滤器

像 SORT、FIND 这样从标准输入读、往标准输出写的程序叫过滤器，可以接在管道里：

```csharp
public class Upper : IPhiProgram
{
    public int Run(IDos dos, string[] args)
    {
        var buffer = new byte[4096];
        int n;
        while ((n = dos.Read(0, buffer)) > 0)          // 从标准输入读
        {
            string text = Encoding.UTF8.GetString(buffer, 0, n).ToUpperInvariant();
            dos.Write(1, Encoding.UTF8.GetBytes(text)); // 写到标准输出
        }
        return 0;
    }
}
```

```
A:\>TYPE README.TXT | UPPER | MORE
```

直接从键盘运行时，`Read(0, ...)` 一次读到用户输入的一整行，用户按 Ctrl+Z 再回车表示输入结束。

### 数据来自管道时，怎样读键盘

MORE 的做法（照原版）：

```csharp
int data = dos.Duplicate(0);   // 复制一份标准输入，以后从它读数据
dos.Close(0);                  // 关掉标准输入
dos.Duplicate(2);              // 复制标准错误（屏幕和键盘）；新句柄是最小的空闲号，也就是 0
// 现在 dos.ReadKey 读的是键盘，dos.Read(data, ...) 读的是管道里的数据
```

## 10. 运行其他程序、环境变量、磁盘信息

### 运行其他程序

```csharp
int code = dos.Exec("SORT", "/R");
```

- 不写扩展名时依次找 .COM、.EXE；不写路径时先找当前目录，再按 PATH 找。
- 不能用它运行 .BAT（原版 DOS 也不行）。
- 子程序继承你的全部句柄，所以你的重定向也会传给它。
- 返回子程序的退出码。找不到时抛出 `DosException(FileNotFound)`。

### 环境变量

```csharp
string? user = dos.GetEnv("USER");   // 用户之前执行过 SET USER=...
string? path = dos.GetEnv("PATH");
```

每个程序拿到的是一份环境变量的副本，只能读不能改。

### 当前驱动器、目录、时间

```csharp
char drive = dos.CurrentDrive;                  // 'A'
string dir = dos.CurrentDirectory;              // "\GAMES"
string other = dos.GetCurrentDirectory('B');
DateTime now = dos.Now;                         // DATE、TIME 设定的时间
```

### 磁盘

```csharp
DiskInfo info = dos.GetDiskInfo('A');
dos.WriteLine($"{info.FreeBytes} bytes free of {info.TotalBytes}, label {info.VolumeLabel ?? "(none)"}");
if (info.IsFolder) dos.WriteLine("this drive is a folder on the host");
```

`dos.FormatDisk('B', "LABEL")` 会清空一张虚拟磁盘，文件夹驱动器不能格式化。慎用。

## 11. 规则与限制

### 程序文件

- 正好一个公开类实现 `IPhiProgram`，有无参构造函数。
- 文件不超过 640KB。
- 只能依赖 .NET 自带的库和 PhiDos.Abi。引用别的 DLL 时，盘上没有那个 DLL，加载会失败。
- 目标框架和系统一致（net10.0）。

### 禁止使用的 API

加载时系统会检查程序引用了哪些 API，下面这些会被拒绝，运行时显示 `Error in EXE file`，下一行写着具体原因，例如 `forbidden API: System.IO.File`：

| 类别 | 禁止的 | 用什么代替 |
|---|---|---|
| 电脑上的文件 | File、FileInfo、FileStream、Directory、DirectoryInfo、DriveInfo、StreamReader、StreamWriter 等 | `dos.Open` / `dos.Create` / `dos.FindFiles` |
| 控制台 | Console | `dos.Write` / `dos.ReadKey` |
| 进程 | Process、Environment.Exit、Environment.FailFast、读写宿主环境变量、宿主当前目录 | 返回退出码；`dos.GetEnv` |
| 线程和异步 | Thread、ThreadPool、Timer、Task、Parallel，以及 async / await（它会用到 Task） | 普通的循环；`dos.KeyAvailable` 轮询 |
| 动态代码 | Assembly、Activator、System.Reflection.Emit、System.Runtime.Loader | 不需要 |
| 本机代码 | P/Invoke、Marshal、NativeLibrary | 不需要 |
| 网络 | System.Net | 不支持 |

可以放心用的：字符串、集合、LINQ、`Random`、`Math`、`StringBuilder`、`Encoding`、`MemoryStream`、`DateTime`、`Regex`、`yield return` 等等，只要不碰上表里的东西。

这只是基本防护，不是安全沙箱。它挡的是无意中写出的危险代码。

### 系统自动帮你做的

- **死循环**：每个循环里都被插入了检查点。重启总能打断它；BREAK ON 时 Ctrl+C 也能打断。
- **无限递归**：每个方法开头会检查剩余栈空间，快用完时抛出 `InsufficientExecutionStackException`，程序以 `Abnormal program termination` 结束，不会把整个宿主弄崩溃。
- **卸载**：程序结束后它占用的内存会被回收，反复运行不会越占越多。

## 12. 调试

### 先看错误信息

- `Bad command or file name`：盘上没找到这个名字的 .COM / .EXE。用 DIR 确认文件在不在，名字是不是 8.3。
- `Error in EXE file` 加原因：
  - `not a .NET assembly`：文件不是 .NET 程序集，比如拷错了文件。
  - `no public class implements IPhiProgram`：入口类不是 public，或者没实现接口。
  - `more than one class implements IPhiProgram`：入口类多于一个。
  - `forbidden API: ...`：用了第 11 节表里的 API。
  - `missing dependency: ...`：引用了盘上不存在的 DLL。
- `Abnormal program termination` 加异常：程序自己抛出了没有处理的异常，第二行是异常类型和消息。

### 在 IDE 里打断点

盘上加载的程序是改写过的副本，IDE 的断点打不进去。想单步调试时，用**内置程序表**：让系统直接调用你项目里的类，而不是从盘上加载。

新建一个控制台项目，引用 PhiDos.Core、PhiDos.SystemDisk 和你的程序项目：

```csharp
using PhiDos.Abi;
using PhiDos.Core.Machine;
using PhiDos.SystemDisk;

var options = new MachineOptions
{
    PromptForDateTimeAtBoot = false,
    BuiltinPrograms = new Dictionary<string, Func<IPhiProgram>>(StringComparer.OrdinalIgnoreCase)
    {
        ["GUESS"] = () => new Guess(),   // 直接用你的类
    },
};

using var machine = new DosMachine(options);
machine.MountImage('A', SystemPrograms.CreateSystemDisk(), ownsStream: true);

// 内置程序表要求盘上有同名文件，内容无所谓
machine.WithDrive('A', d => d.CreateFile(["GUESS.COM"], DateTime.Now).Close(DateTime.Now));

machine.Start();
machine.Console.WaitForInputIdle(TimeSpan.FromSeconds(5));

// 模拟用户输入，在 Guess.Run 里打的断点会命中
machine.Console.PushText("GUESS\n50\n25\n");
machine.Console.WaitForInputIdle(TimeSpan.FromMinutes(10));
Console.Write(machine.Console.TakeOutput());
```

这种方式下程序不经过 API 检查，也没有注入检查点。调试完记得再用真正的盘测一次。

### 写自动测试

phi-DOS 自己的测试就是这么写的，可以参考 `tests/PhiDos.Tests/TestMachine.cs` 和 `LoaderTests.cs`：开一台机器、把程序放上盘、送入按键、等它回到提示符，再检查 `machine.Console.Screen.GetLine(n)` 或者输出文字。

### 内部错误

如果看到 `phi-DOS internal error`，说明是 phi-DOS 自己的问题，不是你的程序的问题。设置环境变量 `PHIDOS_TRACE=1` 后再运行，会显示完整的调用栈，请把它报告出来。

## 13. 把程序放到盘上

| 方式 | 做法 | 适合 |
|---|---|---|
| A 文件夹 | `dotnet build -p:PhiDosDrive=...\A`，再双击 `run-phidos.bat` | 平时开发 |
| 在 DOS 里复制 | `phidos -a 盘.phidisk -b 编译输出目录` 开机，然后 `COPY B:GUESS.DLL A:GUESS.COM` | 往 .phidisk 镜像里放 |
| mtools | 镜像改名 .img，`mcopy -i 盘.img GUESS.dll ::GUESS.COM` | 不开 phi-DOS 也能放 |
| 代码 | `machine.WithDrive('A', d => ...)`，见 GODOT-INTEGRATION.md 第 8.4 节 | 游戏里按剧情放程序 |
| 成为系统命令 | 把项目放进 `src/PhiDos.Programs/`，在 `SystemPrograms.props` 里加一行 | 每张新建的系统盘都自带 |

注意文件夹驱动器只认 8.3 名字：编译输出目录里 `GUESS.dll` 看得见（显示为 `GUESS.DLL`），`GUESS.deps.json` 这种名字 DOS 看不见，正好不会混进来。

## 14. 可以参考的现成程序

系统自带的命令都是用同样的方式写的，源码在 `src/PhiDos.Programs/` 下：

| 程序 | 值得看的地方 |
|---|---|
| `samples/Hello` | 最小的完整例子 |
| FIND | 自己解析 CommandTail、打开多个文件、按行处理 |
| SORT | 过滤器：从标准输入读、往标准输出写 |
| MORE | Duplicate 句柄的技巧，分页 |
| FORMAT | GetDiskInfo、FormatDisk、问答式交互 |
| ILDOSVER | 颜色、对齐、色板 |
| VI | 全屏界面、按键处理、宽字符、读写文本文件 |

## 15. 速查

```csharp
using PhiDos.Abi;

public class Prog : IPhiProgram
{
    public int Run(IDos dos, string[] args)
    {
        dos.WriteLine("\x1b[1;32mhello\x1b[0m");      // 彩色输出
        string line = dos.ReadLine(); dos.WriteLine(); // 读一行
        char key = dos.ReadKey(echo: false);           // 读一个键

        int h = dos.Create("OUT.TXT");                 // 写文件
        dos.Write(h, System.Text.Encoding.UTF8.GetBytes(line + "\r\n"));
        dos.Close(h);

        foreach (var e in dos.FindFiles("*.*"))        // 列文件
            dos.WriteLine(e.Name);

        return 0;                                      // 退出码
    }
}
```

检查清单：

- [ ] 正好一个 public 类实现 IPhiProgram
- [ ] 引用 PhiDos.Abi 时写了 `Private="false"`
- [ ] 没用 File、Console、Thread、Task、async、Environment.Exit 这些
- [ ] 打开的句柄都 Close 了
- [ ] 改过颜色的话，结束前写了 `ESC[0m`
- [ ] 全屏程序不写第 25 行第 80 列，退出时清屏
- [ ] 盘上的文件名符合 8.3
