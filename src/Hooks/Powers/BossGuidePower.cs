using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Powers;

/// <summary>
/// 三层储君 3 号「**抉择储**」的特殊效果「**指导**」（用户 2026-11-10 设计）。
///
/// 用户原话：
/// > 特殊效果：指导：每当储君打出抉择抉择时，你替他选择要被重复打出的牌。
///
/// 这层 power 本身**不做事**：它只是把这条规则显示在她身上（能力说明 + 能力面板），
/// 真正的实现是 <c>Patches/ChoiceRegentPickPatch</c> —— 当「抉择，抉择」向她的影子玩家
/// 要一次选牌时，把自动选择器换成**给玩家看的选择界面**（知识恶魔那套
/// <c>CardSelectCmd.FromChooseACardScreen</c>），选完再继续结算；选中的那张会被原版
/// 重复打出 <c>Repeat</c> 次。
/// </summary>
public sealed class BossGuidePower : PowerModel
{
    // 特殊效果按 Buff 显示（和「吞噬生命」「引雷针」一样：设计效果，不是掉到减益行的 debuff）
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>
    /// 恐惧模式（2026-11-13 用户口径）：由玩家替他挑 **2 张**（再打一次「抉择，抉择」并拦掉抽牌）
    /// ⇒ 图标说明另存 `powers` 表的 `<ID>.description_dread`（16 语言）。
    /// </summary>
    public override MegaCrit.Sts2.Core.Localization.LocString Description
    {
        get
        {
            try
            {
                if (Core.DreadMode.IsActive)
                {
                    return new MegaCrit.Sts2.Core.Localization.LocString(
                        "powers", Id.Entry + ".description_dread");
                }
                return base.Description;
            }
            catch { return base.Description; }
        }
    }
}
