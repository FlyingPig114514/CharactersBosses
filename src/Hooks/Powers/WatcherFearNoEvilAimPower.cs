#if STS2_111   // 观者 Boss 只在 0.113 构建里（用户：107 不加这个 Boss）
using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Powers;

/// <summary>
/// 「**不惧妖邪**」这一手的预示 buff（用户 2026-11-16）：
/// > 「类似眼部攻击，如果观者本回合会打出不惧妖邪（第 8、12 回合），她获得一个不惧妖邪 buff，
/// >   如果本回合被她锁定的玩家打出攻击牌，观者就在本回合打出不惧妖邪时进入平静，
/// >   否则打出不惧妖邪时不改变姿态。这个 buff 1 代崩坠就有，图标就用 1 代崩坠的那个 buff 的图标。」
///
/// 所以：
///   · 规划期（玩家回合开始）她的计划里有「不惧妖邪」⇒ 挂上这个 buff（见 `CharBossMonster.PrepareTurn`）；
///   · 本回合**被她锁定的玩家**打出攻击牌 ⇒ <see cref="PlayerAttacked"/> 置 true（下面 `AfterCardPlayed`）；
///   · 她真打出「不惧妖邪」时，`Core.WatcherOmegaMechanics` 的后缀按这个标记决定要不要进「平静」；
///   · 她这一手结束 ⇒ 自己移除（`AfterSideTurnEnd`）。
///
/// 图标：`res://CharacterBosses/images/powers/watcher_fear_no_evil.png`
/// （从 1 代崩坠素材库 `.assets-sts1-downfall` 的 `WatcherFearNoEvil84.png` 导出，随我们的 PCK 发布）。
/// </summary>
public sealed class WatcherFearNoEvilAimPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    /// <summary>
    /// ★ v0.2.759（用户 2026-11-18：「让『不惧妖邪』、『眼部攻击』buff 在获得时会有她获得增益的
    /// 特效和音效」）：以前这里恒为 false（当它是「内部记账 buff」），于是玩家看不到任何获得提示。
    /// 现在只对**我们的 Boss**（她本人）放行 —— 真人玩家身上的这一层照旧静音。
    /// </summary>
    public override bool ShouldPlayVfx
        => Owner?.Monster is Monsters.CharBossMonster;

    /// <summary>本回合被她锁定的玩家**打出过攻击牌**（打「不惧妖邪」时据此决定进不进平静）。</summary>
    internal bool PlayerAttacked;

    /// <summary>记下「谁在这个 buff 生效期间打了攻击牌」——只认**被她锁定的那位真人玩家**。</summary>
    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            if (PlayerAttacked) return Task.CompletedTask;
            var card = cardPlay?.Card;
            var owner = card?.Owner;
            if (card == null || owner == null) return Task.CompletedTask;

            bool attack = false;
            try { attack = card.Type == CardType.Attack; } catch { attack = false; }
            if (!attack) return Task.CompletedTask;

            var boss = Core.WatcherOmegaMechanics.OmegaOwnerOf(this);
            if (boss == null) return Task.CompletedTask;
            if (!Core.BossLockOn.IsLocked(boss, owner)) return Task.CompletedTask;

            PlayerAttacked = true;
            Logger.Log($"[CharBoss] 不惧妖邪 buff：被锁定的 {owner.Creature?.Name} 打出了攻击牌 " +
                       $"「{SafeName(card)}」⇒ 她这一手不惧妖邪会进「平静」");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 不惧妖邪 buff 结算失败: " + e.Message); }
        return Task.CompletedTask;
    }

    /// <summary>她这一手打完就摘掉（只对本回合有效）。</summary>
    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext,
                                                MegaCrit.Sts2.Core.Combat.CombatSide side,
                                                System.Collections.Generic.IEnumerable<MegaCrit.Sts2.Core.Entities.Creatures.Creature> participants)
    {
        try
        {
            if (side != Owner?.Side) return;
            PlayerAttacked = false;
            await MegaCrit.Sts2.Core.Commands.PowerCmd.Remove(this);
        }
        catch { /* 忽略 */ }
    }

    private static string SafeName(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }
}
#endif
