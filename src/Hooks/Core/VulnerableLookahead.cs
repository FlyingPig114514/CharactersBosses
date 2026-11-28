#if STS2_111
using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// **「同回合、前面的 Boss 会先给我挂易伤」的前瞻**（用户 2026-11-28）。
///
/// ## 用户口径（原话）
/// > 原版所有有施加易伤意图的怪都是最后一个行动，这是为了避免「假意图」。而部分亡灵契约师、铁甲战士和储君
/// > boss 都会使用施加易伤的卡牌，有的还是群体易伤，这就会导致在一个 boss 施加易伤后后面 boss 的伤害意图
/// > 预测的是不准的。比如养手骨和巨石战会在第四回合分别打出击掌（群体易伤）和彼岸咆哮（群攻），
/// > 导致彼岸咆哮的伤害预览因为没有计算易伤而偏低。你尝试让施加易伤意图后的后续 boss 的攻击考虑新施加的易伤
///
/// ## 为什么需要它
/// 意图里的伤害数字走的是原版伤害管道（<see cref="BossDamagePredictor.FinalDamageFor"/> →
/// `GameCompat.ModifyDamage`），读的是**当前**的能力状态 ⇒ 它只知道「现在有没有易伤」，
/// 不知道「同一个敌方回合里，排在前面那只 Boss 待会儿会先给我挂一层」。
///
/// ## 算法（正向模拟一遍敌方行动次序）
/// ```
/// 现场状态：已经带易伤的真人玩家先进集合
/// for Boss in 行动次序里排在我前面的每一只 Boss：      // 次序 = CombatState.Enemies 的顺序 = 站位/行动次序
///     for 这张 Boss 本回合计划要打的每一张牌：
///         · 苦难（MISERY）：把「被它锁定的那位」身上的负面逐种复制给其他玩家
///                           ⇒ 如果被锁定那位此时会带易伤，其他玩家也会被「传染」易伤
///         · 会挂易伤的牌（`DynamicVars.Vulnerable` 存在，且不是 `TargetType.Self` 打自己）：
///             - 群攻（`TargetType.AllEnemies`，例如击掌）⇒ 所有玩家都会带上
///             - 单体（例如痛击）⇒ 只有它计划打的那个目标会带上
/// 结论：目标玩家在「轮到我出手」那一刻会带易伤、而**现在没有** ⇒ 这一记攻击的预览 ×1.5（有摧残 ×2）
/// ```
///
/// ## 刻意不处理的几种（保持原样，绝不乱加）
/// · **无来源伤害**（充能球、灾厄、血肉戏法…）：它们根本不走伤害管道，也就调不到这里 ⇒ 天然不受易伤影响；
/// · 目标**现在就已经带易伤**：原版管道已经把它算进去了，这里再乘就重复了；
/// · 目标不是真人玩家（Boss 的影子玩家 / 召唤物）⇒ 一律 1.0；
/// · dealer 不是我们的 Boss（原版怪物 / 别的 mod）⇒ 一律 1.0，一个字节都不动。
/// </summary>
internal static class VulnerableLookahead
{
    /// <summary>召唤物/影子玩家这些不算「玩家」，不给它们做易伤前瞻。</summary>
    private const string DebilitateEntry = "DEBILITATE_POWER";   // 摧残：虚弱/易伤的效果翻倍

    private static int _logs;

    /// <summary>
    /// 预览倍率：`1.0`（不调整）/ `1.5`（会先吃到一层易伤）/ `2.0`（会先吃到易伤且身上有摧残）。
    /// 直接乘在**没取整之前**的伤害值上（调用方拿到的 decimal 还没 floor），所以不会因为取整而少 1 点。
    /// </summary>
    internal static decimal MultiplierFor(Creature? dealer, Creature? victim, CardModel? card)
    {
        try
        {
            if (dealer == null || victim?.CombatState == null) return 1m;
            if (card == null) return 1m;

            // 只对真人玩家做前瞻（影子玩家 = 我们的 Boss 自己；召唤物替人挨打，不是「谁会被挂易伤」）
            if (victim.Monster != null || victim.Player == null) return 1m;

            var attacker = dealer.Monster as CharBossMonster;
            if (attacker == null) return 1m;                       // 原版怪物：不动

            if (BossDamagePredictor.HasVulnerable(victim)) return 1m;   // 现在就有 ⇒ 管道已算

            if (!WillGainVulnerable(attacker, victim)) return 1m;

            bool debilitate = PowerAmount(victim, DebilitateEntry) > 0;
            decimal mult = debilitate ? 2.0m : 1.5m;

            if (_logs++ < 24)
                Logger.Log($"[CharBoss] 易伤前瞻：{NameOf(card)} 的伤害预览 ×{mult}" +
                           $"（{BossLockOn.DescribeTarget(victim.Player)} 现在没有易伤，" +
                           $"但排在 {EntryOf(attacker)} 前面的 Boss 会先给它挂上；摧残={debilitate}）");
            return mult;
        }
        catch { return 1m; }
    }

    /// <summary>目标玩家会不会在「排在我前面的那些 Boss 都打完」之后带上易伤。</summary>
    private static bool WillGainVulnerable(CharBossMonster attacker, Creature victim)
    {
        var combat = attacker.CombatState;
        if (combat?.Enemies == null) return false;

        // 行动次序 = CombatState.Enemies 的顺序（= 站位次序，见 BossEnemySpreadPatch 的 PlacementRank）
        var bosses = new List<CharBossMonster>();
        foreach (var e in combat.Enemies)
            if (e?.Monster is CharBossMonster b && b.Creature != null) bosses.Add(b);

        int myIndex = -1;
        for (int i = 0; i < bosses.Count; i++)
            if (ReferenceEquals(bosses[i], attacker)) { myIndex = i; break; }
        if (myIndex <= 0) return false;                            // 我是第一个行动的 ⇒ 前面没人

        // 现场状态：已经带易伤的真人玩家先记上（这些人的下场攻击照旧由原版管道算）
        var vuln = new HashSet<Creature>();
        foreach (var p in BossLockOn.RealPlayers(combat))
            if (p?.Creature != null && BossDamagePredictor.HasVulnerable(p.Creature)) vuln.Add(p.Creature);

        for (int i = 0; i < myIndex; i++)
        {
            var boss = bosses[i];
            var plays = boss.Plan.Count > 0 ? boss.Plan : boss.DisplayPlays;
            foreach (var play in plays)
            {
                var card = play?.Card;
                if (card == null) continue;

                // 苦难（血肉骨）：把「被它锁定的那位」身上的负面逐种复制给其他玩家
                if (string.Equals(EntryOf(card), "MISERY", StringComparison.Ordinal))
                {
                    if (play!.Target != null && vuln.Contains(play.Target))
                        foreach (var p in BossLockOn.RealPlayers(combat))
                            if (p?.Creature != null) vuln.Add(p.Creature);
                    continue;
                }

                if (!AppliesVulnerable(card)) continue;

                if (HitsAllPlayers(card))
                {
                    foreach (var p in BossLockOn.RealPlayers(combat))
                        if (p?.Creature != null) vuln.Add(p.Creature);
                }
                else if (play!.Target != null)
                {
                    vuln.Add(play.Target);                          // 单体易伤：只有它计划打的那个目标
                }
            }
        }

        return vuln.Contains(victim);
    }

    /// <summary>
    /// 这张牌会不会挂易伤（不执行牌，只看它的动态变量 `Vulnerable`）。
    ///
    /// 反编译实证（`.refs\sts2_111.dll`）：
    /// <code>
    /// // Bash（痛击）：PowerCmd.Apply&lt;VulnerablePower&gt;(ctx, cardPlay.Target, DynamicVars.Vulnerable.BaseValue, ...)
    /// new PowerVar&lt;VulnerablePower&gt;(2m)          // ← 键名就是 "Vulnerable"
    /// // HighFive（击掌）：Apply&lt;VulnerablePower&gt;(ctx, CombatState.HittableEnemies, DynamicVars.Vulnerable.BaseValue, ...)
    /// : base(2, CardType.Attack, CardRarity.Uncommon, TargetType.AllEnemies)
    /// </code>
    /// 所以「有 `Vulnerable` 这个动态变量」就是「这张牌会挂易伤」的可靠判据，不用维护一份牌名清单。
    /// 打自己的那种（`TargetType.Self`）跳过 —— 那是给自己上负面，不影响玩家。
    /// </summary>
    private static bool AppliesVulnerable(CardModel card)
    {
        try
        {
            if (card.TargetType == TargetType.Self) return false;
            if (!BossDamagePredictor.HasVar(card, "Vulnerable")) return false;
            // 「消耗型」的易伤也算（例如牌面写 0 层、靠升级才有效果的），保守起见只要求变量存在
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// 这张牌是不是**打到所有玩家**（群体易伤 / 群攻）。
    /// `TargetType.AllEnemies` 是主要判据；万象斩、刀扇后的刀这类「单体牌打全体」与易伤无关，
    /// 所以这里不抄 `BossIntentBuilder.IsAoE` 的那两个特例（它们只影响伤害溅射，不挂易伤）。
    /// </summary>
    private static bool HitsAllPlayers(CardModel card)
    {
        try { return card.TargetType == TargetType.AllEnemies; }
        catch { return false; }
    }

    private static int PowerAmount(Creature? creature, string entry)
    {
        try
        {
            if (creature?.Powers == null) return 0;
            foreach (var p in creature.Powers)
            {
                if (p == null) continue;
                string e;
                try { e = p.Id?.Entry ?? string.Empty; } catch { continue; }
                if (string.Equals(e, entry, StringComparison.Ordinal)) return (int)p.Amount;
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    private static string EntryOf(CardModel? card)
    {
        try { return card?.Id?.Entry ?? "?"; } catch { return "?"; }
    }

    private static string EntryOf(CharBossMonster? boss)
    {
        try { return boss?.Creature?.Name ?? "?"; } catch { return "?"; }
    }

    private static string NameOf(CardModel? card) => EntryOf(card);
}
#endif
