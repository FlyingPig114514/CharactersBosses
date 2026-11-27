using System;
using System.Collections.Generic;
using Godot;
// `using Godot;` 会带进 Godot.Environment（环境光资源），与 System.Environment 撞名
using Environment = System.Environment;

namespace CharacterBosses.Core;

/// <summary>
/// **游戏内热键的唯一入口**（v0.2.976）。
///
/// 为什么要有这一层（用户 2026-11-27：「游戏内按 F8 生成『最近日志』的功能从没成功过，真正实现出来」）：
///   原来 F8/F9 只挂在**我们自己 new 出来的裸 Node** 的 `_UnhandledKeyInput` 上
///   （见 `RecentLogDumper` / `SceneTreeDumper`）。实测铁证：`mod_log.txt` 里挂载成功行 **58 行**，
///   但「已导出 / 导出失败」**各 0 行**、`characterbosses_recent.txt` 与 `scene_tree.txt` **都不存在**
///   ⇒ 按键根本没进到我们那个回调里（`Dump()` 内部每步都有 try/catch，真跑进去失败会留痕）。
///
/// 所以现在**四条通道并行**，谁先把事件递到就跟谁走：
///   · `game.NGame._Input`                  —— Harmony 后缀，挂在游戏主节点自己的 `_Input` 上（首选，最上游）
///   · `game.NInputManager._UnhandledKeyInput` —— Harmony 后缀，挂在游戏输入管理器上
///   · `node._Input` / `node._UnhandledKeyInput` —— 我们自己的 Node（原来的写法，保留当冗余）
/// 一次按键最多只触发一次（<see cref="FireGuardMs"/> 毫秒内重复到达直接丢掉）。
///
/// 另外每条通道**前 <see cref="ProbeLinesPerChannel"/> 次按键**会打一行「按键探针」，
/// 下次再出问题就能一眼看出「到底哪条通道活着」（这是交接文档 §5.1 要求的那支探针，
/// 常驻但只打几十行，属于零成本自证）。
/// </summary>
internal static class HotkeyRouter
{
    /// <summary>每条通道最多打几行探针（足够判断通道活不活，又不会淹掉日志）。</summary>
    private const int ProbeLinesPerChannel = 30;

    /// <summary>同一次按键会被多条通道各收到一次，这个窗口内只认第一次。</summary>
    private const long FireGuardMs = 400;

    private static readonly Dictionary<string, int> _probeCount = new();
    private static readonly List<string> _channelsSeen = new();
    private static long _lastFireMs = long.MinValue / 4;
    private static string _lastChannel = "?";

    /// <summary>已经递到过按键事件的通道（写进导出表头，出问题时一看就知道是哪条通道在干活）。</summary>
    internal static string ChannelsSeen()
    {
        try
        {
            lock (_probeCount) return _channelsSeen.Count == 0 ? "（还没收到任何按键）" : string.Join(" / ", _channelsSeen);
        }
        catch { return "?"; }
    }

    /// <summary>最后触发导出的是哪条通道。</summary>
    internal static string LastChannel => _lastChannel;

    /// <summary>
    /// 所有通道都调这里。**绝不抛异常**（它挂在游戏的 `_Input` 上，抛出去会把游戏的输入流程打断）。
    /// </summary>
    internal static void Handle(InputEvent? ev, string channel)
    {
        try
        {
            if (ev is not InputEventKey key) return;
            if (!key.Pressed || key.Echo) return;

            bool isF8 = key.Keycode == Key.F8 || key.PhysicalKeycode == Key.F8;
            bool isF9 = key.Keycode == Key.F9 || key.PhysicalKeycode == Key.F9;

            Probe(channel, key, isF8 || isF9);
            if (!isF8 && !isF9) return;

            long now = Environment.TickCount64;
            if (now - _lastFireMs < FireGuardMs) return;      // 同一按键从别的通道又到了一次
            _lastFireMs = now;
            _lastChannel = channel;

            // ★ 交接文档 §3.3：这行**必须**留着 —— 下次玩家说「按了没反应」，
            //   有这行 ⇒ 键到我们这儿了（问题在 Dump）；没这行 ⇒ 键压根没进来（问题在通道）。
            Logger.Log($"[CharBoss] 按到 {(isF8 ? "F8" : "F9")} 了（通道={channel}）");

            if (isF8) RecentLogDumper.Dump();
            else SceneTreeDumper.DumpAll();
        }
        catch (Exception e)
        {
            try { Logger.Log("[CharBoss] 热键处理失败: " + e.Message); } catch { /* 忽略 */ }
        }
    }

    /// <summary>通道探针：每条通道前几十次按键打一行（F8/F9 额外带个 ★）。</summary>
    private static void Probe(string channel, InputEventKey key, bool interesting)
    {
        try
        {
            int n;
            lock (_probeCount)
            {
                if (!_probeCount.TryGetValue(channel, out n))
                {
                    n = 0;
                    _channelsSeen.Add(channel);
                }
                if (n >= ProbeLinesPerChannel) return;
                _probeCount[channel] = n + 1;
            }

            Logger.Log($"[CharBoss] 按键探针 channel={channel} keycode={key.Keycode} physical={key.PhysicalKeycode}" +
                       (interesting ? " ★" : string.Empty));
        }
        catch { /* 探针绝不影响按键 */ }
    }
}
