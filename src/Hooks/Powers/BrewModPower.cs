using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Powers;

/// <summary>
/// 三层「炼药猎」的特殊效果「**调制**」的可视载体（数值逻辑在 BossPotions / BossFairyInABottlePatch 里：
/// 8 个药水栏位、每用一瓶回 5 血、瓶中精灵触发清负面；「可格挡中毒」由 Patches/BrewBlockablePoisonPatch 实现）。
/// 这个类本身只负责让状态栏出现那枚特殊效果图标与说明。
/// </summary>
public sealed class BrewModPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
}
