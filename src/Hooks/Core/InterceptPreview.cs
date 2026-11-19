using System;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CharacterBosses.Core;

/// <summary>
/// 联机无色牌「**拦截**」（`Intercept`）在**意图预览**上的口径（玩家 2026-11-19 定稿）。
///
/// 实机结算在 `Patches/InterceptBossDamagePatch.cs`（把被保护者从目标列表摘掉、把伤害按份数
/// 转到拦截者身上）；这里只负责「屏幕上的数字要跟着对」：
///
/// ```text
/// 我是被保护者            ⇒ 所有伤害预览一律 0
/// 我是拦截者（护着 N 人）  ⇒ Boss 群攻：按**我自己**的易伤/力量算，再 ×(N+1)   （护 1 人 = 双倍）
///                            Boss 单体打别人：按**我自己**的状态算，1 倍（伤害转到我身上）
///                            Boss 单体打我：  照常，1 倍
/// 其他人                  ⇒ 原样
/// ```
///
/// ⚠ 为什么必须自己算：我们的意图数字走 <see cref="BossDamagePredictor.FinalDamage"/>（喂牌面基础值，
/// 由原版管道算一次），而原版 `InterceptPower` 的 ×(N+1) 已经在
/// `InterceptPowerBossDamageNoVanillaMultiplierPatch` 里对我们 Boss 关掉了 ⇒ 预览必须与之一致，
/// 否则又会出现「显示 2 倍、实际 1 倍」这类对不上的老问题。
///
/// 兼容薄荷尖塔：这里**只改数字**，不增删意图、也不动 `MoveState.Intents` 的条数与类型。
/// </summary>
public static class InterceptPreview
{
    /// <summary>本地（这台机器上的）玩家的生物。</summary>
    public static Creature? LocalCreature(ICombatState? combat)
    {
        try
        {
            if (combat == null) return null;
            return MegaCrit.Sts2.Core.Context.LocalContext.GetMe(combat)?.Creature;
        }
        catch { return null; }
    }

    /// <summary>本地玩家是不是被某人「拦截保护」着。</summary>
    public static bool LocalIsCovered(ICombatState? combat)
        => Patches.BossDamageOrigin.InterceptorOf(LocalCreature(combat)) != null;

    /// <summary>本地玩家是拦截者时，他此刻护着几个人（不是拦截者 ⇒ 0）。</summary>
    public static int LocalCoverCount(ICombatState? combat)
    {
        try
        {
            var local = LocalCreature(combat);
            if (local == null) return 0;
            if (local.GetPower<InterceptPower>() == null) return 0;
            return Patches.BossDamageOrigin.CoveredCount(local);
        }
        catch { return 0; }
    }

    /// <summary>
    /// 预览里「该用谁的状态算这一笔伤害」。
    /// 注意：返回 null 时表示「按调用方原来的目标算」。
    /// </summary>
    public static Creature? EffectiveTarget(Creature? bossTarget, ICombatState? combat)
    {
        try
        {
            var local = LocalCreature(combat);
            if (local == null) return bossTarget;

            // ① 我被保护着 ⇒ 这一笔其实落在拦截者身上（对我而言是 0，所以「打我」不成立）
            var myInterceptor = Patches.BossDamageOrigin.InterceptorOf(local);
            if (myInterceptor != null) return myInterceptor;

            // ② 我是拦截者，而被瞄准的是我护着的人 ⇒ 这一笔转到我身上
            if (LocalCoverCount(combat) > 0 && bossTarget != null)
            {
                var theirInterceptor = Patches.BossDamageOrigin.InterceptorOf(bossTarget);
                if (theirInterceptor != null && ReferenceEquals(theirInterceptor, local)) return local;
            }

            return bossTarget;
        }
        catch { return bossTarget; }
    }

    /// <summary>
    /// 本地玩家看到的这一段伤害。
    /// </summary>
    /// <param name="card">这张牌（拿它跑原版伤害管道；可为 null = 无来源伤害）。</param>
    /// <param name="baseDamage">牌面基础值（不喂最终值，管道只跑一次）。</param>
    /// <param name="bossVictim">Boss 本来要打的那个人（我们的意图代码算出来的）。</param>
    /// <param name="isAoE">是不是群攻（`TargetType.AllEnemies`）。</param>
    public static int ViewDamage(CardModel? card, int baseDamage, Creature? bossVictim, bool isAoE)
    {
        try
        {
            var combat = CombatOf(card, bossVictim);
            var local = LocalCreature(combat);

            // ① 被保护者：所有伤害预览归 0
            if (local != null && Patches.BossDamageOrigin.InterceptorOf(local) != null) return 0;

            // ② 拦截者：按**他自己**的状态算；群攻再乘 (护着的人数 + 1)
            if (local != null)
            {
                int covered = LocalCoverCount(combat);
                if (covered > 0)
                {
                    int mine = BossDamagePredictor.FinalDamage(card, baseDamage, local);
                    return isAoE ? Math.Max(0, mine) * (1 + covered) : Math.Max(0, mine);
                }
            }

            // ③ 普通：原样
            return BossDamagePredictor.FinalDamage(card, baseDamage, bossVictim);
        }
        catch { return BossDamagePredictor.FinalDamage(card, baseDamage, bossVictim); }
    }

    private static ICombatState? CombatOf(CardModel? card, Creature? creature)
    {
        try
        {
            var combat = card?.Owner?.Creature?.CombatState;
            if (combat != null) return combat;
        }
        catch { /* 忽略 */ }
        try { return creature?.CombatState; } catch { return null; }
    }

    /// <summary>
    /// 「被保护 ⇒ 所有伤害预览归 0」的收尾：把本地玩家手里那串意图里的**攻击意图**换成 0 伤害的同类节点。
    ///
    /// 只换攻击意图（`SingleAttackIntent` / `MultiAttackIntent` 及其子类），强化/策略/牺牲那些
    /// 一个都不动；也不改条数 ⇒ 薄荷尖塔那边统计到的条数不变。
    /// </summary>
    public static System.Collections.Generic.List<MegaCrit.Sts2.Core.MonsterMoves.Intents.AbstractIntent> ZeroAttacks(
        System.Collections.Generic.List<MegaCrit.Sts2.Core.MonsterMoves.Intents.AbstractIntent>? intents,
        ICombatState? combat)
    {
        var result = new System.Collections.Generic.List<MegaCrit.Sts2.Core.MonsterMoves.Intents.AbstractIntent>();
        if (intents == null) return result;

        bool covered = LocalIsCovered(combat);
        foreach (var intent in intents)
        {
            if (!covered || intent == null) { result.Add(intent!); continue; }

            try
            {
                if (intent is MegaCrit.Sts2.Core.MonsterMoves.Intents.MultiAttackIntent multi)
                {
                    int times = Math.Max(1, multi.Repeats);
                    result.Add(new RawMultiAttackIntent(0, times));
                    continue;
                }
                if (intent is MegaCrit.Sts2.Core.MonsterMoves.Intents.SingleAttackIntent)
                {
                    result.Add(new RawAttackIntent(0));
                    continue;
                }
            }
            catch { /* 认不出来就原样保留 */ }
            result.Add(intent);
        }
        return result;
    }
}
