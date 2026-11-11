using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Core;

namespace CharacterBosses.Patches;

/// <summary>
/// 三层储君 3 号「**抉择储**」的特殊效果「**指导**」的落地（用户 2026-11-10 设计）：
///
/// > 每当储君打出「抉择，抉择」时，你替他选择要被重复打出的牌。
/// > 他打出抉择抉择时要用类似知识恶魔的选择界面，不过会有 5 张牌；
/// > 与知识恶魔相同，被他锁定的玩家选完后他才会打后面的牌。
///
/// 反编译「抉择，抉择」（<c>DecisionsDecisions.OnPlay</c>）：
/// <code>
/// await CardPileCmd.Draw(ctx, DynamicVars.Cards.IntValue, Owner);      // 抽 Cards（+ = 5）张
/// var picked = await CardSelectCmd.FromHand(ctx, Owner, prefs,          // ← 就是这一次选牌
///                  c =&gt; c.Type == Skill &amp;&amp; !c.Keywords.Contains(Unplayable), this);
/// for (int i = 0; i &lt; DynamicVars.Repeat.IntValue; i++)                 // 重复打出 3 次
///     await CardCmd.AutoPlay(ctx, picked, null);
/// </code>
/// 她出牌期间栈顶压着 <see cref="BossCardSelector"/>（自动选择器）⇒ 这一次选牌会被**自动**挑走。
/// 所以这里只接管「这一次选牌」，条件是「发起来源是抉择，抉择 + 选牌的是我们的影子玩家」：
///   · 用知识恶魔那套 <c>CardSelectCmd.FromChooseACardScreen</c> 把候选牌摆给**被锁定的玩家**，
///     并 await 他选完（她这一手就此暂停，后面的牌才会继续打 —— 与知识恶魔一致）；
///   · 选之前把自动选择器**临时摘下来**（<c>SuspendSelectorForTest</c>），否则原版会走选择器、
///     界面根本不会出现；
///   · 把玩家挑的那张记进 <see cref="ChoiceRegent"/>（剧本里「只有挑了星位序列才打得出的天际钻头」靠它）。
///
/// 其它情况（真人玩家自己打抉择，抉择 / 别的牌要选牌）一律 `return true` 走原版，零回归。
/// </summary>
[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHand),
    new[]
    {
        typeof(PlayerChoiceContext), typeof(Player), typeof(CardSelectorPrefs),
        typeof(Func<CardModel, bool>), typeof(AbstractModel),
    })]
internal static class ChoiceRegentPickPatch
{
    private static bool Prefix(PlayerChoiceContext context, Player player, CardSelectorPrefs prefs,
                               Func<CardModel, bool> filter, AbstractModel source,
                               ref Task<IEnumerable<CardModel>> __result)
    {
        try
        {
            // ① 必须是「抉择，抉择」发起的那次选牌
            if (!IsDecisionsDecisions(source)) return true;

            // ② 选牌的必须是**我们的**影子玩家，而且这只 Boss 是「抉择储」
            var boss = player?.Creature?.Monster as Monsters.CharBossMonster;
            if (boss == null || !ChoiceRegent.IsChoiceBoss(boss)) return true;

            // ③ 候选牌 = 她手里通过原版过滤的那些（技能牌、非不可打出）
            var candidates = new List<CardModel>();
            try
            {
                var hand = player!.PlayerCombatState?.Hand?.Cards;
                if (hand != null)
                {
                    foreach (var card in hand)
                    {
                        if (card == null) continue;
                        bool ok;
                        try { ok = filter == null || filter(card); } catch { ok = false; }
                        if (ok) candidates.Add(card);
                    }
                }
            }
            catch { /* 取不到就退回原版 */ }
            if (candidates.Count == 0)
            {
                Logger.Log("[CharBoss] 指导：候选牌为空 ⇒ 退回原版选牌（自动选择器）");
                return true;
            }

            // ④ 谁来选：多人时是**被她锁定的那位**（用户：「被他锁定的玩家选完后他才会打后面的牌」）
            var chooser = ResolveChooser(boss);
            if (chooser == null)
            {
                Logger.Log("[CharBoss] 指导：找不到可选的真人玩家 ⇒ 退回原版选牌");
                return true;
            }

            __result = ChooseAsync(context, candidates, chooser, boss);
            return false;
        }
        catch (Exception e)
        {
            try { Logger.Log("[CharBoss] 指导：接管选牌失败，放行原版: " + e.Message); } catch { /* 忽略 */ }
            return true;
        }
    }

    /// <summary>把候选牌摆给玩家、等他选完；返回只含那一张的序列（「抉择，抉择」只取第一张）。</summary>
    private static async Task<IEnumerable<CardModel>> ChooseAsync(PlayerChoiceContext context,
                                                                 List<CardModel> candidates,
                                                                 Player chooser,
                                                                 Monsters.CharBossMonster boss)
    {
        try
        {
            string listed = string.Join(",", candidates.Select(SafeEntry));
            Logger.Log($"[CharBoss] 指导：储君打出「抉择，抉择」⇒ 让 {SafeName(chooser)} 从 {candidates.Count} 张里挑一张" +
                       $"（{listed}）");
        }
        catch { /* 日志失败不影响流程 */ }

        CardModel? picked = null;
        try
        {
            // ⚠ 她出牌期间栈顶压着自动选择器（`BossCardSelector`）：原版 `FromChooseACardScreen` 开头就是
            //   `if (Selector != null) ⇒ 用选择器`，不摘下来界面根本不会出现。摘完记得还回去。
            using (CardSelectCmd.SuspendSelectorForTest())
            {
                picked = await CardSelectCmd.FromChooseACardScreen(context, candidates, chooser, false);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 指导：选择界面出错，退回第一张: " + e.Message);
        }

        if (picked == null)
        {
            picked = candidates[0];
            Logger.Log($"[CharBoss] 指导：玩家没给出选择 ⇒ 退回第一张 {SafeEntry(picked)}");
        }

        try { ChoiceRegent.NotePick(boss, boss.TurnNumber, picked.GetType()); }
        catch { /* 忽略 */ }

        return new List<CardModel> { picked };
    }

    /// <summary>被锁定的玩家；拿不到就用第一个真人玩家。</summary>
    private static Player? ResolveChooser(Monsters.CharBossMonster boss)
    {
        try
        {
            var locked = BossLockOn.TargetFor(boss);
            if (locked != null) return locked;
        }
        catch { /* 继续兜底 */ }
        try
        {
            var players = BossLockOn.RealPlayers(boss.CombatState);
            if (players != null && players.Count > 0) return players[0];
        }
        catch { /* 忽略 */ }
        return null;
    }

    private static bool IsDecisionsDecisions(AbstractModel? source)
    {
        try
        {
            if (source == null) return false;
            if (source is MegaCrit.Sts2.Core.Models.Cards.DecisionsDecisions) return true;
            return string.Equals(source.Id.Entry, "DECISIONS_DECISIONS", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }

    private static string SafeName(Player? player)
    {
        try { return player?.Creature?.Name ?? "?"; } catch { return "?"; }
    }
}
