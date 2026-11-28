#if STS2_111
using System;
using MegaCrit.Sts2.Core.Entities.Players;

namespace CharacterBosses.Core;

/// <summary>
/// **把本机视角切到某个座位**（用户 2026-11-28）。
///
/// 场景：「统帅模式」（尖塔搭子 AiTeammate：一台机器操控多个座位）下，抉择储的「指导」要**被他锁定的那位玩家**
/// 选牌。玩家回合结束时如果视角停别的座位上，界面就出不来 —— 实测日志：
/// <code>
/// 指导：储君打出「抉择，抉择」⇒ 让 P#d7d5b7 从 5 张里挑一张（HIDDEN_CACHE,BULWARK,ALIGNMENT,RESONANCE,SHOCKWAVE）
/// 指导：选择界面出错，退回第一张: Cannot focus selection owner
/// 指导：玩家没给出选择 ⇒ 退回第一张 HIDDEN_CACHE
/// </code>
/// （第二次退回 BULWARK = 铸墙 —— 正是玩家看到的「默认挑了隐秘藏品和铸墙」）
/// 原因：原版选择界面要求「本机就是这次选择的主人」，而 `LocalContext.IsMe(chooser)` 为假 ⇒ 抛
/// `Cannot focus selection owner`。用户口径：「应该让玩家像其它出现选牌界面时那样**跳到对应玩家的视角**进行选择」。
///
/// 做法：把 `LocalContext.NetId`（游戏认为的「我」）设成那位玩家 —— 这正是统帅模式切视角用的同一个开关
/// （`BossUiSession.EnsurePerspective` 早就在监听它的变化）。
///
/// ⚠ **只在「本机确实能操控多个座位」时才动手**，否则会把普通联机里别人的座位抢过来：
///   · 观察到过 `LocalContext.NetId` 自己在变（= 有切视角的模组在跑，本机操控多座位）；或
///   · 进程里有 `AiTeammate` 程序集（统帅模式的宿主）。
/// 两条都不满足 ⇒ 一律不动（返回 false，调用方按原样退回）。
/// </summary>
internal static class SeatSwitch
{
    private const string TeammateAssembly = "AiTeammate";

    /// <summary>本机观察到过 `LocalContext.NetId` 被改（⇒ 有切视角的模组在跑）。</summary>
    private static bool _observedChange;

    private static int _aiTeammate = -1;   // -1 还没探测；0 否；1 是

    /// <summary>本机能不能操控多个座位（决定我们敢不敢自己切视角）。</summary>
    internal static bool CanSwitchSeats
    {
        get
        {
            if (_observedChange) return true;
            if (_aiTeammate < 0)
            {
                int v = 0;
                try
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try
                        {
                            if (string.Equals(asm.GetName().Name, TeammateAssembly, StringComparison.OrdinalIgnoreCase))
                            {
                                v = 1;
                                break;
                            }
                        }
                        catch { /* 单个程序集读不到就算了 */ }
                    }
                }
                catch { v = 0; }
                _aiTeammate = v;
            }
            return _aiTeammate == 1;
        }
    }

    /// <summary>`BossUiSession.EnsurePerspective` 观察到视角变了就调这里（= 本机能切座位）。</summary>
    internal static void NotePerspectiveChanged()
    {
        _observedChange = true;
    }

    /// <summary>
    /// 把本机视角切到 <paramref name="target"/>。已经是它、或本机不能切座位 ⇒ 返回现状（不报错）。
    /// 返回 true 表示「切完（或本来就在）那位玩家身上」。
    /// </summary>
    internal static bool SwitchTo(Player? target, string why)
    {
        try
        {
            if (target == null) return false;
            if (MegaCrit.Sts2.Core.Context.LocalContext.IsMe(target)) return true;
            if (!CanSwitchSeats) return false;

            ulong? before = null;
            try { before = MegaCrit.Sts2.Core.Context.LocalContext.NetId; } catch { before = null; }

            try { MegaCrit.Sts2.Core.Context.LocalContext.NetId = target.NetId; }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 选牌视角切换失败（继续按原样）: " + e.Message);
                return false;
            }

            // 立刻回读确认（游戏那边可能是异步生效的，读不到就当失败，让调用方走兜底）
            bool ok;
            try { ok = MegaCrit.Sts2.Core.Context.LocalContext.IsMe(target); } catch { ok = false; }

            Logger.Log($"[CharBoss] 选牌视角切换：{(before?.ToString() ?? "?")} → {SafeName(target)}" +
                       $"(NetId={target.NetId}) 因为{why}" + (ok ? "（已生效）" : "（⚠ 回读仍是别人）"));
            return ok;
        }
        catch { return false; }
    }

    private static string SafeName(Player? p)
    {
        try { return p?.Creature?.Name ?? "?"; } catch { return "?"; }
    }
}
#endif
