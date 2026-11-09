# phi-DOS User Manual

[中文](MANUAL.md)

This manual lists every feature of phi-DOS and how to use each command.

- To write your own programs, see [WRITING-PROGRAMS.md](WRITING-PROGRAMS.md) (Chinese).
- To embed phi-DOS in a Godot game, see [GODOT-INTEGRATION.md](GODOT-INTEGRATION.md) (Chinese).
- For the design, see [DESIGN.md](DESIGN.md) (Chinese).

Contents

1. What phi-DOS is
2. Starting: phidos
3. Disk image tool: phidisk
4. Drives and disks
5. Boot sequence
6. Typing and editing a command line
7. The prompt
8. Internal commands
9. External commands
10. Redirection and pipes
11. Batch files
12. Device names
13. Environment variables
14. Colours
15. Error messages
16. Limits
17. Differences from MS-DOS 2.0

---

## 1. What phi-DOS is

phi-DOS is MS-DOS 2.0 re-implemented in C#. Its commands, output formats and messages follow the original. The two big differences are:

- .COM / .EXE programs on disk are .NET assemblies (IL), not x86 machine code.
- All text is UTF-8, so Chinese and other scripts can be displayed, typed and used in file names.

The screen is an 80 × 25 character screen with 16 colours.

## 2. Starting: phidos

### 2.1 The simplest way

A release package unpacks to:

```
phidos.exe          the program
run-phidos.bat      double-click it: boots with the A folder next to it as drive A:
phidisk.bat         disk image tool (see section 3)
A\                  a folder holding the system programs
```

The Linux package has `phidos`, `run-phidos.sh` and `phidisk` instead.

Running `phidos` without arguments gives drive A: a temporary in-memory system disk. Changes to it are lost on exit.

### 2.2 Command line

```
phidos [options] [disk ...]
phidos --new <file> [--size SIZE] [--label LABEL] [--blank]
phidos --phidisk <arguments>
```

**Mounting drives.** A "disk" is either a .phidisk file (a standard FAT12 image) or a folder on your computer. There are three ways to write it, and they can be mixed:

| Style | Example | Meaning |
|---|---|---|
| In order | `phidos sys.phidisk games` | A:, B:, C: … in turn |
| `-a` to `-d` | `phidos -a sys.phidisk -b games` | drives A: to D: |
| `X:=path` | `phidos C:=D:\games` | any drive A: to Z: |

After `X:=path` you can append options separated by colons, in any order:

| Option | Effect |
|---|---|
| `ro` | write-protected; any write gives `Write protect error` |
| `rw` | writable (default) |
| `360K`, `1.44M`, `10M`, `1G`, a plain number | capacity of a folder drive, default 360K |
| `unlimited` | folder drive without a capacity limit |

Example:

```
phidos A:=sys.phidisk C:=D:\games:ro E:=D:\shared:1.44M Z:=D:\big:unlimited
```

Capacity options apply to folders only. A disk image has its own size; a capacity option on it is ignored with a note.

If nothing is mounted on A:, it gets a temporary in-memory system disk.

**Other options:**

| Option | Effect |
|---|---|
| `-r`, `--readonly` | write-protect all disks listed after it |
| `--no-date` | do not ask for the date and time at boot |
| `-h`, `--help`, `/?` | show help |

**Creating a disk:**

| Option | Effect |
|---|---|
| `--new <file>` | create a new .phidisk with the system programs, then exit |
| `--size <size>` | 160K, 180K, 320K, 360K (default), 720K, 1.2M, 1.44M |
| `--label <label>` | volume label, up to 11 bytes |
| `--blank` | leave the new disk empty |

```
phidos --new story.phidisk --size 1.44M --label STORY
```

### 2.3 Keys while running

| Key | Effect |
|---|---|
| F12 | reset the machine (like the Reset button on the case); disks stay mounted |
| `EXIT` at the outermost prompt | power off and quit phidos |

### 2.4 Running a script

When standard input is redirected, phidos uses it as keyboard input and powers off when it runs out. Handy for automated tests:

```
phidos --no-date story.phidisk < commands.txt > output.txt
```

### 2.5 Debugging

With the environment variable `PHIDOS_TRACE=1`, a `phi-DOS internal error` shows a full stack trace.

## 3. Disk image tool: phidisk

`phidisk` makes and unpacks .phidisk images on your computer. It is really `phidos --phidisk`; `phidisk.bat` / `phidisk` in the release package is a one-line wrapper.

```
phidisk <folder> <image>     pack the folder into a new image
phidisk <image> <folder>     unpack the image into the folder
phidisk <image>              list the contents of the image
```

The order of the arguments sets the direction: folder first packs, image first unpacks.

| Option | Effect |
|---|---|
| `--size <size>` | disk size when packing; default is the smallest size that fits |
| `--label <label>` | volume label when packing |
| `--force`, `-f` | overwrite an existing image, or existing files when unpacking |

Rules:

- Subfolders are packed too. File dates are kept in both directions.
- Names are converted to upper case.
- Names that are not valid 8.3 names (such as `Long Name.txt`) cannot go on a DOS disk. They are skipped and listed at the end. Of two names differing only in letter case, only the first is kept. Symbolic links are skipped.
- The root folder holds a limited number of entries (64 on 160K and 180K disks, 112 on 320K to 720K, 224 on 1.2M and 1.44M). The automatic size choice takes this into account.
- If even 1.44M is too small, phidisk reports an error and writes nothing.
- An existing image is not overwritten without `--force`; the same goes for existing files when unpacking.

Exit codes: 0 success; 1 does not fit or a read/write error; 2 bad arguments or refused to overwrite; 3 packed, but some files were skipped.

```
phidisk A games.phidisk --label GAMES
phidisk games.phidisk restored
phidisk games.phidisk
```

## 4. Drives and disks

### 4.1 Drive letters

There are two drives by default, A: and B:, like a PC with two floppy drives. Using later letters on the command line adds more drives, up to Z:.

Each drive remembers its own current directory. Type a drive letter and a colon (for example `B:`) to change the current drive.

### 4.2 Virtual disks (.phidisk)

- A standard FAT12 floppy image. Renamed to .img it opens directly in DOSBox and mtools.
- Sizes 160K, 180K, 320K, 360K, 720K, 1.2M and 1.44M; other valid FAT12 layouts are accepted when reading.
- Subdirectories and volume labels are supported.
- When phidos mounts an image file, changes are written back to the file immediately.
- "Booting" from one (for example in DOSBox) shows `Non-System disk or disk error`, like an original non-system disk.

### 4.3 Folder drives

A folder on your computer used as a disk:

- Only valid 8.3 names are visible; everything else is hidden and inaccessible.
- Names are not case-sensitive; files and directories created in DOS get upper-case names.
- The capacity is simulated, 360K by default, and used space is the sum of the file sizes. Going over it gives `Insufficient disk space`. Change it with a capacity option, or use `unlimited`.
- `CD ..` at the root gives `Invalid directory`; nothing outside the folder can be reached. Symbolic links are ignored.
- RD does not remove a folder that still contains files DOS cannot see, so nothing on your computer is deleted by accident.
- FORMAT does not work on folder drives.

### 4.4 File names

- 8.3 format: up to 8 bytes for the name and 3 for the extension, counted in UTF-8. A Chinese character takes 3 bytes, so a name holds at most two of them (`存档.TXT` fits, `我的存档.TXT` does not).
- Longer parts are cut off: `verylongname.text` is `VERYLONG.TEX`.
- Characters not allowed: `" / \ [ ] : | < > + = ; ,` and space.
- Not case-sensitive; always shown in upper case.

### 4.5 Paths

- Written like `A:\GAMES\SAVE.DAT`: drive, directories separated by backslashes, file name.
- Without a drive it means the current drive; not starting with `\` means relative to the current directory.
- `.` is the current directory, `..` the parent. The root has no parent.
- The full path of the current directory is at most 63 characters.

### 4.6 Wildcards

- `?` matches one character, or no character at all, so `A?.TXT` matches `A.TXT`.
- `*` matches the rest of the name or of the extension. It covers only its own part: `*` alone matches files without an extension; all files is `*.*`.
- Characters after `*` are ignored: `*X.TXT` is the same as `*.TXT`.

### 4.7 Disk errors

Accessing an empty drive, or writing to a write-protected disk, gives the original critical-error prompt:

```
Not ready error reading drive B
Abort, Retry, Ignore?
```

| Key | Effect |
|---|---|
| A | Abort: end the running program (at the prompt, drop the command) |
| R | Retry: for example after inserting the disk |
| I | Ignore: let the operation fail and carry on |

## 5. Boot sequence

1. `phi-DOS Version X.XX` is shown.
2. If drive A: has `AUTOEXEC.BAT` in its root, it runs.
3. Otherwise you are asked for the date and time, like the original (`--no-date` skips this):

   ```
   Current date is Sun 11-09-2026
   Enter new date:
   Current time is 10:12:05.31
   Enter new time:
   ```

   Press Enter to keep the value.

4. The prompt appears and waits for a command.

## 6. Typing and editing a command line

At the prompt, and whenever a program reads a line, these keys work:

| Key | Effect |
|---|---|
| Enter | finish the line |
| Backspace, Left arrow | delete the previous character |
| Esc | discard the line, show `\` and start again on a new line |
| F1, Right arrow | copy one character from the previous line |
| F3 | copy the rest of the previous line (handy to repeat the last command) |
| F5 | make the current line the new "previous line", show `@` and start a new line |
| Ctrl+C | interrupt the command or program, shows `^C` |
| Ctrl+Z | type an end-of-file mark, shown as `^Z` (ends COPY CON) |

A line holds at most 127 bytes; beyond that the bell rings.

## 7. The prompt

The default prompt shows the current drive and directory, for example `A:\>` or `A:\GAMES>`.

Change it with `PROMPT`, using these codes:

| Code | Shows |
|---|---|
| `$P` | current drive and directory |
| `$N` | current drive |
| `$G` | `>` |
| `$L` | `<` |
| `$B` | `|` |
| `$Q` | `=` |
| `$D` | date |
| `$T` | time |
| `$V` | version |
| `$H` | backspace (erases the previous character) |
| `$E` | the Esc character (for colours, see section 14) |
| `$_` | new line |
| `$$` | `$` |

```
PROMPT $N$G            like MS-DOS 2.0: A>
PROMPT $P$G            the default: A:\>
PROMPT [$D $T]$_$P$G   date and time on one line, the path on the next
PROMPT                 back to the default
```

## 8. Internal commands

Internal commands are built into COMMAND and need no file on disk. Command names are not case-sensitive.

In the syntax below, `[ ]` means optional and `|` separates choices.

### BREAK

```
BREAK [ON | OFF]
```

Sets Ctrl+C checking. With OFF (the default), Ctrl+C is only noticed while reading or writing the screen and keyboard; with ON, it is checked on every system call, so even a pure computing loop can be interrupted. Without a parameter it shows the state: `BREAK is off`.

### CD / CHDIR

```
CD [drive:][path]
CD [drive:]
```

Changes or shows the current directory.

```
CD GAMES          enter GAMES
CD ..             up one level
CD \              to the root
CD                show the current directory, e.g. A:\GAMES
CD B:             show drive B's current directory
CD B:\DATA        set drive B's current directory (the current drive stays the same)
```

`CD..` and `CD\` without a space work too. A missing directory gives `Invalid directory`.

### CLS

```
CLS
```

Clears the screen.

### COPY

```
COPY [/A | /B] source [/A | /B] [+ source [/A | /B] ...] [destination [/A | /B]]
```

Copies files.

| Use | Example |
|---|---|
| copy one file | `COPY README.TXT BACKUP.TXT` |
| copy into another directory or drive | `COPY README.TXT GAMES`, `COPY *.* B:` |
| copy many, changing the extension | `COPY *.TXT *.BAK` |
| copy into the current directory | `COPY B:README.TXT` |
| create a file from the keyboard | `COPY CON NOTE.TXT`, end with Ctrl+Z and Enter |
| show a file on the screen | `COPY NOTE.TXT CON` |
| combine files | `COPY A.TXT+B.TXT C.TXT` |
| append to the first file | `COPY A.TXT+B.TXT` |
| combine many files into one | `COPY *.TXT ALL.TXT` |

- With wildcards in the source, or when combining, each source name is shown. Finally `N File(s) copied` is shown.
- `/A` text mode: read up to Ctrl+Z, and add a Ctrl+Z at the end when writing. `/B` binary mode: copy as is. A normal copy defaults to `/B`, combining defaults to `/A`.
- A copy keeps the source's modification time (combining uses the current time).
- Source and destination are the same file: `File cannot be copied onto itself`.
- When combining, a source that is the destination: `Content of destination lost before copy`, and it is skipped.
- Source not found: `name File not found`.
- Disk full: `Insufficient disk space`, and the half-written destination is deleted.

### DATE

```
DATE [month-day-year]
```

Shows and sets the date. Separators may be `-`, `/` or `.`. The year may have two digits (meaning 19xx) or four; the range is 1980 to 2099.

```
DATE              show the date and ask for a new one; Enter keeps it
DATE 12-25-1985   set it directly
```

A wrong format gives `Invalid date` and asks again. This sets phi-DOS's own clock, not your computer's.

### DEL / ERASE

```
DEL [drive:][path]name
```

Deletes files; wildcards are allowed.

```
DEL NOTE.TXT
DEL *.BAK
DEL *.*           asks "Are you sure (Y/N)?" first; type Y and Enter to delete
DEL GAMES         for a directory, deletes all files in it (also asks first)
```

Directories themselves are not deleted, and read-only files are skipped. Nothing found gives `File not found`.

### DIR

```
DIR [drive:][path][name] [/P] [/W]
```

Lists a directory.

| Use | Lists |
|---|---|
| `DIR` | everything in the current directory |
| `DIR GAMES` | everything in GAMES |
| `DIR *.TXT` | files with the extension TXT |
| `DIR README` | files named README, any extension |
| `DIR .TXT` | same as `DIR *.TXT` |
| `DIR B:` | drive B's current directory |
| `DIR /W` | wide format: names only, five per line |
| `DIR /P` | pause after each screen; press any key to go on |

Output:

```
 Volume in drive A is STORY
 Directory of  A:\

README   TXT       54   1-05-84   9:05a
GAMES        <DIR>     10-20-83  12:00p
        2 File(s)    357376 bytes free
```

The columns are the name (8 places), extension (3), size or `<DIR>`, date and time (12-hour, a for am and p for pm). Subdirectories contain the two entries `.` and `..`. The last line gives the count and the free space.

### ECHO

```
ECHO [ON | OFF | message]
```

| Use | Effect |
|---|---|
| `ECHO` | shows `ECHO is on` or `ECHO is off` |
| `ECHO OFF` | stop showing the prompt and the commands of batch files |
| `ECHO ON` | show them again |
| `ECHO Hello` | shows Hello |

Mostly used in batch files, see section 11.

### EXIT

```
EXIT
```

At the outermost prompt of the terminal host: powers off and quits phidos.

### FOR

```
FOR %variable IN (set) DO command
```

Runs the command once for each item of the set. The variable is one character; in a batch file write two percent signs (`%%f`). Items with wildcards expand to the matching file names.

```
FOR %c IN (A B C) DO ECHO %c
FOR %f IN (*.TXT) DO TYPE %f
```

FOR cannot be nested; trying gives `FOR cannot be nested`.

### GOTO

```
GOTO label
```

Only useful in batch files: continue at the line `:label`. Labels are not case-sensitive and only the first 8 characters count. An unknown label gives `Label not found` and ends the batch file.

### IF

```
IF [NOT] ERRORLEVEL number command
IF [NOT] EXIST name command
IF [NOT] string1==string2 command
```

| Condition | True when |
|---|---|
| `ERRORLEVEL n` | the exit code of the last program is n or more |
| `EXIST name` | the file exists (wildcards allowed) |
| `string1==string2` | the strings are equal (case-sensitive) |

```
IF EXIST SAVE.DAT GOTO load
IF NOT ERRORLEVEL 1 ECHO all fine
IF "%1"=="" ECHO missing parameter
```

The quotes in the last example keep it working when the parameter is empty.

### MD / MKDIR

```
MD [drive:]path
```

Creates a directory. If it already exists, its parent does not exist, or the path would exceed 63 characters, the message is `Unable to create directory`.

### PATH

```
PATH [path;path;...]
PATH ;
```

Sets where programs are searched for. A program name is looked up in the current directory first, then in each PATH directory in turn.

```
PATH \BIN;B:\TOOLS
PATH              show the PATH, or "No Path"
PATH ;            clear it
```

A PATH entry on a drive that does not exist gives `Invalid drive in search path` when a program is run.

### PAUSE

```
PAUSE
```

Shows `Strike a key when ready . . .` and waits for a key. Mostly used in batch files.

### PROMPT

```
PROMPT [text]
```

Sets the prompt, see section 7. Without text it goes back to the default.

### RD / RMDIR

```
RD [drive:]path
```

Removes a directory. It must be empty and must not be the current directory or one of its parents. Otherwise:

```
Invalid path, not directory,
or directory not empty
```

### REM

```
REM [comment]
```

Does nothing; used for comments in batch files.

### REN / RENAME

```
REN [drive:][path]oldname newname
```

Renames files. The new name is a name only, without a path; renaming always happens in the same directory. Wildcards are allowed:

```
REN README.TXT READ.ME
REN *.TXT *.OLD
```

If the new name exists or the old file is not found: `Duplicate file name or File not found`. Directories cannot be renamed.

### SET

```
SET [name=[value]]
```

| Use | Effect |
|---|---|
| `SET` | list all environment variables |
| `SET USER=astro` | set one |
| `SET USER=` | delete it |

Names are converted to upper case. The environment holds 160 bytes; when full the message is `Out of environment space`.

### SHIFT

```
SHIFT
```

Only useful in batch files: moves the parameters one place left, so `%1` becomes `%0`, `%2` becomes `%1` and so on. Used to handle more than 9 parameters.

### TIME

```
TIME [hours[:minutes[:seconds[.hundredths]]]]
```

Shows and sets the time, 24-hour. Parts may be separated by `:` or `.`.

```
TIME              show the time and ask for a new one; Enter keeps it
TIME 9:05
TIME 21:30:00
```

A wrong format gives `Invalid time` and asks again.

### TYPE

```
TYPE [drive:][path]name
```

Shows a text file, stopping at Ctrl+Z. No wildcards. Not found gives `File not found`.

### VER

```
VER
```

Shows the version: `phi-DOS Version X.XX`.

### VERIFY

```
VERIFY [ON | OFF]
```

Sets verify-after-write. In phi-DOS it only remembers the setting; virtual disks do not fail to write.

### VOL

```
VOL [drive:]
```

Shows the volume label: `Volume in drive A is STORY`, or `has no label`.

### Changing drives

```
B:
```

A drive letter and a colon change the current drive. A drive that does not exist gives `Invalid drive specification`. Changing to an empty drive is not an error; `Not ready error` only appears when the disk is actually read.

## 9. External commands

External commands are program files on a disk. Every new system disk, and the A folder of the release package, contains them. Without the file, typing the command gives `Bad command or file name`.

| Command | File |
|---|---|
| FORMAT | FORMAT.COM |
| MORE | MORE.COM |
| SORT | SORT.EXE |
| FIND | FIND.EXE |
| VI | VI.EXE (new in phi-DOS) |
| ILDOSVER | ILDOSVER.COM (new in phi-DOS) |

### FORMAT

```
FORMAT [drive:] [/V]
```

Formats a virtual disk: everything on it is erased; its size stays the same. `/V` asks for a volume label afterwards.

```
A:\>FORMAT B: /V
Insert new diskette for drive B:
and strike any key when ready

Formatting...Format complete
Volume label (11 characters, ENTER for none)? STORY

   362496 bytes total disk space
   362496 bytes available on disk

Format another (Y/N)?
```

Folder drives cannot be formatted: the message is `Format failure` and the folder is left alone.

### MORE

```
MORE < file
command | MORE
```

Shows text one screen at a time. After 24 lines it shows `-- More --`; press any key for the next screen. It reads keys from the keyboard even when the data comes through a pipe.

```
TYPE LONG.TXT | MORE
DIR | MORE
```

### SORT

```
SORT [/R] [/+n] [< file]
command | SORT [/R] [/+n]
```

Sorts its input lines and writes them out, ignoring letter case.

| Switch | Effect |
|---|---|
| `/R` | reverse order |
| `/+n` | compare from column n |

```
SORT < NAMES.TXT
DIR | SORT /+10         sort by extension (it starts in column 10)
TYPE LIST.TXT | SORT /R > SORTED.TXT
```

At most 63 KB can be sorted at once; more gives `SORT: Insufficient memory`.

### FIND

```
FIND [/V] [/C] [/N] "string" [file ...]
```

Shows the lines containing a string (case-sensitive). Without files it reads standard input.

| Switch | Effect |
|---|---|
| `/V` | show the lines that do not contain it |
| `/C` | only show how many lines |
| `/N` | show line numbers |

```
FIND "phi" README.TXT
FIND /C /V "x" A.TXT B.TXT
DIR | FIND "<DIR>"
```

The string must be in double quotes; write a double quote inside it as two. With files, each file is introduced by a line `---------- NAME`.

Messages: `FIND: Invalid number of parameters`, `FIND: Syntax error`, `FIND: File not found name`, `FIND: Invalid Parameter letter`. Exit code 0 normally, 2 for bad parameters.

### VI

```
VI [file]
```

A full-screen editor with the feel of vi. A file that does not exist is created. The top 24 lines show the text, with `~` for lines past the end; the bottom line shows status and commands.

VI has two modes. It starts in **normal mode**, where keys are commands. Keys such as `i` switch to **insert mode**, where keys type text; Esc goes back to normal mode.

Normal mode:

| Key | Effect |
|---|---|
| `h` `j` `k` `l`, arrow keys | left, down, up, right |
| `0`, Home | start of line |
| `^` | first non-blank character of the line |
| `$`, End | end of line |
| `w`, `b` | next word, previous word |
| `gg`, `G` | start of file, end of file |
| number `G` | go to that line, e.g. `12G` |
| Ctrl+F, PageDown | page down |
| Ctrl+B, PageUp | page up |
| `i`, `a` | insert before, after the cursor |
| `I`, `A` | insert at the start, end of the line |
| `o`, `O` | open a new line below, above and insert |
| `x`, Delete | delete the character under the cursor |
| `dd` | delete the line |
| `yy` | copy (yank) the line |
| `p`, `P` | put the copied or deleted line below, above |
| `u` | undo the last change; press again to redo |
| `/text` Enter | search forward |
| `?text` Enter | search backward |
| `n`, `N` | next match in the same, opposite direction |
| `ZZ` | save if changed, then quit |

Insert mode: type text, Backspace (at the start of a line it joins with the previous line), Enter for a new line, arrow keys to move, Esc back to normal mode.

Colon commands (type `:` in normal mode, finish with Enter, Esc cancels):

| Command | Effect |
|---|---|
| `:w` | save |
| `:w name` | save as |
| `:q` | quit (refused with unsaved changes) |
| `:q!` | quit without saving |
| `:wq` | save and quit |
| `:x` | save if changed, then quit |
| `:number` | go to that line |
| `:$` | go to the last line |

Messages:

| Message | Meaning |
|---|---|
| `"NOTE.TXT" 3 lines, 45 characters` | the file that was opened or saved |
| `"NOTE.TXT" [New file]` | this is a new file |
| `-- INSERT --` | in insert mode |
| `No write since last change (use ! to override)` | unsaved changes; use `:q!` to quit without saving |
| `Pattern not found: text` | nothing found |
| `search hit BOTTOM, continuing at TOP` | the search wrapped around |
| `Not an editor command: text` | unknown colon command |

Files are read and written as UTF-8, and each line is saved with CR LF. Chinese characters take two columns; long lines wrap. Ctrl+C quits at once without saving.

Not supported: combined commands such as `dw` or `3dd`, visual mode, windows, registers, macros, syntax highlighting.

### ILDOSVER

```
ILDOSVER
```

Colourful system information: a φ logo on the left; on the right the system, CPU (.NET), language (IL), current drive and more; a 16-colour palette at the bottom. The user name in the title comes from the USER environment variable, or guest:

```
SET USER=astro
ILDOSVER
```

### How programs are found

When a command is not internal, COMMAND looks for a program in this order:

1. The current directory of the current drive, then each directory in PATH.
2. In each directory, `.COM`, then `.EXE`, then `.BAT`.
3. If nothing is found: `Bad command or file name`.

With an extension, only that kind is tried; with a path (`\BIN\HELLO`, `B:HELLO`), only that place is searched.

A file that is not a valid phi-DOS program gives `Error in EXE file`, with the reason on the next line. A program that fails while running gives `Abnormal program termination` and the exception.

## 10. Redirection and pipes

| Syntax | Effect |
|---|---|
| `command > file` | write the output to a file (replacing it) |
| `command >> file` | append the output to the file |
| `command < file` | read input from a file |
| `command1 \| command2` | feed the output of command 1 to command 2 |

```
DIR > LIST.TXT
ECHO one more line >> LIST.TXT
SORT < NAMES.TXT > SORTED.TXT
TYPE LONG.TXT | FIND "phi" | MORE
```

Details:

- Redirection works for internal commands and programs alike; programs inherit it.
- Like the original, the space before `>` stays in the command: `ECHO hi > X.TXT` writes `hi ` (with a trailing space). Write `ECHO hi> X.TXT` to avoid it.
- Pipes work like the original, through temporary files: the first command's output goes to `%PIPE1.$$$` in the root of the current drive, and the next command reads it from there; the file is deleted afterwards. That is why `DIR | SORT` lists this file, as the original did. On a write-protected current drive, `Write protect error` appears first; after choosing I, the message is `Intermediate file error during pipe` and the pipe cannot run.
- `<`, `>` and `|` inside double quotes are not redirection, so `FIND "a|b" X.TXT` works.

## 11. Batch files

A text file with the extension `.BAT` holds one command per line. Typing its name (without the extension) runs the lines in turn.

### Parameters

| Syntax | Stands for |
|---|---|
| `%0` | the batch file's own name, as typed |
| `%1` to `%9` | parameters 1 to 9 |
| `%name%` | the value of an environment variable, e.g. `%PATH%` |
| `%%` | one percent sign |

Missing parameters are empty. Use SHIFT for more than 9.

### Example

```
ECHO OFF
REM usage: BACKUP file
IF "%1"=="" GOTO usage
IF NOT EXIST %1 GOTO missing
COPY %1 %1.BAK
ECHO %1 has been backed up.
GOTO end
:missing
ECHO %1 does not exist.
GOTO end
:usage
ECHO Usage: BACKUP file
:end
```

### Rules

- With ECHO on, each command is shown with the prompt before it runs. When the batch file ends, ECHO goes back to what it was before.
- Lines starting with a colon are labels, targets for GOTO; they are not run.
- Running another batch file from a batch file transfers to it and does not come back (2.0 has no CALL).
- The batch file is reopened for every line. If it is deleted while running, the message is `Batch file missing` and it ends.
- Ctrl+C while running asks `Terminate batch job (Y/N)?`: Y ends the batch file, N goes on with the next line.
- A line is at most 127 bytes.
- `AUTOEXEC.BAT` in the root of A: runs at boot, see section 5.

## 12. Device names

These names mean devices, in any directory and with any extension:

| Name | What it is |
|---|---|
| `CON` | the console: writing shows on the screen, reading reads a line from the keyboard |
| `NUL` | the null device: writes are discarded, reads end immediately |
| `PRN`, `LPT1` | printer (the same as NUL in phi-DOS) |
| `AUX`, `COM1` | serial port (the same as NUL in phi-DOS) |

```
COPY CON NOTE.TXT       create a file from the keyboard
TYPE NOTE.TXT > PRN     "print" (nothing happens)
FIND "x" A.TXT > NUL    keep only the exit code
```

## 13. Environment variables

At boot:

| Name | Value |
|---|---|
| `PATH` | empty |
| `COMSPEC` | `A:\COMMAND.COM` (kept as in the original; there is no such file on disk) |

Others that are used:

| Name | Used by |
|---|---|
| `PROMPT` | the prompt, see section 7 |
| `USER` | the user name in ILDOSVER's title |

A program gets a copy of the environment. The environment holds 160 bytes in total.

## 14. Colours

phi-DOS has the features of ANSI.SYS built in. Programs change colours by writing ANSI escape sequences, and the prompt can do it with `$E`:

```
PROMPT $E[1;32m$P$G$E[0m
```

turns the prompt bright green.

| Code | Effect |
|---|---|
| `ESC[0m` | back to the default (grey on black) |
| `ESC[1m` | bright |
| `ESC[5m` | blink |
| `ESC[7m` | reverse |
| `ESC[30m` to `ESC[37m` | foreground: black, red, green, yellow, blue, magenta, cyan, white |
| `ESC[40m` to `ESC[47m` | background, same order |
| `ESC[2J` | clear the screen |
| `ESC[row;columnH` | move the cursor |
| `ESC[K` | clear to the end of the line |

The terminal host passes these sequences to your terminal. Windows Terminal (the default on Windows 11) supports them; very old console windows may show them as garbage.

## 15. Error messages

| Message | Meaning |
|---|---|
| `Bad command or file name` | not an internal command, and no such program was found |
| `File not found` | the file does not exist |
| `Invalid directory` | the directory does not exist |
| `Invalid drive specification` | there is no such drive |
| `Invalid number of parameters` | wrong number of parameters |
| `Invalid parameter` | a wrong parameter or switch |
| `Syntax error` | wrong syntax (IF, FOR, SET …) |
| `Insufficient disk space` | the disk is full |
| `File creation error` | a file could not be created (e.g. the root folder is full) |
| `File cannot be copied onto itself` | COPY's source and destination are the same file |
| `Content of destination lost before copy` | when combining, a source is the destination |
| `Duplicate file name or File not found` | REN's new name exists, or the old file does not |
| `Unable to create directory` | MD failed |
| `Invalid path, not directory, or directory not empty` | RD failed |
| `Out of environment space` | the environment is full |
| `Must specify ON or OFF` | a wrong parameter to BREAK or VERIFY |
| `Label not found` | GOTO could not find the label |
| `FOR cannot be nested` | a FOR inside a FOR |
| `Batch file missing` | the running batch file has disappeared |
| `Invalid drive in search path` | PATH names a drive that does not exist |
| `Intermediate file error during pipe` | the pipe's temporary file could not be created |
| `Not ready error reading drive X` | no disk in the drive |
| `Write protect error writing drive X` | the disk is write-protected |
| `Error in EXE file` | the program file cannot run; the reason is on the next line |
| `Abnormal program termination` | the program failed while running; the exception is on the next line |
| `Program too big to fit in memory` | the program file is over 640 KB, or programs are nested too deeply |
| `phi-DOS internal error` | a bug in phi-DOS itself; please report it (PHIDOS_TRACE=1 shows details) |

## 16. Limits

| Item | Limit |
|---|---|
| Screen | 80 columns × 25 lines, 16 colours |
| Drives | A: to Z:, A: and B: by default |
| File names | 8.3, counted in UTF-8 bytes |
| Current directory path | 63 characters |
| Command line | 127 bytes |
| Environment | 160 bytes |
| Files open per program | 20 (5 of them are the standard devices) |
| Program file size | 640 KB |
| Program nesting | 16 levels |
| Root folder entries | 64 on 160K/180K, 112 on 320K to 720K, 224 on 1.2M/1.44M |
| Folder drive capacity | 360 KB by default, adjustable or unlimited |
| SORT | 63 KB at once |

## 17. Differences from MS-DOS 2.0

phi-DOS follows 2.0 as closely as it can. These differences are deliberate:

| Area | MS-DOS 2.0 | phi-DOS |
|---|---|---|
| Programs | x86 machine code | .NET assemblies (IL) |
| Character set | single-byte code page | UTF-8, including Chinese |
| Default prompt | `A>` | `A:\>` (`PROMPT $N$G` brings back the old one) |
| Drives | floppy and hard disks | virtual disk images and folders on your computer |
| `\|` `<` `>` inside quotes | treated as redirection | not treated as redirection |
| Editor | EDLIN | VI (new) |
| New commands | none | ILDOSVER |
| Not implemented | — | CTTY, FCB file calls, and the external commands CHKDSK, EDLIN, DEBUG, DISKCOPY, SYS etc. |
| COMMAND.COM | a file on disk | built in; no file needed |
| Ctrl+C | cannot stop a pure computing loop | same, unless BREAK ON; the host's reset always stops it |
