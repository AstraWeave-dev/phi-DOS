# phi-DOS

[中文](README.md)

A DOS that runs IL programs.

phi-DOS is an independently written, DOS-style operating system simulator in C#. It works like a classic DOS
command line: 8.3 file names, subdirectories, internal commands, batch files, redirection and pipes, with the
messages and output formats DOS users know. Inside it is entirely different: .COM / .EXE files on disk are not
x86 machine code but .NET assemblies, loaded and
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

The packages are about 1 MB and need the [.NET 10 runtime](https://dotnet.microsoft.com/download/dotnet/10.0). Packages ending in `-with-dotnet`, when a release has them, include the runtime.

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

Add a version with `./tools/release.sh 0.1.0`; the programs then show `phi-DOS Version 0.10`. Add `--with-dotnet` to also build packages that include the .NET runtime (about 30 MB, with the .NET licence files). Every package contains `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt`. The results go to `publish/release/`. The Release workflow on GitHub's Actions page does the same and publishes them to Releases.

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

phi-DOS was designed with reference to Microsoft's [MS-DOS source code](https://github.com/microsoft/MS-DOS), released under the MIT licence, and some of its messages are taken from it.
phi-DOS itself is written independently in C# and does not contain or translate the MS-DOS code. The MS-DOS licence notice and the licences of other third-party components are in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

MS-DOS is a registered trademark of Microsoft Corporation. phi-DOS is not affiliated with, sponsored by or endorsed by Microsoft.
Third-party components and their licences are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
