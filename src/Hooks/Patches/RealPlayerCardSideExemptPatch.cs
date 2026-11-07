using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Patches;

/// <summary>
/// **真人玩家自己的牌要自动打出**时，在**目标被选出来之前**就登记一下。
///
/// ★ 为什么必须挂在 `CardCmd.AutoPlay` 入口（v0.2.294，用户 2026-11-02 本机实测：
///   「第一张剑柄打击还是会打自己」）：原版 `AutoPlay` 是**先挑目标、再走 `Hook.BeforeCardPlayed`**
///   —— 只挂 BeforeCardPlayed 会漏掉每一串自动出牌的**第一张**（后面几张靠前一张尚未注销的登记侥幸生效）。
///   这一条补上的就是「目标选择」那一段。
///
/// 判据：`card.Owner.Creature` 是**真人玩家**（`Monster == null &amp;&amp; Player != null`）。
/// 她自己（影子玩家）的牌走 `AutoPlay` 时不受影响，照旧翻成玩家侧。
/// </summary>
[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.AutoPlay))]
internal static class RealPlayerAutoPlaySideExemptPatch
{
    /// <summary>
    /// ⚠ 参数**只声明用得上的 `card`**：Harmony 按名字绑定参数，把 6 个参数全抄一遍反而容易因为
    /// 名字对不上（原版最后一个形参叫 `skipCardPileVisuals`）而在挂载期抛错。
    /// </summary>
    [HarmonyPrefix]
    public static void Prefix(CardModel card)
    {
        try
        {
            if (!HittableEnemiesSidePatch.IsRealPlayerCardModel(card)) return;
            HittableEnemiesSidePatch.PushRealPlayerAutoPlay(card, CurrentCombat());
        }
        catch { /* 忽略：登记失败就按原规则（翻侧） */ }
    }

    private static ICombatState? CurrentCombat()
    {
        try { return MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.DebugOnlyGetState(); }
        catch { return null; }
    }
}

/// <summary>
/// **真人玩家自己的牌开始结算**时登记一下（配对的是 <see cref="RealPlayerCardPlayedEndPatch"/>）。
///
/// 为什么需要（玩家 2026-11-02 反馈的真 bug）：
///   玩家（铁甲战士）身上有「**地狱狂徒**」（`HellraiserPower`：抽到名字带「打击」的牌就自动打出），
///   又在 Boss 战里被击中 ⇒ 遗物「**百年积木**」（`CentennialPuzzle`）在**敌方回合**里抽 3 张 ⇒
///   抽到「打击」⇒ `HellraiserPower.AfterCardDrawnEarly` 调
///   `CardCmd.AutoPlay(ctx, card, target: null)` ⇒ 原版在 `HittableEnemies` 里挑
///   `c.IsPlayer && c != card.Owner.Creature` 当目标。
///   而我们在敌方回合把 `HittableEnemies` 翻成了「玩家侧」⇒ 玩家自己的打击打到了玩家自己。
///
/// 修法：这张牌只要是**真人玩家**的，那一小段里的 `HittableEnemies` 就走原版
/// （详见 <see cref="HittableEnemiesSidePatch.RealPlayerCardDepth"/>）。
/// 挂在 `Hook.BeforeCardPlayed` / `Hook.AfterCardPlayed` 上 —— 这两条对
/// `CardCmd.AutoPlay` 打出的牌**同样会派发**（她自己的牌走 AutoPlay 时也会进这两个钩子，
/// 只是「扇出给 Power」那一步会漏掉，见 `CharBossMonster` 的注释），所以能包住整段结算。
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCardPlayed))]
internal static class RealPlayerCardPlayedStartPatch
{
    [HarmonyPrefix]
    public static void Prefix(ICombatState combatState, CardPlay cardPlay)
    {
        try { HittableEnemiesSidePatch.PushRealPlayerCard(combatState, cardPlay); }
        catch { /* 忽略：登记失败就按原规则（翻侧） */ }
    }
}

/// <summary>**真人玩家自己的牌结算结束**时取消登记（与 <see cref="RealPlayerCardPlayedStartPatch"/> 配对）。</summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
internal static class RealPlayerCardPlayedEndPatch
{
    [HarmonyPostfix]
    public static void Postfix(ICombatState combatState, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try { HittableEnemiesSidePatch.PopRealPlayerCard(cardPlay); }
        catch { /* 忽略：过期由 RealPlayerCardStale 兜底 */ }
    }
}
