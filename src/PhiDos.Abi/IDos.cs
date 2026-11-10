namespace PhiDos.Abi;

/// <summary>
/// phi-DOS 提供给程序的系统调用接口，相当于 DOS 的 INT 21h。
/// 程序只能通过它和系统打交道：读写控制台、操作文件、运行其他程序。
/// </summary>
/// <remarks>
/// 所有方法出错时抛出 <see cref="DosException"/>，其中的 <see cref="DosException.Error"/>
/// 是错误码。修改本接口时必须同步更新 docs/DESIGN.md。
/// </remarks>
public interface IDos
{
    // ───────── 控制台（INT 21h 01h/02h/08h/09h/0Ah/0Bh）─────────
    // 这些方法走标准输入输出（句柄 0 和 1），所以会跟随重定向和管道。

    /// <summary>向标准输出写文本（UTF-8）。</summary>
    void Write(string text);

    /// <summary>向标准输出写文本并换行（CR LF）。</summary>
    void WriteLine(string text = "");

    /// <summary>
    /// 从标准输入读一个字符。功能键和方向键按 DOS 惯例先返回 '\0'，
    /// 下一次调用再返回扫描码。
    /// </summary>
    char ReadKey(bool echo = true);

    /// <summary>
    /// 读一整行（DOS 缓冲输入，支持退格、Esc、F1、F3 等编辑键）。
    /// 返回的字符串不含结尾的回车。
    /// </summary>
    string ReadLine();

    /// <summary>键盘缓冲区里是否有按键在等待读取。</summary>
    bool KeyAvailable { get; }

    // ───────── 文件（句柄式）─────────

    /// <summary>创建文件，已存在则清空。返回句柄。</summary>
    int Create(string path);

    /// <summary>打开已存在的文件或设备（CON、NUL 等）。返回句柄。</summary>
    int Open(string path, FileAccessMode mode);

    /// <summary>读取数据，返回实际读到的字节数，0 表示文件结束。</summary>
    int Read(int handle, Span<byte> buffer);

    /// <summary>写入数据，返回实际写入的字节数。</summary>
    int Write(int handle, ReadOnlySpan<byte> data);

    /// <summary>移动读写位置，返回新位置。</summary>
    long Seek(int handle, long offset, SeekOrigin origin);

    /// <summary>关闭句柄。</summary>
    void Close(int handle);

    /// <summary>
    /// 复制句柄（INT 21h 45h）：返回一个新句柄，和原句柄指向同一个打开的文件。
    /// 新句柄总是编号最小的空闲句柄。
    /// </summary>
    int Duplicate(int handle);

    /// <summary>
    /// 强制复制句柄（INT 21h 46h）：让 <paramref name="target"/> 指向和 <paramref name="source"/>
    /// 相同的文件。target 原来打开着的话先关闭。重定向就是靠它实现的。
    /// </summary>
    void ForceDuplicate(int source, int target);

    /// <summary>删除文件（不支持通配符）。</summary>
    void Delete(string path);

    /// <summary>改名，也可以在同一驱动器内把文件移动到另一个目录。</summary>
    void Rename(string from, string to);

    /// <summary>查找匹配通配符的普通文件，不含目录。</summary>
    IReadOnlyList<DirEntry> FindFiles(string pattern);

    /// <summary>
    /// 查找匹配通配符的条目。普通文件总是包含在内；
    /// <paramref name="include"/> 里的位决定是否额外包含目录、隐藏文件、系统文件、卷标。
    /// </summary>
    IReadOnlyList<DirEntry> FindFiles(string pattern, DosAttributes include);

    // ───────── 驱动器与目录 ─────────

    /// <summary>当前驱动器，例如 'A'。</summary>
    char CurrentDrive { get; set; }

    /// <summary>当前驱动器的当前目录，形如 "\" 或 "\GAMES\SAVE"。</summary>
    string CurrentDirectory { get; }

    /// <summary>指定驱动器的当前目录。</summary>
    string GetCurrentDirectory(char drive);

    void ChangeDirectory(string path);
    void MakeDirectory(string path);
    void RemoveDirectory(string path);

    /// <summary>驱动器容量、剩余空间和卷标（INT 21h 36h）。</summary>
    DiskInfo GetDiskInfo(char drive);

    /// <summary>
    /// 格式化驱动器上的虚拟磁盘，清空全部内容。文件夹驱动器不支持格式化。
    /// </summary>
    void FormatDisk(char drive, string? volumeLabel);

    // ───────── 进程与环境 ─────────

    /// <summary>
    /// 运行另一个程序并等待它结束，返回它的退出码。
    /// 不带扩展名时依次尝试 .COM 和 .EXE，并按 PATH 查找。
    /// </summary>
    int Exec(string program, string args);

    /// <summary>
    /// 命令行参数的原文（程序名之后的部分，相当于 PSP 80h 处的命令尾）。
    /// 需要自己解析引号之类的程序用它，一般用 Run 的 args 就够了。
    /// </summary>
    string CommandTail { get; }

    /// <summary>读取环境变量，不存在时返回 null。名字不区分大小写。</summary>
    string? GetEnv(string name);

    /// <summary>
    /// 呼叫宿主（运行 phi-DOS 的游戏或应用）登记过的一项功能。
    /// name 不区分大小写。返回宿主给出的回答，没有回答时是空字符串。
    /// 宿主没有登记这个名字时抛出 DosException（InvalidFunction）；
    /// 宿主的处理函数出错时抛出 DosException（HostError）。
    /// </summary>
    /// <remarks>
    /// 相当于老式 DOS 程序发软中断请系统办事。程序只能叫出宿主事先登记的名字，
    /// 宿主怎么处理、回答什么完全由宿主决定。
    /// </remarks>
    string CallHost(string name, string argument = "");

    /// <summary>系统时钟（可被 DATE、TIME 命令调整，不影响宿主电脑的时间）。</summary>
    DateTime Now { get; }
}
