using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Patches;

/// <summary>
/// **豁免机制**：让「玩家施加、在她的回合结算」的那几个 power 走原版选靶（打她，而不是打玩家）。
///
/// 背景（用户 2026-10-24 实测两轮）：
///   · 亡灵契约师玩家打出「**亡灵精通**」后，自己的奥斯提掉血会伤到自己；
///   · 六火玩家打出「**活体炸弹**」引爆「**灼魂**」后同样炸到自己。
/// 它们都是 `CreatureCmd.Damage(ctx, CombatState.HittableEnemies, …)`，而我们的规则是
/// 「敌方回合 ⇒ 可命中敌人 = 玩家侧」⇒ 在敌方回合结算的这一笔就被翻成玩家侧了。
///
/// ⚠ 第一版是按「时间窗」豁免（`RequestVanillaOnce` 两帧内有效），**不可靠**：
/// 这些 power 的伤害常常夹在 `Cmd.Wait`/动画之后，早就过了两帧 ⇒ 豁免失效、还是炸自己。
///
/// 现在改成挂在**状态机**上：把这些 async 方法的 `<方法名>d__N.MoveNext()` 前后夹一下，
/// 「方法体正在跑的那一小段」里所有 `HittableEnemies` 读取都走原版。
/// 判据是 **`power.Applier` 是真人玩家**（`Applier.Monster == null`）——
/// 她自己施加的（如果有）不受影响，照旧翻成玩家侧。
///
/// 只覆盖「玩家施加、在她回合结算」这三个：
/// 原版 `NecroMasteryPower`、崩坠 `SoulBurnPower`、崩坠 `LivingBombPower`。
/// 其它一切效果（储君的炸弹、她的冰雹风暴…）保持原规则，不会被误伤。
/// </summary>
internal static class PlayerEffectExemption
{
    private static readonly Dictionary<Type, FieldInfo?> OwnerFieldCache = new();
    private static bool _installed;

    internal static void TryInstall()
    {
        if (_installed) return;
        _installed = true;

        // ① 原版：亡灵精通
        Install(typeof(MegaCrit.Sts2.Core.Models.Powers.NecroMasteryPower), "AfterCurrentHpChanged");

        // ② 崩坠：灼魂（回合开始掉血 + 被引爆）与活体炸弹（引爆后的追加结算）
        //    ⚠ 官方（创意工坊）版把角色拆成 Awakened.dll / Hexaghost.dll …，旧改版塞在一个 Downfall.dll 里
        //      ⇒ 不能写死命名空间，按**简单类名**跨程序集找（见 Core/ModTypeFinder）。
        foreach (var simpleName in new[] { "SoulBurnPower", "LivingBombPower" })
        {
            var hits = Core.ModTypeFinder.FindAll(simpleName);
            if (hits.Count == 0) continue;
            foreach (var type in hits)
            {
                Install(type, "AfterSideTurnStart", "Detonate", "AfterSoulburnDetonate");
            }
        }
    }

    /// <summary>给某个 power 类型里名字含这些片段的 async 方法的 MoveNext 挂前后夹。</summary>
    private static void Install(Type powerType, params string[] methodNameFragments)
    {
        try
        {
            var prefix = new HarmonyMethod(typeof(PlayerEffectExemption)
                .GetMethod(nameof(BeforeSegment), BindingFlags.Static | BindingFlags.NonPublic)!);
            var postfix = new HarmonyMethod(typeof(PlayerEffectExemption)
                .GetMethod(nameof(AfterSegment), BindingFlags.Static | BindingFlags.NonPublic)!);

            var harmony = new Harmony("CharacterBosses.playereffect." + powerType.Name);
            int patched = 0;

            foreach (var nested in powerType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                string nestedName = nested.Name;
                bool match = false;
                foreach (var fragment in methodNameFragments)
                {
                    if (nestedName.Contains(fragment, StringComparison.Ordinal)) { match = true; break; }
                }
                if (!match) continue;

                var moveNext = nested.GetMethod("MoveNext",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (moveNext == null) continue;

                harmony.Patch(moveNext, prefix: prefix, postfix: postfix);
                patched++;
            }

            Logger.Log(patched > 0
                ? $"[CharBoss] 玩家效果豁免：{powerType.Name} 已挂上 {patched} 个状态机（结算时打她，不打玩家）"
                : $"[CharBoss] 玩家效果豁免：{powerType.Name} 没找到状态机 ⇒ 未挂（版本不符？）");
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 玩家效果豁免：{powerType.Name} 挂载失败（忽略）: " + e.Message);
        }
    }

    /// <summary>每跑一段 async 方法体之前：如果是「真人玩家施加的」，就临时关掉翻侧。</summary>
    private static void BeforeSegment(object __instance, out bool __state)
    {
        __state = false;
        try
        {
            var power = PowerOf(__instance);
            var applier = power?.Applier;
            if (applier == null) return;           // 拿不到施加者：保守起见不动
            if (applier.Monster != null) return;   // 她自己施加的 → 保持原规则（翻成玩家侧）

            HittableEnemiesSidePatch.PushPlayerEffect();
            __state = true;
        }
        catch { /* 忽略 */ }
    }

    private static void AfterSegment(bool __state)
    {
        if (!__state) return;
        try { HittableEnemiesSidePatch.PopPlayerEffect(); } catch { /* 忽略 */ }
    }

    /// <summary>状态机结构体里那个指回 power 的字段（`&lt;&gt;4__this`）。</summary>
    private static PowerModel? PowerOf(object? stateMachine)
    {
        try
        {
            if (stateMachine == null) return null;
            var type = stateMachine.GetType();
            if (!OwnerFieldCache.TryGetValue(type, out var field))
            {
                field = type.GetField("<>4__this",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                OwnerFieldCache[type] = field;
            }
            return field?.GetValue(stateMachine) as PowerModel;
        }
        catch { return null; }
    }
}
