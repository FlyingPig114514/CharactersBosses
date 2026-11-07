using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 遗物「**抱抱先生**」（<c>MrStruggles</c>，二层「王储」进阶 9 的奖励遗物）对 Boss 的结算。
///
/// 反编译原版（`MrStruggles.AfterPlayerTurnStart`）：
/// <code>
/// await CreatureCmd.Damage(choiceContext, combatState.HittableEnemies,
///                          Owner.PlayerCombatState.TurnNumber, ValueProp.Unpowered, Owner.Creature);
/// </code>
/// 也就是「**持有者的回合开始时，对所有可命中敌人造成「回合数」点无来源伤害**」——
/// 伤害随回合数成长（1、2、3…），`Unpowered` = 不吃力量/虚弱这类加成。
///
/// 影子玩家不在 `CombatState.Players` 里 ⇒ 那条 `AfterPlayerTurnStart` 永远不会替她广播
/// （它只对「本回合开始的那位真人玩家」派发），所以由这里在**她的回合开始**手动结算一次
/// （挂点：<see cref="BossHookBridge.FireTurnStart"/>，与其它「回合开始」类遗物同一个地方）。
///
/// 用户 2026-11-02 的三条要求：
///   ① **只会攻击所有玩家** ⇒ 目标取 <see cref="BossLockOn.RealPlayers"/>（真人玩家），
///      不含影子玩家、不含随从，也不是「只打被锁定那位」；
///   ② **伤害正确随回合成长** ⇒ 与她自己的回合号一致（`boss.TurnNumber`，
///      和 `PlayerCombatState.TurnNumber` 由 `AlignTurnNumber` 对齐）；
///   ③ 意图显示与「薄荷尖塔」兼容都复用这里的 <see cref="UpcomingDamage"/>
///      （见 <see cref="BossHandPanel"/> 与 <see cref="MintySpireDamagePreviewCompat"/>）。
/// </summary>
internal static class MrStrugglesPlays
{
    /// <summary>遗物的模型 id（`MrStruggles` → slug 全大写）。</summary>
    internal const string RelicEntry = "MR_STRUGGLES";

    /// <summary>她身上有没有「抱抱先生」。</summary>
    internal static bool Has(CharBossMonster? boss)
    {
        try
        {
            var relics = boss?.Shadow?.Relics;
            if (relics == null) return false;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                try { if (relic.Id.Entry == RelicEntry) return true; } catch { /* 忽略 */ }
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    /// <summary>
    /// 她**下一次**回合开始时会对每个玩家打多少（**展示用**）。
    ///
    /// 直接取 `boss.TurnNumber`，**不要 +1**：`PrepareTurn` 里 `TurnNumber++` 是在她**下一手**生成时做的，
    /// 而玩家回合里手牌栏显示的就是**那一手** ⇒ 此刻 `TurnNumber` 已经等于「她即将到来的那一回合」。
    /// 实测（用户 2026-11-02）：加了 +1 之后第一回合的意图显示 2，而她真正只打 1 点。
    /// </summary>
    internal static int UpcomingDamage(CharBossMonster? boss)
    {
        if (boss == null || !Has(boss)) return 0;
        try
        {
            int turn = Math.Max(0, boss.TurnNumber);
            // ★ v0.2.342（用户 2026-11-03：「抱抱先生的伤害预览在该伤害已触发后，仍然停留在她的意图中。
            //   应该在该伤害触发后就不显示这个意图，下回合开始再显示」）：
            //   这一笔伤害是在**她的回合开始**结算的（`RunAtTurnStart`）。结算完就该从意图里消失，
            //   等下一个回合的规划（`PrepareTurn` 把 `TurnNumber` ++）再显示新的数字。
            if (turn <= boss.MrStrugglesFiredForTurn) return 0;
            return turn;
        }
        catch { return 0; }
    }

    /// <summary>她**当前这一回合**的伤害（结算用）= 她的回合号（反编译：`PlayerCombatState.TurnNumber`）。</summary>
    internal static int ThisTurnDamage(CharBossMonster? boss)
    {
        try
        {
            int turn = Math.Max(0, boss?.TurnNumber ?? 0);
            if (turn > 0) return turn;
            return Math.Max(0, boss?.Shadow?.PlayerCombatState?.TurnNumber ?? 0);
        }
        catch { return 0; }
    }

    /// <summary>
    /// 她的回合开始：对所有**真人玩家**造成「她的回合数」点**无来源伤害**（`ValueProp.Unpowered`）。
    /// 一次传给 `CreatureCmd.Damage` 的是一整个列表 ⇒ 游戏里就是一次全体伤害（一处飘字、一处音效）。
    /// </summary>
    internal static async Task RunAtTurnStart(CharBossMonster boss, PlayerChoiceContext ctx)
    {
        try
        {
            if (boss?.Creature == null || !boss.Creature.IsAlive) return;
            if (!Has(boss)) return;

            int damage = ThisTurnDamage(boss);
            if (damage <= 0) return;

            var targets = new List<Creature>();
            foreach (var player in BossLockOn.RealPlayers(boss.CombatState))
            {
                var creature = player?.Creature;
                if (creature == null || !creature.IsAlive) continue;
                targets.Add(creature);
            }
            if (targets.Count == 0)
            {
                Logger.Log("[CharBoss] 抱抱先生：没有可打的真人玩家，跳过");
                return;
            }

            await CreatureCmd.Damage(ctx, targets, damage, ValueProp.Unpowered, boss.Creature);
            // 记下「第 N 回合这一笔已经打过了」⇒ 意图立刻从手牌栏最左边消失（用户 2026-11-03）
            boss.MrStrugglesFiredForTurn = Math.Max(boss.MrStrugglesFiredForTurn, boss.TurnNumber);
            Logger.Log($"[CharBoss] 抱抱先生：她第 {boss.TurnNumber} 回合开始 → " +
                       $"对 {targets.Count} 名玩家各 {damage} 点无来源伤害（Unpowered）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 抱抱先生结算失败: " + e);
        }
    }
}
