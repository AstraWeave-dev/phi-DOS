using PhiDos.Abi;

namespace PhiDos.Core.Kernel;

internal sealed partial class DosKernel
{
    /// <summary>运行程序（M4/M5 实现）。</summary>
    public int Exec(DosProcess parent, string program, string args)
    {
        EnterCall();
        throw new DosException(DosError.FileNotFound);
    }
}
