namespace PhiDos.Abi;

/// <summary>
/// 每个 .COM / .EXE 程序集里必须有一个实现此接口的公开类。
/// 系统加载程序后创建这个类的实例并调用 <see cref="Run"/>。
/// </summary>
public interface IPhiProgram
{
    /// <summary>
    /// 程序入口。<paramref name="args"/> 是按空格拆开的命令行参数，
    /// 不含程序名本身。返回值是退出码，批处理里可用 IF ERRORLEVEL 判断。
    /// </summary>
    int Run(IDos dos, string[] args);
}
