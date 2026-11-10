# phi-DOS 接入 Godot 详细指南

这份文档讲怎样把 phi-DOS 接进 ProjectVN（Godot 4 .NET），以及接入之后能做什么。
只想快速跑起来的话，先看 [QUICKSTART.md](QUICKSTART.md)，遇到问题再回来查这里。

目录

1. 整体结构
2. 环境要求
3. 把 phi-DOS 加进 Godot 项目
4. 第一个终端节点
5. 屏幕绘制
6. 键盘输入
7. 线程模型
8. 磁盘：读取、换盘、存档
9. 和剧情联动
10. 虚拟机的生命周期
11. 编写自己的 .COM 程序
12. 导出与平台限制
13. 常见问题
14. 公开 API 一览

---

## 1. 整体结构

```
┌──────────── Godot（主线程）────────────┐
│  DosTerminal : Control                  │
│    _Draw()     ← 读 Screen 快照画格子    │
│    _GuiInput() → Console.PushKey(...)   │
└───────────────┬────────────────────────┘
                │ IConsoleDevice（线程安全）
┌───────────────▼──────── 虚拟机线程 ─────┐
│  DosMachine                             │
│    ├─ 内核：路径、句柄、进程             │
│    ├─ COMMAND：命令解释、批处理          │
│    ├─ 程序：磁盘上的 IL 程序集           │
│    └─ 驱动器 A: B: ……                  │
│         ├─ Fat12Drive（.phidisk 镜像）   │
│         └─ FolderDrive（电脑上的文件夹）  │
└─────────────────────────────────────────┘
```

要点：

- 一台 `DosMachine` 就是一台虚拟电脑。插上磁盘、调用 `Start()`，DOS 就在它自己的后台线程上跑起来。
- Godot 只负责两件事：把 `Machine.Console.Screen`（80×25 的字符格）画出来；把按键用 `Machine.Console.PushKey()` 送进去。
- Core 不引用 Godot，Godot 那一侧的代码全在 `docs/godot/` 的三个示例文件里，你可以直接拷进项目，也可以照着改。

需要用到的程序集：

| 程序集 | 作用 | Godot 项目要不要引用 |
|---|---|---|
| PhiDos.Abi | IDos、IPhiProgram 等公共接口 | 间接引用（Core 依赖它） |
| PhiDos.Core | 虚拟机、内核、COMMAND、驱动器 | 要 |
| PhiDos.SystemDisk | 内置 FORMAT、MORE、SORT、FIND、VI、ILDOSVER，新建系统盘 | 需要系统盘时引用 |
| Mono.Cecil | 加载程序时改写 IL | 由 Core 自动带入 |

## 2. 环境要求

- Godot 4 的 .NET 版本（带 C# 支持的那个下载包）。
- .NET 10 SDK。
- Godot 项目的目标框架要改成 net10.0。

Godot 4 的 C# 项目默认是 net8.0。打开项目根目录的 `ProjectVN.csproj`，把

```xml
<TargetFramework>net8.0</TargetFramework>
```

改成

```xml
<TargetFramework>net10.0</TargetFramework>
```

我用 Godot.NET.Sdk 4.5.1 实际编译过 `docs/godot/` 里的示例，net10.0 可以通过编译。不过在编辑器里运行、导出这两步我这边没有条件验证，请你在自己的 Godot 版本上确认一次。

**后备方案：Godot 版本不支持 net10.0 时。**
Core 的代码也能编译成 net8.0（我验证过）。做法是把下面几个项目的

```xml
<TargetFramework>net10.0</TargetFramework>
```

改成

```xml
<TargetFrameworks>net8.0;net10.0</TargetFrameworks>
```

涉及的项目：`src/PhiDos.Abi`、`src/PhiDos.Core`、`src/PhiDos.SystemDisk`，以及 `src/PhiDos.Programs/Directory.Build.props`。
注意磁盘上的程序也要用 net8.0 编译：用 .NET 10 编译的程序引用的是 System.Runtime 10.0，在 .NET 8 运行时里加载不了。

## 3. 把 phi-DOS 加进 Godot 项目

推荐的做法是项目引用，phi-DOS 改了代码，Godot 那边重新编译就能用上。

**第一步：把 phi-DOS 仓库放到 ProjectVN 旁边或里面。**
可以用 Git 子模块，也可以直接复制一份。假设目录是这样：

```
ProjectVN/
├─ ProjectVN.csproj
├─ project.godot
├─ scripts/
└─ external/phi-DOS/        ← phi-DOS 仓库
```

**第二步：在 `ProjectVN.csproj` 里加引用。**

```xml
<ItemGroup>
  <ProjectReference Include="external/phi-DOS/src/PhiDos.Core/PhiDos.Core.csproj" />
  <ProjectReference Include="external/phi-DOS/src/PhiDos.SystemDisk/PhiDos.SystemDisk.csproj" />
</ItemGroup>
```

**第三步：别让 Godot 把 phi-DOS 的源码当成游戏脚本。**
Godot 的 C# 项目默认会编译项目目录下所有 .cs 文件。phi-DOS 放在项目里面时，要把它排除掉：

```xml
<ItemGroup>
  <Compile Remove="external/**" />
</ItemGroup>
```

同样，Godot 编辑器也会扫描这个目录。在 `external/phi-DOS/` 里放一个空文件 `.gdignore`，Godot 就不会导入它。

**第四步：把示例脚本拷进来。**
把 `docs/godot/DosTerminal.cs` 和 `docs/godot/DosKeyMap.cs` 复制到 `scripts/` 下。`StoryCommand.cs` 是剧情联动的例子，需要时再拷。

**不想用项目引用的话**，也可以先把 phi-DOS 编译好，再引用 DLL：

```
dotnet build external/phi-DOS/src/PhiDos.SystemDisk -c Release
```

然后在 csproj 里用 `<Reference Include="...PhiDos.Core.dll" />` 引用 PhiDos.Abi、PhiDos.Core、PhiDos.SystemDisk 三个 DLL，并加上 `<PackageReference Include="Mono.Cecil" Version="0.11.6" />`。

## 4. 第一个终端节点

1. 新建场景，根节点选 `Control`，挂上 `DosTerminal.cs`。
2. 在检查器里设置：
   - **Terminal Font**：一个等宽字体。要显示中文，选一个带中文字形的等宽字体（见第 5 节）。
   - **Font Size**：16 左右。节点的最小尺寸会按字体自动算成 80×25 格。
   - **Disk Path**：A 盘的镜像，比如 `res://disks/story.phidisk`。留空时用一张临时系统盘。
   - **Prompt For Date Time**：开机时要不要问日期和时间。游戏里一般关掉。
3. 运行场景，就能看到：

```
phi-DOS Version X.XX

A:\>
```

点一下终端让它获得焦点，就可以打字了。

DosTerminal 做的事情很少，核心只有这几行：

```csharp
Machine = new DosMachine(new MachineOptions { PromptForDateTimeAtBoot = false });
Machine.MountImage('A', LoadDisk(DiskPath), ownsStream: true);
Machine.Start();
```

其余代码都是在画屏幕、转按键。

## 5. 屏幕绘制

`Machine.Console.Screen` 是一个 `TextScreen`，相当于显卡的文本显存：

- 80 列 × 25 行，常量 `TextScreen.Columns`、`TextScreen.Rows`。
- `Snapshot(out cursorX, out cursorY)` 复制出全部 2000 个格子，返回 `ScreenCell[]`，下标是 `y * 80 + x`。
- `Version` 每次内容变化都会加一。示例在 `_Process` 里比较版本号，只在变化时 `QueueRedraw()`。
- `GetLine(y)` 取某一行的文字，调试和自动测试时方便。

每个 `ScreenCell` 有三个字段：

| 字段 | 含义 |
|---|---|
| Rune | 要显示的字符（`System.Text.Rune`，能表示所有 Unicode 字符） |
| Attribute | 颜色，和 PC 文本模式一样：低 4 位前景色，第 4 到 6 位背景色，最高位闪烁 |
| IsWideTail | 是不是宽字符的右半格。中文占两格，左格放字，右格标记为 IsWideTail，绘制时跳过 |

颜色编号对应的是 CGA 的 16 色，示例里的 `Palette` 数组就是标准值。默认属性是 0x07，即灰字黑底。程序可以用 ANSI 转义序列（例如 `\x1b[1;33m`）改颜色，`CLS` 发出的 `\x1b[2J` 会清屏。

**字体。** 字符屏幕要求等宽字体，而且中文字宽最好正好是英文的两倍，否则会对不齐。可以考虑：

- 更纱黑体（Sarasa Gothic）的 Mono 系列，中英文严格 2:1。
- Noto Sans Mono CJK。
- 想要像素风可以用像素字体，同样要选中英文 2:1 的。

示例按字母 M 的宽度算格子大小，所以中文字会画在两格宽的位置上。

**性能。** 示例每次重画都画全部 2000 格，对文字终端来说足够快。如果想再省，可以把同一行里颜色相同的连续格子合成一次 `DrawString`。

**光标。** 示例在光标所在格的底部画一条 2 像素的横线，每 0.25 秒闪烁一次，只在终端有焦点时显示。

## 6. 键盘输入

按键用 `Machine.Console.PushKey(KeyInput)` 送进去，可以在任何线程调用。`KeyInput` 有两种：

- 普通字符：`new KeyInput('a')`。回车、退格、Esc、Tab 这些有现成的常量：`KeyInput.Enter`、`KeyInput.Backspace`、`KeyInput.Escape`、`KeyInput.Tab`。
- 特殊键：`KeyInput.Special(DosKey.F3)`，支持 F1 到 F10、方向键、Home、End、PageUp、PageDown、Insert、Delete。

`KeyInput.CtrlC` 是 Ctrl+C，它会清空键盘缓冲区并打断当前程序。

示例的 `DosKeyMap.Translate` 已经处理好对应关系：

| Godot | phi-DOS |
|---|---|
| Enter、小键盘 Enter | KeyInput.Enter |
| Backspace、Escape、Tab | 对应常量 |
| F1 到 F10 | DosKey.F1 到 F10 |
| 方向键、Home、End、PageUp、PageDown、Insert、Delete | 对应的 DosKey |
| Ctrl+C | KeyInput.CtrlC |
| Ctrl+字母 | 控制字符（Ctrl+Z 就是文件结束符 0x1A） |
| 其他有字符的键 | `InputEventKey.Unicode` 里的字符 |

DOS 行编辑里有用的键：退格删字，Esc 作废本行，F1 从上一行复制一个字符，F3 复制上一行剩下的部分（相当于"重复上一条命令"），F5 把当前输入设为新模板。

**中文输入法。** `_GuiInput` 收到的按键事件不包含输入法正在组字的内容，打中文需要另外处理。比较省事的办法：放一个看不见的 `LineEdit` 接收输入法输入，在它的 `TextChanged` 信号里把新文字 `PushText` 进去，然后清空它：

```csharp
_imeBox.TextChanged += text =>
{
    if (text.Length == 0) return;
    Machine.Console.PushText(text);
    _imeBox.Text = "";
};
```

`PushText` 会把字符串拆成一个个按键送进去，其中 `'\n'` 当作回车。

文件名里也可以用中文，不过 8.3 是按 UTF-8 字节算的，一个汉字占 3 字节，所以主名最多两个汉字，例如 `存档.TXT`。

## 7. 线程模型

这是最容易出问题的地方，务必记住：

1. **DOS 跑在自己的后台线程上。** COMMAND、所有程序、包括你写的内置程序，都在这个线程上执行。
2. **Godot 的节点只能在主线程上碰。** 所以虚拟机线程上的代码不能直接改节点、播放动画、切换场景。
3. **回到主线程用 CallDeferred。**

   ```csharp
   Callable.From(() => ShowDialog("你解开了密码")).CallDeferred();
   ```

4. **`Console.OutputWritten` 事件在虚拟机线程上触发。** 示例没有用它，而是每帧在主线程上检查 `Screen.Version`，这样最安全。
5. 下面这些方法可以在主线程上随便调用，它们内部已经加锁：`PushKey`、`PushText`、`Screen.Snapshot`、`Mount*`、`Unmount`、`WithDrive`、`ExportImage`、`Reset`、`Stop`。

## 8. 磁盘：读取、换盘、存档

### 8.1 磁盘镜像从哪来

.phidisk 就是标准的 FAT12 软盘镜像。准备剧情用的盘有几种办法：

- **用终端版。** `phidos --new story.phidisk --label STORY` 新建一张带系统命令的盘（加 `--blank` 是空白盘）。然后 `phidos -a story.phidisk -b 素材文件夹` 开机，在 DOS 里用 `COPY B:*.* A:` 把文件拷进去。
- **用 mtools。** 镜像改名成 .img 就是普通软盘镜像，`mcopy -i story.img 文件 ::` 直接往里放文件，`mmd` 建目录。
- **用代码。** 见 8.4 节。

把做好的镜像放到 `res://disks/` 下。

### 8.2 挂载

```csharp
// 从 res:// 读进内存再挂载。导出后 res:// 是只读的，所以必须先读进内存。
Machine.MountImage('A', DosTerminal.LoadDisk("res://disks/story.phidisk"), ownsStream: true);

// 写保护挂载：玩家的任何写操作都会出现 "Write protect error writing drive A"
Machine.MountImage('A', DosTerminal.LoadDisk("res://disks/system.phidisk"), readOnly: true, ownsStream: true);

// 直接挂 user:// 下的文件，改动实时写回这个文件
Machine.MountImage('B', ProjectSettings.GlobalizePath("user://save.phidisk"));

// 映射一个文件夹（开发时方便，正式游戏里一般不用）
Machine.MountFolder('B', ProjectSettings.GlobalizePath("user://shared"));
```

默认有 A: 和 B: 两个软驱，可以用 `MachineOptions.DriveCount` 改成 1 到 4 个。

### 8.3 换盘

随时可以换盘，就像把软盘抽出来再插一张：

```csharp
Machine.Unmount('B');                      // 取出：之后访问 B: 会出现 Not ready error
Machine.MountImage('B', LoadDisk("res://disks/chapter2.phidisk"), ownsStream: true);
```

换盘后，这个驱动器的当前目录自动回到根目录。

一个有意思的玩法：程序访问一个没插盘的驱动器时，屏幕上会出现

```
Not ready error reading drive B
Abort, Retry, Ignore?
```

这时剧情可以提示玩家"去找那张软盘"，找到后游戏代码把盘挂上，玩家按 R 就能继续。

### 8.4 用代码读写盘里的文件

`WithDrive` 让游戏代码直接操作虚拟磁盘，和虚拟机线程互斥：

```csharp
using PhiDos.Core.Text;

// 往玩家的盘里放一封信
Machine.WithDrive('A', drive =>
{
    var file = drive.CreateFile(["LETTER.TXT"], DateTime.Now);
    file.Write(0, System.Text.Encoding.UTF8.GetBytes("给你的一封信\r\n"));
    file.Close(DateTime.Now);
});

// 读玩家写的文件
string? answer = Machine.WithDrive('A', drive =>
{
    if (drive.GetEntry(["ANSWER.TXT"]) is null) return null;
    var file = drive.OpenFile(["ANSWER.TXT"], writable: false);
    var buffer = new byte[file.Length];
    file.Read(0, buffer);
    file.Close(null);
    return System.Text.Encoding.UTF8.GetString(buffer);
});
```

路径写成名字数组，从根目录开始，例如 `\SAVES\SLOT1.DAT` 写成 `["SAVES", "SLOT1.DAT"]`。名字必须是大写的 8.3 形式，不确定时用 `DosNames.Normalize("slot1.dat", allowWildcards: false)` 转一下。

注意：如果 DOS 里的程序正打开着同一个文件，它看不到你这里做的修改。最好在玩家没有运行程序时调用，比如在 `Console.IsWaitingForInput` 为 true、并且提示符在等待输入的时候。

文本文件请用 CR LF（`\r\n`）换行，和 DOS 一致。

### 8.5 存档

```csharp
// 把 A 盘的当前内容完整导出（368640 字节的标准镜像），可以在虚拟机运行时调用
byte[] image = Machine.ExportImage('A');
```

示例里的 `SaveDisk('A', "user://slot1.phidisk")` 就是把它写进 user://。读档时用 `LoadDisk("user://slot1.phidisk")` 再挂上去即可。

这份镜像也可以直接塞进你自己的存档格式里，比如和剧情进度一起序列化。

## 9. 和剧情联动

磁盘上的 IL 程序只能通过 IDos 和系统打交道，碰不到游戏。要让 DOS 里的操作影响剧情，有两种方式：

- **内置程序表**：盘上放一个占位文件，玩家运行它时，系统直接调用游戏里的 C# 类。
- **呼叫宿主**：盘上放一个真正的程序，它自己调用 `dos.CallHost(名字, 参数)` 通知游戏。

### 9.1 内置程序表

用**内置程序表**：

```csharp
var options = new MachineOptions
{
    BuiltinPrograms = new Dictionary<string, Func<IPhiProgram>>(StringComparer.OrdinalIgnoreCase)
    {
        ["UNLOCK"] = () => new UnlockCommand(),
    },
};
Machine = new DosMachine(options);
```

规则是：磁盘上必须存在 `UNLOCK.COM`（或 `UNLOCK.EXE`），系统才会运行它；文件存在时，系统不读文件内容，直接调用你给的 C# 类。所以：

- 文件内容随便写什么都行，一个字节也可以。
- 剧情可以控制"这个命令什么时候出现"：把文件放进盘里，命令就能用；删掉文件，玩家输入后只会看到 "Bad command or file name"。
- 内置程序是你游戏里的普通 C# 代码，可以访问游戏的任何东西，但它跑在虚拟机线程上，碰 Godot 节点要用 `CallDeferred`。

`docs/godot/StoryCommand.cs` 是一个完整的例子：玩家输入 `UNLOCK EVENSTAR` 时触发 `UnlockCommand.Unlocked` 事件。

```csharp
UnlockCommand.Unlocked += who => GetNode<DialogBox>("Dialog").Show($"{who} 解开了密码。");
```

### 9.2 呼叫宿主：盘上的真程序通知游戏

剧情需要"玩家在盘里找到一个程序、运行它，然后游戏切换到另一个场景"时，用这种方式。程序是真的 .COM，可以有自己的界面、问答、动画，最后由它自己决定通知游戏。

**游戏这边**：在 `MachineOptions.HostCalls` 里登记名字和处理函数。

```csharp
var options = new MachineOptions
{
    HostCalls = new Dictionary<string, Func<string, string?>>(StringComparer.OrdinalIgnoreCase)
    {
        ["SCENE"] = path =>
        {
            Callable.From(() => GetTree().ChangeSceneToFile(path)).CallDeferred();
            return "ok";
        },
    },
};
```

**盘上的程序**：

```csharp
using PhiDos.Abi;

public class Portal : IPhiProgram
{
    public int Run(IDos dos, string[] args)
    {
        dos.WriteLine("Opening the portal...");
        string answer = dos.CallHost("SCENE", "res://scenes/chapter2.tscn");
        return answer == "ok" ? 0 : 1;
    }
}
```

编译成 `PORTAL.COM` 放进盘里，玩家运行它，游戏就会切换到 chapter2 场景。

`docs/godot/StoryTerminal.cs` 是完整的例子：它继承 DosTerminal，登记了 `SCENE`（切换场景，只允许 `res://scenes/` 下的场景）和 `SAY`（把一句话交给游戏）两项功能。

规则：

- 处理函数的参数是程序传来的字符串，返回值交还给程序；返回 null 时程序拿到空字符串。名字不区分大小写。
- **处理函数在虚拟机线程上执行。** 碰 Godot 节点（切换场景、显示对话、播放动画）必须用 `Callable.From(...).CallDeferred()` 回到主线程。
- **处理函数应当尽快返回。** 它执行期间程序在等它，屏幕不会有任何变化。常见写法就是上面那样：用 CallDeferred 把主线程上的事安排好，马上返回，程序接着运行到结束，主线程在下一帧做真正的工作。
- **程序只能叫出你登记过的名字。** 这不是让程序任意调用 Godot：加载检查照常拦截危险的 API，盘上的程序仍然引用不到 Godot 或游戏的程序集。处理函数里要检查参数，比如只允许切换到特定目录下的场景。
- 程序叫了没登记的名字时，会收到 `DosException`（`DosError.InvalidFunction`）；程序不捕获的话，屏幕上显示 `Abnormal program termination`。
- 处理函数抛出 `DosException` 时原样交给程序；抛出其他异常时，程序收到 `DosError.HostError`，消息里带着原异常的消息，虚拟机本身不受影响。
- 处理函数执行期间虚拟机被重启或关机时，处理函数返回后程序不会再继续。

**和内置程序表的区别：**

| | 内置程序表 | 呼叫宿主 |
|---|---|---|
| 盘上的文件 | 占位文件，内容不被使用 | 真正的 .COM 程序 |
| 代码写在哪 | 游戏项目里 | 盘上的程序里，游戏只登记处理函数 |
| 能不能调试 | 可以在游戏里直接打断点 | 程序的部分要按 WRITING-PROGRAMS.md 第 12 节的方法调试 |
| 能访问什么 | 游戏的一切 | 只能通过 IDos，和游戏的联系只有登记过的那几个名字 |
| 适合 | 剧情命令、谜题判定 | 玩家"找到并运行"的程序，程序本身就是剧情的一部分 |

### 9.3 其他联动方式

| 想做的事 | 做法 |
|---|---|
| 玩家运行某个命令时触发剧情 | 内置程序表（9.1） |
| 盘上的真程序通知游戏，比如切换场景 | 呼叫宿主（9.2）：登记 `HostCalls`，程序调用 `dos.CallHost` |
| 剧情推进时给玩家一个新文件 | `WithDrive` 往盘里写文件 |
| 检查玩家有没有写出某个文件 | `WithDrive` 读盘 |
| 玩家需要"插入另一张盘" | `Unmount` / `MountImage`，配合 Not ready error |
| 开机就执行一段剧情脚本 | 在盘的根目录放 AUTOEXEC.BAT |
| 让 DOS 时钟跟着游戏时间走 | `MachineOptions.Clock = () => 游戏里的时间` |
| 程序把信息传给剧情 | 程序写一个文件，或者设置退出码由批处理判断 |
| 读环境变量 | 内置程序里 `dos.GetEnv("USER")`；批处理里 `SET USER=...` |

## 10. 虚拟机的生命周期

| 方法 | 效果 |
|---|---|
| `Start()` | 开机。立即返回，DOS 在后台线程上运行 |
| `Reset()` | 重启，相当于按机箱上的 Reset：清屏、重新开机，盘还插着 |
| `Stop()` | 关机，最多等 2 秒让线程退出 |
| `Dispose()` | 关机并释放所有驱动器 |
| `State` | Off、Running、Stopped |
| `StateChanged` | 状态变化事件（可能在虚拟机线程上触发） |

几点说明：

- 节点离开场景树时一定要 `Dispose()`，示例在 `_ExitTree` 里做了。否则后台线程会一直留着，在 Godot 编辑器里还会妨碍重新编译。
- `MachineOptions.ExitPowersOff` 设为 true 时，在最外层输入 EXIT 会关机。游戏里可以借此"关电脑"，监听 `StateChanged` 收到 Stopped 后关闭终端界面。
- 死循环：程序加载时系统会在每个循环里插入检查点，所以 `Reset()` 总能打断它。BREAK ON 时 Ctrl+C 也能打断；BREAK OFF（默认）时纯计算的死循环不理 Ctrl+C。
- 无限递归不会让游戏崩溃，程序会以 "Abnormal program termination" 结束。
- 一个程序把所有异常都吞掉、拒绝退出时，重启后它的线程会被永久冻结，不占 CPU，只占少量内存。

**游戏暂停时**虚拟机还在跑。通常没关系，因为 DOS 大部分时间都在等按键，不占 CPU。如果需要彻底暂停，可以先 `ExportImage` 存下盘，`Stop()`，恢复时重新挂盘并 `Start()`（屏幕内容会重新开机）。

## 11. 编写自己的 .COM 程序

更完整的说明（文件读写、管道、颜色和全屏界面、调试方法）见 [WRITING-PROGRAMS.md](WRITING-PROGRAMS.md)，这一节只是概要。

### 11.1 新建项目

```
dotnet new classlib -n Hello -f net10.0
```

把 `Hello.csproj` 改成这样：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>HELLO</AssemblyName>
    <Nullable>enable</Nullable>
    <DebugType>none</DebugType>
  </PropertyGroup>
  <ItemGroup>
    <!-- Private=false：不复制 PhiDos.Abi.dll，运行时由系统提供 -->
    <ProjectReference Include="../phi-DOS/src/PhiDos.Abi/PhiDos.Abi.csproj" Private="false" />
  </ItemGroup>
</Project>
```

### 11.2 写程序

```csharp
using PhiDos.Abi;

public class Hello : IPhiProgram
{
    public int Run(IDos dos, string[] args)
    {
        dos.WriteLine("What is your name?");
        string name = dos.ReadLine();
        dos.WriteLine();
        dos.WriteLine($"Hello, {name}!");
        return 0;   // 退出码，批处理里可以用 IF ERRORLEVEL 判断
    }
}
```

规则：

- 程序集里必须**正好有一个**公开的、非抽象的类实现 `IPhiProgram`，而且要有无参构造函数。
- 编译出的 `HELLO.dll` 改名成 `HELLO.COM`（或 `HELLO.EXE`，两者没区别）放到盘上就能运行。
- 大小不能超过 640KB。
- 只能依赖 .NET 自带的库和 PhiDos.Abi，引用别的 DLL 会加载失败。

### 11.3 放到盘上

- 终端版：`phidos -a story.phidisk -b bin/Debug/net10.0`，然后 `COPY B:HELLO.DLL A:HELLO.COM`。
- mtools：`mcopy -i story.img bin/Debug/net10.0/HELLO.dll ::HELLO.COM`。
- 代码：`Machine.WithDrive('A', d => { var f = d.CreateFile(["HELLO.COM"], DateTime.Now); f.Write(0, bytes); f.Close(DateTime.Now); });`

### 11.4 IDos 能做什么

完整定义在 `src/PhiDos.Abi/IDos.cs`，每个成员都有注释。常用的：

| 分类 | 成员 |
|---|---|
| 控制台 | `Write`、`WriteLine`、`ReadKey`、`ReadLine`、`KeyAvailable` |
| 文件 | `Create`、`Open`、`Read`、`Write(handle, ...)`、`Seek`、`Close`、`Delete`、`Rename`、`FindFiles` |
| 句柄 | `Duplicate`、`ForceDuplicate`（0 标准输入、1 标准输出、2 标准错误） |
| 目录 | `CurrentDrive`、`CurrentDirectory`、`ChangeDirectory`、`MakeDirectory`、`RemoveDirectory` |
| 磁盘 | `GetDiskInfo`、`FormatDisk` |
| 进程 | `Exec`（运行另一个程序）、`CommandTail`、`GetEnv`、`CallHost`（呼叫宿主，见 9.2）、`Now` |

出错时抛出 `DosException`，`Error` 属性是 DOS 的错误码，例如 `DosError.FileNotFound`。

几个细节：

- `Write` / `WriteLine` 走标准输出，会跟着 `>` 和 `|` 走。
- `ReadKey` 读到功能键时先返回 `'\0'`，下一次调用返回扫描码，例如 F1 是 59、上箭头是 72。
- `ReadLine` 在输入被重定向、文件已读完时返回 `"\x1a"`。
- 文本输出用 UTF-8，中文直接写即可。

### 11.5 加载时的检查

为了避免误伤游戏本身，系统会拒绝直接使用下列 API 的程序，运行时显示 `Error in EXE file` 和原因：

- 直接访问电脑上的文件：`File`、`Directory`、`FileStream`、`StreamReader`、`StreamWriter` 等。
- 结束或控制整个进程：`Environment.Exit`、`Environment.FailFast`、`Process`、读写宿主环境变量。
- 绕过 IDos：`Console`。
- 动态加载代码：`Assembly`、`Activator`、`System.Reflection.Emit`、`System.Runtime.Loader`。
- 本机代码：P/Invoke、`Marshal`、`NativeLibrary`。
- 新线程：`Thread`、`ThreadPool`、`Timer`、`Task`、`Parallel`。
- 网络：`System.Net`。

这只是基本防护，不是安全沙箱。它的作用是挡住无意中写出的危险代码，挡不住刻意绕过的人。正式游戏里的程序都是你自己写的，这一点通常足够了。

调试自己的程序时，用内置程序表最方便：把 `["HELLO"] = () => new Hello()` 加进 `BuiltinPrograms`，盘上放一个 `HELLO.COM` 占位，就能在 IDE 里打断点。

## 12. 导出与平台限制

- **Windows、macOS、Linux 桌面版**：正常支持。
- **Android**：.NET 在 Android 上有 JIT，动态加载 IL 程序理论上可行，我没有实际验证。
- **iOS**：iOS 上 .NET 是完全 AOT 编译的，不能在运行时加载新的 IL，所以磁盘上的 .COM 程序跑不起来。COMMAND 的内置命令和内置程序表照常可用。
- **Web**：Godot 4 的 C# 项目目前不支持导出到 Web。
- 导出时**不要开启裁剪**（trimming）。磁盘上的程序可能用到 .NET 库里的任何东西，裁剪掉就会加载失败。
- 记得把 `res://disks/` 下的 .phidisk 加进导出资源。Godot 默认只导出它认识的资源类型，在导出设置的"资源"页把 `*.phidisk` 加进"要导出的非资源文件"。

## 13. 常见问题

**屏幕是空的，什么都没有。**
检查 `Machine.Start()` 有没有被调用；检查 `_Process` 里有没有 `QueueRedraw()`；检查字体是否为空。

**打字没反应。**
终端要有焦点。示例在 `_Ready` 里调用了 `GrabFocus()`，点击别的控件后焦点会转走，再点一下终端即可。

**中文显示成方块。**
字体里没有中文字形，换一个带中文的等宽字体。

**中文和英文对不齐。**
字体的中文宽度不是英文的两倍，换一个中英文 2:1 的等宽字体。

**开机后一直停在 "Current date is ..."。**
这是经典 DOS 的习惯：盘上没有 AUTOEXEC.BAT 时会问日期和时间。把 `PromptForDateTimeAtBoot` 设为 false，或者在盘里放一个 AUTOEXEC.BAT。

**输入程序名后显示 "Error in EXE file"。**
下一行会写原因。常见的有：不是 .NET 程序集；没有实现 IPhiProgram 的公开类；用了被禁止的 API；引用了别的 DLL。

**在编辑器里重新编译后报"程序集无法卸载"。**
有虚拟机还没关掉。确认每个 DosTerminal 在 `_ExitTree` 里调用了 `Dispose()`。

**想看内部错误的详细信息。**
设置环境变量 `PHIDOS_TRACE=1` 再启动，内部错误时会显示完整的调用栈。

## 14. 公开 API 一览

命名空间 `PhiDos.Core.Machine`：

| 类型 | 说明 |
|---|---|
| `DosMachine` | 虚拟电脑。`Mount`、`MountImage`、`MountFolder`、`MountDrive`、`Unmount`、`IsMounted`、`Start`、`Reset`、`Stop`、`WaitForExit`、`WithDrive`、`ExportImage`、`Console`、`State`、`StateChanged` |
| `MachineOptions` | `DriveCount`、`PromptForDateTimeAtBoot`、`ExitPowersOff`、`DefaultPrompt`、`Clock`、`BuiltinPrograms`、`HostCalls`、`InjectInterruptChecks`、`VerifyPrograms`、`EnvironmentSize`、`ThreadStackSize` |
| `IConsoleDevice` | `PushKey`、`PushText`、`EndOfInput`、`TakeOutput`、`Screen`、`IsWaitingForInput`、`WaitForInputIdle`、`OutputWritten` |
| `TextScreen` | `Columns`、`Rows`、`Snapshot`、`GetCell`、`GetLine`、`GetLines`、`Version`、`CursorX`、`CursorY`、`Bell` |
| `ScreenCell` | `Rune`、`Attribute`、`IsWideTail` |
| `KeyInput`、`DosKey` | 按键 |
| `MachineState` | Off、Running、Stopped |

命名空间 `PhiDos.Core.Storage`：

| 类型 | 说明 |
|---|---|
| `Fat12Image` | `Create`（新建镜像文件）、`CreateInMemory` |
| `DiskGeometry` | 软盘规格：160K、180K、320K、360K、720K、1.2M、1.44M |
| `Fat12Drive` | FAT12 镜像驱动器，`ToArray` 导出内容 |
| `FolderDrive` | 文件夹驱动器 |
| `IDrive`、`IDriveFile`、`DriveEntry`、`DriveSpace` | 驱动器接口，实现它可以做自定义的驱动器 |

命名空间 `PhiDos.SystemDisk`：

| 类型 | 说明 |
|---|---|
| `SystemPrograms` | `Files`（内置命令的文件内容）、`InstallTo`、`CreateSystemDisk`、`CreateSystemDiskFile` |

命名空间 `PhiDos.Core.Text`：

| 类型 | 说明 |
|---|---|
| `DosNames` | 8.3 名字规范化、通配符匹配 |
| `DisplayWidth` | 计算文字在字符屏幕上占几格 |
