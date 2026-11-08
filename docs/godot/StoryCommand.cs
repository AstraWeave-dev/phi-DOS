// 剧情联动示例：一个"内置程序"，玩家在 DOS 里运行它时通知游戏。
// 详细说明见 docs/GODOT-INTEGRATION.md 的"和剧情联动"一节。

using System;
using Godot;
using PhiDos.Abi;

/// <summary>
/// 玩家在 DOS 里输入 UNLOCK 密码 时运行。它跑在虚拟机线程上，
/// 所以通知游戏时要用 CallDeferred 回到 Godot 主线程。
/// </summary>
public sealed class UnlockCommand : IPhiProgram
{
    /// <summary>密码正确时触发（在 Godot 主线程上）。</summary>
    public static event Action<string>? Unlocked;

    public int Run(IDos dos, string[] args)
    {
        if (args.Length == 0)
        {
            dos.WriteLine("Usage: UNLOCK password");
            return 1;
        }
        if (args[0] != "EVENSTAR")
        {
            dos.WriteLine("Access denied");
            return 1;
        }

        dos.WriteLine("Access granted");
        string who = dos.GetEnv("USER") ?? "guest";
        Callable.From(() => Unlocked?.Invoke(who)).CallDeferred();
        return 0;
    }
}
