# phi-DOS 速查

全部功能和命令用法见 [MANUAL.md](MANUAL.md)，接入 Godot 的详细说明见 [GODOT-INTEGRATION.md](GODOT-INTEGRATION.md)，设计见 [DESIGN.md](DESIGN.md)。

## 在电脑上直接运行

```
dotnet run --project src/PhiDos.ConsoleHost                    # 临时系统盘
dotnet run --project src/PhiDos.ConsoleHost -- story.phidisk   # 挂自己的盘
dotnet run --project src/PhiDos.ConsoleHost -- -a story.phidisk -b ./folder
dotnet run --project src/PhiDos.ConsoleHost -- --new story.phidisk --label STORY
dotnet run --project src/PhiDos.ConsoleHost -- C:=D:\games:ro Z:=D:\big:unlimited
```

镜像工具：`phidisk 文件夹 镜像` 打包，`phidisk 镜像 文件夹` 解包，`phidisk 镜像` 列出内容（源码里运行时写作 `phidos --phidisk ...`）。

打包全部平台：`./tools/release.sh`（Windows 上用 `tools/release.ps1`），结果在 `publish/release/`。解压后双击 `run-phidos.bat`，旁边的 `A` 文件夹就是 A 盘。

终端里 F12 重启，EXIT 退出。

## 接进 Godot（四步）

1. `ProjectVN.csproj`：

   ```xml
   <TargetFramework>net10.0</TargetFramework>
   ...
   <ItemGroup>
     <ProjectReference Include="external/phi-DOS/src/PhiDos.Core/PhiDos.Core.csproj" />
     <ProjectReference Include="external/phi-DOS/src/PhiDos.SystemDisk/PhiDos.SystemDisk.csproj" />
     <Compile Remove="external/**" />
   </ItemGroup>
   ```

2. 在 `external/phi-DOS/` 里放一个空的 `.gdignore`。
3. 把 `docs/godot/DosTerminal.cs`、`docs/godot/DosKeyMap.cs` 拷进脚本目录。
4. 建一个 Control 节点挂上 DosTerminal，设置字体和 Disk Path，运行。

## 最少的代码

```csharp
using PhiDos.Core.Machine;

var machine = new DosMachine(new MachineOptions { PromptForDateTimeAtBoot = false });
machine.MountImage('A', diskStream, ownsStream: true);
machine.Start();

// 每帧：
var cells = machine.Console.Screen.Snapshot(out int cx, out int cy);   // 80×25 个 ScreenCell

// 按键：
machine.Console.PushKey(new KeyInput('a'));
machine.Console.PushKey(KeyInput.Enter);
machine.Console.PushKey(KeyInput.Special(DosKey.F3));
machine.Console.PushText("DIR\n");

// 离开场景时：
machine.Dispose();
```

## 常用操作

| 想做的事 | 代码 |
|---|---|
| 从 res:// 读盘 | `DosTerminal.LoadDisk("res://disks/x.phidisk")` |
| 写保护挂载 | `MountImage('A', stream, readOnly: true, ownsStream: true)` |
| 换盘 | `Unmount('B')`，再 `MountImage('B', ...)` |
| 存档 | `byte[] image = machine.ExportImage('A')` |
| 往盘里放文件 | `machine.WithDrive('A', d => { var f = d.CreateFile(["A.TXT"], DateTime.Now); f.Write(0, bytes); f.Close(DateTime.Now); })` |
| 重启 | `machine.Reset()` |
| 关机 | `machine.Stop()` |
| 命令触发剧情 | `BuiltinPrograms = new Dictionary<string, Func<IPhiProgram>> { ["UNLOCK"] = () => new UnlockCommand() }`，盘上放 `UNLOCK.COM` |
| 回到 Godot 主线程 | `Callable.From(() => ...).CallDeferred()` |
| 游戏时间当 DOS 时钟 | `Clock = () => gameTime` |
| 开机不问日期 | `PromptForDateTimeAtBoot = false` |
| 提示符用 2.0 原版的 `A>` | `DefaultPrompt = ""`（默认是 `$P$G`，即 `A:\>`） |
| EXIT 时关机 | `ExitPowersOff = true`，监听 `StateChanged` |

## 屏幕格子

| 字段 | 含义 |
|---|---|
| `Rune` | 字符 |
| `Attribute` | 颜色：低 4 位前景，4 到 6 位背景（CGA 16 色），默认 0x07 |
| `IsWideTail` | 宽字符（中文）的右半格，跳过不画 |

`Screen.Version` 变了才需要重画。

## 写一个 .COM 程序

```xml
<!-- HELLO.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>HELLO</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="path/to/PhiDos.Abi.csproj" Private="false" />
  </ItemGroup>
</Project>
```

```csharp
using PhiDos.Abi;

public class Hello : IPhiProgram
{
    public int Run(IDos dos, string[] args)
    {
        dos.WriteLine("Hello!");
        return 0;
    }
}
```

编译出的 `HELLO.dll` 改名为 `HELLO.COM` 放到盘上。仓库里的 `samples/Hello` 可以用 `dotnet build samples/Hello -p:PhiDosDrive=A文件夹路径` 一步编译并放好。完整说明见 [WRITING-PROGRAMS.md](WRITING-PROGRAMS.md)。程序里不能直接用 File、Console、Thread、Task、Environment.Exit 这类 API，否则会显示 "Error in EXE file"。

## 线程三条规则

1. DOS 和所有程序跑在虚拟机自己的线程上。
2. 那个线程上的代码不能直接碰 Godot 节点，用 CallDeferred。
3. PushKey、Snapshot、Mount、WithDrive、ExportImage、Reset、Stop 都可以在主线程上直接调用。
