using System;

namespace CharacterBosses.Core;

/// <summary>
/// **按幕分档**的支配强度（用户 2026-11-21）：
/// > 「修改猎人、机器人和储君的支配，这 3 个支配做出层数的区分
/// >  猎人 1/2/3 层 = −1/−2/−3 力量 + 1/2/3 虚弱；机器人 = 所有敌人 1/2/3 层人工制品；
/// >  储君 = 回合开始变非状态诅咒牌，1 层变眩晕、2 层变粘液、3 层变碎屑。
/// >  战士、观者和愚者的支配不动，还是每层都一样。」
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
}
