#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Core;

/// <summary>
/// **「净化+」真的把点名的牌消耗掉**（用户 2026-11-18 的三层 3 号「红蓝观」剧本：
/// 「净化+（消耗 腐朽、改造现实、防御×3）」「净化+（消耗 打击×4、防御）」
/// 「净化+（消耗 粉碎关节、碰撞连击、当头棒喝、冥想、义愤填膺）」）。
///
/// 为什么不能靠原版：本体 `Purity` 的结算走「从手牌里选最多 N 张消耗」的**选牌界面**，
/// 而她的选牌由自动选择器接管、点名牌不保证被选中 ⇒ 由剧本点名（`ScriptCard.ExhaustWith`），
/// 她打完这张牌之后我们自己把名单里的牌从**手牌 → 消耗堆**（手牌没有就依次找抽牌堆 / 弃牌堆）。
///
/// 时机：在 `CharBossMonster.PlayOne` 打完一张牌之后调（和「阿尔法/贝塔生成牌」同一个位置）。
/// </summary>
internal static class BossPurify
{
    private static int _logs;

    /// <summary>她刚打出 <paramref name="played"/> ⇒ 如果剧本给它点了「消耗名单」，就照名单消耗。</summary>
    internal static async void Handle(Monsters.CharBossMonster? boss, CardModel? played)
    {
        try
        {
            if (boss?.Shadow?.PlayerCombatState == null || played == null) return;

            var entry = FindEntry(boss, played);
            if (entry == null || entry.ExhaustWith == null || entry.ExhaustWith.Length == 0) return;

            var pcs = boss.Shadow.PlayerCombatState;

            // ★ v0.2.775（2026-11-19 日志实证：「第 8 回合的净化+ 只烧了 1/5 张」）：
            //   **先把这 5 张抓成实参**，再等 0.3 秒去烧。以前是「等完 300ms 再逐张 Find」，
            //   而这 300ms + 每张的换堆动画一旦跨过她的回合边界，下一回合的 `BuildHand`
            //   会把旧手牌整批清掉（新实例顶上）⇒ 后面几张再也找不到，只烧掉 1 张。
            //
            //   同时按「未被占用」去重：`Find` 每次都返回同类型的**第一张**，直接连续调用
            //   会拿到同一个实例三次（T6 的 防御×3 就是这种），现在跳过已经收过的那几张。
            var targets = new List<CardModel>();
            foreach (var type in entry.ExhaustWith)
            {
                if (type == null) continue;
                var card = FindUnused(pcs, type, targets);
                if (card != null) targets.Add(card);
            }

            // ★ v0.2.778（用户 2026-11-19：「能不能让三次净化+ 都有烧牌特效？现在只有最后一次有」）：
            //   **特效必须当场播，不能等那 300ms。** 以前顺序是「等 300ms → 逐张 PlayExhaustFx + Exhaust」，
            //   而第 6 / 第 8 回合的「净化+」正好是那一回合的**最后一张牌**：300ms 一过，回合已经收尾、
            //   手牌栏跟着重建 ⇒ 那几张牌的格子早没了 ⇒ `PlayExhaustFx` 找不到格子、静默什么都不做
            //   （所以只有第 11 回合那次看得到 —— 它后面还有一张「警惕」，300ms 落在回合中途）。
            //   现在**先把卡面搬到 HUD 层、把烧牌特效点着**（`PlayExhaustFx` 会把 NCard 从面板里摘走 ⇒
            //   之后再怎么重建手牌都拆不掉它），等 300ms 再走数据（`CardCmd.Exhaust`）。
            try
            {
                var panel = CharacterBosses.UI.BossHandUi.PanelFor(boss);
                int fx = 0;
                foreach (var card in targets)
                {
                    try { if (panel != null && panel.PlayExhaustFx(card)) fx++; }
                    catch (Exception e) { if (_logs++ < 10) Logger.Log("[CharBoss] 净化+：烧牌特效失败: " + e.Message); }
                }
                if (_logs++ < 20)
                    Logger.Log($"[CharBoss] 净化+：烧牌特效 {fx}/{targets.Count} 张（当场播；之后才挪数据）");
            }
            catch { /* 纯表现 */ }

            // 等净化本身结算完（约 0.3 秒）再挪数据，不然会把还在场上的牌提前收走
            try { await Task.Delay(300); } catch { /* 忽略 */ }
            var ctx = new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
            int done = 0;
            foreach (var card in targets)
            {
                try
                {
                    // ★ v0.2.773（用户 2026-11-19：「真的牌被消耗了的动画和音效」）：
                    //   ① 以前先 `RemoveInternal(card, true)` 再 `CardPileCmd.Add`。反编译 `CardPileCmd.Add`
                    //      可见它自己会 `card.RemoveFromCurrentPile(skipVisuals)` + 播换堆动画；
                    //      我们先摘一次 ⇒ `oldPile == null` ⇒ 那条动画整段走不到，卡面节点也没了。
                    //   ② 就算不先摘，原版那条换堆动画对**非本机玩家**的牌是**整段跳过**的
                    //      （`GetTweenForCardsChangingPiles`：`if (!LocalContext.IsMe(card.Owner) && …) continue;`），
                    //      而她的牌属于**影子玩家** ⇒ 永远不会飞、也不会响烧牌音效。
                    //   ⇒ 特效自己做（上面那段，照原版 `NCardExhaustQuickVfx` + `card_exhaust.mp3` 那三步），
                    //      数据走原版 `CardCmd.Exhaust`（含 History 记录 + `AfterCardExhausted` 钩子）。
                    await CardCmd.Exhaust(ctx, card, false, false);
                    done++;
                }
                catch (Exception e)
                {
                    if (_logs++ < 10) Logger.Log($"[CharBoss] 净化+：消耗 {card.GetType().Name} 失败: {e.Message}");
                }
            }

            if (_logs++ < 20)
                Logger.Log($"[CharBoss] 净化+：按剧本点名消耗了 {done}/{entry.ExhaustWith.Length} 张" +
                           $"（{string.Join(",", Array.ConvertAll(entry.ExhaustWith, t => t?.Name ?? "?"))}）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 净化+：结算失败（不影响战斗）: " + e.Message); }
    }

    /// <summary>这一回合的剧本里，哪一条对应她刚打出的这张牌、而且点了「消耗名单」。</summary>
    private static ScriptCard? FindEntry(Monsters.CharBossMonster boss, CardModel played)
    {
        try
        {
            var turn = boss.Def?.ScriptForAct(boss.ActIndex)?.TurnAt(boss.TurnNumber);
            if (turn?.Cards == null) return null;
            foreach (var entry in turn.Cards)
            {
                if (entry?.ExhaustWith == null || entry.ExhaustWith.Length == 0) continue;
                var type = MultiplayerCardSwap.Resolve(entry.Card, boss);
                if (type != null && played.GetType() == type) return entry;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>按类型找一张实例：手牌优先，其次抽牌堆，最后弃牌堆。</summary>
    private static CardModel? Find(PlayerCombatState pcs, Type type)
    {
        foreach (var c in pcs.Hand.Cards) if (c != null && c.GetType() == type) return c;
        foreach (var c in pcs.DrawPile.Cards) if (c != null && c.GetType() == type) return c;
        foreach (var c in pcs.DiscardPile.Cards) if (c != null && c.GetType() == type) return c;
        return null;
    }

    /// <summary>
    /// 同 <see cref="Find"/>，但跳过 <paramref name="taken"/> 里已经收过的实例。
    /// 剧本里写「防御×3」时三次调用必须拿到**三张不同的牌**（`Find` 每次只返回第一张）。
    /// </summary>
    private static CardModel? FindUnused(PlayerCombatState pcs, Type type, List<CardModel> taken)
    {
        foreach (var c in pcs.Hand.Cards)
            if (c != null && c.GetType() == type && !taken.Contains(c)) return c;
        foreach (var c in pcs.DrawPile.Cards)
            if (c != null && c.GetType() == type && !taken.Contains(c)) return c;
        foreach (var c in pcs.DiscardPile.Cards)
            if (c != null && c.GetType() == type && !taken.Contains(c)) return c;
        return null;
    }
}
#endif
