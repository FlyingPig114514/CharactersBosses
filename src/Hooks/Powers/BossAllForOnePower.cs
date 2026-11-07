using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Powers;

/// <summary>
/// 三层「**事件机**」（故障机器人 · 事件牌轴，Key `DEFECT_EVENT`）的特殊效果「**一心化万**」
/// —— 用户 2026-11-03 设计稿：
///
/// > 一心化万：故障机器人的万物一心只会将弃牌堆中的 0 费攻击牌放回它的手牌。
/// > 每当它有一张牌从弃牌堆回到手牌，所有单位都获得 1 点力量。
///
/// 判定与结算都不在这层 power 上（两半各有落点）：
///   · 「只返 0 费攻击牌」= 原版 `AllForOne.Filter` 被关掉（`Patches/BossAllForOneFilterPatch`），
///     返哪几张由剧本点名（`ScriptCard.ReturnsToHand`）；
///   · 「返一张 ⇒ 所有单位 +1 力量」= `Core/EventMachineMechanics.AllForOneReturnsAsync`。
///
/// 这层 power 只负责**在状态栏上把特殊效果显示出来**（并让说明里的数字与实际一致）。
/// </summary>
public sealed class BossAllForOnePower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldScaleInMultiplayer => false;

    public override bool ShouldPlayVfx => false;
}
