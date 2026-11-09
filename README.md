# phi-DOS

[English](README.en.md)

基于 IL 运行的 DOS 系统模块。

phi-DOS 是一个用 C# 独立编写的 DOS 风格操作系统模拟软件。它在操作上模仿经典 DOS 命令行：8.3 文件名、子目录、
内置命令、批处理、重定向和管道，提示文字和输出格式也是 DOS 用户熟悉的样子。内部实现和 DOS 完全不同：磁盘上的 .COM / .EXE
不是 x86 机器码，而是 .NET 程序集，由系统加载执行，所以可以跨架构运行。

它既可以作为独立程序在电脑上运行，也可以嵌进 Godot 游戏，作为游戏里的一台"电脑"。

```
phi-DOS Version X.XX

A:\>DIR

 Volume in drive A has no label
 Directory of  A:\

FIND     EXE     8704  11-08-26   6:29p
FORMAT   COM     6144  11-08-26   6:29p
MORE     COM     4608  11-08-26   6:29p
SORT     EXE     6144  11-08-26   6:29p
        4 File(s)    335872 bytes free

A:\>
```

## 功能

- 虚拟磁盘 .phidisk：标准 FAT12 镜像，可读写，改名为 .img 后 DOSBox、mtools 都能打开。也可以把电脑上的文件夹挂成驱动器。驱动器从 A: 到 Z:。
- 内置命令：DIR、CD、MD、RD、COPY、DEL、REN、TYPE、DATE、TIME、VER、VOL、CLS、PAUSE、REM、SET、PATH、PROMPT、BREAK、VERIFY、ECHO、IF、GOTO、FOR、SHIFT、EXIT。
- 外部命令：FORMAT、MORE、SORT、FIND，一个模仿 vi 手感的全屏编辑器 VI，以及彩色的系统信息展示 ILDOSVER。
- 重定向 `<` `>` `>>`，管道 `|`，批处理和 AUTOEXEC.BAT。
- 字符集为 UTF-8，支持中文文件名和中文显示。
- 程序加载时自动注入中断检查点，死循环和无限递归都不会卡死或弄垮宿主。
- 镜像工具 `phidisk`：把文件夹打包成磁盘镜像，也能解包回来。

## 下载

到 [Releases](https://github.com/AstroCanvas-dev/phi-DOS/releases) 下载对应平台的 zip，解压后双击 `run-phidos.bat`（Linux 上运行 `run-phidos.sh`）。

## 构建和运行

需要 .NET 10 SDK。

```
dotnet build
dotnet test
dotnet run --project src/PhiDos.ConsoleHost
dotnet run --project src/PhiDos.ConsoleHost -- --help
```

打包发布（Windows x86 / x64 / ARM64、Linux x64 / ARM64）：

```
./tools/release.sh 0.1.0                  # Linux、macOS、WSL
./tools/release.ps1 -Version 0.1.0        # Windows PowerShell
```

版本号会编译进程序，`0.1.0` 显示为 `phi-DOS Version 0.10`；不写版本号时显示 `X.XX`。结果在 `publish/release/`。也可以在 GitHub 的 Actions 页面运行 Release 工作流，由 GitHub 打包并发布到 Releases。解压后双击 `run-phidos.bat`（Linux 上运行 `run-phidos.sh`），就会把旁边的 `A` 文件夹挂成 A 盘开机，里面放好了全部系统命令。

默认每个平台打一个约 1MB 的包，不带 .NET 运行时，电脑上需要装有 .NET 10 运行时。需要自带运行时的版本时加上 `--with-dotnet`（PowerShell 是 `-WithDotnet`，Actions 里勾选 with_dotnet），会另外打出 `-with-dotnet` 结尾的包，约 30MB，并附上 .NET 的授权文件。每个包里都有 `LICENSE.txt` 和 `THIRD-PARTY-NOTICES.txt`。

## 文档

- [docs/MANUAL.md](docs/MANUAL.md)：使用手册，全部功能和每个命令的用法（[English](docs/MANUAL.en.md)）
- [docs/WRITING-PROGRAMS.md](docs/WRITING-PROGRAMS.md)：编写自己的 .COM / .EXE 程序
- [docs/QUICKSTART.md](docs/QUICKSTART.md)：速查
- [docs/GODOT-INTEGRATION.md](docs/GODOT-INTEGRATION.md)：接入 Godot 的详细指南，包括编写自己的程序
- [docs/DESIGN.md](docs/DESIGN.md)：设计文档
- [docs/godot/](docs/godot/)：Godot 示例脚本

## 目录

```
src/PhiDos.Abi          程序使用的接口（IDos、IPhiProgram）
src/PhiDos.Core         虚拟机、内核、COMMAND、驱动器、程序加载器
src/PhiDos.Programs     外部命令，每个命令一个项目
src/PhiDos.SystemDisk   打包外部命令，新建系统盘
src/PhiDos.ConsoleHost  终端版宿主（phidos）和镜像工具 phidisk
samples/Hello           程序示例
tools/                  打包发布脚本
tests/PhiDos.Tests      测试
docs/                   文档与 Godot 示例
```

## 授权

本项目以 MIT 协议发布，见 LICENSE。

phi-DOS 在设计时参考了微软以 MIT 协议开源的 [MS-DOS 源码](https://github.com/microsoft/MS-DOS)，部分提示文字取自其中。
phi-DOS 本身是用 C# 独立编写的，没有包含或翻译 MS-DOS 的代码。MS-DOS 的授权声明和其他第三方组件的授权见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

MS-DOS 是 Microsoft Corporation 的注册商标。phi-DOS 与微软没有关联，也没有得到微软的赞助或认可。
