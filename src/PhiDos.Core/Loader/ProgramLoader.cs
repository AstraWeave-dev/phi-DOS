using PhiDos.Core.Kernel;

namespace PhiDos.Core.Loader;

/// <summary>IL 程序加载器（M5 实现）。</summary>
internal sealed class ProgramLoader(DosKernel kernel)
{
    private readonly DosKernel _kernel = kernel;
}
