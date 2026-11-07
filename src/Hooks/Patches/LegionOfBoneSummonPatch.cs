using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using CharacterBosses.Core;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 多人换牌 **A2：吸引仇恨 → 骸骨军团** 的效果（设计见 <c>docs/plans/multiplayer-cards.md</c> §2）。
///
/// ## 原版实现（反编译确认）
/// <code>
/// // LegionOfBone（2 费技能，AllAllies，MultiplayerOnly，Summon 变量 6；升级 8）
/// await CreatureCmd.TriggerAnim(owner, 召唤动画…);
/// foreach (Creature c in CombatState.PlayerCreatures.Where(c =&gt; c.Alive))
///     await OstyCmd.Summon(ctx, c.Player, 6, this);
/// </code>
///
/// ## 为什么必须整段接管（2026-10-22 实测证据）
/// `CombatState.PlayerCreatures` 只含**真人玩家** —— 影子 Player 不在里面
/// （mod_log: 打出这张牌时循环只跑了 2 次，两次都是真人，被我们拦掉；
///  同一份日志里 `BossHookBridge` 也一直写着「影子 Player 不在 CombatState.Players 里」）。
/// 所以「只拦真人那一路」是不够的：那样**谁都不会被召唤**，Boss 的奥斯提只能等 `BossOsty` 的自动补建。
///
/// 现在改成：施法者是我们的 Boss 时，**直接给场上每一只 CharBossMonster 各召唤一只奥斯提**
/// （归属天然正确 —— `OstyCmd.Summon` 会把 PetOwner 设成那只 Boss 的影子 Player），
/// 真人玩家一把也不给（总原则：换牌后的效果只作用在 Boss 侧）。
///
/// 单人零回归：单人局里这张牌不会进卡组（换牌只在多人生效）；真人施法时前缀直接放行 → 走原版。
/// </summary>
[HarmonyPatch(typeof(LegionOfBone), "OnPlay")]
public static class LegionOfBonePlayPatch
{
    private static int _logged;

    [HarmonyPrefix]
    public static bool Prefix(LegionOfBone __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay,
                              ref Task __result)
    {
        try
        {
            var owner = __instance.Owner;
            if (owner?.Creature?.Monster is not CharBossMonster caster) return true;   // 真人施法 → 原版
            __result = RunAsync(__instance, owner, caster, choiceContext);
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 骸骨军团接管失败（按原版放行）: " + e);
            return true;
        }
    }

    private static async Task RunAsync(LegionOfBone card, Player owner, CharBossMonster caster,
                                       PlayerChoiceContext ctx)
    {
        decimal amount = 6m;
        try { amount = card.DynamicVars.Summon.BaseValue; } catch { /* 用默认 6 */ }

        // 召唤动画（照抄原版；取不到角色专用动画就退回通用的 Cast）
        try
        {
            string anim = "Cast";
            float delay = 0.15f;
            try
            {
                anim = MegaCrit.Sts2.Core.Models.Characters.Necrobinder
                    .GetSummonAnimIfApplicable(owner.Character) ?? anim;
                delay = MegaCrit.Sts2.Core.Models.Characters.Necrobinder
                    .GetSummonDelayIfApplicable(owner.Character);
            }
            catch { /* 用默认 */ }
            await CreatureCmd.TriggerAnim(owner.Creature, anim, delay);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 骸骨军团召唤动画失败: " + e.Message);
        }

        // 给**每一只**我们的 Boss 各召一只（真人玩家不给）
        var bosses = AllBosses(caster);
        foreach (var boss in bosses)
        {
            var summoner = boss.Shadow;
            if (summoner == null || boss.Creature == null || !boss.Creature.IsAlive) continue;
            try
            {
                var result = await OstyCmd.Summon(ctx, summoner, amount, card);
                if (_logged++ < 20)
                {
                    Logger.Log($"[CharBoss] 骸骨军团：给 {boss.Id.Entry} 召唤奥斯提" +
                               $"（amount={amount}，结果={result?.Creature?.Name ?? "已有奥斯提(加血)"}）");
                }
            }
            catch (Exception e)
            {
                Logger.Log($"[CharBoss] 骸骨军团：给 {boss.Id.Entry} 召唤失败: {e.Message}");
            }
        }

        if (bosses.Count == 0) Logger.Log("[CharBoss] 骸骨军团：场上没有我们模组的 Boss（异常态）");
    }

    /// <summary>场上所有活着的我们的 Boss（施法者在敌方阵营，取敌方列表再并上自己）。</summary>
    private static List<CharBossMonster> AllBosses(CharBossMonster caster)
    {
        var list = new List<CharBossMonster>();
        try
        {
            if (!list.Contains(caster)) list.Add(caster);
            var enemies = caster.CombatState?.Enemies;
            if (enemies != null)
            {
                foreach (var c in enemies)
                {
                    if (c?.Monster is CharBossMonster other && !list.Contains(other)) list.Add(other);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 骸骨军团：收集 Boss 失败: " + e.Message);
        }
        return list;
    }
}
