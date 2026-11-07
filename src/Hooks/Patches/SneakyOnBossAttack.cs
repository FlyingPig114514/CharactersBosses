using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**鬼祟**」（<c>SneakyPower</c>）漏触发：真人玩家打出攻击牌会给她格挡，**其它 Boss 打出攻击牌却不会**。
///
/// 用户 2026-10-29：「玩家打出攻击牌会触发奇巧猎的鬼祟，而其它 boss 打攻击牌不会。」
///
/// 原版实现（反编译 `SneakyPower.AfterCardPlayed`）：
/// <code>
/// if (cardPlay.Card.Owner.Creature != Owner &amp;&amp; cardPlay.Card.Type == CardType.Attack)
///     await CreatureCmd.GainBlock(Owner, Amount, Unpowered, null);
/// </code>
/// 也就是「**别人**打出攻击牌 → 我获得 N 格挡」。而它是由原版
/// <c>Hook.AfterCardPlayed</c> 扇出到各个 Power 的 —— 我们 Boss 的牌是
/// <c>CardCmd.AutoPlay</c> 打出来的，出牌者所在的生物又**不在 <c>CombatState.Players</c> 里**
/// （见 `BossHookBridge` 类注释），扇出会漏掉这条，于是「其它 Boss 打攻击牌」不触发。
///
/// 做法：**出牌前后各拍一次「持有者的格挡」**——
///   · 原版给了（格挡变了）⇒ 什么都不做；
///   · 原版没给（格挡没变）⇒ 按卡面数值补一次。
/// 这样即使以后原版行为变了也不会出现双份。只在**我们 Boss 打出攻击牌**时检查。
/// </summary>
internal static class SneakyOnBossAttack
{
    private static int _logged;

    /// <summary>
    /// 出牌**之前**拍快照：场上每个「持有鬼祟、且**不是出牌者本人**」的生物 → 当前格挡。
    /// 返回 null 表示这张牌不是攻击牌 / 场上没有别人持有鬼祟（后续就不必补偿）。
    ///
    /// ⚠ 「排除自己」是**用户明确要求**的（2026-10-29）：「让鬼祟判定攻击牌时排除奇巧猎自己，只判定其它 boss」。
    /// 反编译里原版也是这个语义 —— `SneakyPower.AfterCardPlayed` 的条件是
    /// `cardPlay.Card.Owner.Creature != Owner`，也就是**自己打牌不给自己格挡**。
    /// 这里同时按「出牌者 Boss 的生物」和「卡牌 owner 的生物」两路比对，任何一路相等都不算。
    /// </summary>
    internal static Dictionary<Creature, int>? Snapshot(CharBossMonster attacker, CardModel? card,
                                                        out decimal amount)
    {
        amount = 0m;
        try
        {
            if (attacker?.Creature == null || card == null) return null;
            if (card.Type != CardType.Attack) return null;

            var combat = attacker.CombatState;
            if (combat == null) return null;

            var self = attacker.Creature;
            Creature? cardOwner = null;
            try { cardOwner = card.Owner?.Creature; } catch { cardOwner = null; }

            var snap = new Dictionary<Creature, int>();
            foreach (var holder in Holders(combat))
            {
                var creature = holder?.Creature;
                if (creature == null) continue;

                // ① 出牌者自己 → 不触发（原版条件 `Card.Owner.Creature != Owner`）
                if (ReferenceEquals(creature, self)) { LogSelfSkip(attacker, card); continue; }
                // ② 卡牌 owner 也是自己（换牌/生成牌的极端情况）→ 同样不触发
                if (cardOwner != null && ReferenceEquals(creature, cardOwner)) { LogSelfSkip(attacker, card); continue; }

                var power = creature.GetPower<SneakyPower>();
                if (power == null) continue;
                amount = Math.Max(0m, (decimal)power.Amount);
                if (amount <= 0m) continue;

                snap[creature] = creature.Block;
            }
            return snap.Count > 0 ? snap : null;
        }
        catch { return null; }
    }

    private static bool _selfSkipLogged;

    /// <summary>「自己打攻击牌 → 不给自己格挡」只报一次（用于核对用户这条要求真的生效）。</summary>
    private static void LogSelfSkip(CharBossMonster attacker, CardModel card)
    {
        if (_selfSkipLogged) return;
        _selfSkipLogged = true;
        try
        {
            Logger.Log($"[CharBoss] 鬼祟：{attacker.Id.Entry} 自己打出攻击牌 {card.Id.Entry} → **不触发**" +
                       "（只判定其它 Boss 的攻击牌；原版条件 Card.Owner.Creature != Owner）");
        }
        catch { /* 忽略 */ }
    }

    /// <summary>出牌**之后**核对：格挡没变的那几位说明原版没给 → 补上（出牌者本人永远跳过）。</summary>
    internal static async Task EnsureGrantedAsync(CharBossMonster attacker, CardModel? card,
                                                 Dictionary<Creature, int>? before, decimal amount)
    {
        if (before == null || amount <= 0m || card == null) return;

        try
        {
            foreach (var kv in before)
            {
                var holder = kv.Key;
                if (holder == null || !holder.IsAlive) continue;
                if (ReferenceEquals(holder, attacker.Creature)) continue;   // 二次保险：不给自己
                if (holder.Block != kv.Value) continue;      // 原版已经给过了

                await CreatureCmd.GainBlock(holder, amount, ValueProp.Unpowered, null);
                if (_logged++ < 30)
                {
                    Logger.Log($"[CharBoss] 鬼祟：{attacker.Id.Entry} 打出攻击牌 {card.Id.Entry}，" +
                               $"但原版没给 {holder.Name} 格挡（影子玩家不在 CombatState.Players，扇出漏了）" +
                               $"⇒ 补 {amount} 点");
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 鬼祟补触发失败（忽略）: " + e.Message);
        }
    }

    /// <summary>场上所有带「鬼祟」的生物（真人玩家 + 我们的 Boss 的影子玩家都算）。</summary>
    private static List<MegaCrit.Sts2.Core.Entities.Players.Player> Holders(MegaCrit.Sts2.Core.Combat.ICombatState combat)
    {
        var list = new List<MegaCrit.Sts2.Core.Entities.Players.Player>();
        try
        {
            var players = combat.Players;
            if (players != null)
            {
                foreach (var p in players)
                {
                    if (p?.Creature == null) continue;
                    if (!list.Contains(p)) list.Add(p);
                }
            }

            // 我们的 Boss 的影子 Player 不在 `CombatState.Players` 里 ⇒ 从敌方生物上补齐
            var enemies = combat.Enemies;
            if (enemies != null)
            {
                foreach (var c in enemies)
                {
                    if (c?.Player == null) continue;
                    if (!list.Contains(c.Player)) list.Add(c.Player);
                }
            }
        }
        catch { /* 忽略 */ }
        return list;
    }
}
