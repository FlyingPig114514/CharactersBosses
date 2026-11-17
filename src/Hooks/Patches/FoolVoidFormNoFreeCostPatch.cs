#if STS2_113   // ⚠ 「愚者 / 塔罗牌」是 0.113.0 才有的 ⇒ 用户 2026-11-14：107 版本不加这个 Boss
using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **不让「虚空形态」把塔罗愚印出来的牌全部变成 0 费**（用户 2026-11-14）：
///
/// > 「你怎么给第 11 和第 12 回合印出来的牌全变成零费了？
/// >   第十一回合印出来的无色牌只 -1 费，第 12 回合的那些牌都是原本的费用。」
///
/// 反编译 `VoidFormPower`：
/// <code>
/// public override bool TryModifyEnergyCostInCombatLate(CardModel card, decimal originalCost, out decimal modifiedCost)
/// {
///     modifiedCost = originalCost;
///     if (ShouldSkip(card)) return false;      // 「本回合已经打够 N 张」
///     modifiedCost = 0;                        // 否则本回合免费
///     return true;
/// }
/// // ShouldSkip = GetInternalData&lt;Data&gt;().cardsPlayedThisTurn &gt;= Amount
/// // 而那个计数器只在 `AfterCardPlayed` 里 +1
/// </code>
///
/// 影子玩家不在 `CombatState.Players` 里 ⇒ `AfterCardPlayed` 不会替他触发 ⇒
/// **计数器永远是 0** ⇒ 每张牌都被判成「还在免费额度内」⇒ 卡面费用全变 0（他第 9 回合打出了虚空形态）。
///
/// 他本来就不花能量（牌全是剧本 AutoPlay 印出来的）⇒ 这条效果对他只有「显示错」这一个后果，
/// 所以直接对我们的 Boss 跳过这层改写，卡面保持原本的费用。
/// </summary>
[HarmonyPatch(typeof(VoidFormPower), "TryModifyEnergyCostInCombatLate")]
public static class FoolVoidFormNoFreeCostPatch
{
    [HarmonyPrefix]
    public static bool Prefix(CardModel card, decimal originalCost, ref decimal modifiedCost, ref bool __result)
    {
        try
        {
            if (card?.Owner?.Creature?.Monster is not CharBossMonster) return true;
        }
        catch { return true; }

        modifiedCost = originalCost;   // 原样返回 = 不改费用
        __result = false;              // false = 「我没有改动这条费用」
        return false;
    }
}
#endif
