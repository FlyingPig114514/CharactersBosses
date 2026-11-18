#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Powers;

/// <summary>
/// **红蓝观（`WATCHER_RUSHDOWN`）第 12 回合起「循环回合」的提示 buff** —— 也叫「不惧妖邪」、
/// 也用同一个图标（用户 2026-11-19 原话）：
/// > 「新增一个 buff，也叫不惧妖邪，也用不惧妖邪的图标，在且只在红蓝观 12 及之后的重复回合出现，
/// >   描述为：如果你在本回合打出攻击牌，观者会改为使用不惧妖邪进行无限。
/// >   和原本那个第四位（第 4 回合）出现的不惧妖邪是不同的 buff，不过都是起到一个提醒玩家的作用」
///
/// ## 和 <see cref="WatcherFearNoEvilAimPower"/> 的分工（**两个不同的 buff**）
/// | | `WatcherFearNoEvilAimPower`（旧的） | 本类（新的） |
/// | --- | --- | --- |
/// | 出现时机 | 她的计划里有「不惧妖邪」时（第 4 / 8 回合，以及切换段） | **第 12 回合起的每个循环回合** |
/// | 有没有实际作用 | **有**：记「被锁玩家这回合打过攻击牌」，决定她那记不惧妖邪进不进平静 | **没有**：纯提示，不参与任何结算 |
/// | 文案 | 「本回合观者会打出不惧妖邪…」 | 「如果你在本回合打出攻击牌，观者会改为使用不惧妖邪进行无限。」 |
///
/// ⚠ 第 12 回合起的回合里，旧的那个**仍然挂着**（平静判定要用它的 `PlayerAttacked`），
/// 但已被置为**不显示**（见 `WatcherFearNoEvilAimPower.IsVisibleInternal`），
/// 这样状态栏上只有一个同图标、同名字的 buff，不会出现两个一模一样的图标。
///
/// 图标：与旧 buff 同一个 PNG（`Patches.BossPowerIconPatch.DirectPathOf`）。
/// </summary>
public sealed class WatcherFearNoEvilHintPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    /// <summary>只有一层（和旧的那个一样）：它只是提示，不该堆层。</summary>
    public override PowerStackType StackType => PowerStackType.Single;

    /// <summary>只对**我们的 Boss**（她本人）放行「获得增益」特效；真人玩家身上的这一层照旧静音。</summary>
    public override bool ShouldPlayVfx
        => Owner?.Monster is Monsters.CharBossMonster;

    /// <summary>她这一轮打完就摘掉（下一轮由 `PrepareTurn` 按回合号重新决定挂不挂）。</summary>
    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext,
                                               CombatSide side,
                                               IEnumerable<Creature> participants)
    {
        try
        {
            if (side != Owner?.Side) return;
            await PowerCmd.Remove(this);
        }
        catch { /* 忽略 */ }
    }
}
#endif
