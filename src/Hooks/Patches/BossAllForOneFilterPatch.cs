using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Cards;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**一心化万**」（三层「事件机」`DEFECT_EVENT` 的特殊效果，用户 2026-11-03 设计稿）：
///
/// > 故障机器人的万物一心只会将弃牌堆中的 **0 费攻击牌**放回它的手牌。
/// > 每当它有一张牌从弃牌堆回到手牌，**所有单位**都获得 1 点力量。
///
/// 反编译 `AllForOne.OnPlay`：
/// <code>
/// Deal 10 damage (upgrade 14) → foreach (Discard.GetPile(Owner).Cards.Where(Filter))
///                                    await CardPileCmd.Add(card, PileType.Hand);
/// </code>
/// 而 `Filter` 是
/// <code>
/// card.EnergyCost.GetWithModifiers(All) == 0 &amp;&amp; !CostsX &amp;&amp; (uint)(card.Type - 1) &lt;= 2
///   // Type ∈ { Attack, Skill, Power }
/// </code>
/// 也就是说原版会把 0 费的**技能 / 能力**也拿回来，而且一张牌都不给力量。
///
/// 这里只改「拿哪些牌」这一半：让她的 `Filter` **恒为 false**（原版一张都不搬），
/// 真正的搬牌 + 全体加力量由 `Core/EventMachineMechanics.AllForOneReturnsAsync` 按剧本点名的名单做。
/// 真人玩家的万物一心**完全不受影响**（判据是「牌的所有者是我们的影子 Boss」）。
/// </summary>
[HarmonyPatch(typeof(AllForOne), "Filter")]
public static class BossAllForOneFilterPatch
{
    [HarmonyPostfix]
    public static void Postfix(AllForOne __instance, ref bool __result)
    {
        try
        {
            if (!__result) return;
            if (__instance?.Owner?.Creature?.Monster is not CharBossMonster) return;
            __result = false;   // 原版不搬；改由 EventMachineMechanics 按剧本名单搬（并给力量）
        }
        catch
        {
            // 读不到所有者就按原版走
        }
    }
}
