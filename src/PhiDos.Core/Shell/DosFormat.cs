namespace PhiDos.Core.Shell;

/// <summary>
/// 数字和日期时间的显示格式，照 MS-DOS 2.0 COMMAND 的美国格式：
/// 月份和小时的十位是 0 时显示为空格，日、分、秒补 0。
/// </summary>
internal static class DosFormat
{
    /// <summary>DIR 里的日期："10-20-83"、" 1-05-84"。</summary>
    public static string DirDate(DateTime t) => $"{t.Month,2}-{t.Day:00}-{t.Year % 100:00}";

    /// <summary>DIR 里的时间（12 小时制）："12:00p"、" 9:05a"。</summary>
    public static string DirTime(DateTime t)
    {
        int h = t.Hour % 12;
        if (h == 0) h = 12;
        return $"{h,2}:{t.Minute:00}{(t.Hour < 12 ? 'a' : 'p')}";
    }

    /// <summary>DATE 命令显示的日期："Thu 10-20-1983"、"Sun  1-01-1984"。</summary>
    public static string LongDate(DateTime t)
        => Messages.WeekDays.Substring((int)t.DayOfWeek * 3, 3) + " " + $"{t.Month,2}-{t.Day:00}-{t.Year}";

    /// <summary>TIME 命令显示的时间（24 小时制，带百分之一秒）："12:00:00.00"、" 9:05:07.31"。</summary>
    public static string LongTime(DateTime t) => $"{t.Hour,2}:{t.Minute:00}:{t.Second:00}.{t.Millisecond / 10:00}";

    /// <summary>右对齐到 9 位，DIR 的文件大小和合计行都用它。</summary>
    public static string Number9(long n) => n.ToString().PadLeft(9);
}
