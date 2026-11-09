using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace PhiDos.Tests;

/// <summary>测试用：把一段 C# 源码现场编译成程序集字节，就是一个可以放到磁盘上的 .COM。</summary>
internal static class ProgramCompiler
{
    private static readonly Lazy<MetadataReference[]> s_references = new(() =>
    {
        // 引用当前运行时的全部框架程序集，再加上 PhiDos.Abi
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        return tpa.Where(p => Path.GetFileName(p).StartsWith("System.") || Path.GetFileName(p) is "netstandard.dll" or "mscorlib.dll")
                  .Append(typeof(Abi.IPhiProgram).Assembly.Location)
                  .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
                  .ToArray();
    });

    public static byte[] Compile(string source, string name = "PROG")
    {
        var tree = CSharpSyntaxTree.ParseText("using System; using PhiDos.Abi;\n" + source);
        var compilation = CSharpCompilation.Create(name, [tree], s_references.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);
        if (!result.Success)
            throw new InvalidOperationException(string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return ms.ToArray();
    }
}
