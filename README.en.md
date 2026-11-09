# phi-DOS

[中文](README.md)

A DOS that runs IL programs.

phi-DOS re-implements the behaviour of MS-DOS 2.0 in C#: 8.3 file names, subdirectories, every internal
command of COMMAND, batch files, redirection and pipes, with the original messages and output formats.
The big difference: .COM / .EXE files on disk are not x86 machine code but .NET assemblies, loaded and
run by the system, so they work on any CPU architecture.

It runs as a stand-alone program on your computer, and can also be embedded in a Godot game as an
in-game computer.

```
phi-DOS Version X.XX

A:\>DIR

 Volume in drive A has no label
 Directory of  A:\

FIND     EXE     8704  11-09-26   2:35a
FORMAT   COM     6144  11-09-26   2:35a
ILDOSVER COM     7680  11-09-26   2:35a
MORE     COM     4608  11-09-26   2:35a
SORT     EXE     6144  11-09-26   2:35a
VI       EXE    23552  11-09-26   2:35a
        6 File(s)    304128 bytes free

A:\>
```

## Features

- Virtual disks (.phidisk): standard FAT12 images, readable and writable; renamed to .img they open in DOSBox and mtools. Folders on your computer can be mounted as drives too. Drives A: to Z:.
- Internal commands: DIR, CD, MD, RD, COPY, DEL, REN, TYPE, DATE, TIME, VER, VOL, CLS, PAUSE, REM, SET, PATH, PROMPT, BREAK, VERIFY, ECHO, IF, GOTO, FOR, SHIFT, EXIT.
- External commands: FORMAT, MORE, SORT, FIND, a vi-like full-screen editor VI, and a colourful system-information display ILDOSVER.
- Redirection `<` `>` `>>`, pipes `|`, batch files and AUTOEXEC.BAT.
- UTF-8 throughout, including Chinese file names and text; 16 colours via ANSI escape sequences.
- Interrupt checks are injected into programs when they load, so endless loops and runaway recursion cannot hang or crash the host.
- `phidisk` packs a folder into a disk image and unpacks it again.

## Download

Get the zip for your platform from [Releases](https://github.com/AstroCanvas-dev/phi-DOS/releases), unpack it and double-click `run-phidos.bat` (on Linux run `run-phidos.sh`). The `A` folder next to it becomes drive A:.

Packages without `-small` include the .NET runtime. The `-small` ones are about 1 MB and need the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

## Build and run

Requires the .NET 10 SDK.

```
dotnet build
dotnet test
dotnet run --project src/PhiDos.ConsoleHost
dotnet run --project src/PhiDos.ConsoleHost -- --help
```

Packaging for Windows x86 / x64 / ARM64 and Linux x64 / ARM64:

```
./tools/release.sh          # Linux, macOS, WSL
./tools/release.ps1         # Windows PowerShell
```

The results go to `publish/release/`. The Release workflow on GitHub's Actions page does the same and publishes them to Releases.

## Documentation

- [docs/MANUAL.en.md](docs/MANUAL.en.md): user manual, every feature and command (English)
- [docs/MANUAL.md](docs/MANUAL.md): the same in Chinese
- [docs/WRITING-PROGRAMS.md](docs/WRITING-PROGRAMS.md): writing your own .COM / .EXE programs (Chinese)
- [docs/GODOT-INTEGRATION.md](docs/GODOT-INTEGRATION.md): embedding in Godot (Chinese)
- [docs/QUICKSTART.md](docs/QUICKSTART.md): quick reference (Chinese)
- [docs/DESIGN.md](docs/DESIGN.md): design (Chinese)

## Layout

```
src/PhiDos.Abi          the interfaces programs use (IDos, IPhiProgram)
src/PhiDos.Core         machine, kernel, COMMAND, drives, program loader
src/PhiDos.Programs     external commands, one project each
src/PhiDos.SystemDisk   packs the external commands, creates system disks
src/PhiDos.ConsoleHost  terminal host (phidos) and the phidisk tool
samples/Hello           sample program
tools/                  packaging scripts
tests/PhiDos.Tests      tests
docs/                   documentation and Godot samples
```

## Licence

MIT, see LICENSE.

Some messages and behaviour follow Microsoft's open-source MS-DOS 2.0 code
(https://github.com/microsoft/MS-DOS, MIT licence); the files concerned keep its copyright notice.
phi-DOS is not affiliated with Microsoft and does not use the MS-DOS trademark.
