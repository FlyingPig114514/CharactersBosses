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
            //    ⚠ 恐惧模式会**再打一次**「抉择，抉择」（见 CharBossMonster.RunChoiceFollowUpsAsync）：
            //      那次它自己也在手牌里、而且它本身是技能牌 ⇒ 必须把它自己排除掉，
            //      否则「从剩下的 4 张里选」会变成「4 张 + 抉择，抉择自己」；
            //      已经挑过的那张也一并排除（用户口径是「**剩下**的 4 张里选 1」）。
            var second = false;
            List<Type> alreadyPicked = new();
            try
            {
                alreadyPicked = ChoiceRegent.AllPicksFor(boss, boss.TurnNumber);
                second = alreadyPicked.Count > 0;
            }
            catch { second = false; }

            var candidates = new List<CardModel>();
            try
            {
                var hand = player!.PlayerCombatState?.Hand?.Cards;
                if (hand != null)
                {
                    foreach (var card in hand)
                    {
                        if (card == null) continue;
                        if (IsDecisionsDecisions(card)) continue;
                        if (second && alreadyPicked.Contains(card.GetType())) continue;
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

            __result = ChooseAsync(context, candidates, chooser, boss, second);
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
                                                                 Monsters.CharBossMonster boss,
                                                                 bool second)
    {
        try
        {
            string listed = string.Join(",", candidates.Select(SafeEntry));
            Logger.Log($"[CharBoss] 指导：储君打出「抉择，抉择」⇒ 让 {SafeName(chooser)} 从 {candidates.Count} 张里挑一张" +
                       $"{(second ? "（恐惧模式·第二次）" : string.Empty)}（{listed}）");
        }
        catch { /* 日志失败不影响流程 */ }

        CardModel? picked = null;
        try
        {
            // ⚠ 她出牌期间栈顶压着自动选择器：原版每个选牌入口开头都是
            //   `if (Selector != null) ⇒ 用选择器`，不摘下来界面根本不会出现。摘完记得还回去。
            //
            // ★ v0.2.640 曾改成网格界面（`FromSimpleGrid`）；用户否掉：「肯定用知识恶魔的界面，
            //   因为之前就没出过问题」⇒ 恢复知识恶魔界面（`NChooseACardSelectionScreen`），
            //   只在「候选 > 3 张」时换成我们自己发的复刻版（那个入口对 >3 张会直接 throw，
            //   而界面本身没有张数限制 —— 见 ChooseViaKnowledgeDemonAsync 的注释）。
            using (Core.SelectionCompat.SuspendForUi(chooser))
            {
                picked = candidates.Count <= 3
                    ? await CardSelectCmd.FromChooseACardScreen(context, candidates, chooser, false)
                    : await ChooseViaKnowledgeDemonAsync(context, candidates, chooser);
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

        try { ChoiceRegent.NotePick(boss, boss.TurnNumber, picked.GetType(), second); }
        catch { /* 忽略 */ }

        return new List<CardModel> { picked };
    }

    /// <summary>
    /// **知识恶魔那个选牌界面**（`NChooseACardSelectionScreen`）的「不限张数」复刻。
    ///
    /// 为什么不直接用 `CardSelectCmd.FromChooseACardScreen`：它的第一句就是
    /// <code>
    /// if (cards.Count > 3) throw new ArgumentException("Only works with less than 3 cards", "cards");
    /// </code>
    /// 只能喂 3 张以内的牌（原版 `发现` / `类星体` 那种三选一）。而用户的设计是「会有 5 张牌」，
    /// 恐惧模式再挑第二张时还剩 4 张 ⇒ 两种都超限。
    ///
    /// 但**界面本身没有张数限制**（`ShowScreen(IReadOnlyList&lt;CardModel&gt;, bool)` 随便传），
    /// 那道检查只在外层那个静态方法里。所以这里把它拆开、逐句照抄
    /// `FromChooseACardScreen` 的「非选择器」分支，只去掉开头那个判断：
    /// <list type="number">
    ///   <item>`UndoEndTurnIfNecessary(player)` —— 玩家已经点了结束回合就撤回（她这一手要等玩家点完）；</item>
    ///   <item>`ReserveChoiceId` → `SignalPlayerChoiceBegun`（多人：其它客户端进入等待）；</item>
    ///   <item>自己是本机玩家 ⇒ `NPlayerHand.CancelAllCardPlay()` + `ShowScreen` + await `CardsSelected()`
    ///         ⇒ `PlayerChoiceResult.FromIndex(下标)` + `SyncLocalChoice`（把结果广播给其它端）；</item>
    ///   <item>不是本机 ⇒ `WaitForRemoteChoice` + `AsIndex()`（等房主那边选完）；</item>
    ///   <item>`SignalPlayerChoiceEnded`。</item>
    /// </list>
    /// 视觉、交互、同步语义与原版完全一致，只是张数不再受限。
    /// </summary>
    private static async Task<CardModel?> ChooseViaKnowledgeDemonAsync(
        PlayerChoiceContext context, List<CardModel> candidates, Player chooser)
    {
        // ① 撤回「已结束回合」（照抄 CardSelectCmd.UndoEndTurnIfNecessary）
        try
        {
            var combat = MegaCrit.Sts2.Core.Combat.CombatManager.Instance;
            var state = chooser.Creature?.CombatState;
            if (combat != null && state != null
                && state.CurrentSide == MegaCrit.Sts2.Core.Combat.CombatSide.Player
                && combat.IsPlayerReadyToEndTurn(chooser))
            {
#if STS2_111
                combat.TryUndoReadyToEndTurn(chooser);
#else
                combat.UndoReadyToEndTurn(chooser);
#endif
            }
        }
        catch { /* 拿不到就跳过，界面照样弹 */ }

        var sync = MegaCrit.Sts2.Core.Runs.RunManager.Instance.PlayerChoiceSynchronizer;
        uint choiceId = sync.ReserveChoiceId(chooser);
#if STS2_111
        await context.SignalPlayerChoiceBegun(
            chooser, MegaCrit.Sts2.Core.Entities.Multiplayer.PlayerChoiceOptions.None);
#else
        await context.SignalPlayerChoiceBegun(
            MegaCrit.Sts2.Core.Entities.Multiplayer.PlayerChoiceOptions.None);
#endif
        try
        {
            // ② 本机玩家：弹界面、等他点
            if (MegaCrit.Sts2.Core.Context.LocalContext.IsMe(chooser))
            {
                try { MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand.Instance?.CancelAllCardPlay(); }
                catch { /* 只是停掉出牌动画，失败无所谓 */ }

                var screen = MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NChooseACardSelectionScreen
                    .ShowScreen(candidates, false);
                var chosen = await screen.CardsSelected();

                CardModel? first = null;
                if (chosen != null)
                {
                    foreach (var c in chosen) { first = c; break; }
                }
                int index = first == null ? -1 : candidates.IndexOf(first);
                try
                {
                    sync.SyncLocalChoice(chooser, choiceId,
                        MegaCrit.Sts2.Core.GameActions.PlayerChoiceResult
                            .FromIndex(index < 0 ? (int?)null : index));
                }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 指导：同步本地选择失败（本地结果不受影响）: " + e.Message);
                }
                return first;
            }

            // ③ 别的客户端：等那名玩家选完
            var remote = await sync.WaitForRemoteChoice(chooser, choiceId);
            int remoteIndex = remote.AsIndex();
            return (remoteIndex < 0 || remoteIndex >= candidates.Count) ? null : candidates[remoteIndex];
        }
        finally
        {
            try { await context.SignalPlayerChoiceEnded(); }
            catch { /* 忽略 */ }
        }
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
