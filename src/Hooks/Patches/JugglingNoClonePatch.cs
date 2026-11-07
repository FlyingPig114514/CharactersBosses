using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CharacterBosses.Patches;

/// <summary>
/// **拦掉原版「杂耍」（<c>JugglingPower</c>）的复制** —— 二层「杂耍战」的特殊效果「燃烧契约」。
///
/// 用户 2026-11-02：「拦掉原版杂耍的复制，提前在他手牌里加上对应的牌（并且要带有虚无和消耗）」。
///
/// 原版实现（反编译 `JugglingPower.BeforeCardPlayed`）：
/// <code>
/// if (cardPlay.Card.Owner == Owner.Player &amp;&amp; cardPlay.Card.Type == CardType.Attack)
/// {
///     if (attacksPlayedThisTurn == 3) return;                  // 每回合最多 3 张
///     for (int i = 0; i &lt; Amount; i++)
///     {
///         var card = cardPlay.Card.CreateClone();              // ← 复制
///         await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, Owner.Player);
///     }
/// }
/// </code>
/// 它有两个问题（对我们这只 Boss 而言）：
///   1. 复制出来的是**原样克隆**，没有「虚无 / 消耗」⇒ 手牌越堆越多，和剧本手牌打架；
///   2. 复制哪张取决于她**这回合打了什么**（随机性），而设计稿要的是**剧本写死**。
///
/// 所以：拥有者是我们自己的 <see cref="Monsters.CharBossMonster"/> 时，这条钩子**整个跳过**；
/// 复制牌改由剧本用 `ScriptCard.JugglingCopy` 提前摆好（带虚无 + 消耗），
/// 回合结束时由「惊逃」打出去。真人玩家的杂耍完全不受影响。
/// </summary>
[HarmonyPatch]
public static class JugglingNoClonePatch
{
    /// <summary>
    /// ★ 双版本（2026-11-05 实测）：`JugglingPower.BeforeCardPlayed` **只有 v0.111 才声明**；
    ///   v0.107 的 JugglingPower 没有它（只有 `AfterCardPlayed`）⇒ Harmony 按 DeclaredOnly 解析不到目标。
    ///   原来写死 `nameof(...)` 会让整个 `PatchAll` 抛异常（游戏里报「程序集DLL初始化失败」），
    ///   所以改成运行时解析 + Prepare 守卫：旧版直接跳过本补丁（旧版上「杂耍」的复制拦不掉，会多几张克隆牌）。
    /// </summary>
    [HarmonyPrepare]
    public static bool Prepare()
    {
        var target = TargetMethod();
        if (target != null) return true;
        Logger.Log("[CharBoss] 杂耍拦复制：本版本 JugglingPower 没有 BeforeCardPlayed ⇒ 跳过这个补丁");
        return false;
    }

    internal static System.Reflection.MethodBase? TargetMethod()
        => AccessTools.Method(typeof(JugglingPower), "BeforeCardPlayed",
                               new[] { typeof(CardPlay) });

    [HarmonyPrefix]
    public static bool Prefix(JugglingPower __instance, CardPlay cardPlay, ref System.Threading.Tasks.Task __result)
    {
        try
        {
            // ⚠ 杂耍的 `Owner` 是**生物**（PowerModel.Owner 就是 Creature）⇒ 直接看 Monster。
            var owner = __instance.Owner;
            if (owner?.Monster is not Monsters.CharBossMonster) return true;   // 真人玩家 → 原版

            // ⚠ 跳过返回 Task 的方法必须给一个已完成的 Task，否则调用方 await null → NRE。
            __result = System.Threading.Tasks.Task.CompletedTask;
            return false;
        }
        catch
        {
            return true;   // 判断失败就按原版走，绝不影响流程
        }
    }
}
