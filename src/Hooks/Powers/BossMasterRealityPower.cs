#if STS2_111   // 观者 Boss 只在 0.113 构建里（用户：107 不加这个 Boss）
using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace CharacterBosses.Powers;

/// <summary>
/// 二层 1 号观者 Boss「**印牌观**」的特殊效果「**操控现实**」（用户 2026-11-16）。
///
/// 用户原话：
/// > 「操控现实：观者通过色彩哲学家获得了武器库、创世之柱和何人僭越。每当**你**在战斗中生成卡牌时，
/// >   其会被**降级**；如果不是衍生牌，还会获得**消耗**。」
///
/// ⇒ 这是**对玩家的削弱**（面板也写着「特殊效果类型：单体」）：
///   · 任何**真人玩家**在战斗中新生成的牌（「改造现实」印的惩恶、「欺瞒现实」印的平安、
///     夜魇复制、战术大师…任何来源）⇒ **降级**（`DowngradeInternal()`，把升级版退回去）；
///   · 而且**只要它不是衍生牌**（`CardRarity.Token` 之外的牌）⇒ 再挂一条**消耗**（`CardKeyword.Exhaust`）。
///
/// ⚠ 观者模组自己也有一个同名 power（`WATCHER-MASTER_REALITY_POWER`，效果是「生成牌被**升级**」），
///   那是**给玩家用的**卡「操控现实」；我们这只是它的**反面**，两者互不相干（玩家别指望装了就抵消）。
///
/// ⚠ 触发点用的是原版钩子 `AfterCardGeneratedForCombat(card, creator)` —— 与本体「武器库」
///   `ArsenalPower`、「创世之柱」`PillarOfCreationPower` 同一个钩子（反编译确认），
///   所以只要玩家那边生成走原版管线就一定吃得到。
/// </summary>
public sealed class BossMasterRealityPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    /// <summary>没有层数概念 ⇒ 不显示数字。</summary>
    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    public override async Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
    {
        try
        {
            if (card == null) return;
            var boss = OwnerMonster();
            if (boss == null) { await base.AfterCardGeneratedForCombat(card, creator); return; }

            // 只管**真人玩家**生成的牌：她自己剧本造的牌、她印的 token 都不受影响。
            if (creator == null) return;
            var shadow = boss.Shadow;
            if (shadow != null && ReferenceEquals(creator, shadow)) return;

            bool downgraded = false;
            try
            {
                if (card.IsUpgraded)
                {
                    card.DowngradeInternal();
                    downgraded = true;
                }
            }
            catch (Exception e) { Logger.Log("[CharBoss] 操控现实：降级失败: " + e.Message); }

            bool exhausted = false;
            try
            {
                // 「不是衍生牌」⇒ 才加消耗。衍生牌 = `CardRarity.Token`（惩恶/平安/小刀/奇迹这类）。
                if (card.Rarity != CardRarity.Token)
                {
                    card.AddKeyword(CardKeyword.Exhaust);
                    exhausted = true;
                }
            }
            catch (Exception e) { Logger.Log("[CharBoss] 操控现实：加消耗失败: " + e.Message); }

            if ((downgraded || exhausted) && _logs++ < 12)
            {
                Logger.Log($"[CharBoss] 操控现实：玩家生成了 {card.Id?.Entry}" +
                           $"（{(downgraded ? "已降级" : "未升级")}、{(exhausted ? "附加消耗" : "衍生牌不加消耗")}）");
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 操控现实：处理生成牌失败: " + e.Message); }
        await base.AfterCardGeneratedForCombat(card, creator);
    }

    private static int _logs;

    /// <summary>
    /// **印牌触发**（用户：「每当她打出改造现实和欺瞒现实时都会“印牌”，此时要触发创世之柱和武器库」）。
    ///
    /// 为什么必须手动结算：这两张牌原版会「往手牌里塞一张惩恶/平安」，而那条生成被我们
    /// 用 `CharBossMonster.BlocksGeneratedCardsEntries` **拦掉了**（剧本里本来就写着那两张，
    /// 不拦会多一份）⇒ 原版 `AfterCardGeneratedForCombat` 也就不会派发 ⇒
    /// 「创世之柱」（+格挡）与「武器库」（+力量）这两条副作用会整条丢掉。
    ///
    /// 所以这里等价地补一次：
    ///   · `PillarOfCreationPower` 在身 ⇒ `CreatureCmd.GainBlock(她, Amount, Unpowered)`
    ///   · `ArsenalPower` 在身       ⇒ `PowerCmd.Apply&lt;StrengthPower&gt;(她, Amount)`
    /// （两者都是本体牌的 power，`Amount` 就是牌上写的 2 / 1。）
    /// </summary>
    internal static async Task TriggerPrintAsync(Monsters.CharBossMonster? boss, string because)
    {
        try
        {
            var creature = boss?.Creature;
            if (creature == null) return;

            var pillar = creature.GetPower<PillarOfCreationPower>();
            if (pillar != null && pillar.Amount > 0)
            {
                await CreatureCmd.GainBlock(creature, pillar.Amount, ValueProp.Unpowered, null);
            }

            var arsenal = creature.GetPower<ArsenalPower>();
            if (arsenal != null && arsenal.Amount > 0)
            {
                await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), creature,
                                                    arsenal.Amount, creature, null, false);
            }

            if (pillar != null || arsenal != null)
            {
                Logger.Log($"[CharBoss] 印牌（{because}）⇒ " +
                           $"创世之柱 +{(pillar != null ? pillar.Amount.ToString("0.##") : "0")} 格挡、" +
                           $"武器库 +{(arsenal != null ? arsenal.Amount.ToString("0.##") : "0")} 力量");
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 印牌触发失败: " + e.Message); }
    }

    private Monsters.CharBossMonster? OwnerMonster()
    {
        try { return Owner?.Monster as Monsters.CharBossMonster; }
        catch { return null; }
    }
}
#endif
