using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CharacterBosses.Patches;

/// <summary>
/// 恐惧模式「**铁甲战士的支配**」挂在**敌人**身上的「恶魔形态」：**玩家回合开始时**加力量。
///
/// 用户 2026-11-19：
/// > 「战士的支配让恶魔形态在玩家回合开始时获得力量，而不是怪物的回合开始时」
///
/// 反编译原版（`DemonFormPower.AfterSideTurnStart`）：
/// <code>
/// if (participants.Contains(base.Owner))
/// {
///     Flash();
///     Vfx?.OnEffectTriggered();
///     await PowerCmd.Apply&lt;StrengthPower&gt;(ctx, base.Owner, base.Amount, base.Owner, null);
/// }
/// </code>
/// 也就是「**谁那一侧的回合开始、就在谁的回合开始加力量**」——玩家自己拿这张能力牌时没问题，
/// 但支配把它塞给**敌人**之后，力量就跑到敌人的回合开始了（用户看到的那个错时机）。
///
/// ## 做法：不改写逻辑，只**换掉 `participants`**
/// 原版唯一的判据就是 `participants.Contains(Owner)`。所以这个前缀只做一件事：
///   · 持有者是**敌人** + 恐惧模式 + `side == Player` ⇒ 把 `participants` 换成「只含持有者」
///     ⇒ 原版照常跑（连 `Flash()` 与恶魔形态的 VFX 都在）；
///   · 持有者是敌人 + `side == Enemy` ⇒ 换成空列表 ⇒ 原版那句话不成立 ⇒ 敌方回合开始不再加力量；
///   · 持有者是玩家（玩家自己真拿了恶魔形态）或非恐惧模式 ⇒ 原样不动。
///
/// ## ★ v0.2.815 修：**额外回合不算回合**
/// 用户 2026-11-19：
/// > 「玩家的额外回合开始时（如佩尔之眼、倒吊人、龙涎香、腾跃等提供的），所有怪物的恶魔形态也会触发，
/// >  但实际上怪物并没有真的过去一个回合」
///
/// 根因很直接：把时机挪到「玩家回合开始」之后，**额外回合也有一次「玩家回合开始」**
/// （`CombatManager.StartTurn` 里同样是 `Hook.AfterSideTurnStart(state, Player, …)`），
/// 于是怪物一回合成两、三回合地叠力量。
///
/// ### 判据用的是游戏自己的标记，不猜
/// 反编译 `CombatManager.SwitchFromPlayerToEnemySide`（玩家回合结束时跑）：
/// <code>
/// turnState.PlayersTakingExtraTurn.Clear();
/// foreach (Player p in turnState.State.Players)
///     if (Hook.ShouldTakeExtraTurn(turnState.State, p, out source))
///         turnState.PlayersTakingExtraTurn.Add(p);
/// </code>
/// 而 `Hook.ShouldTakeExtraTurn` 会把**所有合法来源**都问一遍（`ShouldTakeExtraTurnEarly` 一轮、
/// `ShouldTakeExtraTurn` 一轮）——佩尔之眼 `PaelsEye`（走 Early）、`ExtraTurnPower`
/// （龙涎香 / 腾跃 / 倒吊人 / 任何 mod 给的能力）全在这条路上。
/// 于是：
///   · 这份列表在**该次额外回合开始前**就被填好，并在**下一次玩家回合结束**时才 `Clear()`
///     ⇒ 额外回合的 `AfterSideTurnStart(Player)` 触发时它**一定是非空**的；
///   · 普通玩家回合开始时它**一定是空的**（上一轮结束就清过）。
/// 所以 `CombatManager.Instance.PlayersTakingExtraTurn.Count > 0` 就是「现在是额外回合」的
/// 官方判据 —— 不用赌某个具体遗物的类型，也不用自己数回合。
/// （`PlayersTakingExtraTurn` 在 0.111 / 0.113 两份 `sts2` 里都有，取值器在没有 turnState 时返回空数组。）
///
/// 额外回合里改成空列表 ⇒ 那一侧什么都不做（不 Flash、不加力量），
/// 等下一个**真正的**玩家回合开始再加，于是「每整轮 +N 层力量」的节奏回到正确值。
///
/// ⚠ 只改这一个方法收到的实参（换的是引用、没有 `Clear()` 调用方的列表），
///   同一批派发的其它监听者拿到的仍是原来那份列表。
/// </summary>
[HarmonyPatch(typeof(DemonFormPower), nameof(DemonFormPower.AfterSideTurnStart))]
internal static class EnemyDemonFormPlayerTurnStartPatch
{
    /// <summary>空列表（让原版那句 `Contains(Owner)` 不成立）。</summary>
    private static readonly IReadOnlyList<Creature> None = Array.Empty<Creature>();

    /// <summary>诊断日志限流：最多 40 行，免得刷爆 mod_log。</summary>
    private static int _extraSkipsLogged;
    [HarmonyPrefix]
    private static void Prefix(DemonFormPower __instance, CombatSide side,
                               ref IReadOnlyList<Creature> participants)
    {
        try
        {
            var owner = __instance?.Owner;
            if (owner == null) return;
            if (owner.Side != CombatSide.Enemy) return;        // 玩家自己的恶魔形态：原版
            // ★ 用户 2026-11-19：「boss 不应该受支配影响」⇒ 我们自己的角色 Boss 一律不碰
            //   （她要是自己打出了恶魔形态，就照原版「她自己的回合开始」加力量）
            if (owner.Monster is Monsters.CharBossMonster) return;
            if (!Core.DreadMode.IsActive) return;              // 非恐惧模式：原版

            if (side == CombatSide.Player)
            {
                // ★ v0.2.815：额外回合（佩尔之眼 / 龙涎香 / 腾跃 / 倒吊人 …）不是真回合
                //   ⇒ 什么都不做，等下一个真·玩家回合开始再加力量（判据见 Core/ExtraTurnWatch.cs）
                if (Core.ExtraTurnWatch.IsExtraPlayerTurn)
                {
                    participants = None;
                    if (_extraSkipsLogged < 40)
                    {
                        _extraSkipsLogged++;
                        Logger.Log($"[CharBoss] 铁甲战士的支配：{owner.Name} 的恶魔形态" +
                                   $"**跳过本次（额外回合）**，不叠加力量" +
                                   (_extraSkipsLogged == 40 ? "（后续同类日志已省略）" : ""));
                    }
                    return;
                }

                // 骗过原版那句 `participants.Contains(base.Owner)` ⇒ 它在**玩家回合开始**加力量
                participants = new[] { owner };
                Logger.Log($"[CharBoss] 铁甲战士的支配：{owner.Name} 的恶魔形态在**玩家回合开始**加力量" +
                           $"（{__instance.Amount} 层）");
            }
            else
            {
                // 它自己那一侧回合开始：什么都不做（力量已经改到玩家回合开始了）
                participants = None;
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 铁甲战士的支配（恶魔形态时机）失败: " + e.Message);
        }
    }

}
