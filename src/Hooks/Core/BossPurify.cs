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
            // 等净化本身结算完（约 0.3 秒）再消耗，不然会把还在场上的牌提前收走、看不到动画
            try { await Task.Delay(300); } catch { /* 忽略 */ }
            int done = 0;
            foreach (var type in entry.ExhaustWith)
            {
                if (type == null) continue;
                var card = Find(pcs, type);
                if (card == null) continue;
                try
                {
                    // 从它现在所在的牌堆里摘掉，再塞进消耗堆（和原版「消耗」同一个落点）
                    if (pcs.Hand.Cards.Contains(card)) pcs.Hand.RemoveInternal(card, true);
                    else if (pcs.DrawPile.Cards.Contains(card)) pcs.DrawPile.RemoveInternal(card, true);
                    else if (pcs.DiscardPile.Cards.Contains(card)) pcs.DiscardPile.RemoveInternal(card, true);
                    // ★ v0.2.768（用户 2026-11-18：「净化+ 还是没有真的烧牌（没有烧牌的特效）」）：
                    //   以前直接 `ExhaustPile.AddInternal` —— 只是把数据挪过去，**没有任何消耗动画**。
                    //   现在走原版指令 `CardPileCmd.Add(…, PileType.Exhaust, …)`（卡面飞进消耗堆 + 烧掉特效）。
                    await CardPileCmd.Add(card, PileType.Exhaust, CardPilePosition.Bottom, null, false);
                    done++;
                }
                catch (Exception e)
                {
                    if (_logs++ < 10) Logger.Log($"[CharBoss] 净化+：消耗 {type.Name} 失败: {e.Message}");
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
}
#endif
