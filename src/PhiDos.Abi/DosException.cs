namespace PhiDos.Abi;

/// <summary>DOS 风格的错误码。</summary>
public enum DosError
{
    None = 0,
    InvalidFunction = 1,
    FileNotFound = 2,
    PathNotFound = 3,
    TooManyOpenFiles = 4,
    AccessDenied = 5,
    InvalidHandle = 6,
    InsufficientMemory = 8,
    InvalidFormat = 11,
    InvalidAccess = 12,
    InvalidDrive = 15,
    CurrentDirectory = 16,
    NotSameDevice = 17,
    NoMoreFiles = 18,

    // 以下是 phi-DOS 为了表达更具体的情况加的。
    DiskFull = 0x100,
    WriteProtected = 0x101,
    DriveNotReady = 0x102,
    FileExists = 0x103,
    DirectoryNotEmpty = 0x104,
    BadProgram = 0x105,
    NotSupported = 0x106,
    HostError = 0x107,
}

/// <summary>系统调用失败时抛出的异常。</summary>
public class DosException : Exception
{
    public DosError Error { get; }

    public DosException(DosError error, string? message = null)
        : base(message ?? DescribeError(error))
    {
        Error = error;
    }

    public static string DescribeError(DosError error) => error switch
    {
        DosError.InvalidFunction => "Invalid function",
        DosError.FileNotFound => "File not found",
        DosError.PathNotFound => "Path not found",
        DosError.TooManyOpenFiles => "Too many open files",
        DosError.AccessDenied => "Access denied",
        DosError.InvalidHandle => "Invalid handle",
        DosError.InsufficientMemory => "Insufficient memory",
        DosError.InvalidFormat => "Invalid format",
        DosError.InvalidAccess => "Invalid access code",
        DosError.InvalidDrive => "Invalid drive specification",
        DosError.CurrentDirectory => "Attempt to remove current directory",
        DosError.NotSameDevice => "Not same device",
        DosError.NoMoreFiles => "No more files",
        DosError.DiskFull => "Insufficient disk space",
        DosError.WriteProtected => "Write protect error",
        DosError.DriveNotReady => "Not ready error",
        DosError.FileExists => "File exists",
        DosError.DirectoryNotEmpty => "Directory not empty",
        DosError.BadProgram => "Error in EXE file",
        DosError.NotSupported => "Not supported",
        DosError.HostError => "Host function failed",
        _ => $"DOS error {(int)error}",
    };
}
