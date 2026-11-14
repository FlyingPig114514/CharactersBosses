using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Patches;

/// <summary>
/// ★ 2026-11-13（用户：「恐惧模式下，黑球机的特殊效果描述没有改。这只是我查看的第一个 boss，
/// 你看看别的 boss 是不是也有这种情况？」）
///
/// **根因**：常驻的「特殊效果说明框」（`UI/BossSpecialTip`）和特殊效果面板（`UI/BossSpecialUi`）
/// 都是拿 `power.HoverTips` 渲染的，而 `PowerModel.HoverTips` 里那条说明来自
/// <see cref="PowerModel.Description"/>，它是
/// <code>
/// public virtual LocString Description =&gt; new LocString("powers", base.Id.Entry + ".description");
/// </code>
/// 也就是说 —— **它读的是 `powers` 表，不是 `monsters` 表**。
///
/// 而恐惧版文案之前只写进了 `monsters` 表的 `<KEY>_BOSS.&lt;special&gt;_dread`，那条只被
/// `BossDefinition.SpecialLocForAct` 用到（目前只有 `LockOnPower` 的悬浮提示会读）。
/// ⇒ **除了当初单独加了 `Description` 覆盖的华丽猎「压制」和抉择储「指导」，其余 30 来个 Boss
///   在恐惧模式下显示的都还是普通模式那段文案。**
///
/// **修法**：给 `PowerModel.Description` 挂后置，恐惧模式下只要 `powers` 表里存在
/// `&lt;ID&gt;.description_dread` 就换成它。这样：
///   · 只对我们**真写了恐惧版文案**的 power 生效（原版 power 没有这个键 ⇒ 一个字节都不动）；
///   · 华丽猎/抉择储那两个自己覆盖了 `Description` 的类不受影响（覆盖后不会走到基类实现，
///     它们本来就已经按 `DreadMode.IsActive` 选了 `_dread` 键）；
///   · 说明框、能力面板、悬浮提示三处同时生效。
/// </summary>
[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.Description), MethodType.Getter)]
internal static class DreadSpecialPowerDescriptionPatch
{
    private static int _logs;

    [HarmonyPostfix]
    public static void Postfix(PowerModel __instance, ref LocString __result)
    {
        try
        {
            if (!Core.DreadMode.IsActive) return;

            string entry;
            try { entry = __instance?.Id?.Entry ?? string.Empty; }
            catch { return; }
            if (string.IsNullOrEmpty(entry)) return;

            string key = entry + ".description_dread";
            if (!LocString.Exists("powers", key)) return;

            __result = new LocString("powers", key);

            if (_logs++ < 40)
                Logger.Log($"[CharBoss] 恐惧模式：特殊效果说明换成恐惧版（{entry} → powers/{key}）");
        }
        catch { /* 绝不影响提示渲染 */ }
    }
}
