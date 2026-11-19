using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CharacterBosses.Patches;

/// <summary>
/// 「鬼祟」（`SneakyPower`）在打我们 Boss 时**误把敌方当成队友**的修正。
///
/// 玩家反馈 2026-11-19：
/// > 「猎宝的鬼祟会把对面识别成队友」「道中根本不触发」「打 BOSS 战对面攻击会触发」
/// > 补充：「这里指的是**玩家**的鬼祟出了问题，只把敌方 Boss 识别成队友」
///
/// 反编译原版（`SneakyPower.AfterCardPlayed`，逐字）：
/// <code>
/// public override Task AfterCardPlayed(PlayerChoiceContext ctx, CardPlay cardPlay)
/// {
///     if (cardPlay.Card.Owner.Creature != Owner &amp;&amp; cardPlay.Card.Type == CardType.Attack)
///         await CreatureCmd.GainBlock(Owner, Amount, Unpowered, null, fast: true);
/// }
/// </code>
/// 原版语义 =「**别人**（联机时就是队友）打出攻击牌 ⇒ 我获得格挡」。它只用
/// `Card.Owner.Creature != Owner` 区分「不是我自己」，**无法区分队友和敌人**。
///
/// 原版普通怪物的攻击不走 `CardCmd`（没有 `CardPlay`）⇒ 天然不会触发，所以这个能力
/// 在单人走路上本来就没用；它只在**队友真的打出攻击牌**时才生效。
///
/// ⚠ 我们的 Boss 是**用影子玩家（Player 侧）真的打出卡牌**的 ⇒ 在玩家的鬼祟眼里，
/// 「出牌者是一个 Player、且不是我」⇒ 判定成队友 ⇒ **敌方 Boss 攻击时玩家白拿格挡**。
///
/// 修法：**只要出牌者的 `Creature.Monster` 是我们自己的 `CharBossMonster`，就不算队友**
/// （那是敌人），让原版那句不成立。其余情况（真人队友 / 其他来源）一律放行，保持原版行为。
/// </summary>
[HarmonyPatch(typeof(SneakyPower), nameof(SneakyPower.AfterCardPlayed))]
internal static class SneakyPowerEnemyBossIsNotTeammatePatch
{
    [HarmonyPrefix]
    private static bool Prefix(CardPlay cardPlay)
    {
        try
        {
            Player? payer = cardPlay?.Card?.Owner;
            if (payer == null) return true;

            // 出牌的是我们自己的 Boss（通过影子玩家）⇒ 它是敌人，不是队友 ⇒ 不给格挡
            if (payer.Creature?.Monster is Monsters.CharBossMonster)
            {
                return false;
            }

            return true;
        }
        catch
        {
            return true;    // 判不出来就交给原版，绝不吞掉正常结算
        }
    }
}
