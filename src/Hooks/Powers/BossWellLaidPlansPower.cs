using System;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Powers;

/// <summary>
/// 二层「**蛇眼猎**」（静默猎手 · 异蛇之眼轴，Key `SILENT_SNECKO`）的特殊效果「**计划妥当**」
/// —— 用户 2026-11-03 设计：
///
/// > 计划妥当：静默猎手每回合第一次抽到耗能为 3 的卡牌时，将其保留。
///
/// 她的**手牌每回合都是剧本重建的**（`BuildHand`），并不存在「真抽牌」这一步，
/// 所以「抽到 3 费牌 → 保留」直接由剧本表达：用户原文里那些标了「(保留)」的牌
/// （子弹时间 / 跟踪 / 谋杀 / 匕首雨）在剧本里就是 `ScriptCard.Retain = true`
/// ⇒ 造牌时加 `CardKeyword.Retain`，回合结束真的留在手里。
///
/// 这层 power 只负责**在状态栏上把「计划妥当」显示出来**（和它的说明），不参与判定
/// —— 判定已经体现在牌上，多一层状态反而会和剧本打架。
/// </summary>
public sealed class BossWellLaidPlansPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldScaleInMultiplayer => false;

    public override bool ShouldPlayVfx => false;
}
