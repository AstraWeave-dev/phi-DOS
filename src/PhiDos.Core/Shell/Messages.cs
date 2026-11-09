namespace PhiDos.Core.Shell;

/// <summary>COMMAND 的提示信息，用的是 DOS 用户熟悉的说法。</summary>
internal static class Messages
{
    public const string BadBatch = "\r\nBatch file missing\r\n";
    public const string BadCommand = "Bad command or file name\r\n";
    public const string DuplicateOrNotFound = "Duplicate file name or File not found\r\n";
    public const string FileNotFound = "File not found\r\n";
    public const string NoSpace = "Insufficient disk space\r\n";
    public const string OutOfEnvironment = "Out of environment space\r\n";
    public const string FileCreationError = "File creation error\r\n";
    public const string CopyOntoItself = "File cannot be copied onto itself\r\n";
    public const string ContentLost = "Content of destination lost before copy\r\n";
    public const string FilesCopied = " File(s) copied\r\n";
    public const string DirFiles = " File(s) ";
    public const string BytesFree = " bytes free\r\n";
    public const string InvalidDrive = "Invalid drive specification\r\n";
    public const string Pause = "Strike a key when ready . . . ";
    public const string InvalidParameter = "Invalid parameter\r\n";
    public const string WeekDays = "SunMonTueWedThuFriSat";
    public const string InvalidDate = "\r\nInvalid date";
    public const string CurrentDate = "Current date is ";
    public const string NewDate = "\r\nEnter new date: ";
    public const string InvalidTime = "\r\nInvalid time";
    public const string CurrentTime = "Current time is ";
    public const string NewTime = "\r\nEnter new time: ";
    public const string AreYouSure = "Are you sure (Y/N)? ";
    public const string DirMarker = " <DIR>   ";
    public const string VolumeIn = " Volume in drive ";
    public const string VolumeIs = " is ";
    public const string NoLabel = " has no label";
    public const string InvalidDirectory = "Invalid directory\r\n";
    public const string UnableToCreateDirectory = "Unable to create directory\r\n";
    public const string InvalidRmdir = "Invalid path, not directory,\r\nor directory not empty\r\n";
    public const string MustSpecifyOnOff = "Must specify ON or OFF\r\n";
    public const string DirectoryOf = " Directory of  ";
    public const string NoPath = "No Path";
    public const string BadPathDrive = "Invalid drive in search path\r\n";
    public const string InvalidDevice = "Invalid device\r\n";
    public const string LabelNotFound = "Label not found\r\n";
    public const string SyntaxError = "Syntax error\r\n";
    public const string ForCannotBeNested = "\rFOR cannot be nested\r\n";
    public const string PipeError = "Intermediate file error during pipe\r\n";
    public const string BinaryReadFromDevice = "Cannot do binary reads from a device\r\n";
    public const string Off = "off\r\n";
    public const string On = "on\r\n";
    public const string BreakIs = "BREAK is ";
    public const string VerifyIs = "VERIFY is ";
    public const string EchoIs = "ECHO is ";
    public const string InvalidPathOrFileName = "Invalid path or file name\r\n";
    public const string InvalidNumberOfParameters = "Invalid number of parameters\r\n";
    public const string ErrorWritingDevice = "Error writing to device\r\n";
    public const string TerminateBatch = "Terminate batch job (Y/N)? ";
    public const string ClearScreen = "\x1b[2J";
}
