using System;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;
using CharacterBosses.Powers;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**征召上前**」的召唤部分（用户 2026-11-05）：
/// 储君打出**仆从牌**时，若当前仆从数不足 2，就**不改用原版效果**，改为召唤对应种类的仆从。
///
/// 三张仆从牌（原版只是带 `CardTag.Minion` 的普通牌）：
///   仆从打击 `MinionStrike` → **打击仆从** `MinionStrikeMonster`
///   仆从俯冲 `MinionDiveBomb` → **俯冲仆从** `MinionDiveBombMonster`
///   仆从捐躯 `MinionSacrifice` → **捐躯仆从** `MinionSacrificeMonster`
///
/// 判定口径按用户原话：「若其仆从数**不足 2**，改为召唤对应种类的仆从」——
/// 也就是说第 1、2 张仆从牌是召唤，从第 3 张起才恢复原版效果（很可能是真的造成伤害/获得格挡）。
/// 召唤出来的仆从**不会在本回合行动**（它们排在储君之后出场，而行动顺序是仆从 → 储君，
/// 所以下一轮才开始动）。
///
/// 只对**我们自己的**储君（身上挂着 <see cref="BossCallToArmsPower"/>）生效，
/// 真人玩家打这三张牌时完全不受影响。
/// </summary>
[HarmonyPatch]
public static class MinionSummonPatch
{
    private static int _logs;

    // ── 三张牌各挂一个前缀（都走同一个实现） ──
    [HarmonyPatch(typeof(MinionStrike), "OnPlay",
                  new[] { typeof(PlayerChoiceContext), typeof(CardPlay) })]
    [HarmonyPrefix]
    public static bool StrikePrefix(MinionStrike __instance, PlayerChoiceContext choiceContext,
                                    ref Task __result)
        => TrySummon(__instance, choiceContext, typeof(MinionStrikeMonster), ref __result);

    [HarmonyPatch(typeof(MinionDiveBomb), "OnPlay",
                  new[] { typeof(PlayerChoiceContext), typeof(CardPlay) })]
    [HarmonyPrefix]
    public static bool DivePrefix(MinionDiveBomb __instance, PlayerChoiceContext choiceContext,
                                  ref Task __result)
        => TrySummon(__instance, choiceContext, typeof(MinionDiveBombMonster), ref __result);

    [HarmonyPatch(typeof(MinionSacrifice), "OnPlay",
                  new[] { typeof(PlayerChoiceContext), typeof(CardPlay) })]
    [HarmonyPrefix]
    public static bool SacrificePrefix(MinionSacrifice __instance, PlayerChoiceContext choiceContext,
                                       ref Task __result)
        => TrySummon(__instance, choiceContext, typeof(MinionSacrificeMonster), ref __result);

    /// <summary>
    /// 公共实现：能召唤就召唤并跳过原版效果（返回 false），否则放行（返回 true）。
    /// </summary>
    private static bool TrySummon(CardModel card, PlayerChoiceContext choiceContext, Type minionType,
                                  ref Task __result)
    {
        try
        {
            if (card?.Owner?.Creature?.Monster is not CharBossMonster boss) return true;
            if (boss.Creature?.GetPower<BossCallToArmsPower>() == null) return true;   // 不是仆从储

            int alive = BossMinionMonster.CountAlive(boss);
            if (alive >= BossCallToArmsPower.MinionCapNow) return true;                  // 已经够 2 只 ⇒ 走原版

            var combat = boss.CombatState;
            if (combat == null) return true;

            if (_logs++ < 30)
            {
                Logger.Log($"[CharBoss] 征召上前：储君打出 {SafeEntry(card)} 时仆从只有 {alive} 只" +
                           $"（不足 {BossCallToArmsPower.MinionCapNow}）⇒ 改为召唤 {minionType.Name}");
            }
            __result = SummonAsync(boss, minionType, choiceContext);
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 征召上前（召唤判定）失败，按原版效果: " + e.Message);
            return true;
        }
    }

    private static async Task SummonAsync(CharBossMonster boss, Type minionType,
                                          PlayerChoiceContext choiceContext)
    {
        try
        {
            var combat = boss.CombatState;
            if (combat == null) return;

            // 原版就有的「加入一只敌方怪物」API：`CreatureCmd.Add<T>(ICombatState, string? slotName = null)`
            // （反编译确认，原版测试怪 MockAttackAndSummonMinionMonster 就是这么召唤的）。
            // ⚠ 以前这里用反射按「1 个参数」找泛型方法 ⇒ 它有 2 个参数（第二个可选）⇒ 永远找不到，
            //   于是「拦掉了原版效果却召唤不出来」（用户 2026-11-05 实测）。现在直接强类型调用。
            Creature? creature;
            if (minionType == typeof(MinionStrikeMonster))
                creature = await CreatureCmd.Add<MinionStrikeMonster>(combat);
            else if (minionType == typeof(MinionDiveBombMonster))
                creature = await CreatureCmd.Add<MinionDiveBombMonster>(combat);
            else
                creature = await CreatureCmd.Add<MinionSacrificeMonster>(combat);
            if (creature?.Monster is not BossMinionMonster minion)
            {
                Logger.Log("[CharBoss] 征召上前：召唤失败（拿不到仆从生物）");
                return;
            }

            minion.Summoner = boss;
            BossMinionMonster.Register(boss, minion);

            // 爪牙：原版 MinionPower（储君死亡时我们**自己**负责连带清场，见 CharBossMonster）
            try
            {
                await PowerCmd.Apply<MinionPower>(choiceContext, creature, 1m, boss.Creature, null);
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 征召上前：给仆从挂「爪牙」失败（忽略）: " + e.Message);
            }

            // ⚠ 顺序改成在**敌方回合开始前**统一重排（见 Core/MinionOrder.ReorderAtEnemyTurnStart）。
            //   在这里（她回合进行中）改 `_enemies` 会把行动循环卡死（用户 2026-11-05 实测）。

            // 召唤完成 ⇒ 立刻重算她的意图（后续的仆从牌会从「召唤」变成攻击/格挡）
            try
            {
                boss.RefreshPlannedIntents();
                UI.BossHandUi.RefreshForce(boss);
            }
            catch { /* 忽略 */ }

            Logger.Log($"[CharBoss] 征召上前：召出 {minion.Id.Entry}（血量 {creature.MaxHp}，" +
                       $"当前仆从 {BossMinionMonster.CountAlive(boss)} 只）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 征召上前（召唤）失败: " + e.Message);
        }
    }

    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }
}
