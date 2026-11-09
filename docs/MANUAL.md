# phi-DOS 使用手册

[English](MANUAL.en.md)

这份手册列出 phi-DOS 的全部功能和每个命令的用法。

- 想写自己的程序：看 [WRITING-PROGRAMS.md](WRITING-PROGRAMS.md)
- 想接进 Godot 游戏：看 [GODOT-INTEGRATION.md](GODOT-INTEGRATION.md)
- 想了解设计：看 [DESIGN.md](DESIGN.md)

目录

1. phi-DOS 是什么
2. 启动：phidos
3. 镜像工具：phidisk
4. 驱动器与磁盘
5. 开机过程
6. 命令行的输入与编辑
7. 提示符
8. 内置命令
9. 外部命令
10. 重定向与管道
11. 批处理
12. 设备名
13. 环境变量
14. 颜色
15. 错误提示一览
16. 各种限制
17. 和 MS-DOS 2.0 的区别

---

## 1. phi-DOS 是什么

phi-DOS 是用 C# 重新实现的 MS-DOS 2.0。它的命令、输出格式、提示文字都照原版来，和原版最大的不同有两点：

- 磁盘上的 .COM / .EXE 程序是 .NET 程序集（IL），不是 x86 机器码。
- 文字一律用 UTF-8，可以显示和输入中文，文件名里也能用中文。

屏幕是 80 列 × 25 行的字符屏幕，支持 16 色。

## 2. 启动：phidos

### 2.1 最简单的用法

发布包解压后的结构：

```
phidos.exe          主程序
run-phidos.bat      双击它：把旁边的 A 文件夹挂成 A 盘开机
phidisk.bat         镜像工具（见第 3 节）
A\                  放好了系统命令的文件夹
```

Linux 包里对应的是 `phidos`、`run-phidos.sh`、`phidisk`。

直接运行 `phidos`、不带参数时，A 盘是一张内存里的临时系统盘，关掉后改动就没了。

### 2.2 命令行

```
phidos [选项] [磁盘 ...]
phidos --new <文件> [--size 大小] [--label 卷标] [--blank]
phidos --phidisk <参数>
```

**挂载驱动器。** "磁盘"可以是一个 .phidisk 文件（标准 FAT12 镜像），也可以是电脑上的一个文件夹。有三种写法，可以混用：

| 写法 | 例子 | 说明 |
|---|---|---|
| 按顺序 | `phidos sys.phidisk games` | 依次挂到 A:、B:、C: …… |
| `-a` 到 `-d` | `phidos -a sys.phidisk -b games` | 指定 A: 到 D: |
| `X:=路径` | `phidos C:=D:\games` | 任意盘符 A: 到 Z: |

`X:=路径` 后面可以用冒号追加选项，顺序随意：

| 选项 | 作用 |
|---|---|
| `ro` | 写保护，任何写操作都会出现 `Write protect error` |
| `rw` | 可写（默认） |
| `360K`、`1.44M`、`10M`、`1G`、纯数字 | 文件夹驱动器的容量，默认 360K |
| `unlimited` | 文件夹驱动器不限容量 |

例子：

```
phidos A:=sys.phidisk C:=D:\games:ro E:=D:\shared:1.44M Z:=D:\big:unlimited
```

容量选项只对文件夹有效。镜像文件有自己的大小，给它加容量选项会被忽略并提示。

没有指定 A 盘时，A 盘自动是一张内存里的临时系统盘。

**其他选项：**

| 选项 | 作用 |
|---|---|
| `-r`、`--readonly` | 之后列出的磁盘全部写保护 |
| `--no-date` | 开机时不问日期和时间 |
| `-h`、`--help`、`/?` | 显示帮助 |

**新建磁盘：**

| 选项 | 作用 |
|---|---|
| `--new <文件>` | 新建一个 .phidisk，放好系统命令，然后退出 |
| `--size <大小>` | 160K、180K、320K、360K（默认）、720K、1.2M、1.44M |
| `--label <卷标>` | 卷标，最多 11 字节 |
| `--blank` | 新建空白盘，不放系统命令 |

```
phidos --new story.phidisk --size 1.44M --label STORY
```

### 2.3 运行时的按键

| 按键 | 作用 |
|---|---|
| F12 | 重启虚拟机（相当于按机箱上的 Reset），盘保持插着 |
| 在最外层提示符输入 `EXIT` | 关机并退出 phidos |

### 2.4 用脚本运行

标准输入被重定向时，phidos 会把文件内容当作键盘输入，读完后自动关机。适合自动测试：

```
phidos --no-date story.phidisk < commands.txt > output.txt
```

### 2.5 调试

设置环境变量 `PHIDOS_TRACE=1` 后再运行，出现 `phi-DOS internal error` 时会显示完整的调用栈。

## 3. 镜像工具：phidisk

`phidisk` 用来在电脑上制作和拆开 .phidisk 镜像。它其实是 `phidos --phidisk`，发布包里的 `phidisk.bat` / `phidisk` 只是一行调用。

```
phidisk <文件夹> <镜像>     把文件夹打包成新镜像
phidisk <镜像> <文件夹>     把镜像解包到文件夹
phidisk <镜像>              列出镜像的内容
```

参数的先后顺序决定方向：先写文件夹是打包，先写镜像是解包。

| 选项 | 作用 |
|---|---|
| `--size <大小>` | 打包时的软盘规格。默认自动挑放得下的最小规格 |
| `--label <卷标>` | 打包时的卷标 |
| `--force`、`-f` | 镜像已存在时覆盖；解包时覆盖已有的文件 |

规则：

- 子文件夹也会打包进去，文件的修改时间两个方向都保留。
- 名字一律转成大写。
- 不符合 8.3 的名字（比如 `Long Name.txt`）放不进 DOS 的盘，会被跳过，并在最后列出来。名字只差大小写的两个文件，只保留第一个。符号链接也跳过。
- 根目录的项数有上限（160K 和 180K 盘是 64 项，320K 到 720K 是 112 项，1.2M 和 1.44M 是 224 项）。自动挑规格时会考虑这一点。
- 最大的 1.44M 也放不下时报错，不生成文件。
- 镜像已经存在时，不加 `--force` 就拒绝覆盖；解包时目标文件已存在也一样。

退出码：0 成功；1 放不下或读写出错；2 参数错误或拒绝覆盖；3 打包成功，但有文件被跳过。

```
phidisk A games.phidisk --label GAMES
phidisk games.phidisk restored
phidisk games.phidisk
```

## 4. 驱动器与磁盘

### 4.1 盘符

默认有 A: 和 B: 两个驱动器，和当年双软驱的 PC 一样。命令行用到更后面的盘符时，驱动器会相应增加，最多到 Z:。

每个驱动器各自记着一个当前目录。输入盘符加冒号（比如 `B:`）切换当前驱动器。

### 4.2 虚拟磁盘（.phidisk）

- 就是标准的 FAT12 软盘镜像，改名成 .img 后，DOSBox、mtools 都能直接打开。
- 支持 160K、180K、320K、360K、720K、1.2M、1.44M 七种规格，读取时也接受其他合法的 FAT12 规格。
- 支持子目录和卷标。
- 用 `phidos` 直接挂载文件时，改动实时写回文件。
- 从非系统盘"启动"时（比如在 DOSBox 里），会显示 `Non-System disk or disk error`，和原版一样。

### 4.3 文件夹驱动器

把电脑上的一个文件夹当成磁盘：

- 只看得见符合 8.3 的名字，其他的完全隐藏、不可访问。
- 不区分大小写；DOS 里新建的文件和目录用大写名字。
- 容量是假的，默认 360K，已用空间按文件大小累计；超过时报 `Insufficient disk space`。可以用容量选项改，或者设为 `unlimited`。
- 在根目录 `CD ..` 会报 `Invalid directory`，访问不到文件夹外面；符号链接一律忽略。
- 文件夹里有 DOS 看不见的文件时，RD 不会删除这个文件夹，免得误删电脑上的东西。
- FORMAT 对它无效。

### 4.4 文件名

- 8.3 格式：主名最多 8 字节，扩展名最多 3 字节，按 UTF-8 计算。一个汉字占 3 字节，所以主名最多两个汉字（`存档.TXT` 可以，`我的存档.TXT` 太长）。
- 超出的部分会被截掉：`verylongname.text` 等于 `VERYLONG.TEX`。
- 不能用的字符：`" / \ [ ] : | < > + = ; ,` 和空格。
- 不区分大小写，统一显示为大写。

### 4.5 路径

- `A:\GAMES\SAVE.DAT` 这样的写法：盘符、反斜杠分隔的目录、文件名。
- 不写盘符就是当前驱动器；不以 `\` 开头就是从当前目录算起。
- `.` 是当前目录，`..` 是上一级。根目录没有上一级。
- 当前目录的完整路径最长 63 个字符。

### 4.6 通配符

- `?` 匹配一个字符，也可以匹配"没有字符"，所以 `A?.TXT` 能匹配 `A.TXT`。
- `*` 匹配主名或扩展名中剩下的全部。`*` 只管它所在的那一部分：`*` 只匹配没有扩展名的文件，所有文件要写 `*.*`。
- `*` 后面的字符会被忽略：`*X.TXT` 等于 `*.TXT`。

### 4.7 磁盘错误

访问没插盘的驱动器，或者在写保护的盘上写东西时，会出现原版的严重错误提示：

```
Not ready error reading drive B
Abort, Retry, Ignore?
```

| 按键 | 作用 |
|---|---|
| A | 放弃：结束当前程序（在提示符下则放弃这条命令） |
| R | 重试：比如把盘插上之后按 R |
| I | 忽略：让这次操作失败，程序继续 |

## 5. 开机过程

1. 显示 `phi-DOS Version X.XX`。
2. 如果 A 盘根目录有 `AUTOEXEC.BAT`，运行它。
3. 没有的话，像原版一样询问日期和时间（启动时加 `--no-date` 可以跳过）：

   ```
   Current date is Sun 11-09-2026
   Enter new date:
   Current time is 10:12:05.31
   Enter new time:
   ```

   直接按回车表示不改。

4. 显示提示符，等你输入命令。

## 6. 命令行的输入与编辑

在提示符下输入命令，以及程序读一整行输入时，可以用这些键：

| 按键 | 作用 |
|---|---|
| 回车 | 结束输入 |
| 退格、左箭头 | 删掉前一个字符 |
| Esc | 作废这一行，显示 `\` 并换行重新输入 |
| F1、右箭头 | 从上一次的输入里复制一个字符 |
| F3 | 复制上一次输入剩下的部分（常用来重复上一条命令） |
| F5 | 把当前输入设为新的"上一次输入"，显示 `@` 并换行 |
| Ctrl+C | 中断当前命令或程序，显示 `^C` |
| Ctrl+Z | 输入一个文件结束符，显示为 `^Z`（COPY CON 用它结束） |

一行最多 127 字节，超出时会响铃。

## 7. 提示符

默认提示符显示当前驱动器和目录，例如 `A:\>`、`A:\GAMES>`。

用 `PROMPT` 命令可以自定义，里面可以用这些元字符：

| 元字符 | 显示 |
|---|---|
| `$P` | 当前驱动器和目录 |
| `$N` | 当前驱动器 |
| `$G` | `>` |
| `$L` | `<` |
| `$B` | `|` |
| `$Q` | `=` |
| `$D` | 日期 |
| `$T` | 时间 |
| `$V` | 版本信息 |
| `$H` | 退格（删掉前一个字符） |
| `$E` | Esc 字符（用来写颜色，见第 14 节） |
| `$_` | 换行 |
| `$$` | `$` |

```
PROMPT $N$G            原版 2.0 的样子：A>
PROMPT $P$G            默认的样子：A:\>
PROMPT [$D $T]$_$P$G   第一行显示日期时间，第二行是路径
PROMPT                 恢复默认
```

## 8. 内置命令

内置命令由 COMMAND 自己执行，不需要盘上有文件。命令名不区分大小写。

下文语法里，`[ ]` 表示可以省略，`|` 表示几选一。

### BREAK

```
BREAK [ON | OFF]
```

设置 Ctrl+C 检查。OFF（默认）时只有在读写屏幕和键盘时才检查 Ctrl+C；ON 时所有系统调用都检查，连纯计算的死循环也能用 Ctrl+C 打断。不带参数时显示当前状态：`BREAK is off`。

### CD / CHDIR

```
CD [盘:][路径]
CD [盘:]
```

切换当前目录，或者显示当前目录。

```
CD GAMES          进入 GAMES
CD ..             回到上一级
CD \              回到根目录
CD                显示当前目录，例如 A:\GAMES
CD B:             显示 B 盘的当前目录
CD B:\DATA        把 B 盘的当前目录设为 \DATA（当前驱动器不变）
```

`CD..`、`CD\` 这种不加空格的写法也可以。目录不存在时显示 `Invalid directory`。

### CLS

```
CLS
```

清屏。

### COPY

```
COPY [/A | /B] 源 [/A | /B] [+ 源 [/A | /B] ...] [目标 [/A | /B]]
```

复制文件。

| 用法 | 例子 |
|---|---|
| 复制一个文件 | `COPY README.TXT BACKUP.TXT` |
| 复制到另一个目录或盘 | `COPY README.TXT GAMES`、`COPY *.* B:` |
| 改扩展名批量复制 | `COPY *.TXT *.BAK` |
| 复制到当前目录 | `COPY B:README.TXT` |
| 从键盘创建文件 | `COPY CON NOTE.TXT`，输入完按 Ctrl+Z 再回车 |
| 把文件显示到屏幕 | `COPY NOTE.TXT CON` |
| 合并文件 | `COPY A.TXT+B.TXT C.TXT` |
| 追加到第一个文件 | `COPY A.TXT+B.TXT` |
| 多个文件合并成一个 | `COPY *.TXT ALL.TXT` |

- 源里有通配符、或者在合并时，会逐个显示源文件名。最后显示 `N File(s) copied`。
- `/A` 文本方式：读到 Ctrl+Z 为止，写的时候在结尾补一个 Ctrl+Z。`/B` 二进制方式：原样复制。普通复制默认 `/B`，合并时默认 `/A`。
- 复制会保留源文件的修改时间（合并时用当前时间）。
- 源和目标是同一个文件：`File cannot be copied onto itself`。
- 合并时某个源就是目标：`Content of destination lost before copy`，跳过它。
- 源不存在：`文件名 File not found`。
- 磁盘写满：`Insufficient disk space`，并删掉写了一半的目标文件。

### DATE

```
DATE [月-日-年]
```

显示并设置日期。分隔符可以是 `-`、`/`、`.`。年份可以写两位（按 19xx 算）或四位，范围 1980 到 2099。

```
DATE              显示当前日期并询问新日期，直接回车表示不改
DATE 12-25-1985   直接设置
```

日期格式不对时显示 `Invalid date` 并重新询问。这里设置的是 phi-DOS 自己的时钟，不会改动电脑的时间。

### DEL / ERASE

```
DEL [盘:][路径]文件名
```

删除文件，可以用通配符。

```
DEL NOTE.TXT
DEL *.BAK
DEL *.*           会先问 Are you sure (Y/N)?，输入 Y 回车才删
DEL GAMES         参数是目录时，删除目录里的全部文件（同样会先问）
```

不会删除目录本身，只读文件会被跳过。找不到时显示 `File not found`。

### DIR

```
DIR [盘:][路径][文件名] [/P] [/W]
```

列出目录内容。

| 用法 | 列出 |
|---|---|
| `DIR` | 当前目录的全部 |
| `DIR GAMES` | GAMES 目录的全部 |
| `DIR *.TXT` | 扩展名是 TXT 的 |
| `DIR README` | 主名是 README 的，任意扩展名 |
| `DIR .TXT` | 等于 `DIR *.TXT` |
| `DIR B:` | B 盘当前目录 |
| `DIR /W` | 宽格式：只显示名字，每行五个 |
| `DIR /P` | 每满一屏暂停，按任意键继续 |

输出格式：

```
 Volume in drive A is STORY
 Directory of  A:\

README   TXT       54   1-05-84   9:05a
GAMES        <DIR>     10-20-83  12:00p
        2 File(s)    357376 bytes free
```

依次是名字（8 格）、扩展名（3 格）、大小或 `<DIR>`、日期、时间（12 小时制，a 上午、p 下午）。子目录里会有 `.` 和 `..` 两项。最后一行是数量和剩余空间。

### ECHO

```
ECHO [ON | OFF | 消息]
```

| 用法 | 作用 |
|---|---|
| `ECHO` | 显示 `ECHO is on` 或 `ECHO is off` |
| `ECHO OFF` | 不再显示提示符和批处理里的命令 |
| `ECHO ON` | 恢复显示 |
| `ECHO Hello` | 显示 Hello |

主要用在批处理里，见第 11 节。

### EXIT

```
EXIT
```

在终端版的最外层提示符下：关机并退出 phidos。

### FOR

```
FOR %变量 IN (集合) DO 命令
```

对集合里的每一项执行一次命令。变量是一个字符；在批处理里要写两个百分号（`%%f`）。集合里带通配符的项会展开成匹配的文件名。

```
FOR %c IN (A B C) DO ECHO %c
FOR %f IN (*.TXT) DO TYPE %f
```

FOR 不能嵌套，嵌套时显示 `FOR cannot be nested`。

### GOTO

```
GOTO 标号
```

只在批处理里有用，跳到 `:标号` 那一行继续执行。标号不区分大小写，只比较前 8 个字符。找不到时显示 `Label not found` 并结束批处理。

### IF

```
IF [NOT] ERRORLEVEL 数字 命令
IF [NOT] EXIST 文件名 命令
IF [NOT] 字符串1==字符串2 命令
```

| 条件 | 成立的情况 |
|---|---|
| `ERRORLEVEL n` | 上一个程序的退出码大于等于 n |
| `EXIST 文件名` | 文件存在（可以用通配符） |
| `字符串1==字符串2` | 两个字符串相同（区分大小写） |

```
IF EXIST SAVE.DAT GOTO load
IF NOT ERRORLEVEL 1 ECHO all fine
IF "%1"=="" ECHO missing parameter
```

最后一种写法给参数加上引号，是为了在参数为空时也不出错。

### MD / MKDIR

```
MD [盘:]路径
```

建立目录。已经存在、上一级不存在、或者路径超过 63 个字符时，显示 `Unable to create directory`。

### PATH

```
PATH [路径;路径;...]
PATH ;
```

设置找程序的搜索路径。输入一个程序名时，先找当前目录，再按 PATH 里的目录依次找。

```
PATH \BIN;B:\TOOLS
PATH              显示当前的 PATH，没有时显示 No Path
PATH ;            清空
```

PATH 里有不存在的驱动器时，运行程序会显示 `Invalid drive in search path`。

### PAUSE

```
PAUSE
```

显示 `Strike a key when ready . . .`，等按任意键。主要用在批处理里。

### PROMPT

```
PROMPT [文字]
```

设置提示符，见第 7 节。不带参数时恢复默认。

### RD / RMDIR

```
RD [盘:]路径
```

删除目录。目录必须是空的，而且不能是当前目录或它的上级。不满足时显示：

```
Invalid path, not directory,
or directory not empty
```

### REM

```
REM [注释]
```

什么也不做，用来在批处理里写注释。

### REN / RENAME

```
REN [盘:][路径]旧名字 新名字
```

改名。新名字只写文件名，不能带路径，改名总是在原来的目录里进行。可以用通配符：

```
REN README.TXT READ.ME
REN *.TXT *.OLD
```

新名字已经存在、或者找不到旧文件时，显示 `Duplicate file name or File not found`。不能给目录改名。

### SET

```
SET [名字=[值]]
```

| 用法 | 作用 |
|---|---|
| `SET` | 列出全部环境变量 |
| `SET USER=astro` | 设置 |
| `SET USER=` | 删除 |

名字会转成大写。环境变量区只有 160 字节，满了显示 `Out of environment space`。

### SHIFT

```
SHIFT
```

只在批处理里有用：把参数往左移一位，原来的 `%1` 变成 `%0`，`%2` 变成 `%1`，以此类推。用来处理超过 9 个的参数。

### TIME

```
TIME [时[:分[:秒[.百分秒]]]]
```

显示并设置时间。时分秒之间可以用 `:` 或 `.` 分隔，24 小时制。

```
TIME              显示当前时间并询问新时间，直接回车表示不改
TIME 9:05
TIME 21:30:00
```

格式不对时显示 `Invalid time` 并重新询问。

### TYPE

```
TYPE [盘:][路径]文件名
```

显示文本文件的内容，遇到 Ctrl+Z 就停。不能用通配符。找不到时显示 `File not found`。

### VER

```
VER
```

显示版本：`phi-DOS Version X.XX`。

正式发布的版本里，X.XX 是发布时的版本号，按 DOS 的习惯显示成两段：发布 0.1.0 显示 0.10，发布 5.2.10 显示 5.21。自己编译、没有指定版本号时显示 X.XX。

### VERIFY

```
VERIFY [ON | OFF]
```

设置写入后校验。phi-DOS 里这个开关只是记住状态，不影响实际行为（虚拟磁盘不会写坏）。

### VOL

```
VOL [盘:]
```

显示卷标：`Volume in drive A is STORY`，没有卷标时显示 `has no label`。

### 切换驱动器

```
B:
```

输入盘符加冒号，切换当前驱动器。驱动器不存在时显示 `Invalid drive specification`。切到没插盘的驱动器不会报错，要等到真正读盘时才会出现 `Not ready error`。

## 9. 外部命令

外部命令是盘上的程序文件。新建的系统盘、发布包里的 A 文件夹都带着它们。盘上没有这个文件时，输入命令会显示 `Bad command or file name`。

| 命令 | 文件 |
|---|---|
| FORMAT | FORMAT.COM |
| MORE | MORE.COM |
| SORT | SORT.EXE |
| FIND | FIND.EXE |
| VI | VI.EXE（phi-DOS 新增） |
| ILDOSVER | ILDOSVER.COM（phi-DOS 新增） |

### FORMAT

```
FORMAT [盘:] [/V]
```

格式化虚拟磁盘：清空盘上的全部内容，盘的规格不变。`/V` 表示格式化后询问卷标。

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

文件夹驱动器不能格式化，会显示 `Format failure`，文件夹里的东西不会被动。

### MORE

```
MORE < 文件
命令 | MORE
```

一屏一屏地显示文字。满 24 行显示 `-- More --`，按任意键看下一屏。数据来自管道时，它仍然从键盘读按键。

```
TYPE LONG.TXT | MORE
DIR | MORE
```

### SORT

```
SORT [/R] [/+n] [< 文件]
命令 | SORT [/R] [/+n]
```

把输入的每一行排序后输出，不区分大小写。

| 开关 | 作用 |
|---|---|
| `/R` | 倒序 |
| `/+n` | 从第 n 列开始比较 |

```
SORT < NAMES.TXT
DIR | SORT /+10         按扩展名排序（扩展名从第 10 列开始）
TYPE LIST.TXT | SORT /R > SORTED.TXT
```

一次最多排序 63KB，超出时显示 `SORT: Insufficient memory`。

### FIND

```
FIND [/V] [/C] [/N] "字符串" [文件 ...]
```

显示包含某个字符串的行，区分大小写。不给文件时从标准输入读。

| 开关 | 作用 |
|---|---|
| `/V` | 显示不包含的行 |
| `/C` | 只显示有几行 |
| `/N` | 在每行前面显示行号 |

```
FIND "phi" README.TXT
FIND /C /V "x" A.TXT B.TXT
DIR | FIND "<DIR>"
```

字符串必须用双引号括起来，字符串里的双引号要写两个。给了文件时，每个文件前面会显示一行 `---------- 文件名`。

错误提示：`FIND: Invalid number of parameters`、`FIND: Syntax error`、`FIND: File not found 文件名`、`FIND: Invalid Parameter 字母`。退出码：正常为 0，参数错误为 2。

### VI

```
VI [文件]
```

一个模仿 vi 手感的全屏编辑器。文件不存在时新建。屏幕上面 24 行是文字，空行显示 `~`；最后一行显示状态和命令。

VI 有两种模式：一开始是**普通模式**，按键是命令；按 `i` 等键进入**插入模式**，这时按键就是打字，按 Esc 回到普通模式。

普通模式：

| 按键 | 作用 |
|---|---|
| `h` `j` `k` `l`、方向键 | 左、下、上、右 |
| `0`、Home | 行首 |
| `^` | 本行第一个非空白字符 |
| `$`、End | 行尾 |
| `w`、`b` | 下一个词、上一个词 |
| `gg`、`G` | 文件开头、文件结尾 |
| 数字 `G` | 跳到第几行，例如 `12G` |
| Ctrl+F、PageDown | 往下翻一页 |
| Ctrl+B、PageUp | 往上翻一页 |
| `i`、`a` | 在光标前、光标后开始输入 |
| `I`、`A` | 在行首、行尾开始输入 |
| `o`、`O` | 在下面、上面新开一行并开始输入 |
| `x`、Delete | 删除光标处的字符 |
| `dd` | 删除这一行 |
| `yy` | 复制这一行 |
| `p`、`P` | 把复制或删除的行粘贴到下面、上面 |
| `u` | 撤销上一次修改；再按一次是重做 |
| `/文字` 回车 | 向后搜索 |
| `?文字` 回车 | 向前搜索 |
| `n`、`N` | 同方向、反方向找下一个 |
| `ZZ` | 有修改就保存，然后退出 |

插入模式：打字、退格（在行首退格会和上一行合并）、回车换行、方向键移动，Esc 回到普通模式。

冒号命令（在普通模式下输入 `:`，写完按回车，按 Esc 取消）：

| 命令 | 作用 |
|---|---|
| `:w` | 保存 |
| `:w 文件名` | 另存为 |
| `:q` | 退出（有未保存的修改时拒绝） |
| `:q!` | 不保存，强制退出 |
| `:wq` | 保存并退出 |
| `:x` | 有修改就保存，然后退出 |
| `:数字` | 跳到第几行 |
| `:$` | 跳到最后一行 |

提示信息：

| 提示 | 意思 |
|---|---|
| `"NOTE.TXT" 3 lines, 45 characters` | 打开或保存的文件信息 |
| `"NOTE.TXT" [New file]` | 这是一个新文件 |
| `-- INSERT --` | 正在插入模式 |
| `No write since last change (use ! to override)` | 有未保存的修改，用 `:q!` 才能不保存退出 |
| `Pattern not found: 文字` | 没搜到 |
| `search hit BOTTOM, continuing at TOP` | 搜到结尾，从头继续 |
| `Not an editor command: 文字` | 不认识的冒号命令 |

文件按 UTF-8 读写，保存时每行以 CR LF 结尾。中文按两格宽显示，长行自动折行。按 Ctrl+C 会直接退出，不保存。

不支持：`dw`、`3dd` 这类组合命令、可视模式、多窗口、寄存器、宏、语法高亮。

### ILDOSVER

```
ILDOSVER
```

彩色的系统信息：左边是 φ 图标，右边是系统、CPU（.NET）、语言（IL）、当前驱动器等信息，最后是 16 色色板。标题里的用户名取自环境变量 USER，没有时显示 guest：

```
SET USER=astro
ILDOSVER
```

### 运行程序的规则

输入的命令不是内置命令时，COMMAND 按这个顺序找程序：

1. 当前驱动器的当前目录，然后是 PATH 里的每个目录。
2. 在每个目录里依次找 `.COM`、`.EXE`、`.BAT`。
3. 都找不到：`Bad command or file name`。

写了扩展名就只找那一种；写了路径（`\BIN\HELLO`、`B:HELLO`）就只在那里找。

程序文件不是有效的 phi-DOS 程序时显示 `Error in EXE file`，下一行写着原因。程序运行中出错时显示 `Abnormal program termination` 和异常信息。

## 10. 重定向与管道

| 写法 | 作用 |
|---|---|
| `命令 > 文件` | 把输出写进文件（覆盖） |
| `命令 >> 文件` | 把输出追加到文件末尾 |
| `命令 < 文件` | 从文件读输入 |
| `命令1 \| 命令2` | 把命令 1 的输出交给命令 2 当输入 |

```
DIR > LIST.TXT
ECHO one more line >> LIST.TXT
SORT < NAMES.TXT > SORTED.TXT
TYPE LONG.TXT | FIND "phi" | MORE
```

细节：

- 重定向对内置命令和程序都有效，运行的程序会继承它。
- 和原版一样，`>` 前面的空格会留在命令里：`ECHO hi > X.TXT` 写进文件的是 `hi `（带一个空格）。想要没有空格，写成 `ECHO hi> X.TXT`。
- 管道照原版用临时文件中转：前一个命令的输出先写进当前驱动器根目录的 `%PIPE1.$$$`，后一个命令再从里面读，用完删除。所以 `DIR | SORT` 的结果里会看到这个临时文件，原版也是这样。当前驱动器是写保护的盘时，会先出现 `Write protect error`，选 I 之后显示 `Intermediate file error during pipe`，管道无法使用。
- 双引号里的 `<`、`>`、`|` 不算重定向，所以 `FIND "a|b" X.TXT` 可以用。

## 11. 批处理

扩展名为 `.BAT` 的文本文件，每行一条命令，输入文件名（不带扩展名）就会依次执行。

### 参数

| 写法 | 代表 |
|---|---|
| `%0` | 批处理自己的名字（照输入的写法） |
| `%1` 到 `%9` | 第 1 到第 9 个参数 |
| `%名字%` | 环境变量的值，例如 `%PATH%` |
| `%%` | 一个百分号 |

参数不存在时是空的。超过 9 个参数用 SHIFT 处理。

### 例子

```
ECHO OFF
REM 用法：BACKUP 文件名
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

### 规则

- ECHO 开着时，每条命令执行前会连同提示符一起显示出来。批处理结束后，ECHO 恢复成开始前的状态。
- 以冒号开头的行是标号，只用来给 GOTO 跳转，本身不执行。
- 批处理里运行另一个批处理，是"转过去"不再回来（2.0 没有 CALL）。
- 每执行一行都会重新打开批处理文件读下一行。运行中文件被删掉了，会显示 `Batch file missing` 并结束。
- 运行中按 Ctrl+C，会问 `Terminate batch job (Y/N)?`，Y 结束整个批处理，N 继续下一行。
- 一行最长 127 字节。
- A 盘根目录的 `AUTOEXEC.BAT` 在开机时自动运行，见第 5 节。

## 12. 设备名

这些名字代表设备，不论在哪个目录、带什么扩展名，都指向设备而不是文件：

| 名字 | 是什么 |
|---|---|
| `CON` | 控制台：写入就显示在屏幕上，读取就从键盘读一行 |
| `NUL` | 空设备：写入的内容被丢弃，读取时立刻结束 |
| `PRN`、`LPT1` | 打印机（phi-DOS 里等于 NUL） |
| `AUX`、`COM1` | 串口（phi-DOS 里等于 NUL） |

```
COPY CON NOTE.TXT       从键盘创建文件
TYPE NOTE.TXT > PRN     "打印"（什么也不会发生）
FIND "x" A.TXT > NUL    只要退出码，不要输出
```

## 13. 环境变量

开机时的环境变量：

| 名字 | 值 |
|---|---|
| `PATH` | 空 |
| `COMSPEC` | `A:\COMMAND.COM`（只是照原版保留，盘上并没有这个文件） |

其他会用到的：

| 名字 | 谁用 |
|---|---|
| `PROMPT` | 提示符，见第 7 节 |
| `USER` | ILDOSVER 标题里的用户名 |

运行程序时，程序拿到的是一份环境变量的副本。环境变量区一共 160 字节。

## 14. 颜色

phi-DOS 内置了 ANSI.SYS 的功能。程序输出 ANSI 转义序列就能改颜色，提示符里用 `$E` 也行：

```
PROMPT $E[1;32m$P$G$E[0m
```

提示符就变成亮绿色了。

| 代码 | 效果 |
|---|---|
| `ESC[0m` | 恢复默认（灰字黑底） |
| `ESC[1m` | 高亮 |
| `ESC[5m` | 闪烁 |
| `ESC[7m` | 反色 |
| `ESC[30m` 到 `ESC[37m` | 前景：黑 红 绿 黄 蓝 品红 青 白 |
| `ESC[40m` 到 `ESC[47m` | 背景，顺序同上 |
| `ESC[2J` | 清屏 |
| `ESC[行;列H` | 移动光标 |
| `ESC[K` | 清到行尾 |

终端版把这些序列交给你的终端显示。Windows 11 自带的 Windows Terminal 支持它们；很老的命令行窗口可能显示成乱码。

## 15. 错误提示一览

| 提示 | 意思 |
|---|---|
| `Bad command or file name` | 不是内置命令，也找不到这个程序 |
| `File not found` | 文件不存在 |
| `Invalid directory` | 目录不存在 |
| `Invalid drive specification` | 没有这个驱动器 |
| `Invalid number of parameters` | 参数个数不对 |
| `Invalid parameter` | 参数或开关不对 |
| `Syntax error` | 写法不对（IF、FOR、SET 等） |
| `Insufficient disk space` | 磁盘满了 |
| `File creation error` | 无法建立文件（比如根目录已满） |
| `File cannot be copied onto itself` | COPY 的源和目标是同一个文件 |
| `Content of destination lost before copy` | 合并时某个源就是目标 |
| `Duplicate file name or File not found` | REN 的新名字已存在，或旧文件不存在 |
| `Unable to create directory` | MD 失败 |
| `Invalid path, not directory, or directory not empty` | RD 失败 |
| `Out of environment space` | 环境变量区满了 |
| `Must specify ON or OFF` | BREAK、VERIFY 的参数不对 |
| `Label not found` | GOTO 找不到标号 |
| `FOR cannot be nested` | FOR 里面又用了 FOR |
| `Batch file missing` | 运行中的批处理文件不见了 |
| `Invalid drive in search path` | PATH 里有不存在的驱动器 |
| `Intermediate file error during pipe` | 管道的临时文件建立不了 |
| `Not ready error reading drive X` | 驱动器里没有盘 |
| `Write protect error writing drive X` | 盘是写保护的 |
| `Error in EXE file` | 程序文件无法运行，下一行是原因 |
| `Abnormal program termination` | 程序运行中出错，下一行是异常信息 |
| `Program too big to fit in memory` | 程序文件超过 640KB，或程序嵌套运行太深 |
| `phi-DOS internal error` | phi-DOS 自己的问题，请报告（设置 PHIDOS_TRACE=1 可看详情） |

## 16. 各种限制

| 项目 | 限制 |
|---|---|
| 屏幕 | 80 列 × 25 行，16 色 |
| 驱动器 | A: 到 Z:，默认 A: 和 B: |
| 文件名 | 8.3，按 UTF-8 字节算 |
| 当前目录路径 | 63 个字符 |
| 一行命令 | 127 字节 |
| 环境变量区 | 160 字节 |
| 每个程序同时打开的文件 | 20 个（其中 5 个是标准设备） |
| 程序文件大小 | 640KB |
| 程序嵌套运行 | 16 层 |
| 根目录项数 | 160K/180K 盘 64 项，320K 到 720K 盘 112 项，1.2M/1.44M 盘 224 项 |
| 文件夹驱动器默认容量 | 360KB，可以改或设为不限 |
| SORT | 一次 63KB |

## 17. 和 MS-DOS 2.0 的区别

phi-DOS 尽量照 2.0 的行为做，下面是有意不同的地方：

| 方面 | MS-DOS 2.0 | phi-DOS |
|---|---|---|
| 程序 | x86 机器码 | .NET 程序集（IL） |
| 字符集 | 单字节代码页 | UTF-8，支持中文 |
| 默认提示符 | `A>` | `A:\>`（`PROMPT $N$G` 可以改回去） |
| 驱动器 | 软盘、硬盘 | 虚拟磁盘镜像和电脑上的文件夹 |
| 引号里的 `\|` `<` `>` | 当作重定向 | 不当作重定向 |
| 编辑器 | EDLIN | VI（新增） |
| 新增命令 | 无 | ILDOSVER |
| 没有实现 | — | CTTY、FCB 方式的文件调用、CHKDSK、EDLIN、DEBUG、DISKCOPY、SYS 等外部命令 |
| COMMAND.COM | 盘上的文件 | 内置，盘上不需要这个文件 |
| Ctrl+C | 纯计算的循环无法打断 | 同样无法打断，但 BREAK ON 时可以；宿主的重启总能打断 |
