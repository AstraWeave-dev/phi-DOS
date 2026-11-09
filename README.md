# phi-DOS

基于 IL 运行的 DOS 系统模块。

phi-DOS 用 C# 按功能重新实现了 MS-DOS 2.0 的行为：8.3 文件名、子目录、COMMAND 的全部内置命令、
批处理、重定向和管道，提示文字和输出格式与原版一致。和原版最大的不同是，磁盘上的 .COM / .EXE
不是 x86 机器码，而是 .NET 程序集，由系统加载执行，所以可以跨架构运行。

它既可以作为独立程序在电脑上运行，也可以嵌进 Godot 游戏，作为游戏里的一台"电脑"。

```
phi-DOS Version X.XX

A>DIR

 Volume in drive A has no label
 Directory of  A:\

FIND     EXE     8704  11-08-26   6:29p
FORMAT   COM     6144  11-08-26   6:29p
MORE     COM     4608  11-08-26   6:29p
SORT     EXE     6144  11-08-26   6:29p
        4 File(s)    335872 bytes free

A>
```

## 功能

- 虚拟磁盘 .phidisk：标准 FAT12 镜像，可读写，改名为 .img 后 DOSBox、mtools 都能打开。也可以把电脑上的文件夹挂成驱动器。
- 内置命令：DIR、CD、MD、RD、COPY、DEL、REN、TYPE、DATE、TIME、VER、VOL、CLS、PAUSE、REM、SET、PATH、PROMPT、BREAK、VERIFY、ECHO、IF、GOTO、FOR、SHIFT、EXIT。
- 外部命令：FORMAT、MORE、SORT、FIND，一个模仿 vi 手感的全屏编辑器 VI，以及彩色的系统信息展示 ILDOSVER。
- 重定向 `<` `>` `>>`，管道 `|`，批处理和 AUTOEXEC.BAT。
- 字符集为 UTF-8，支持中文文件名和中文显示。
- 程序加载时自动注入中断检查点，死循环和无限递归都不会卡死或弄垮宿主。

## 构建和运行

需要 .NET 10 SDK。

```
dotnet build
dotnet test
dotnet run --project src/PhiDos.ConsoleHost
dotnet run --project src/PhiDos.ConsoleHost -- --help
```

打包发布（Windows x86 / x64 / ARM64、Linux x64 / ARM64，每个平台一个完整版和一个小体积版）：

```
./tools/release.sh          # Linux、macOS、WSL
./tools/release.ps1         # Windows PowerShell
```

结果在 `publish/release/`。解压后双击 `run-phidos.bat`（Linux 上运行 `run-phidos.sh`），就会把旁边的 `A` 文件夹挂成 A 盘开机，里面放好了全部系统命令。小体积版约 1MB，需要电脑上装有 .NET 10 运行时；完整版约 30MB，自带运行时。

## 文档

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
src/PhiDos.ConsoleHost  终端版宿主（phidos）
samples/Hello           程序示例
tools/                  打包发布脚本
tests/PhiDos.Tests      测试
docs/                   文档与 Godot 示例
```

## 授权

本项目以 MIT 协议发布，见 LICENSE。

部分提示文字和行为参照了微软开源的 MS-DOS 2.0 源码（https://github.com/microsoft/MS-DOS ，MIT 协议），
相关文件的开头保留了其版权声明。phi-DOS 与微软没有关联，也不使用 MS-DOS 商标。
