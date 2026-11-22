using System;

namespace CharacterBosses.Core;

/// <summary>
/// **按幕分档**的支配强度（用户 2026-11-21）：
/// > 「修改猎人、机器人和储君的支配，这 3 个支配做出层数的区分
/// >  猎人 1/2/3 层 = −1/−2/−3 力量 + 1/2/3 虚弱；机器人 = 所有敌人 1/2/3 层人工制品；
/// >  储君 = 回合开始变非状态诅咒牌，1 层变眩晕、2 层变粘液、3 层变碎屑。
/// >  战士、观者和愚者的支配不动，还是每层都一样。」
///
/// ★ 2026-11-23 用户追加：**愚者的支配也按幕分档**（1/2/3 幕 = 2/3/4 张「悲伤」），
///   见 <see cref="SadnessCount"/> —— 愚者目前只有三幕的 Boss，但三档都先写。
///
/// ⚠ 必须在**运行期**读（`RunManager` 的 run）——不能在 `BossDefs` 静态初始化里判，
///   那时 `RunManager` 还没有 run（本项目已在多个剧本里踩过这个坑）。
/// </summary>
internal static class DreadActScaling
{
    /// <summary>当前第几幕（1 / 2 / 3，夹在 1..3）。</summary>
    internal static int ActNumber()
    {
#if STS2_113
        try
        {
            var run = MegaCrit.Sts2.Core.Runs.RunManager.Instance?.DebugOnlyGetState();
            int idx = run?.CurrentActIndex ?? 0;
            return Math.Clamp(idx + 1, 1, 3);
        }
        catch { return 1; }
#else
        return 1;
#endif
    }

    /// <summary>「储君的支配」1 / 2 / 3 幕分别变成：眩晕（`Dazed`）/ 粘液（`Slimed`）/ 碎屑（`Debris`）。</summary>
    internal static Type RegentTransformCardType() => ActNumber() switch
    {
        1 => typeof(MegaCrit.Sts2.Core.Models.Cards.Dazed),
        2 => typeof(MegaCrit.Sts2.Core.Models.Cards.Slimed),
        _ => typeof(MegaCrit.Sts2.Core.Models.Cards.Debris),
    };

    /// <summary>同上，给人看的名字（日志 / 描述占位符）。</summary>
    internal static string RegentTransformCardLabel() => ActNumber() switch
    {
        1 => "眩晕",
        2 => "粘液",
        _ => "碎屑",
    };

    /// <summary>
    /// 「**愚者的支配**」每场战斗开始往玩家抽牌堆里塞几张「悲伤」(<c>Sadness</c>)：
    /// **1 / 2 / 3 幕 = 2 / 3 / 4 张**。
    ///
    /// ★ 用户 2026-11-23：「增强愚者的支配：在战斗开始时，将 2/3/4 张悲伤加入你的抽牌堆。
    ///   分层，3 层不同，虽然目前只有 3 层才有愚者 boss，但先都写上」。
    ///   （在这之前是**固定 2 张**，三幕一样。）
    /// </summary>
    internal static int SadnessCount() => ActNumber() switch
    {
        1 => 2,
        2 => 3,
        _ => 4,
    };
}
