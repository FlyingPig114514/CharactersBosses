using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace CharacterBosses.Patches;

/// <summary>
/// **纯诊断**：把「瓦皮（<c>WuppysLoyaltyPower</c>）承伤重定向」这一步照出来。
///
/// 玩家反馈 2026-11-23：
/// > 「抉择储的天际钻头好像不会优先攻击瓦皮（天际钻头是攻击牌，造成攻击伤害，
/// >   按理说应该瓦皮优先承伤的）」
///
/// ## 反编译（0.113）确认的机制（和我们的模组没有交集，所以先取证再改）
/// <code>
/// // WuppysLoyaltyPower.ModifyPreBlockDamageTarget
/// if (originalTarget != Owner.PetOwner?.Creature) return target;   // 只替「主人」承伤
/// if (Owner.IsDead) return target;                                 // 瓦皮自己死了就不挡
/// if (!props.IsPoweredAttack()) return target;                     // 只挡「强化攻击」（Move 且非 Unpowered）
/// return Owner;                                                    // ⇒ 伤害改由瓦皮掉血
/// </code>
/// 调用点在 `CreatureCmd.Damage` 内部：先 `Hook.ModifyDamage`（力量/虚弱/易伤），
/// 再 `Hook.ModifyPreBlockDamageTarget`（瓦皮在**格挡之前**直接掉血），
/// 玩家只吃「溢出伤害」。这条链**不经过** `HittableEnemies`，也不经过 `AttackCommand` 选靶，
/// 所以我们的选靶类补丁改不动它 —— 也就意味着「不承伤」只可能来自上面三条之一：
///   ① 目标不是瓦皮的主人（例如这一手打的是**别的玩家**，或者目标是宠物/召唤物）；
///   ② 伤害不是「强化攻击」（`ValueProp` 里带 `Unpowered`）；
///   ③ 瓦皮已经死了（`IsDead`）—— 天际钻头是 8 点 × N 段，瓦皮血量低时会被前几段打死，
///      之后每一段都直接落到玩家身上（这在本模组里尤其明显：抉择储那一手 X=8 ⇒ **16 段**）。
///
/// ⇒ 这里只**加日志**（挂载失败/版本没有瓦皮都安静跳过），用来一眼分辨是上面哪一种：
/// 日志里会出现 `瓦皮承伤诊断：…`，包含原目标、出牌者、props、是否强化攻击、瓦皮死活、
/// 以及最终有没有转给瓦皮。等下一次复现就能定性，再决定要不要改行为。
///
/// ⚠ 用反射挂载而不是 `[HarmonyPatch]`：`WuppysLoyaltyPower` **在 0.111 的参考程序集里不存在**
/// （0.111 还没有瓦皮这个遗物），直接引用会在 111 目标上编译失败。
/// </summary>
internal static class WuppyRedirectTracePatch
{
    private static bool _installed;
    private static int _logged;

    internal static void TryInstall()
    {
        if (_installed) return;
        _installed = true;

        try
        {
            var type = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Powers.WuppysLoyaltyPower");
            if (type == null)
            {
                Logger.Log("[CharBoss] 瓦皮承伤诊断：本版本没有 WuppysLoyaltyPower（0.111 没有瓦皮）⇒ 不挂");
                return;
            }

            MethodInfo? target = null;
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != "ModifyPreBlockDamageTarget") continue;
                target = method;
                break;
            }
            if (target == null)
            {
                Logger.Log("[CharBoss] 瓦皮承伤诊断：找不到 ModifyPreBlockDamageTarget ⇒ 不挂");
                return;
            }

            var postfix = new HarmonyMethod(typeof(WuppyRedirectTracePatch)
                .GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic)!);
            new Harmony("CharacterBosses.wuppytrace").Patch(target, postfix: postfix);
            Logger.Log("[CharBoss] 瓦皮承伤诊断：已挂（复现时看 `瓦皮承伤诊断：` 那几行）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 瓦皮承伤诊断挂载失败（忽略）: " + e.Message);
        }
    }

    private static void Postfix(object __instance, Creature originalTarget, ValueProp props,
                                Creature? dealer, Creature? __result)
    {
        try
        {
            // 只看我们自己 Boss 打出的伤害，免得被原版怪物/玩家自己的伤害刷屏
            if (dealer?.Monster is not Monsters.CharBossMonster) return;
            if (_logged++ >= 40) return;

            Creature? wuppy = null;
            try { wuppy = (__instance as PowerModel)?.Owner; } catch { /* 忽略 */ }

            string verdict;
            if (wuppy == null) verdict = "拿不到瓦皮本体";
            else if (ReferenceEquals(__result, wuppy)) verdict = "**转给瓦皮**";
            else if (wuppy.IsDead) verdict = "瓦皮已死 ⇒ 不挡";
            else if (!props.IsPoweredAttack()) verdict = "不是强化攻击 ⇒ 不挡";
            else verdict = "原目标不是瓦皮的主人 ⇒ 不挡";

            Logger.Log($"[CharBoss] 瓦皮承伤诊断：出牌者={Name(dealer)} 原目标={Name(originalTarget)} " +
                       $"props={props} 强化攻击={SafePowered(props)} 瓦皮HP={SafeHp(wuppy)} " +
                       $"主人={Name(SafeOwnerCreature(wuppy))} ⇒ {verdict}");
        }
        catch { /* 纯诊断，绝不影响战斗 */ }
    }

    private static string SafePowered(ValueProp props)
    {
        try { return props.IsPoweredAttack() ? "是" : "否"; } catch { return "?"; }
    }

    private static string SafeHp(Creature? creature)
    {
        try { return creature == null ? "-" : $"{creature.CurrentHp}/{creature.MaxHp}{(creature.IsDead ? "(死)" : "")}"; }
        catch { return "?"; }
    }

    private static Creature? SafeOwnerCreature(Creature? wuppy)
    {
        try { return wuppy?.PetOwner?.Creature; } catch { return null; }
    }

    private static string Name(Creature? creature)
    {
        try { return creature == null ? "(空)" : ($"{creature.Name}#{creature.CombatId}"); }
        catch { return "?"; }
    }
}
