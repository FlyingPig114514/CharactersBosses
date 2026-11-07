using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**致死性**」（`LethalityPower`：每回合的**第一张**攻击牌造成 50% 额外伤害）在**意图预览**上的补正
/// —— 用户 2026-11-03：「伤害预览要正确的，因为致死性只让她每回合第一张攻击牌增伤」。
///
/// 反编译 `LethalityPower.ModifyDamageMultiplicative`：它会数
/// `CombatManager.History.CardPlaysStarted` 里「本回合她已经开始打的攻击牌」数量，
/// 只有数量匹配时才返回 `1 + Amount/100`（= ×1.5）。
///
/// 问题：**意图是在规划期渲染的，那时历史里一张牌都还没打** ⇒ 计数恒为 0 ⇒
/// 反编译里的「是这一回合第一张」判定对**每一张**攻击牌都成立
/// ⇒ 手牌栏里第 2 张起的攻击意图统统虚高 50%。
///
/// 补正：拿**本回合计划出战的第一张攻击牌**当唯一受益者 ——
///   · 它就是计划里第一个 `CardType.Attack` 的那张 ⇒ 保留 ×1.5；
///   · 其余攻击牌一律回落到 ×1（等真正出牌时，原版自己按历史计数算，不会少也不会多）。
/// </summary>
[HarmonyPatch(typeof(LethalityPower), nameof(LethalityPower.ModifyDamageMultiplicative))]
public static class LethalityFirstAttackOnlyPatch
{
    [HarmonyPostfix]
    public static void Postfix(decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource,
                               ref decimal __result)
    {
        try
        {
            if (__result <= 1m) return;                                  // 本来就没加成
            _ = amount; _ = props; _ = dealer;

            var boss = cardSource?.Owner?.Creature?.Monster as CharBossMonster;
            if (boss == null) return;                                    // 只修正我们自己的 Boss

            if (IsFirstPlannedAttack(boss, cardSource!)) return;          // 计划里的第一张攻击牌：留着这 50%
            __result = 1m;                                                // 其余攻击牌：去掉这 50%
        }
        catch { /* 出问题就维持原版结果，绝不打断战斗 */ }
    }

    /// <summary>这张牌是不是**本回合计划里第一张攻击牌**（判据统一放在 Boss 身上，见
    /// <c>CharBossMonster.IsFirstPlannedAttackCard</c>）。</summary>
    private static bool IsFirstPlannedAttack(CharBossMonster boss, CardModel card)
        => boss.IsFirstPlannedAttackCard(card);
}
