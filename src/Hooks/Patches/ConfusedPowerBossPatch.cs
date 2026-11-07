using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**混乱**」（<c>ConfusedPower</c>，遗物「异蛇之眼」`SneckoEye` 施加的那层）对她手牌费用的随机化 ——
/// 对我们的影子 Boss **关掉**。
///
/// 反编译 `ConfusedPower.AfterCardDrawn`：
/// <code>
/// if (card.Owner != Owner.Player) return;
/// if (card.EnergyCost.Canonical &lt; 0) return;
/// card.EnergyCost.SetThisCombat(Rng.CombatEnergyCosts.NextInt(4));   // 0～3 随机
/// </code>
/// 二层「蛇眼猎」的设计是「**每张牌的费用按剧本写死的那个数字**」（`ScriptCard.CostThisTurn`），
/// 所以只要这层随机在造牌之后又跑一次，卡面/规划/扣费就会对不上
/// （用户 2026-11-03：「有的卡牌的费用没有被强制修改成我想要的」）。
/// 真人玩家手里的异蛇之眼**完全不受影响**（判据是「牌主是不是我们的影子 Boss」）。
///
/// <para>
/// ⚠ **2026-11-04 顺手修的同类致命 bug**：这个 Prefix 原先只 <c>return false</c>、没给
/// <c>__result</c> 赋值。而 <c>ConfusedPower.AfterCardDrawn</c> 返回 <c>Task</c>，
/// 跳过后方法体不执行 ⇒ 返回 **null Task** ⇒ 调用方 <c>…GetAwaiter()</c> 抛 NullReferenceException，
/// 把**抽牌管线**炸掉。事件机上「情感芯片」踩的是同一个坑（那条链直接把回合开始流程打断，
/// 导致结束回合按钮一直是 Hidden）；这一处是**蛇眼猎**身上的定时炸弹
/// （`异蛇之眼` 一给她上混乱，她每抽一张牌就炸一次）。两处都已改成给 <c>Task.CompletedTask</c>。
/// </para>
/// </summary>
[HarmonyPatch(typeof(ConfusedPower), nameof(ConfusedPower.AfterCardDrawn))]
public static class ConfusedPowerBossPatch
{
    [HarmonyPrefix]
    public static bool Prefix(ConfusedPower __instance, PlayerChoiceContext choiceContext,
                              CardModel card, bool fromHandDraw, ref Task __result)
    {
        try
        {
            _ = choiceContext; _ = fromHandDraw;
            if (card?.Owner?.Creature?.Monster is not CharBossMonster) return true;   // 不是我们的 Boss：照常随机
            Logger.Log($"[CharBoss] 混乱（异蛇之眼）：{card.Id.Entry} 的费用按剧本写死的值为准 ⇒ 跳过原版随机");
            __result = Task.CompletedTask;   // ← 关键：不赋值就是 null Task ⇒ 抽牌管线抛 NRE
            return false;
        }
        catch
        {
            return true;
        }
    }
}
