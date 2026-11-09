using System.Reflection;
using System.Runtime.Loader;
using PhiDos.Abi;
using PhiDos.Core.Kernel;

namespace PhiDos.Core.Loader;

/// <summary>加载好的程序。Dispose 时卸载它的程序集。</summary>
internal sealed class LoadedProgram(IPhiProgram program, Action unload) : IDisposable
{
    public IPhiProgram Program { get; } = program;
    public void Dispose() => unload();
}

/// <summary>
/// IL 程序加载器：从磁盘读出 .COM / .EXE 的内容（一个 .NET 程序集），
/// 经过 <see cref="IlRewriter"/> 检查和注入后，放进一个可回收的 AssemblyLoadContext 里加载。
/// 程序结束后整个 AssemblyLoadContext 被卸载，所以反复运行程序不会让内存越占越多。
/// </summary>
internal sealed class ProgramLoader(DosKernel kernel)
{
    /// <summary>程序文件的大小上限。DOS 2.0 的常规内存是 640KB，这里也用这个数。</summary>
    private const int MaxImageSize = 640 * 1024;

    public LoadedProgram Load(DosPath path)
    {
        byte[] image = ReadImage(path);
        var options = kernel.Machine.Options;
        if (options.VerifyPrograms || options.InjectInterruptChecks)
            image = IlRewriter.Process(image, options.VerifyPrograms, options.InjectInterruptChecks);

        var context = new ProgramLoadContext(path.Leaf!);
        try
        {
            Assembly assembly;
            try
            {
                assembly = context.LoadFromStream(new MemoryStream(image));
            }
            catch (BadImageFormatException)
            {
                throw new DosException(DosError.BadProgram, "not a .NET assembly");
            }
            catch (Exception ex) when (ex is not DosException)
            {
                throw new DosException(DosError.BadProgram, "cannot load: " + ex.Message);
            }

            Type type = FindEntryType(assembly);
            IPhiProgram program;
            try
            {
                program = (IPhiProgram)Activator.CreateInstance(type)!;
            }
            catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or MemberAccessException)
            {
                throw new DosException(DosError.BadProgram, $"cannot create {type.Name}: {ex.InnerException?.Message ?? ex.Message}");
            }
            return new LoadedProgram(program, context.Unload);
        }
        catch
        {
            context.Unload();
            throw;
        }
    }

    private byte[] ReadImage(DosPath path)
    {
        return kernel.DriveOp(path.Drive, write: false, d =>
        {
            var file = d.OpenFile(path.Parts, writable: false);
            try
            {
                if (file.Length > MaxImageSize) throw new DosException(DosError.InsufficientMemory);
                var bytes = new byte[file.Length];
                int read = file.Read(0, bytes);
                return read == bytes.Length ? bytes : bytes[..read];
            }
            finally
            {
                file.Close(null);
            }
        });
    }

    /// <summary>找到程序集里唯一的那个"公开、可创建、实现了 IPhiProgram"的类。</summary>
    private static Type FindEntryType(Assembly assembly)
    {
        Type[] types;
        try
        {
            types = assembly.GetExportedTypes();
        }
        catch (Exception ex) when (ex is ReflectionTypeLoadException or FileNotFoundException or TypeLoadException)
        {
            throw new DosException(DosError.BadProgram, "missing dependency: " + ex.Message);
        }

        var candidates = types.Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPhiProgram).IsAssignableFrom(t)).ToList();
        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new DosException(DosError.BadProgram, "no public class implements IPhiProgram"),
            _ => throw new DosException(DosError.BadProgram, "more than one class implements IPhiProgram"),
        };
    }

    /// <summary>
    /// 每个程序一个独立的加载上下文。isCollectible: true 表示用完可以卸载。
    /// PhiDos.Abi 必须用系统自己已经加载的那一份，否则程序里的 IPhiProgram
    /// 和系统认识的 IPhiProgram 会被当成两个不同的类型。
    /// </summary>
    private sealed class ProgramLoadContext(string name) : AssemblyLoadContext("phi-DOS " + name, isCollectible: true)
    {
        private static readonly Assembly Abi = typeof(IPhiProgram).Assembly;

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == Abi.GetName().Name) return Abi;
            return null; // 其余（.NET 自带的库）交给默认规则
        }
    }
}
