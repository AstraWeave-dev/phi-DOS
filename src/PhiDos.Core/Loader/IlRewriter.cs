using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks; // SimplifyMacros / OptimizeMacros 在这个命名空间里
using PhiDos.Abi;

namespace PhiDos.Core.Loader;

/// <summary>
/// 在程序加载前改写它的 IL：
/// 1. 检查：拒绝直接使用危险 API 的程序（访问电脑文件、启动进程、退出整个游戏等）。
/// 2. 注入：在每个方法开头和每个"往回跳"的分支前插入一次 DosRuntime.Poll()，
///    这样死循环和无限递归也能被 Ctrl+C（BREAK ON 时）和重启打断。
/// </summary>
/// <remarks>
/// 这里的检查只是基本防护，不是安全沙箱：用反射之类的手段仍然可以绕过。
/// 它的目的是挡住无意间写出的危险代码，比如误用 File.Delete 或 Environment.Exit。
/// </remarks>
internal static class IlRewriter
{
    /// <summary>被禁止的类型（完整类型名精确匹配，也包括它们的嵌套类型和泛型版本）。</summary>
    private static readonly HashSet<string> ForbiddenTypes =
    [
        // 直接访问电脑上的文件
        "System.IO.File", "System.IO.FileInfo", "System.IO.FileStream", "System.IO.FileSystemInfo",
        "System.IO.FileSystemWatcher", "System.IO.Directory", "System.IO.DirectoryInfo",
        "System.IO.DriveInfo", "System.IO.RandomAccess",
        "System.IO.StreamReader", "System.IO.StreamWriter", // 这两个可以直接按路径打开文件
        // 进程和整个程序的生命周期
        "System.Diagnostics.Process", "System.Diagnostics.ProcessStartInfo",
        "System.AppDomain", "System.AppContext",
        // 绕过 IDos 直接读写宿主的控制台
        "System.Console",
        // 动态加载代码、调用本机代码
        "System.Reflection.Assembly", "System.Activator",
        "System.Runtime.InteropServices.Marshal", "System.Runtime.InteropServices.NativeLibrary",
        // 新线程不受 Ctrl+C 和重启控制
        "System.Threading.Thread", "System.Threading.ThreadPool", "System.Threading.Timer",
        "System.Threading.Tasks.Task", "System.Threading.Tasks.Parallel",
    ];

    /// <summary>
    /// 被禁止的单个成员。System.Environment 不能整个禁止：编译器为 yield 生成的代码会读
    /// Environment.CurrentManagedThreadId，这是无害的。
    /// </summary>
    private static readonly HashSet<string> ForbiddenMembers =
    [
        "System.Environment::Exit", "System.Environment::FailFast",
        "System.Environment::GetEnvironmentVariable", "System.Environment::GetEnvironmentVariables",
        "System.Environment::SetEnvironmentVariable", "System.Environment::ExpandEnvironmentVariables",
        "System.Environment::get_CurrentDirectory", "System.Environment::set_CurrentDirectory",
        "System.Environment::GetCommandLineArgs", "System.Environment::get_CommandLine",
        "System.Environment::GetFolderPath", "System.Environment::GetLogicalDrives",
    ];

    /// <summary>被禁止的整个命名空间（前缀匹配）。</summary>
    private static readonly string[] ForbiddenNamespaces =
    [
        "System.IO.Pipes.", "System.IO.MemoryMappedFiles.", "System.IO.IsolatedStorage.",
        "System.Net.", "System.Reflection.Emit.", "System.Runtime.Loader.",
    ];

    public static byte[] Process(byte[] image, bool verify, bool inject)
    {
        try
        {
            return ProcessCore(image, verify, inject);
        }
        catch (DosException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 不管程序文件坏成什么样，都只是"这个程序不能运行"，不能让整台机器出错
            throw new DosException(DosError.BadProgram, ex is BadImageFormatException or EndOfStreamException
                ? "not a .NET assembly"
                : "cannot load: " + ex.Message);
        }
    }

    private static byte[] ProcessCore(byte[] image, bool verify, bool inject)
    {
        using var input = new MemoryStream(image);
        // Deferred：用到时才解码。Immediate 会立刻解码所有特性，解码时要去磁盘上找框架程序集，
        // 发布成单个可执行文件时找不到。
        using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { ReadingMode = ReadingMode.Deferred });
        var module = assembly.MainModule;
        if (verify) Verify(module);
        if (inject) Inject(module);
        using var output = new MemoryStream();
        assembly.Write(output);
        return output.ToArray();
    }

    private static void Verify(ModuleDefinition module)
    {
        foreach (var type in module.GetTypeReferences())
            Check(type.FullName);
        foreach (var member in module.GetMemberReferences())
        {
            Check(member.DeclaringType.FullName);
            string key = member.DeclaringType.FullName + "::" + member.Name;
            if (ForbiddenMembers.Contains(key))
                throw new DosException(DosError.BadProgram, $"forbidden API: {member.DeclaringType.FullName}.{member.Name}");
        }

        foreach (var type in AllTypes(module))
        {
            foreach (var method in type.Methods)
            {
                if (method.IsPInvokeImpl || method.HasPInvokeInfo)
                    throw new DosException(DosError.BadProgram, $"forbidden API: native method {method.Name}");
            }
        }
    }

    private static void Check(string fullName)
    {
        // 嵌套类型写作 Outer/Inner，泛型写作 Name`1，都按外层的名字判断
        string name = fullName;
        int cut = name.IndexOfAny(['/', '`', '<']);
        if (cut > 0) name = name[..cut];

        bool forbidden = ForbiddenTypes.Contains(name);
        foreach (string ns in ForbiddenNamespaces)
            if (fullName.StartsWith(ns, StringComparison.Ordinal)) forbidden = true;
        if (forbidden)
            throw new DosException(DosError.BadProgram, $"forbidden API: {fullName}");
    }

    private static void Inject(ModuleDefinition module)
    {
        var poll = PollReference(module);

        foreach (var type in AllTypes(module))
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody || method.Body.Instructions.Count == 0) continue;
                InjectInto(method.Body, poll);
            }
        }
    }

    /// <summary>
    /// 手工构造对 PhiDos.Abi.DosRuntime.Poll() 的引用。
    /// 不用 module.ImportReference(反射得到的 MethodInfo)：那样 Cecil 会去磁盘上找 System.Runtime.dll，
    /// 发布成单个可执行文件时这些文件并不存在。
    /// </summary>
    private static MethodReference PollReference(ModuleDefinition module)
    {
        var abiName = typeof(DosRuntime).Assembly.GetName();
        var scope = module.AssemblyReferences.FirstOrDefault(r => r.Name == abiName.Name);
        if (scope is null)
        {
            scope = new AssemblyNameReference(abiName.Name!, abiName.Version);
            module.AssemblyReferences.Add(scope);
        }
        var type = new TypeReference(typeof(DosRuntime).Namespace, nameof(DosRuntime), module, scope);
        return new MethodReference(nameof(DosRuntime.Poll), module.TypeSystem.Void, type) { HasThis = false };
    }

    private static void InjectInto(MethodBody body, MethodReference poll)
    {
        // 先把 br.s 这类短跳转展开成长跳转，插入指令后偏移量才不会超出范围
        body.SimplifyMacros();
        var il = body.GetILProcessor();
        var original = body.Instructions.ToList();
        var index = new Dictionary<Instruction, int>();
        for (int i = 0; i < original.Count; i++) index[original[i]] = i;

        foreach (var instr in original)
        {
            if (!IsBackwardBranch(instr, index)) continue;
            var call = il.Create(OpCodes.Call, poll);
            il.InsertBefore(instr, call);
            Retarget(body, instr, call);
        }

        // 方法开头也插一次，防止无限递归（递归不会产生往回跳的分支）
        il.InsertBefore(body.Instructions[0], il.Create(OpCodes.Call, poll));
        body.OptimizeMacros();
    }

    private static bool IsBackwardBranch(Instruction instr, Dictionary<Instruction, int> index)
    {
        int self = index[instr];
        return instr.Operand switch
        {
            Instruction target => (instr.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch)
                                  && index.TryGetValue(target, out int t) && t <= self,
            Instruction[] targets => targets.Any(x => index.TryGetValue(x, out int t) && t <= self),
            _ => false,
        };
    }

    /// <summary>原来跳到 old 的地方（分支、switch、异常处理块的边界）改成跳到 replacement。</summary>
    private static void Retarget(MethodBody body, Instruction old, Instruction replacement)
    {
        foreach (var i in body.Instructions)
        {
            if (ReferenceEquals(i, replacement)) continue;
            if (i.Operand == old) i.Operand = replacement;
            else if (i.Operand is Instruction[] targets)
                for (int k = 0; k < targets.Length; k++)
                    if (targets[k] == old) targets[k] = replacement;
        }
        foreach (var h in body.ExceptionHandlers)
        {
            if (h.TryStart == old) h.TryStart = replacement;
            if (h.TryEnd == old) h.TryEnd = replacement;
            if (h.HandlerStart == old) h.HandlerStart = replacement;
            if (h.HandlerEnd == old) h.HandlerEnd = replacement;
            if (h.FilterStart == old) h.FilterStart = replacement;
        }
    }

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        var stack = new Stack<TypeDefinition>(module.Types);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            yield return t;
            foreach (var n in t.NestedTypes) stack.Push(n);
        }
    }
}
