#if STS2_111   // ⚠ 「愚者 / 塔罗牌」是 0.113.0 才有的 ⇒ 用户 2026-11-14：107 版本不加这个 Boss
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **塔罗愚的「魔术师」**（用户 2026-11-14）：
///
/// > 「魔术师（生成 1 电 1 冰 1 玻璃）」
/// > 恐惧模式：「魔术师改为生成 **2 电 1 冰 1 玻璃**」
/// > 「魔术师并没有生成指定的充能球，并且没有给将要生成的充能球伤害预览」
///
/// 反编译 `TheMagician.OnPlay` 是：
/// <code>
/// await OrbCmd.AddSlots(owner, DynamicVars.Repeat.IntValue);            // 3（升级 4）
/// for (i = 0; i &lt; Repeat; i++)
///     await OrbCmd.Channel(ctx, OrbModel.GetRandomOrb(Rng.CombatOrbGeneration).ToMutable(), owner);
/// </code>
/// 也就是**随机**球 —— 而我们的设计是固定球序。这里给「我们的 Boss 打出这张牌」加一个前缀，
/// 自己按固定顺序引导；玩家的愚者打出这张牌时**完全不受影响**（`return true` 走原版）。
///
/// 升级版 `Repeat` = 4，正好对应恐惧模式的 4 颗球（2 电 + 1 冰 + 1 玻璃）⇒ 不用另外判断模式，
/// 只看这张牌**是不是升级过**即可（恐惧模式会把他的非无色牌全部升级）。
///
/// ⚠ 意图侧的预览在 <see cref="Core.BossOrbSim.Predict"/> 里另有一条同款分支，两边顺序必须一致。
/// </summary>
[HarmonyPatch(typeof(TheMagician), "OnPlay")]
public static class FoolMagicianOrbPatch
{
    [HarmonyPrefix]
    public static bool Prefix(TheMagician __instance, PlayerChoiceContext choiceContext, ref Task __result)
    {
        try
        {
            if (__instance?.Owner?.Creature?.Monster is not CharBossMonster) return true;
        }
        catch { return true; }

        __result = PlayOurWay(__instance, choiceContext);
        return false;
    }

    /// <summary>固定球序：先加球位，再按 电 / （升级再多 1 电）/ 冰 / 玻璃 入队。</summary>
    private static async Task PlayOurWay(TheMagician card, PlayerChoiceContext ctx)
    {
        try
        {
            var owner = card.Owner;
            if (owner == null) return;

            int repeat = 3;
            try { repeat = Math.Max(0, card.DynamicVars.Repeat.IntValue); } catch { repeat = 3; }
            if (repeat <= 0) repeat = 3;

            var wanted = new List<Type> { typeof(LightningOrb) };
            bool upgraded = false;
            try { upgraded = card.IsUpgraded; } catch { upgraded = false; }
            if (upgraded) wanted.Add(typeof(LightningOrb));   // 恐惧模式：2 电
            wanted.Add(typeof(FrostOrb));
            wanted.Add(typeof(GlassOrb));

            await OrbCmd.AddSlots(owner, repeat);

            foreach (var orbType in wanted)
            {
                var canonical = ModelDb.GetById<OrbModel>(ModelDb.GetId(orbType));
                if (canonical == null) continue;
                await OrbCmd.Channel(ctx, canonical.ToMutable(1), owner);
            }

            Logger.Log($"[CharBoss] 塔罗愚·魔术师：生成 {wanted.Count} 颗固定球" +
                       $"（{string.Join("/", wanted.ConvertAll(t => t.Name))}），球位 +{repeat}");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 塔罗愚·魔术师引导充能球失败: " + e);
        }
    }
}
#endif
