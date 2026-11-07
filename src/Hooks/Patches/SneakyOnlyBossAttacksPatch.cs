using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CharacterBosses.Patches;

/// <summary>
/// **鬼祟（<c>SneakyPower</c>）只认「别的 Boss 打出的攻击牌」，不认真人玩家的攻击牌。**
///
/// 用户 2026-10-29（第二次口径）：
///   · 第一次：「让鬼祟判定攻击牌时排除奇巧猎自己，只判定其它 boss」；
///   · 这一次：「玩家打出攻击牌**还是**会触发奇巧猎的鬼祟」 ⇒ 玩家那条路也要掐掉。
///
/// 所以最终语义 = **只有「另一只 Boss 打出攻击牌」才给奇巧猎格挡**。
///
/// ## 原版实现（反编译，别猜）
/// <code>
/// // SneakyPower.AfterCardPlayed(PlayerChoiceContext, CardPlay)
/// if (cardPlay.Card.Owner.Creature != Owner &amp;&amp; cardPlay.Card.Type == CardType.Attack)
///     await CreatureCmd.GainBlock(Owner, Amount, ValueProp.Unpowered, null, fast: true);
/// </code>
/// 也就是原版是「**任何**别的生物打攻击牌 ⇒ 我格挡」，真人玩家打出攻击牌正好命中这条（用户不想要）。
///
/// ## 判据：这张牌的 owner 是不是**真人玩家**
/// 我们的 Boss 用的是**影子 Player**（`ShadowPlayerFactory` 造的那种），它**不在**
/// `CombatState.Players` 里 —— 这正是「Boss 打牌时原版扇出会漏掉」的原因
/// （见 <c>SneakyOnBossAttack</c>）。反过来，用同一条性质就能干净地区分：
///   · `Owner` 在 `CombatState.Players` 里 且 它的生物不是怪物 ⇒ **真人玩家** ⇒ 不触发；
///   · 其余（我们的 Boss 影子玩家）⇒ 交给原版（实际也扇出不到，由 `SneakyOnBossAttack` 补）。
///
/// ⚠ 只在 <see cref="CardType.Attack"/> 上动手：非攻击牌一律 `return true`，原版行为一个像素不变。
/// ⚠ 判据取不到（异常 / 空）时 `return true`，宁可多给一次格挡也不要凭空吞掉原版逻辑。
/// </summary>
[HarmonyPatch(typeof(SneakyPower), nameof(SneakyPower.AfterCardPlayed))]
internal static class SneakyOnlyBossAttacksPatch
{
    private static int _logged;

    /// <summary>
    /// ⚠⚠ 2026-10-29 **血的教训**：这是 `Task` 返回值的方法，前缀 `return false` 时**必须**
    /// 顺手把 `__result` 填成 `Task.CompletedTask`。
    /// 第一版只写了 `return false`，Harmony 就把返回值留成 `null` ⇒ 原版
    /// `Hook.AfterCardPlayed` 对每个 power 的结果 `await` 一个 null Task
    /// ⇒ **NullReferenceException 把出牌流程打断** ⇒ 表现是
    /// 「Boss 打出的指向性攻击牌停在空中」「玩家打出的指向性攻击牌停在空中」
    /// （用户 2026-10-29 两次实测：先是奇巧猎的打击，后来连他自己打的中和/打击也停住）。
    /// </summary>
    [HarmonyPrefix]
    public static bool Prefix(SneakyPower __instance, CardPlay cardPlay, ref Task __result)
    {
        try
        {
            var card = cardPlay?.Card;
            if (card == null) return true;
            if (card.Type != CardType.Attack) return true;          // 非攻击牌：不动

            var powerOwner = __instance?.Owner;
            if (powerOwner == null) return true;

            Player? cardOwner = null;
            try { cardOwner = card.Owner; } catch { cardOwner = null; }
            if (cardOwner == null) return true;                      // 判不清是谁打的：交回原版

            // ① 自己打牌不给自己格挡（原版条件，顺手在这里也判一次）
            try
            {
                if (ReferenceEquals(cardOwner.Creature, powerOwner))
                {
                    __result = Task.CompletedTask;                   // ← 必须！否则 await null 崩掉整次出牌
                    return false;
                }
            }
            catch { /* 忽略 */ }

            // ② 真人玩家的攻击牌 → 不触发（用户 2026-10-29 要求）
            var combat = powerOwner.CombatState;
            if (IsRealPlayer(combat, cardOwner))
            {
                if (_logged++ < 8)
                {
                    Logger.Log($"[CharBoss] 鬼祟：真人玩家 {cardOwner.Creature?.Name ?? "玩家"} 打出攻击牌 " +
                               $"{card.Id.Entry} → **不触发**（只有其它 Boss 的攻击牌才算）");
                }
                __result = Task.CompletedTask;                       // ← 同上
                return false;
            }

            return true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 鬼祟前置判定失败（按原版处理）: " + e.Message);
            return true;
        }
    }

    /// <summary>这个 Player 是不是**真人玩家**（我们的 Boss 影子 Player 不在 <c>CombatState.Players</c> 里）。</summary>
    private static bool IsRealPlayer(MegaCrit.Sts2.Core.Combat.ICombatState? combat, Player player)
    {
        try
        {
            if (combat == null || player == null) return false;
            if (player.Creature?.Monster != null) return false;       // 怪物身上的影子 Player：不是真人

            var players = combat.Players;
            if (players == null) return false;
            foreach (var p in players)
            {
                if (p == null) continue;
                if (ReferenceEquals(p, player)) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }
}
