using PhiDos.Core.Kernel;
using PhiDos.Core.Machine;

namespace PhiDos.Core.Shell;

/// <summary>
/// 命令解释器，相当于 COMMAND.COM。
/// </summary>
internal sealed partial class CommandShell
{
    private readonly DosKernel _kernel;
    private readonly DosProcess _process;
    private readonly bool _isPrimary;

    public CommandShell(DosKernel kernel, DosProcess process, bool isPrimary)
    {
        _kernel = kernel;
        _process = process;
        _isPrimary = isPrimary;
    }

    public bool EchoOn { get; set; } = true;

    public void Run()
    {
        bool needNewLine = true;
        while (true)
        {
            if (needNewLine && EchoOn) Out("\r\n");
            PrintPrompt();

            string line;
            try
            {
                line = _kernel.ReadConsoleLine();
            }
            catch (DosBreakException)
            {
                needNewLine = true;
                continue;
            }
            Out("\r\n");

            if (line.Trim().Length == 0)
            {
                needNewLine = false;
                continue;
            }
            needNewLine = true;

            if (line.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase))
            {
                if (_isPrimary && _kernel.Machine.Options.ExitPowersOff)
                    _kernel.Machine.PowerOffFromInside(_kernel.Gate);
            }
        }
    }

    private void PrintPrompt() => Out(_kernel.CurrentDrive + ">");

    private void Out(string text) => _kernel.StdWrite(_process, text);
}
