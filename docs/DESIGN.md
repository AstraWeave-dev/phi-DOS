# phi-DOS 设计文档

## 项目概述
phi-DOS 是一个用 C# 实现的、行为仿照 MS-DOS 2.0 的磁盘操作系统。它有两种用法：

1. 嵌入繁星工作室视觉小说 ProjectVN（Godot 4 .NET）内部，作为游戏中的"系统底层"支线。与 phi-engine 同属一个工作室产品线。
2. 作为独立应用程序运行，挂载磁盘后直接使用，方便在电脑上开发和测试。

两种用法共用同一个 Core，行为完全一致。

## 目标
- 行为级重写：不模拟 CPU，用 C# 按功能重新实现 DOS 的行为。
- 统一以 MS-DOS 2.0 为行为基准（含目录支持）。不混用 1.x 的行为。
- 参考资料：微软开源的 MS-DOS 源码（MIT 协议，github.com/microsoft/MS-DOS），以 2.0 为主。源码为汇编，参考其逻辑与行为，不逐行翻译。
- 保持原汁原味：8.3 文件名、目录、原版扩展名（.COM / .EXE / .BAT / .SYS）、原版提示信息。
- 程序即 IL：磁盘上的 .COM 文件内容是 .NET 程序集（IL），由系统加载执行，跨架构运行。

## 非目标
- 不运行原版 x86 二进制（未来可通过静态重编译或模拟器另行扩展）。
- 不实现 FCB 式文件调用（只做 2.0 的句柄式 API）。
- 不实现 EMS、FAT16、大于 32MB 的分区、代码页切换、安装程序等硬件相关功能。

## 技术平台
- .NET 10。
- 字符集：UTF-8。控制台输出、文件内容、文件名都按 UTF-8 处理。
  8.3 的限制按字节计算：主名最多 8 字节、扩展名最多 3 字节，所以一个主名最多放 2 个汉字。
  字符屏幕上中日韩文字占两格。
- ProjectVN 的 Godot 项目也需要以 net10.0 为目标框架，否则无法引用 Core。

## 分层架构
1. Host（宿主层）：显示器和键盘。负责字符屏幕渲染、键盘输入。只与 DosMachine 打交道，不关心机器内部。
2. Machine（机器层）：DosMachine，即"虚拟电脑"。负责挂载驱动器、开机、重启、关机。
3. Kernel（内核层）：IDos 接口实现、路径解析、驱动器、文件句柄、进程执行。
4. Shell（外壳层）：COMMAND.COM 的等价物，负责命令解析、内置命令、程序查找。
5. Programs（程序层）：外部命令与用户程序，以 IL 程序集形式存于磁盘。

## 解决方案结构
- PhiDos.Abi：极小的类库，只包含 IDos、IPhiProgram 等接口和公共类型。程序只引用它。
- PhiDos.Core：机器、内核与外壳，纯 C#，不依赖 Godot。依赖 Mono.Cecil（程序加载时改写 IL）。
- PhiDos.Programs：外部命令，每个命令一个小项目（Format、More、Sort、Find），编译出的 DLL 就是磁盘上的 FORMAT.COM、MORE.COM、SORT.EXE、FIND.EXE。
- PhiDos.SystemDisk：把上面这些命令作为资源打包，提供"新建系统盘"的功能。宿主引用它。
- PhiDos.ConsoleHost：终端版宿主，即开发期的独立应用（可执行文件名 phidos）。
- PhiDos.Tests：单元测试（xUnit），用 Roslyn 现场编译测试程序。
- Godot 宿主由使用者按 docs 里的接入文档自行接入。Core 层不得引用 Godot。

## 虚拟机（DosMachine）
所有宿主都通过 DosMachine 拉起 DOS：

~~~csharp
var machine = new DosMachine();
machine.Mount('A', "game.phidisk");   // 挂载虚拟磁盘或文件夹
machine.Start();                      // 后台线程开机，运行 COMMAND
// 宿主循环中：
machine.Console.TakeOutput();         // 取出要显示的字符
machine.Console.PushKey(key);         // 送入按键
machine.Reset();                      // 重启
machine.Stop();                       // 关机
~~~

- 宿主与机器之间通过 IConsoleDevice 交换输出和按键（Core 内定义）。图形宿主绘制 TextScreen（80×25 字符格，支持宽字符和 CGA 颜色属性）。
- 游戏代码访问磁盘内容：WithDrive（和虚拟机线程互斥地读写驱动器）、ExportImage（导出虚拟磁盘的当前内容，用于存档）。终端宿主和 Godot 宿主各自实现屏幕绘制，Core 不感知宿主类型。
- 开机信息：

      phi-DOS Version X.XX

  版本号 X.XX 为占位符，正式版本号确定后统一替换。

## 驱动器
内核只通过驱动器接口 IDrive 访问存储，不关心底层实现。每个盘符可独立挂载以下两种之一。

### 虚拟磁盘（.phidisk）
- 内容为标准 FAT12 磁盘镜像，扩展名为 .phidisk。文件开头不加任何自定义标记。
- 改名为 .img 后可被 DOSBox 和 mtools 直接挂载，便于往盘内放文件和对照测试。
- 支持读写，包括子目录（子目录为普通簇链，必须写入 . 和 .. 两个目录项）。
- 要求引导扇区含 BPB。读取时按 BPB 支持任意合法的 FAT12 软盘规格；新建时默认 360KB，也可选 160K、180K、320K、720K、1.2M、1.44M。
- 新建的盘带一段极短的引导代码，从这张盘启动时显示 "Non-System disk or disk error"，和原版的非系统盘一样。
- 根目录项数固定（360KB 盘为 112 项），写满后无法再在根目录新建文件；子目录满了会自动扩展。

### 文件夹
- 把电脑上的一个文件夹映射为驱动器，支持读写。主要用于开发时方便放取文件。
- 不符合 8.3 规则的文件和目录一律隐藏，不可访问。
- 查找时不区分大小写；DOS 新建的文件和目录一律以大写名字写入。
- 容量为设定值（默认 360KB），已用空间按文件夹内文件的实际大小累计。超出时报 "Insufficient disk space"。
- 路径无法越出映射的文件夹：根目录没有上一级，在根目录执行 CD .. 会和原版一样报 "Invalid directory"；符号链接一律忽略。
- 文件夹里如果有 DOS 看不见的文件（长文件名等），RD 不会删除这个文件夹，避免误删电脑上的东西。

## 路径与目录（照 2.0）
- 路径格式：可选盘符 + 以 \ 分隔的目录 + 8.3 文件名，支持 . 和 ..。
- 每个驱动器各自维护一个当前目录。
- 当前目录路径最长 64 个字符。
- 卷标：支持，DIR 首行显示卷标信息（如 "Volume in drive A has no label"）。

## 核心接口（修改需同步本文档）
源码位于 src/PhiDos.Abi，下面是摘要。所有方法出错时抛出 DosException，其 Error 属性是 DOS 2.0 的错误码（DosError）。

~~~csharp
public interface IDos
{
    // 控制台（走标准输入输出，跟随重定向和管道）
    void Write(string text);
    void WriteLine(string text = "");
    char ReadKey(bool echo = true);     // 功能键先返回 '\0'，下一次返回扫描码
    string ReadLine();                  // 输入结束（重定向的文件读完）时返回 "\x1a"
    bool KeyAvailable { get; }

    // 文件（句柄式，参照 DOS 2.0 INT 21h 3Ch-42h）
    int Create(string path);
    int Open(string path, FileAccessMode mode);
    int Read(int handle, Span<byte> buffer);
    int Write(int handle, ReadOnlySpan<byte> data);   // 写 0 字节表示截断到当前位置
    long Seek(int handle, long offset, SeekOrigin origin);
    void Close(int handle);
    int Duplicate(int handle);                         // INT 21h 45h
    void ForceDuplicate(int source, int target);       // INT 21h 46h
    void Delete(string path);
    void Rename(string from, string to);
    IReadOnlyList<DirEntry> FindFiles(string pattern);                         // 只含普通文件
    IReadOnlyList<DirEntry> FindFiles(string pattern, DosAttributes include);  // 额外包含目录/隐藏/系统/卷标

    // 驱动器与目录（2.0）
    char CurrentDrive { get; set; }
    string CurrentDirectory { get; }              // 当前驱动器的当前目录
    string GetCurrentDirectory(char drive);       // 指定驱动器的当前目录
    void ChangeDirectory(string path);
    void MakeDirectory(string path);
    void RemoveDirectory(string path);
    DiskInfo GetDiskInfo(char drive);             // 容量、剩余空间、卷标
    void FormatDisk(char drive, string? volumeLabel);  // 只支持虚拟磁盘

    // 进程与环境
    int Exec(string program, string args);
    string CommandTail { get; }         // 命令行参数原文
    string? GetEnv(string name);
    DateTime Now { get; }
}

public interface IPhiProgram
{
    int Run(IDos dos, string[] args);
}
~~~

相比最初草案新增的成员：

| 成员 | 对应的 DOS 功能 | 为什么需要 |
|---|---|---|
| KeyAvailable | INT 21h 0Bh | 游戏类程序要不等待地检查按键 |
| FindFiles(pattern, include) | INT 21h 4Eh 的属性参数 | DIR 要列出子目录 |
| GetDiskInfo | INT 21h 36h | DIR 的剩余空间、FORMAT 的容量报告 |
| FormatDisk | 无（原版 FORMAT 直接操作磁盘） | FORMAT 命令 |
| Duplicate / ForceDuplicate | INT 21h 45h / 46h | MORE 从管道读数据、从键盘读按键 |
| CommandTail | PSP 80h 处的命令尾 | FIND 要解析带空格的 "字符串" |

句柄 0、1、2 分别是标准输入、标准输出、标准错误，3、4 是 AUX 和 PRN（都接到 NUL）。每个进程最多 20 个句柄，子程序继承父程序的全部句柄。

## 关键约定
- 程序格式：.COM 和 .EXE 文件都是一个 .NET 程序集，内含唯一一个实现 IPhiProgram 的公开类。加载器找到该类型并调用 Run。两种扩展名在 phi-DOS 里没有区别，只影响查找顺序。
- 程序加载：使用可回收的 AssemblyLoadContext 加载，程序结束后卸载，避免反复运行导致内存增长。程序文件最大 640KB。PhiDos.Abi 始终使用系统自己加载的那一份。
- 加载失败（不是 .NET 程序集、找不到入口类、调用了禁止的 API）时显示 "Error in EXE file"，下一行缩进显示具体原因。
- 程序未处理的异常：显示 "Abnormal program termination" 和异常信息，退出码 255。
- API 检查：加载时拒绝直接使用以下 API 的程序（只是基本防护，不是安全沙箱，反射之类的手段仍可绕过）：
  直接访问电脑文件（File、Directory、FileStream、StreamReader/Writer 等）、Process、Environment.Exit 等危险成员、Console、Assembly、Activator、Marshal、NativeLibrary、P/Invoke、Thread、ThreadPool、Timer、Task、Parallel，以及 System.Net、System.Reflection.Emit、System.Runtime.Loader 等命名空间。
- 程序查找顺序（照 2.0）：内置命令 → 当前目录 → PATH 中的各目录；每个目录内依次找 .COM → .EXE → .BAT。都找不到输出 "Bad command or file name"。
- 内置程序表：部分外部命令可先用 Dictionary<string, Func<IPhiProgram>> 直接映射到 C# 实现，但磁盘上必须存在对应文件才能运行（删掉文件即不可用）。
- 线程：每个程序在后台线程运行；输出写入线程安全队列，由宿主在主线程取出渲染。
- 中断：IDos 每个方法开头检查 Ctrl+C。和 DOS 一样，BREAK OFF（默认）时只有控制台调用检查，BREAK ON 时所有调用都检查。
- 注入检查点：加载时用 Mono.Cecil 在每个方法开头和每个回跳分支前插入 DosRuntime.Poll()。
  - 重启：Poll 总是响应，所以死循环也能被重启打断。
  - Ctrl+C：Poll 只在 BREAK ON 时响应。BREAK OFF 时纯计算的死循环不理 Ctrl+C，只能重启，这和原版一致。
  - 无限递归：Poll 会检查剩余栈空间，栈快用完时抛出可以捕获的异常，不会让整个游戏因栈溢出崩溃。
- 被放弃的线程：.NET 10 不支持强制终止线程。重启时旧线程会收到 MachineHaltException；如果程序把异常吞掉继续运行，超过 16 次后线程会在 Poll 里永久睡眠，不再占用 CPU。
- 提示符：默认 "A>"，支持 PROMPT 及 $T $D $P $V $N $G $L $B $Q $H $E $_ $$ 元字符。
- 重定向：< > >>。照原版，> 前面的空格会留在命令里（ECHO hi > X 写入 "hi "）。双引号里的 < > | 不当作重定向，方便 FIND "a|b"（这一点比原版宽松）。
- 管道：照 2.0，前一个命令的输出写进当前驱动器根目录的临时文件 %PIPE1.$$$、%PIPE2.$$$，后一个命令从中读取，用完删除。所以 DIR | SORT 的结果里会看到这个临时文件，和原版一样。
- 批处理：%0 到 %9、%%、%名字%（环境变量）、ECHO、GOTO（标号只比较前 8 个字符）、IF [NOT] ERRORLEVEL / EXIST / ==、FOR %%v IN (...) DO（不能嵌套）、SHIFT、PAUSE、REM。
  批处理里运行另一个批处理是"转过去"不再回来（2.0 没有 CALL）。每执行一行都重新打开批处理文件，运行中被删除会显示 "Batch file missing"。
  Ctrl+C 时询问 "Terminate batch job (Y/N)?"。批处理结束后 ECHO 恢复为开始前的状态。
- 开机：A 盘根目录有 AUTOEXEC.BAT 时运行它，没有时询问日期和时间（MachineOptions.PromptForDateTimeAtBoot 可关闭）。

## 独立应用（终端版）
~~~
phidos game.phidisk                    A 盘挂虚拟磁盘
phidos -a game.phidisk -b ./dev        A 盘挂虚拟磁盘，B 盘挂文件夹
phidos --new blank.phidisk             新建一张空白的 360KB 虚拟磁盘
~~~
- 参数是文件则按虚拟磁盘挂载，是文件夹则按文件夹挂载。
- 发布为单个可执行文件，无需另装 .NET 即可运行：
  `dotnet publish src/PhiDos.ConsoleHost -p:PublishProfile=win-x64`，结果在 publish/win-x64/phidos.exe。
  另有 linux-x64、osx-arm64 两个配置。不能开启裁剪（PublishTrimmed），磁盘上的程序可能用到 .NET 库里的任何东西。
- 终端里按 F12 重启虚拟机，在最外层输入 EXIT 退出。标准输入可以重定向为脚本文件，读完后自动关机。
- 设置环境变量 PHIDOS_TRACE=1 时，内部错误会显示完整调用栈。
- Godot 宿主完成后，同样可导出为独立应用。

## 里程碑
- M0 ✅：搭好解决方案结构和 DosMachine，终端版宿主能显示开机信息和 A> 提示符。
- M1 ✅：FAT12 虚拟磁盘只读支持、文件夹驱动器只读支持、路径解析器（盘符、\、.、..、每盘当前目录）。
- M2 ✅：外壳与只读命令 DIR、CD、TYPE、REM、PAUSE、DATE、TIME。
- M3 ✅：写操作 COPY、ERASE/DEL、RENAME/REN、MD、RD，两种驱动器均支持；新建空白 .phidisk。
- M4 ✅：程序查找（当前目录 → PATH）与内置程序表。
- M5 ✅：IL 程序加载器（可回收加载、取消检查注入），第一个 .COM 程序在磁盘上跑通。
- M6 ✅：重定向、管道、批处理（IF/GOTO/FOR/SHIFT/ECHO）。
- M7 ✅：Godot 宿主接入文档（详细版 docs/GODOT-INTEGRATION.md、速查版 docs/QUICKSTART.md），示例脚本在 docs/godot/，已用 Godot.NET.Sdk 4.5.1 以 net10.0 编译验证。由使用者按文档接入 ProjectVN。

里程碑完成后在标题前标 ✅，对应的提交标题以 "M0："、"M1：" 这样的前缀开头。（远端仓库不接受推送 Git 标签，所以不打标签。）

外部命令范围：FORMAT（只对虚拟磁盘生效，支持 /V）、MORE、SORT（/R、/+n）、FIND（/V、/C、/N），以及 phi-DOS 自己加的 VI 和 ILDOSVER。CHKDSK 不提供，输入后报 "Bad command or file name"。
终端版在没有指定 A 盘时，自动给 A 盘一张内存中的临时系统盘（带上述命令）。

### ILDOSVER（phi-DOS 自己加的系统信息展示）
类似 neofetch：左边是彩色的 φ 图标，右边列出系统信息（OS 写 phi-DOS、CPU 写 .NET、语言写 IL 等，系统名和版本号直接写在程序里），最后是 16 色色板。当前驱动器的信息通过 GetDiskInfo 读取，环境变量 USER 决定标题里的用户名，没有时显示 guest。

### VI（phi-DOS 自己加的全屏编辑器）
DOS 2.0 没有全屏编辑器，原版只有 EDLIN。VI 模仿 vi 的手感，只做常用部分，不追求兼容：

- 普通模式：h j k l 和方向键、0 ^ $、w b、gg G 和 数字G、Ctrl+F Ctrl+B 翻页、i a I A o O、x、dd、yy、p P、u（只能撤销一步，再按一次是重做，和原版 vi 一样）、/ ? n N 搜索。
- 插入模式：打字、退格（在行首退格会和上一行合并）、回车、Esc 回到普通模式。
- 冒号命令：:w、:w 文件名、:q、:q!、:wq、:x、ZZ、:数字、:$。
- 屏幕：上面 24 行是文字，空行显示 ~，长行自动折行；最后一行显示状态（-- INSERT --、文件信息、错误提示）。
- 不做：dw、3dd 这类组合命令、可视模式、多窗口、寄存器、宏、配置文件、语法高亮。
- 文件按 UTF-8 读写，保存时每行用 CR LF 结尾。Ctrl+C 会和其他 DOS 程序一样终止它，退出时清屏。

## 验证方法
在 DOSBox 中从镜像启动原版 MS-DOS 2.0，执行一组操作；在 phi-DOS 中挂载内容相同的 .phidisk（可以是同一个镜像改名），执行同样操作，比对输出（列宽、日期格式、错误提示）。差异即 bug。

## 授权与命名
- 参照或移植 MS-DOS 源码逻辑的部分，保留其 MIT 版权声明。
- 系统名称为 phi-DOS，不使用 MS-DOS 商标。

## 协作规则（给 Claude Code）
- 每次只做一个里程碑或一个模块，开始前先说明计划。
- 修改 IDos 等公共接口时，同步更新本文档。
- 用户正在学习 C#：关键设计和不常见的写法要简要说明原因。
- 用户不熟悉 Git：由 Claude Code 负责提交，并在每次提交后说明提交了什么。
