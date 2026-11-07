using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 多人时**不让本模组的 Boss 身上的 Power 层数按人数翻倍**（用户 2026-10-25 实测的 bug）。
///
/// ## 症状
/// 2 人联机、第一幕「愤怒战」：队友那边看到她 **12 层覆甲**、格挡 22；另一端只有 10（锚的 10）。
///
/// ## 根因（反编译 `PlatingPower`）
/// <code>
/// // PowerCmd.Apply 内部：
/// if (players.Count > 1 &amp;&amp; (target.IsPrimaryEnemy || target.IsSecondaryEnemy) &amp;&amp; power.ShouldScaleInMultiplayer)
///     amount = power.GetScaledAmountForMultiplayer(combatState, applier, amount, target, cardSource);
///
/// // PlatingPower 自己的实现（不是基类那套 ×人数×幕系数）：
/// ((players.Count - 1) * 2 + 1) * amount      // 2 人 = 3 倍 ⇒ 护喉甲的 4 层变成 12 层
/// </code>
/// 于是 12 层覆甲在「第 1 回合玩家回合开始」被 `PlatingPower.BeforeSideTurnStart` 换成 12 点格挡，
/// 12 + 锚 10 = 22。
///
/// ## 为什么必须拦
/// 我们所有 Boss 的数值（血量、格挡、Power 层数）都是按**单人设计值**定的：
/// 血量见 <see cref="BossNoMultiplayerHpScalePatch"/>、格挡见 <see cref="BossNoMultiplayerBlockScalePatch"/>，
/// Power 层数也必须是原值 —— 否则「护喉甲 4 层覆甲」「人工合成 3 层人工制品」
/// （`ArtifactPower` 也有自己的缩放实现）在联机里全都不是设计的那个数。
///
/// ## 实现
/// `GetScaledAmountForMultiplayer` 是 virtual，`PowerCmd.Apply` 走的是**虚调用** ⇒ 只补基类拦不到重写，
/// 所以基类 + 目前所有重写它的 Power 一起挂同一个 postfix：目标是我们的 Boss 就还原成原值。
/// 单人局 `PowerCmd.Apply` 根本不会进缩放分支，所以这条**不改变单人任何一个数字**；原版怪物不受影响。
/// </summary>
internal static class BossPowerNoMultiplayerScale
{
    /// <summary>这个目标是不是我们的 Boss（影子随从不算：它们不是 CharBossMonster）。</summary>
    internal static bool IsOurBoss(Creature? target)
    {
        try { return target?.Monster is CharBossMonster; }
        catch { return false; }
    }

    /// <summary>统一的 postfix 主体：把缩放后的值还原成 <paramref name="amount"/>。</summary>
    internal static void Revert(Creature target, decimal amount, ref decimal __result, string powerName)
    {
        try
        {
            if (!IsOurBoss(target)) return;
            if (__result == amount) return;
            Logger.Log($"[CharBoss] 多人：{target!.Monster.Id.Entry} 的 {powerName} 层数缩放 ×{__result / (amount == 0m ? 1m : amount)} " +
                       $"→ ×1（{amount} 层，保持单人设计值）");
            __result = amount;
        }
        catch { /* 出错就用原版缩放 */ }
    }
}

[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.GetScaledAmountForMultiplayer))]
public static class BossNoScalePowerBasePatch
{
    [HarmonyPostfix]
    public static void Postfix(Creature target, decimal amount, ref decimal __result)
        => BossPowerNoMultiplayerScale.Revert(target, amount, ref __result, "Power");
}

[HarmonyPatch(typeof(PlatingPower), nameof(PlatingPower.GetScaledAmountForMultiplayer))]
public static class BossNoScalePlatingPatch
{
    [HarmonyPostfix]
    public static void Postfix(Creature target, decimal amount, ref decimal __result)
        => BossPowerNoMultiplayerScale.Revert(target, amount, ref __result, "覆甲");
}

[HarmonyPatch(typeof(ArtifactPower), nameof(ArtifactPower.GetScaledAmountForMultiplayer))]
public static class BossNoScaleArtifactPatch
{
    [HarmonyPostfix]
    public static void Postfix(Creature target, decimal amount, ref decimal __result)
        => BossPowerNoMultiplayerScale.Revert(target, amount, ref __result, "人工制品");
}

[HarmonyPatch(typeof(BufferPower), nameof(BufferPower.GetScaledAmountForMultiplayer))]
public static class BossNoScaleBufferPatch
{
    [HarmonyPostfix]
    public static void Postfix(Creature target, decimal amount, ref decimal __result)
        => BossPowerNoMultiplayerScale.Revert(target, amount, ref __result, "缓冲");
}

[HarmonyPatch(typeof(SkittishPower), nameof(SkittishPower.GetScaledAmountForMultiplayer))]
public static class BossNoScaleSkittishPatch
{
    [HarmonyPostfix]
    public static void Postfix(Creature target, decimal amount, ref decimal __result)
        => BossPowerNoMultiplayerScale.Revert(target, amount, ref __result, "Skittish");
}

[HarmonyPatch(typeof(SlipperyPower), nameof(SlipperyPower.GetScaledAmountForMultiplayer))]
public static class BossNoScaleSlipperyPatch
{
    [HarmonyPostfix]
    public static void Postfix(Creature target, decimal amount, ref decimal __result)
        => BossPowerNoMultiplayerScale.Revert(target, amount, ref __result, "Slippery");
}
