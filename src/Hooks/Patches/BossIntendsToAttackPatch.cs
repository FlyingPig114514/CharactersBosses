using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 让「**她这一手算不算攻击**」按**她会不会打出攻击牌**来回答（用户 2026-10-26 订正）。
///
/// 背景：原版
/// <code>
/// public bool IntendsToAttack => NextMove.Intents.Any(i => i.IntentType is Attack or DeathBlow);
/// </code>
/// 「眼部攻击」（`GoForTheEyes`）等牌就是靠它决定要不要上虚弱，别的模组的同类牌也一样。
///
/// 但我们这排意图是**逐张牌**画的：她打出「群蛇形态」之后，**每张牌**下面都会补一条
/// 「打 4」的攻势意图（那是原版 power 触发的伤害，不是她打了攻击牌）。
/// 于是第 3 回合这种「只出技能牌、但意图里满是攻势图标」的回合，第三方会误以为她要攻击。
///
/// 用户口径：「**只看打没打攻击牌**，而不是意图里包不包括攻势。」
/// 所以这里对 `MonsterModel.IntendsToAttack` 挂 postfix，只对**我们的 Boss** 改成按计划回答：
///   · 计划里有 `CardType.Attack` 的牌 ⇒ 攻击；
///   · 计划里有会生成小刀的攻击牌（小刀当回合会打出去）⇒ 攻击；
///   · 展示牌（奇巧被丢弃后自动打出的那些）是攻击牌 ⇒ 攻击；
///   · 手牌里已经躺着的小刀（回合末会被打出去）⇒ 攻击；
///   · 其它（只有技能/能力牌，哪怕意图里有群蛇的攻势）⇒ **不攻击**。
///
/// 计划还没算出来时（`PlanReady == false`，例如读档瞬间）**不动原值**，避免误判成「不攻击」。
/// </summary>
[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.IntendsToAttack), MethodType.Getter)]
public static class BossIntendsToAttackPatch
{
    [HarmonyPostfix]
    public static void Postfix(MonsterModel __instance, ref bool __result)
    {
        try
        {
            if (__instance is not CharBossMonster boss) return;      // 别的怪物/我们的随从：一个字都不改
            if (!boss.PlanReady) return;                             // 计划还没算：保留原值

            // 被击晕（吹哨等）⇒ 她这一回合**一张牌都不会打**：原版 `NextMove` 已经是 STUNNED、
            // 里面只有 `StunIntent`，所以这里必须回答「不攻击」。
            // 不特判的话，保留下来的那份计划里还全是攻击牌 ⇒ 「眼部攻击」这类牌会白给她上虚弱
            // （用户 2026-11-04 的击晕需求，见 docs/plans/stun-behavior.md）。
            if (boss.Stunned)
            {
                if (__result) Logger.Log("[CharBoss] 攻击意图判定：她被击晕，本回合不出牌 → 不攻击");
                __result = false;
                return;
            }

            bool attacks = boss.PlansToAttackWithCards();
            if (attacks != __result)
            {
                Logger.Log("[CharBoss] 攻击意图判定：按「会不会打出攻击牌」回答 → " +
                           (attacks ? "是攻击" : "不攻击") + $"（{boss.Def.Key}）");
            }
            __result = attacks;
        }
        catch { /* 绝不能因为这段把别人的判定带崩 */ }
    }
}
