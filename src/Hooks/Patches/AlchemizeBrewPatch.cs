using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using CharacterBosses.Core;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**炼制药水**」（<c>Alchemize</c>）改炼**指定的那一瓶**（用户 2026-10-25 的三层「炼药猎」设计）。
///
/// 原版实现：`OnPlay` 里
/// <code>
/// PotionCmd.TryToProcure(PotionFactory.CreateRandomPotionInCombat(Owner, Owner.RunState.Rng.CombatPotionGeneration).ToMutable(), Owner);
/// </code>
/// 而剧本把「这张炼制药水要炼格挡药水 / 复制药水 / 异鱼之油」写死（`ScriptCard.BrewPotion`）⇒
/// 出牌前由 <c>CharBossMonster</c> 把目标记进 <see cref="BossPotions.PendingBrew"/>，
/// 这里把它换掉：我们的 Boss 且登记了目标时，直接 `TryToProcure(指定药水)`，原版随机那一步跳过。
/// 其它情况（真人玩家炼药、没登记）一律走原版。
/// </summary>
[HarmonyPatch(typeof(Alchemize), "OnPlay")]
public static class AlchemizeBrewPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Alchemize __instance, ref Task __result)
    {
        try
        {
            var owner = __instance?.Owner;
            if (owner == null) return true;
            if (owner.Creature?.Monster is not CharBossMonster boss) return true;

            if (!BossPotions.PendingBrew.TryGetValue(owner, out var potionType) || potionType == null)
            {
                // 剧本这一张没指定「炼哪瓶」⇒ 按原版随机（第 2 回合那种就属于没指定）
                Logger.Log($"[CharBoss] 炼制药水：{boss.Def?.Key} 这张没指定药剂 → 走原版随机");
                return true;
            }

            BossPotions.PendingBrew.Remove(owner);
            var canonical = BossPotions.CanonicalPotion(potionType);
            if (canonical == null)
            {
                Logger.Log($"[CharBoss] 炼制药水：取不到 {potionType.Name} 的原版模型 → 退回随机");
                return true;
            }

            Logger.Log($"[CharBoss] 炼制药水：改炼指定药水 {potionType.Name}");
            __result = Brew(canonical, owner);
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 炼制药水接管失败，走原版: " + e.Message);
            return true;
        }
    }

    private static async Task Brew(PotionModel canonical, Player owner)
    {
        try
        {
            await PotionCmd.TryToProcure(canonical.ToMutable(), owner, -1);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 炼制药水（指定）失败: " + e.Message);
        }
    }
}
