using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Players;

namespace CharacterBosses.Core;

/// <summary>
/// Boss 自己的牌堆操作。
///
/// 为什么不用原版的 <c>CardPileCmd.Draw</c> / <c>Shuffle</c>：那些会走「本地玩家」的动画与 UI 路径，
/// 而 Boss 的牌堆挂在影子 Player 上、没有对应的界面节点。这里直接操作 <see cref="CardPile"/> 的
/// internal 增删（<c>silent: true</c>），只动数据不出动画。
/// </summary>
public static class BossPiles
{
    /// <summary>把手牌全部丢进弃牌堆（回合结束时用）。</summary>
    public static void DiscardHand(PlayerCombatState pcs)
    {
        foreach (var card in pcs.Hand.Cards.ToList())
        {
            pcs.Hand.RemoveInternal(card, true);
            pcs.DiscardPile.AddInternal(card, -1, true);
        }
    }

    /// <summary>
    /// 把手里**除 token 小刀以外**的牌丢掉。
    ///
    /// 为什么单独留一手：静默猎手的剧本每回合会重建手牌，而「袖里乾坤」是在**玩家回合**里
    /// 往她手上塞 3 张小刀的 —— 照原样清空就白塞了（用户设计里那 3 张是要打出来的）。
    /// </summary>
    public static void DiscardHandExceptTokens(PlayerCombatState pcs)
    {
        foreach (var card in pcs.Hand.Cards.ToList())
        {
            if (ShivPlays.IsShiv(card)) continue;
            pcs.Hand.RemoveInternal(card, true);
            pcs.DiscardPile.AddInternal(card, -1, true);
        }
    }

    /// <summary>抽 N 张；抽牌堆空了就先把弃牌堆洗回去。</summary>
    public static void Draw(PlayerCombatState pcs, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (pcs.DrawPile.IsEmpty) Reshuffle(pcs);
            if (pcs.DrawPile.IsEmpty) return;

            var card = pcs.DrawPile.Cards[0];
            pcs.DrawPile.RemoveInternal(card, true);
            pcs.Hand.AddInternal(card, -1, true);
        }
    }

    private static void Reshuffle(PlayerCombatState pcs)
    {
        foreach (var card in pcs.DiscardPile.Cards.ToList())
        {
            pcs.DiscardPile.RemoveInternal(card, true);
            pcs.DrawPile.AddInternal(card, -1, true);
        }
    }
}
