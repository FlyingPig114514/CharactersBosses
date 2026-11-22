using System.Threading.Tasks;
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
/// ⚠ 我们的 Boss 是**用影子玩家（Player 侧）真的打出卡牌**的 ⇒ 在玩家的鬼祟眼里，
/// 「出牌者是一个 Player、且不是我」⇒ 判定成队友 ⇒ **敌方 Boss 攻击时玩家白拿格挡**。
///
/// 修法：**只要出牌者的 `Creature.Monster` 是我们自己的 `CharBossMonster`，就不算队友**
/// （那是敌人），让原版那句不成立。其余情况（真人队友 / 其他来源）一律放行，保持原版行为。
///
/// ★★ v0.2.896 两处修正：
///   ① **`Task` 返回值的方法，前缀 `return false` 时必须填 `__result`** ——
///      以前这里只 `return false`，Harmony 把 `__result` 留成 null，原版
///      `Hook.AfterCardPlayed` 就会 `await` 一个 null Task（同一坑见 `SneakyOnlyBossAttacksPatch` 的注释）。
///   ② 只对「**持有者是真人玩家**」的鬼祟生效 —— 我们 Boss 自己的鬼祟（奇巧猎）**应该**由别的 Boss
///      的攻击牌给格挡（用户 2026-10-29 口径），那条由 `SneakyOnBossAttack` 补，不能被这里掐掉。
/// </summary>
[HarmonyPatch(typeof(SneakyPower), nameof(SneakyPower.AfterCardPlayed))]
internal static class SneakyPowerEnemyBossIsNotTeammatePatch
{
    [HarmonyPrefix]
    private static bool Prefix(SneakyPower __instance, CardPlay cardPlay, ref Task __result)
    {
        try
        {
            Player? payer = cardPlay?.Card?.Owner;
            if (payer == null) return true;

            // 出牌的是我们自己的 Boss（通过影子玩家）⇒ 它是敌人，不是队友 ⇒ 不给格挡
            if (payer.Creature?.Monster is not Monsters.CharBossMonster) return true;

            // 鬼祟的持有者也是我们的 Boss（奇巧猎自己 / 另一只 Boss）⇒ 放行给原版，
            // 真正的「别的 Boss 打攻击牌」由 `SneakyOnBossAttack` 统一补。
            var holder = __instance?.Owner;
            if (holder?.Monster is Monsters.CharBossMonster) return true;

            __result = Task.CompletedTask;      // ← 必须！否则 await null 会打断出牌流程
            return false;
        }
        catch
        {
            return true;    // 判不出来就交给原版，绝不吞掉正常结算
        }
    }
}
