using System;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace CharacterBosses.Core;

/// <summary>
/// **本机在联机里的角色**（房主 / 客人 / 单人）。
///
/// 用户 2026-10-29 要求：「多人时，所有影响游戏的设置（除音乐外的所有）以房主的为准，避免不同步。」
///
/// 为什么必须这样做：我们的设置（地图开关、与原本 Boss 共用匹配池…）存在**每台机器自己的**
/// `charbosses_config.cfg` 里，而它们决定「这一幕到底上哪个 Boss」。两个人配置不同 ⇒
/// 各自算出不同的 Boss ⇒ 进 Boss 房就「多人数据不同步」。
///
/// 判据：`RunManager.Instance.NetService.Type`（`NetGameType`：Singleplayer / Host / Client / Replay）。
/// 拿不到时**按「房主」处理**（宁可让单人/未知环境照常替换，也不要凭空关掉功能）。
/// </summary>
internal static class NetRole
{
    /// <summary>联机类型（拿不到按 None 处理）。</summary>
    internal static NetGameType Type
    {
        get
        {
            try
            {
                var rm = RunManager.Instance;
                var svc = rm?.NetService;
                return svc?.Type ?? NetGameType.None;
            }
            catch { return NetGameType.None; }
        }
    }

    /// <summary>是不是「房主或单人」（= 本机说了算）：读取本机配置文件。</summary>
    internal static bool IsHostOrSolo => Type is NetGameType.Host or NetGameType.Singleplayer
                                              or NetGameType.None or NetGameType.Replay;

    /// <summary>
    /// 是不是**客人**（多人里非房主的那一方）：这类客户端**不读本机配置**，
    /// 一律跟随房主已经写进同步状态的结论（见 `BossConfig` / `ForVanillaBoss` 的客人分支）。
    /// </summary>
    internal static bool IsGuest => Type == NetGameType.Client;
}
