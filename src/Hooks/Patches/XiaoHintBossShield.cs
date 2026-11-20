using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Core;

namespace CharacterBosses.Patches;

/// <summary>
/// 与模组「**小小姐的战斗提示**」（`XiaoSpireHint`，创意工坊 3833912666）的互操作护栏。
///
/// 玩家 2026-11-20 反馈：
/// > 「装载此模组后，这个模组的 AI 会尝试接管角色 Boss 出牌。」
///
/// 反编译那个模组（`XiaoSpireHint.dll`，v?，本机 workshop 3833912666）查到的入口只有两个：
///   · `XiaoSpireHint.Picker.Before(MethodBase __originalMethod, object[] __args)` —— **返回 void**，
///     是它自己 `Picker.Patch(h)` 挂到 `CardSelectCmd` 全家上的前缀，用来观察「谁在选牌」并登记
///     `PendingPick`（真正的作答由它的界面 `Resolve` 完成）；
///   · `XiaoSpireHint.Pilot.Act(Answer a, Player me, CombatState combat, string ver)` —— **返回 string**
///     （非 null = 它自己拒绝执行的原因），里面 `Play(me, combat, a.Card, a.Target)` 直接从
///     `me.PlayerCombatState.Hand.Cards` 挑牌打出。
///
/// 我们的角色 Boss 是**用影子玩家真的出牌**的 ⇒ 在它眼里「有一个玩家要选牌 / 该自动出牌了」，
/// 于是替她选、替她打，看起来就是「AI 接管了 Boss 出牌」。
///
/// 护栏做法：**不碰它的实现**，只在它这两个入口外面各套一层前缀 —— 只要这次调用涉及
/// 我们的角色 Boss（影子玩家 / `CharBossMonster` 的生物 / 她持有的牌 / 候选牌集合），
/// 就 `return false` 把它的那段整段跳过（选牌交回我们的 `BossCardSelector`、出牌交回我们自己的流程）。
/// 正常玩家流程零影响；它没装时整段是 no-op。
///
/// ⚠ v0.2.830 修的两个坑（玩家 `mod_log (47).txt` 实证）：
///   ① 以前**一个前缀同时服务两个入口**、签名里写死 `ref object __result` ⇒ 给返回 void 的
///      `Picker.Before` 打补丁时 Harmony 直接抛
///      `Cannot get result from void method static System.Void XiaoSpireHint.Picker::Before(...)`
///      ⇒ 整个 `TryApply` 被 catch 掉，**一条护栏都没装上**（玩家日志里那行「护栏安装失败」）。
///      现在按返回类型分两个前缀：void 的用 <see cref="PrefixVoid"/>，string 的用 <see cref="PrefixString"/>。
///   ② 以前只给「返回 void 的 Act」打补丁，而 `Pilot.Act` 返回 **string** ⇒ 会把它跳过，
///      也就是**最该拦的托管出牌反而没拦**。现在 string 返回值也照拦（`__result = null` = 它眼里的「成功」）。
/// </summary>
internal static class XiaoHintBossShield
{
    private const string PickerTypeName = "XiaoSpireHint.Picker";
    private const string PilotTypeName = "XiaoSpireHint.Pilot";
    private const string PickerMethodName = "Before";
    private const string PilotMethodName = "Act";

    private static bool _tried;
    private static int _blocked;

    /// <summary>装护栏（幂等；对方模组没装就什么都不做）。启动时调一次。</summary>
    internal static void TryApply()
    {
        if (_tried) return;
        _tried = true;

        try
        {
            var pickerType = FindType(PickerTypeName);
            var pilotType = FindType(PilotTypeName);
            if (pickerType == null && pilotType == null) return;      // 没装那个模组

            var voidPrefix = typeof(XiaoHintBossShield).GetMethod(nameof(PrefixVoid),
                BindingFlags.Static | BindingFlags.NonPublic);
            var stringPrefix = typeof(XiaoHintBossShield).GetMethod(nameof(PrefixString),
                BindingFlags.Static | BindingFlags.NonPublic);
            var harmony = new Harmony("CharacterBosses.XiaoSpireHintBossShield");

            var patched = new List<string>();
            var skipped = new List<string>();

            // ① 选牌观察入口：Picker.Before(MethodBase, object[]) —— 返回 void
            var pickerTarget = pickerType?.GetMethod(PickerMethodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (pickerTarget != null)
            {
                if (pickerTarget.ReturnType == typeof(void))
                {
                    harmony.Patch(pickerTarget, prefix: new HarmonyMethod(voidPrefix!));
                    patched.Add("Picker.Before(选牌)");
                }
                else
                {
                    skipped.Add("Picker.Before 返回 " + pickerTarget.ReturnType.Name);
                }
            }

            // ② 托管出牌入口：Pilot.Act(Answer, Player, CombatState, string) —— 本版返回 **string**
            if (pilotType != null)
            {
                foreach (var m in pilotType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (m.Name != PilotMethodName) continue;

                    HarmonyMethod prefix;
                    if (m.ReturnType == typeof(void)) prefix = new HarmonyMethod(voidPrefix!);
                    else if (m.ReturnType == typeof(string)) prefix = new HarmonyMethod(stringPrefix!);
                    else { skipped.Add("Pilot.Act 返回 " + m.ReturnType.Name); continue; }

                    harmony.Patch(m, prefix: prefix);
                    patched.Add("Pilot.Act(托管出牌)");
                }
            }

            if (patched.Count == 0)
            {
                Logger.Log("[CharBoss] 小小姐的战斗提示：没找到它的接管入口（版本可能变了）⇒ 未加护栏" +
                           (skipped.Count > 0 ? "；跳过：" + string.Join("、", skipped) : string.Empty));
                return;
            }

            Logger.Log("[CharBoss] 小小姐的战斗提示：已加护栏（" + string.Join(" + ", patched) + "）" +
                       (skipped.Count > 0 ? "；跳过：" + string.Join("、", skipped) : string.Empty) +
                       " —— 角色 Boss 的选牌与出牌不再被它接管");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 小小姐的战斗提示护栏安装失败（忽略）: " + e.Message);
        }
    }

    /// <summary>给**返回 void** 的入口（`Picker.Before`）用：false = 跳过它那段。</summary>
    private static bool PrefixVoid(MethodBase? __originalMethod, object[] __args)
    {
        return !ShouldBlock(__originalMethod, __args, out _);
    }

    /// <summary>给**返回 string** 的入口（`Pilot.Act`，非 null = 它眼里的「拒绝原因」）用。</summary>
    private static bool PrefixString(MethodBase? __originalMethod, object[] __args, ref string? __result)
    {
        if (!ShouldBlock(__originalMethod, __args, out var who)) return true;
        __result = null;      // null = 它眼里的「成功」，不会触发它「连续失败 2 次就自动关掉托管」的逻辑
        return false;
    }

    /// <summary>这次调用涉不涉及我们的角色 Boss（涉及就拦，并前 5 次打日志留证据）。</summary>
    private static bool ShouldBlock(MethodBase? method, object[]? args, out string who)
    {
        who = string.Empty;
        try
        {
            if (args == null) return false;
            foreach (var arg in args)
            {
                who = Describe(arg);
                if (who.Length == 0) continue;

                // 只在前 5 次打日志：够定位、不刷屏
                if (_blocked < 5)
                {
                    _blocked++;
                    Logger.Log($"[CharBoss] 小小姐的战斗提示：已拦下它对角色 Boss 的接管（" +
                               $"{method?.DeclaringType?.Name}.{method?.Name}，{who}，第 {_blocked} 次）");
                }
                return true;
            }
        }
        catch { /* 判不出来就放行 */ }
        return false;
    }

    /// <summary>参数里有没有我们的 Boss；有就回一句人话（用于日志），没有就回空串。</summary>
    private static string Describe(object? arg)
    {
        try
        {
            if (arg == null) return string.Empty;

            if (arg is Player player)
            {
                if (player.Creature?.Monster is Monsters.CharBossMonster) return "影子玩家";
                if (BossKillerTracker.BossOf(player.Creature) != null) return "Boss 玩家";
                return string.Empty;
            }

            if (arg is Creature c)
            {
                if (c.Monster is Monsters.CharBossMonster) return "Boss 生物";
                if (BossKillerTracker.BossOf(c) != null) return "Boss 生物";
                return string.Empty;
            }

            if (arg is CardModel card)
            {
                var owner = card.Owner?.Creature;
                if (owner?.Monster is Monsters.CharBossMonster || BossKillerTracker.BossOf(owner) != null)
                    return "她的牌 " + (card.Id?.Entry ?? "?");
                return string.Empty;
            }

            // 其它模型（RelicModel / PowerModel / 遭遇…）一律看它的 Owner 属性（反射拿，避免类型依赖）
            if (arg is AbstractModel)
            {
                try
                {
                    var ownerProp = arg.GetType().GetProperty("Owner",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var owner = ownerProp?.GetValue(arg);
                    if (owner != null)
                    {
                        var inner = Describe(owner);
                        if (inner.Length > 0) return inner + " 的" + arg.GetType().Name;
                    }
                }
                catch { /* 忽略 */ }
            }

            // 集合（选牌候选 / 玩家列表）
            if (arg is IEnumerable seq && arg is not string)
            {
                foreach (var item in seq)
                {
                    var inner = Describe(item);
                    if (inner.Length > 0) return inner + "（候选集合）";
                }
            }
        }
        catch { /* 忽略 */ }
        return string.Empty;
    }

    private static Type? FindType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
            catch { /* 某些程序集不允许反射 */ }
        }
        return null;
    }
}
