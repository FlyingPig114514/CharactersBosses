using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 遗物「**情感芯片**」（<c>EmotionChip</c>，用户 2026-11-03 给三层「事件机」加的）的手动结算：
///
/// > 如果你在上一个回合失去过生命，在你的回合开始时触发**所有充能球**的被动。
///
/// 原版实现（反编译 `EmotionChip.&lt;AfterPlayerTurnStart&gt;d__5`，逐行核对过）：
/// <code>
/// if (player != Owner) return;
/// if (!LostHpInPreviousTurn) return;
/// foreach (var orb in Owner.PlayerCombatState.OrbQueue.Orbs)
///     await OrbCmd.Passive(choiceContext, orb, null, countAffectedByHooks: true);
///     await Cmd.Wait(0.25f);
/// // LostHpInPreviousTurn = History 里存在 DamageReceivedEntry：
/// //     e.Receiver == Owner.Creature &amp;&amp; !e.Result.WasFullyBlocked &amp;&amp; e.HappenedLastPlayerTurn(Owner)
/// </code>
/// 关键在最后那一句：`HappenedLastPlayerTurn(Owner)` 比的是**玩家回合号**，而她的回合号是
/// 我们手动对齐的 ⇒ 那条判定对影子玩家从来不成立（用户 2026-11-03 报「把情感芯片的功能实现」）。
///
/// <para>
/// ★ v0.2.357（用户 2026-11-04：「情感芯片没有正确的在它回合开始时触发它所有充能球的被动」）：
/// 原来这里**只**靠 `HpAtOwnTurnStart` 那一份生命快照做比较，而且**任何一步不满足都静默 return**
/// —— 日志里只有外面那句 `步骤：情感芯片完成`，看不出到底卡在哪一步（实测确实一次都没触发过）。
/// 现在改成：
///   ① 判据**两条并联**：生命快照差 **或者** 战斗历史里「她从上一个回合开始有没有真的掉过血」
///      （历史那条不依赖快照，快照万一被别处重置也不会漏）；
///   ② **每一步都打日志**（快照/当前生命/历史条数/有没有这件遗物/有几颗球），下次一眼就能定位；
///   ③ 触发时把每颗球的**被动数值**一起打出来（`PassiveVal`），避免「触发了但因为是 0 所以看不出来」。
/// </para>
///
/// 原版那条 relic 钩子对我们这只 Boss 会被 <c>Patches/EmotionChipBossTakeoverPatch</c> 关掉，
/// 避免同一次掉血触发两遍。
/// </summary>
internal static class EmotionChipPlays
{
    /// <summary>她回合开始时跑一次（`CharBossMonster.PlayPlannedCards` 里、出牌之前）。</summary>
    public static async Task RunAtOwnTurnStart(CharBossMonster boss, PlayerChoiceContext ctx)
    {
        if (boss?.Creature == null || boss.Creature.IsDead) return;

        int hpNow = boss.Creature.CurrentHp;
        int hpBefore = boss.HpAtOwnTurnStart;
        // 先记下本回合的基线（不管有没有芯片，下一回合都要用）
        boss.HpAtOwnTurnStart = hpNow;

        bool hasChip = HasEmotionChip(boss);
        int lostByHp = hpBefore > 0 ? Math.Max(0, hpBefore - hpNow) : 0;

        // ② 历史那条判据（她自己那一侧的「没被完全格挡的受伤」）
        int lostHits = CountDamageTakenSinceLastTurn(boss);
        bool lostByHistory = lostHits > 0;
        // 不管这一回合触不触发，游标都要推到当前末尾 —— 否则下一次会把两回合前那笔再算一次。
        MarkHistoryConsumed(boss);

        int orbsCount = 0;
        try { orbsCount = boss.Shadow?.PlayerCombatState?.OrbQueue?.Orbs?.Count ?? 0; }
        catch { orbsCount = 0; }

        Logger.Log($"[CharBoss] 情感芯片判定：遗物={hasChip} 生命 {hpBefore}→{hpNow}（快照少 {lostByHp}）" +
                   $" 历史受伤 {lostHits} 条 充能球 {orbsCount} 颗");

        if (!hasChip)
        {
            Logger.Log("[CharBoss] 情感芯片：她身上没有这件遗物 ⇒ 不触发");
            return;
        }
        if (hpBefore <= 0)
        {
            // 第 1 回合：没有「上一个回合」可比，快照也刚建好
            if (!lostByHistory)
            {
                Logger.Log("[CharBoss] 情感芯片：这是她第 1 个回合（还没有上一个回合可比）⇒ 不触发");
                return;
            }
        }
        if (lostByHp <= 0 && !lostByHistory)
        {
            Logger.Log("[CharBoss] 情感芯片：上一个回合她没掉过血 ⇒ 不触发");
            return;
        }

        // 触发之后历史游标已经在上面推到末尾了（避免下一次重复计数）

        try
        {
            var orbs = boss.Shadow?.PlayerCombatState?.OrbQueue?.Orbs;
            if (orbs == null || orbs.Count == 0)
            {
                Logger.Log("[CharBoss] 情感芯片：上个回合掉了血，但身上没有充能球（无被动可触发）");
                return;
            }

            var snapshot = orbs.ToList();     // 触发过程中队列可能变（玻璃球之类），先拍快照
            Logger.Log($"[CharBoss] 情感芯片：上个回合掉了血（快照 {lostByHp} / 历史 {lostHits} 条）" +
                       $" ⇒ 逐颗触发 {snapshot.Count} 颗充能球的被动");
            int index = 0;
            foreach (var orb in snapshot)
            {
                index++;
                if (orb == null) continue;
                try
                {
                    int passive = 0;
                    try { passive = (int)Math.Round(orb.PassiveVal); } catch { passive = 0; }
                    Logger.Log($"[CharBoss] 情感芯片：第 {index} 颗 {SafeTitle(orb)}（PassiveVal={passive}）触发中…");
                    await CharacterBosses.Core.GameCompatExtra.OrbPassive(ctx, orb, null);
                    Logger.Log($"[CharBoss] 情感芯片：第 {index} 颗 {SafeTitle(orb)} 被动已触发");
                }
                catch (Exception e)
                {
                    Logger.Log($"[CharBoss] 情感芯片：第 {index} 颗球的被动触发失败: {e}");
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 情感芯片结算失败: " + e);
        }
    }

    /// <summary>
    /// 「情感芯片」**会不会**在她的下一个回合开始时再触发一次球被动
    /// （= 她带着这件遗物，而且这次回合里已经掉过血）。
    ///
    /// 用途：手牌栏那条「充能球被动」独立意图要显示**接下来这一整个敌方回合**的真实伤害
    /// —— 情感芯片会让每颗球的被动多触发一次，所以那里要按翻倍显示
    /// （用户 2026-11-03：「会触发情感芯片时，应该正确显示充能球被动会造成的实际伤害（可能翻倍）」）。
    /// </summary>
    internal static bool WillTriggerAtOwnTurnStart(CharBossMonster? boss)
    {
        try
        {
            if (boss?.Creature == null || boss.Creature.IsDead) return false;
            if (!HasEmotionChip(boss)) return false;

            // 与 RunAtOwnTurnStart 同一套并联判据
            int baseline = boss.HpAtOwnTurnStart;
            if (baseline > 0 && boss.Creature.CurrentHp < baseline) return true;
            return CountDamageTakenSinceLastTurn(boss) > 0;
        }
        catch { return false; }
    }

    private static bool HasEmotionChip(CharBossMonster boss)
    {
        try
        {
            var relics = boss.Shadow?.Relics;
            for (int i = 0; i < (relics?.Count ?? 0); i++)
            {
                var relic = relics![i];
                if (relic == null) continue;
                // 双重判据：类型判定（最稳）+ entry 判定（老写法，留着兜底）
                if (relic is EmotionChip) return true;
                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry == "EMOTION_CHIP") return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    /// <summary>
    /// 从「她上一个回合开始」到现在，她**真的掉过几次血**（战斗历史里 `DamageReceivedEntry`）。
    ///
    /// 为什么用它兜底：原版 `LostHpInPreviousTurn` 靠 `HappenedLastPlayerTurn(Owner)` 比玩家回合号，
    /// 对影子玩家不成立；而生命快照那条如果被别处重置/漏记就整条失效。
    /// 历史只按「受伤者是不是她的生物、有没有被完全格挡」筛，不受回合号影响。
    /// </summary>
    private static int CountDamageTakenSinceLastTurn(CharBossMonster boss)
    {
        try
        {
            var creature = boss.Creature;
            var history = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.History;
            if (creature == null || history?.Entries == null) return 0;

            int total = 0;
            foreach (var _ in history.Entries) total++;
            int from = Math.Max(0, boss.EmotionChipHistoryMark);
            if (from > total) from = 0;      // 历史被清过（换战斗）⇒ 从头数

            int hits = 0;
            int index = 0;
            foreach (var entry in history.Entries)
            {
                int here = index++;
                if (here < from) continue;
                if (entry is not MegaCrit.Sts2.Core.Combat.History.Entries.DamageReceivedEntry e) continue;
                try
                {
                    if (!ReferenceEquals(e.Receiver, creature)) continue;
                    if (e.Result.WasFullyBlocked) continue;
                    if (e.Result.UnblockedDamage <= 0) continue;
                    hits++;
                }
                catch { /* 单条读不出来就跳过 */ }
            }
            return hits;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 情感芯片：读战斗历史失败: " + e.Message);
            return 0;
        }
    }

    /// <summary>把「历史游标」推到当前末尾（本轮受伤已经算过，别重复计数）。</summary>
    private static void MarkHistoryConsumed(CharBossMonster boss)
    {
        try
        {
            var entries = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.History?.Entries;
            if (entries == null) return;
            int total = 0;
            foreach (var _ in entries) total++;
            boss.EmotionChipHistoryMark = total;
        }
        catch { /* 忽略 */ }
    }

    private static string SafeTitle(OrbModel orb)
    {
        try { return orb.Title?.ToString() ?? orb.GetType().Name; }
        catch { return orb?.GetType().Name ?? "?"; }
    }
}
