using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>两条版本分支共用的判定与日志。</summary>
internal static class FeralNoReturnShared
{
    private static int _logs;

    /// <summary>「这张牌的主人是不是我们的 Boss」。</summary>
    internal static bool ShouldBlock(CardModel? card)
        => card?.Owner?.Creature?.Monster is CharBossMonster;

    internal static void Note(CardModel? card)
    {
        try
        {
            if (_logs++ < 20)
            {
                Logger.Log($"[CharBoss] 野性回手已拦：{card?.Id.Entry} 打完仍进弃牌堆（手牌由剧本给）");
            }
        }
        catch { /* 忽略 */ }
    }
}

#if STS2_111
/// <summary>
/// **拦掉「野性」（<c>FeralPower</c>）把 0 费攻击牌塞回她的手牌**（用户 2026-10-24）。
///
/// 反编译依据（v0.111）：
/// <code>
/// public override CardLocation ModifyCardPlayResultLocation(CardModel card, bool isAutoPlay,
///                                                          ResourceInfo resources, CardLocation location)
/// </code>
/// 我们的 Boss 每回合的手牌是剧本**重建**的（`BossScriptRunner.BuildHand`），剧本里
/// 「被野性捞回来的那张」已经写成独立一条 ⇒ 原版再塞一张回来 = 手牌多一张、意图与实际对不上。
/// 这里直接**返回原去向**（弃牌堆），只对**我们的** Boss 生效。
///
/// ★ 双版本：`CardLocation` 类型 v0.107 没有 ⇒ 这一支只在 `STS2_111` 下编译；
///   旧版走下面的 <see cref="FeralNoReturnLegacyPatch"/>。
/// </summary>
[HarmonyPatch]
public static class FeralNoReturnPatch
{
    [HarmonyPrepare]
    public static bool Prepare() => Core.GameCompat.HasFeralResultLocation;

    internal static System.Reflection.MethodBase? TargetMethod()
        => Core.GameCompat.DeclaredMethod(typeof(FeralPower), "ModifyCardPlayResultLocation");

    [HarmonyPrefix]
    public static bool Prefix(CardModel card, CardLocation location, ref CardLocation __result)
    {
        try
        {
            if (!FeralNoReturnShared.ShouldBlock(card)) return true;
            __result = location;                    // 按原去向（弃牌堆）
            FeralNoReturnShared.Note(card);
            return false;
        }
        catch
        {
            return true;   // 出任何问题都退还原版行为
        }
    }
}
#endif

/// <summary>
/// 旧版（v0.107）那一支：`FeralPower.ModifyCardPlayResultPileTypeAndPosition`，
/// 返回 `(PileType, CardPilePosition)` —— 和新版是同一件事（改「打完去哪」）。
/// 目标用**字符串**解析：这个方法在新版里不存在，写 `nameof` 会编译不过。
/// 新版上 `Prepare()` 返回 false（探测不到那个方法）⇒ 自动跳过，不会重复拦。
/// </summary>
[HarmonyPatch]
public static class FeralNoReturnLegacyPatch
{
    [HarmonyPrepare]
    public static bool Prepare() => Core.GameCompat.HasFeralPileTypeAndPosition;

    internal static System.Reflection.MethodBase? TargetMethod()
        => Core.GameCompat.DeclaredMethod(typeof(FeralPower), "ModifyCardPlayResultPileTypeAndPosition");

    [HarmonyPrefix]
    public static bool Prefix(CardModel card, PileType pileType, CardPilePosition position,
                             ref ValueTuple<PileType, CardPilePosition> __result)
    {
        try
        {
            if (!FeralNoReturnShared.ShouldBlock(card)) return true;
            __result = new ValueTuple<PileType, CardPilePosition>(pileType, position);
            FeralNoReturnShared.Note(card);
            return false;
        }
        catch
        {
            return true;
        }
    }
}
