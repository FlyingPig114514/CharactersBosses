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
/// ⚠ 只改这一个方法收到的实参（换的是引用、没有 `Clear()` 调用方的列表），
///   同一批派发的其它监听者拿到的仍是原来那份列表。
/// </summary>
[HarmonyPatch(typeof(DemonFormPower), nameof(DemonFormPower.AfterSideTurnStart))]
internal static class EnemyDemonFormPlayerTurnStartPatch
{
    /// <summary>空列表（敌方回合开始时让原版那句 `Contains(Owner)` 不成立）。</summary>
    private static readonly IReadOnlyList<Creature> None = Array.Empty<Creature>();

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
