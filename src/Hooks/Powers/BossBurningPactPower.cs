using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CharacterBosses.Powers;

/// <summary>
/// 二层「**杂耍战**」的特殊效果「**燃烧契约**」（用户 2026-11-02 设计稿）：
///
/// > 杂耍生成的攻击牌获得**虚无**和**消耗**。
///
/// ## 为什么只是一层「显示用」的 Power
/// 真正的机制分两处实现（Power 收不到钩子，见下）：
///   1. **拦掉原版杂耍的复制** —— <see cref="Patches.JugglingNoClonePatch"/>：
///      原版 `JugglingPower.BeforeCardPlayed` 每回合把前 3 张攻击牌各复制一张扔进手牌，
///      而影子玩家不在 `CombatState.Players` 里、这条钩子本来也可能收不到。
///   2. **剧本提前把复制牌摆进她的手牌**，并给它们加「虚无 + 消耗」 ——
///      `ScriptCard.JugglingCopy`（见 `Core/JugglerIroncladScript.cs` 的 `Copy<T>()`），
///      最后被「惊逃」`StampedePower` 在回合结束时免费打出去（`Core/StampedePlays.cs`）。
///
/// 所以这层 Power 的作用是：**让玩家在状态栏看到这条特殊效果与它的说明**（和别的特殊效果一致）。
/// </summary>
public sealed class BossBurningPactPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>纯展示：不参与任何结算，也不改任何数值。</summary>
    public override Task AfterApplied(MegaCrit.Sts2.Core.Entities.Creatures.Creature? applier,
                                      MegaCrit.Sts2.Core.Models.CardModel? cardSource)
    {
        try
        {
            Logger.Log("[CharBoss] 特殊效果已就位：燃烧契约（杂耍生成的攻击牌带「虚无 + 消耗」）");
        }
        catch { /* 忽略 */ }
        return Task.CompletedTask;
    }
}
