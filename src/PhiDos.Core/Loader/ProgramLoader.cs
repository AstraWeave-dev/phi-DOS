using PhiDos.Abi;
using PhiDos.Core.Kernel;

namespace PhiDos.Core.Loader;

/// <summary>加载好的程序。Dispose 时卸载它的程序集。</summary>
internal sealed class LoadedProgram(IPhiProgram program, Action unload) : IDisposable
{
    public IPhiProgram Program { get; } = program;
    public void Dispose() => unload();
}

/// <summary>IL 程序加载器（M5 实现）。</summary>
internal sealed class ProgramLoader(DosKernel kernel)
{
    private readonly DosKernel _kernel = kernel;

    public LoadedProgram Load(DosPath path) => throw new DosException(DosError.BadProgram);
}
