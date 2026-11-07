using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Core;

namespace CharacterBosses.Powers;

/// <summary>
/// 二层「**王储**」（储君 · 王权轴，Key `REGENT_KING`）的特殊效果「**征服者**」。
///
/// 用户 2026-11-02 改稿（取代原来的「王之凝视 / 独白」两半）：
/// > 更改王储特殊效果：征服者：每当储君将一张牌放回抽牌堆顶部时，
/// > 所有玩家打出的下一张攻击牌的伤害减少 50%。增加 debuff：麻痹。
///
/// 实现：她每**把一张牌放回抽牌堆顶部**（＝她这套牌里的「返顶」效果），就给每个真人玩家
/// 施加 1 层 <see cref="ParalysisPower"/>（「麻痹」）。减伤幅度与失效时机都在那层 power 里做。
///
/// 判据为什么按「卡」而不按「牌堆事件」：把牌放到堆顶在原版是**卡牌自己的效果**
/// （`CardPileCmd.Add(card, PileType.Draw, 0)`），而「抽牌堆洗回」这类事件同样会触发
/// `AfterCardChangedPiles` —— 那样她每次洗牌都会白给玩家上麻痹（明显不对）。
/// 所以这里认**她这套牌里会返顶的那几张**：
///   · `PHOTON_CUT` 光子切割（抽 1，然后把一张手牌放到抽牌堆顶）；
///   · `COSMIC_INDIFFERENCE` 宇宙冷漠（把弃牌堆的一张牌放到抽牌堆顶）；
///   · `SHINING_STRIKE` 明耀打击 / `GLIMMER` 微光 —— 她现在的剧本没用，留着以防以后加进来。
/// 以后剧本里再出现别的「返顶」牌，往 <see cref="ReturnToTopCards"/> 里加一行即可。
/// </summary>
public sealed class BossConquerorPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    /// <summary>层数不随人数放大：我们对每个玩家各施加 1 层，自己控制总量。</summary>
    public override bool ShouldScaleInMultiplayer => false;

    public override bool ShouldPlayVfx => false;

    /// <summary>每次触发给几层「麻痹」。</summary>
    private const int PerTrigger = 1;

    /// <summary>会把牌放回抽牌堆顶部的牌（她这套牌里实际用到的：光子切割、宇宙冷漠）。</summary>
    private static readonly HashSet<string> ReturnToTopCards = new(StringComparer.Ordinal)
    {
        "PHOTON_CUT", "COSMIC_INDIFFERENCE", "SHINING_STRIKE", "GLIMMER",
    };

    internal static bool IsReturnToTopCard(CardModel? card)
    {
        if (card == null) return false;
        try { return ReturnToTopCards.Contains(card.Id.Entry); }
        catch { return false; }
    }

    private int _logs;

    /// <summary>她打出「返顶」牌 → 所有玩家各得 1 层麻痹。</summary>
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var self = Owner;
            if (self == null || !self.IsAlive) return;
            var card = cardPlay?.Card;
            if (card == null) return;
            if (!ReferenceEquals(card.Owner?.Creature, self)) return;   // 只认她自己打出的牌
            if (!IsReturnToTopCard(card)) return;

            var ctx = choiceContext ?? new BlockingPlayerChoiceContext();
            int hit = await ParalyzePlayers(ctx);

            if (_logs++ < 30)
            {
                string entry;
                try { entry = card.Id.Entry; } catch { entry = "?"; }
                Logger.Log($"[CharBoss] 征服者：她打出「{entry}」（返顶）→ {hit} 名玩家各得 " +
                           $"{PerTrigger} 层「麻痹」");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 征服者（返顶 → 麻痹）结算失败: " + e.Message);
        }
    }

    /// <summary>所有**真人玩家**各得 1 层麻痹。</summary>
    private async Task<int> ParalyzePlayers(PlayerChoiceContext ctx)
    {
        int hit = 0;
        foreach (var player in RealPlayers())
        {
            var creature = player?.Creature;
            if (creature == null || !creature.IsAlive) continue;
            if (Owner != null && ReferenceEquals(creature, Owner)) continue;

            await PowerCmd.Apply<ParalysisPower>(ctx, creature, PerTrigger, Owner, null, false);
            hit++;
        }
        return hit;
    }

    private List<Player> RealPlayers()
    {
        try { return BossLockOn.RealPlayers(Owner?.CombatState); }
        catch { return new List<Player>(); }
    }
}
