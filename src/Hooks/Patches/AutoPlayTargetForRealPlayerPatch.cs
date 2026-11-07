using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using HarmonyLib;

namespace CharacterBosses.Patches;

/// <summary>
/// **自动出牌「没给目标」时，替它补一个相反阵营的目标** —— 两侧都管。
///
/// 玩家反馈（2026-11-02）：
/// > 「打炼药猎的时候用**倾泻**打**巨石**疑似只打自己。我战士玩的少，倾泻应该不会打自己的吧」
///
/// 链路（反编译核对）：
///   1. 「倾泻」`Cascade.OnPlay` → `CardPileCmd.AutoPlayFromDrawPile(ctx, Owner, N, Top, false)`，
///      从**摸牌堆顶**把牌自动打出去；
///   2. 那些牌走 `CardCmd.AutoPlay(ctx, card, **target: null**)`；
///   3. 原版给 null 目标找靶的条件是 `HittableEnemies` 里
///      `c.IsPlayer && c != card.Owner.Creature` —— **它只对「怪物驾驶的牌」成立**；
///      一旦出牌方是真人玩家（敌方阵营里没有 `IsPlayer` 的候选），就**挑不到人**，
///      于是落回目标 = 自己（这就是「巨石砸自己」）。
///
/// 所以这里统一兜底（只在**没给目标**时动手，有目标一律不碰）：
///   · 拥有者是**真人玩家** ⇒ 补「Boss 侧」里第一个活着的；
///   · 拥有者是**我们的 Boss**（影子玩家）⇒ 补「玩家侧」里第一个活着的
///     （优先它锁定的那位，和跟踪锁定同一口径）；
///   · 其它（原版怪物 / 别的模组）⇒ 不动，保持原样。
///
/// 这样不管「这张牌到底是谁的、从哪个牌堆被自动打出来」，都不会再砸到自己人。
/// </summary>
[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.AutoPlay))]
internal static class AutoPlayTargetForRealPlayerPatch
{
    [HarmonyPrefix]
    public static void Prefix(CardModel card, ref Creature target)
    {
        try
        {
            // ★ 一层「轰击储」的「环绕轨道」：**唤醒之后，轰击的伤害反过来打她自己**。
            //   轰击（`Bombardment`）是 `AfterAutoPrePlayPhaseEnteredEarly` 里
            //   `CardCmd.AutoPlay(ctx, this, target: null)` 自动打出来的 ⇒ 这里把目标直接改成她本人，
            //   于是整张牌（攻击动画、力量/易伤、格挡）都按「打她自己」结算。
            //   ⚠ 必须在「target == null」那道检查**之前**判断：原版正是给 null 才走到下面挑玩家那一步。
            if (CharacterBosses.Powers.BossOrbitalBombardPower.Awake
                && SafeEntry(card) == "BOMBARDMENT")
            {
                var bombardOwner = card.Owner?.Creature;
                if (bombardOwner?.Monster is Monsters.CharBossMonster bombardBoss && bombardBoss.Creature != null)
                {
                    target = bombardBoss.Creature;
                    Logger.Log("[CharBoss] 环绕轨道：轰击已唤醒 ⇒ 这一发反过来打她自己");
                    return;
                }
            }

            if (target != null || card == null) return;                  // 已经有目标：一律不碰
            if (card.TargetType != TargetType.AnyEnemy) return;          // 只补「需要敌方目标」的攻击牌

            var owner = card.Owner?.Creature;
            if (owner == null) return;

            var combat = owner.CombatState;
            if (combat == null) return;

            Creature? picked = null;
            string side;
            if (owner.Monster == null && owner.Player != null)
            {
                side = "Boss 侧";
                picked = FirstAlive(combat.Enemies) ?? FirstAlive(combat.HittableEnemies);
            }
            else if (owner.Monster is Monsters.CharBossMonster boss)
            {
                side = "玩家侧";
                picked = LockedPlayer(boss) ?? FirstAlivePlayer(combat);
            }
            else
            {
                return;                                                   // 原版怪物 / 别的模组：不动
            }

            if (picked == null) return;
            target = picked;
            Logger.Log($"[CharBoss] 自动出牌没给目标：{SafeEntry(card)}（拥有者 {SafeName(owner)}）⇒ " +
                       $"补成{side} {SafeName(picked)}");
        }
        catch { /* 忽略：判断不了就留给原版 */ }
    }

    private static Creature? FirstAlive(IReadOnlyList<Creature>? list)
    {
        try
        {
            if (list == null) return null;
            foreach (var each in list)
            {
                if (each == null) continue;
                try { if (each.IsAlive) return each; } catch { /* 下一个 */ }
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>玩家侧第一个活着的**真人玩家**（跳过随从/宠物）。</summary>
    private static Creature? FirstAlivePlayer(ICombatState? combat)
    {
        try
        {
            foreach (var player in Core.BossLockOn.RealPlayers(combat))
            {
                var creature = player?.Creature;
                if (creature == null) continue;
                try { if (creature.IsAlive) return creature; } catch { /* 下一个 */ }
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>她锁定的那位玩家（没有就 null）。</summary>
    private static Creature? LockedPlayer(Monsters.CharBossMonster boss)
    {
        try
        {
            var locked = Core.BossLockOn.TargetFor(boss)?.Creature;
            if (locked != null && locked.IsAlive) return locked;
        }
        catch { /* 忽略 */ }
        return null;
    }

    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }

    private static string SafeName(Creature? creature)
    {
        try { return creature?.Name ?? "?"; } catch { return "?"; }
    }
}
