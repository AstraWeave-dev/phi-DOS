// 呼叫宿主示例：盘上的真程序通知游戏切换场景。
// 用法：把场景里的 DosTerminal 节点换成挂这个脚本（它继承 DosTerminal，绘制和按键处理都一样）。
// 详细说明见 docs/GODOT-INTEGRATION.md 第 9 节。

using System;
using System.Collections.Generic;
using Godot;
using PhiDos.Core.Machine;

/// <summary>
/// 登记了两项宿主功能的终端：
///   SCENE  参数是场景路径，例如 res://scenes/chapter2.tscn，切换到那个场景
///   SAY    参数是一句话，交给游戏的对话系统（这里只是打印出来）
/// 盘上的程序这样调用：dos.CallHost("SCENE", "res://scenes/chapter2.tscn")
/// </summary>
public partial class StoryTerminal : DosTerminal
{
    /// <summary>只允许切换到这个目录下的场景，免得盘上的程序把玩家带到不该去的地方。</summary>
    [Export] public string AllowedScenePrefix { get; set; } = "res://scenes/";

    protected override MachineOptions CreateOptions() => new()
    {
        PromptForDateTimeAtBoot = PromptForDateTime,
        HostCalls = new Dictionary<string, Func<string, string?>>(StringComparer.OrdinalIgnoreCase)
        {
            ["SCENE"] = ChangeScene,
            ["SAY"] = Say,
        },
    };

    /// <summary>
    /// 这个函数在虚拟机线程上执行，不能直接碰 Godot 节点。
    /// 做法是：先做简单的检查，然后用 CallDeferred 把真正的切换安排到主线程，马上返回。
    /// 程序拿到 "ok" 后继续运行到结束，主线程在下一帧切换场景。
    /// </summary>
    private string? ChangeScene(string path)
    {
        if (!path.StartsWith(AllowedScenePrefix, StringComparison.Ordinal) || !path.EndsWith(".tscn", StringComparison.Ordinal))
            return "denied";

        Callable.From(() =>
        {
            // 这里已经回到了主线程
            if (!ResourceLoader.Exists(path))
            {
                GD.PushWarning($"phi-DOS asked for a missing scene: {path}");
                return;
            }
            GetTree().ChangeSceneToFile(path);
        }).CallDeferred();
        return "ok";
    }

    private string? Say(string text)
    {
        Callable.From(() => GD.Print("[phi-DOS] " + text)).CallDeferred();
        return null; // 没有回答：程序拿到空字符串
    }
}
