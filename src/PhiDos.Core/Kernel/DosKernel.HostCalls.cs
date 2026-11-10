using PhiDos.Abi;
using PhiDos.Core.Machine;

namespace PhiDos.Core.Kernel;

internal sealed partial class DosKernel
{
    /// <summary>
    /// IDos.CallHost：调用宿主在 MachineOptions.HostCalls 里登记的函数。
    /// 函数在当前线程（虚拟机线程）上同步执行。
    /// </summary>
    public string CallHost(string name, string? argument)
    {
        EnterCall();
        var handler = FindHostCall(name) ?? throw new DosException(DosError.InvalidFunction);

        string? answer;
        try
        {
            answer = handler(argument ?? "");
        }
        catch (Exception ex) when (ex is MachineHaltException or DosBreakException or DosAbortException)
        {
            // 虚拟机自己用来打断程序的异常，原样放行
            throw;
        }
        catch (DosException)
        {
            // 宿主有意给出的 DOS 错误，原样交给程序
            throw;
        }
        catch (Exception ex)
        {
            // 宿主的函数出了错，不能让它弄垮虚拟机线程
            throw new DosException(DosError.HostError, "Host function failed: " + ex.Message);
        }

        // 处理函数执行期间机器可能被重启或关机了：这时不再回到程序里
        Gate.ThrowIfHalted();
        return answer ?? "";
    }

    /// <summary>按名字找处理函数，不区分大小写（即使宿主给的字典本身区分大小写）。</summary>
    private Func<string, string?>? FindHostCall(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var table = Machine.Options.HostCalls;
        if (table.TryGetValue(name, out var handler)) return handler;
        foreach (var (key, value) in table)
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return value;
        return null;
    }
}
