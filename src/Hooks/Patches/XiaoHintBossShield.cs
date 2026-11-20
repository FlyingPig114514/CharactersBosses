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
/// 反编译那个模组（`XiaoSpireHint.dll`）查到的原因：它用**运行期反射**给
/// `CardSelectCmd` 的**全部方法**挂了一个前缀（`XiaoSpireHint.Picker.Before`）来自动应答选牌。
/// 而我们的角色 Boss 是**用影子玩家真的出牌**的 —— 她一旦打出需要选牌的牌
/// （抉择储的「抉择，抉择」、各种 `FromHand` / `FromDiscard` 选牌），
/// 在它眼里就是「有一个玩家要选牌」⇒ 它的 AI 直接替她选，看起来就是「AI 接管了 Boss 出牌」。
///
/// 护栏做法：**不碰它的实现**，只在它那个前缀外面再套一层 —— 只要这次选牌涉及
/// 我们的角色 Boss（影子玩家 / `CharBossMonster` 的生物 / 她持有的牌），
/// 就 `return false` 让它的自动应答整段跳过，选牌照旧交回我们的
/// `BossCardSelector` / 原版界面（玩家/正常玩家流程完全不受影响，它没装时这段是 no-op）。
/// </summary>
internal static class XiaoHintBossShield
{
    private const string ModTypeName = "XiaoSpireHint.Picker";
    private const string ModMethodName = "Before";

    private static bool _tried;

    /// <summary>装护栏（幂等；对方模组没装就什么都不做）。启动时调一次。</summary>
    internal static void TryApply()
    {
        if (_tried) return;
        _tried = true;

        try
        {
            var picker = FindType(ModTypeName);
            var pilotProbe = FindType("XiaoSpireHint.Pilot");
            if (picker == null && pilotProbe == null) return;      // 没装那个模组

            var prefix = typeof(XiaoHintBossShield).GetMethod(nameof(Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            var harmony = new Harmony("CharacterBosses.XiaoSpireHintBossShield");

            var patched = new List<string>();

            // ① 选牌器：Picker.Before(object[] __args) —— 它给 CardSelectCmd 的 FromHand 系列打的前缀
            var pickerTarget = picker.GetMethod(ModMethodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (pickerTarget != null)
            {
                harmony.Patch(pickerTarget, prefix: new HarmonyMethod(prefix));
                patched.Add("Picker.Before(选牌器)");
            }

            // ② 托管出牌：Pilot.Act(Answer, Player me, CombatState, string) ——
            //    它从 me.PlayerCombatState.Hand 里挑牌打出，而 me 来自「谁是本地玩家」；
            //    我们的角色 Boss 用**影子玩家**出牌，影子正是照着本地玩家造的 ⇒ 它会替 Boss 出牌。
            var pilotType = FindType("XiaoSpireHint.Pilot");
            if (pilotType != null)
            {
                foreach (var m in pilotType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (m.Name != "Act") continue;
                    harmony.Patch(m, prefix: new HarmonyMethod(prefix));
                    patched.Add("Pilot." + m.Name + "(托管出牌)");
                    break;
                }
            }

            if (patched.Count == 0)
            {
                Logger.Log("[CharBoss] 小小姐的战斗提示：没找到它的接管入口（版本可能变了）⇒ 未加护栏");
                return;
            }
            Logger.Log("[CharBoss] 小小姐的战斗提示：已加护栏（" + string.Join(" + ", patched) +
                       "）—— 角色 Boss 的选牌与出牌不再被它接管");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 小小姐的战斗提示护栏安装失败（忽略）: " + e.Message);
        }
    }

    /// <summary>返回 false = 跳过它的自动应答 / 托管出牌（交回我们的选择器 / 原版流程）。</summary>
    private static bool Prefix(MethodBase? __originalMethod, object[] __args, ref object __result)
    {
        try
        {
            if (__args == null) return true;
            foreach (var arg in __args)
            {
                if (!InvolvesBoss(arg)) continue;

                // 跳过对方逻辑；如果它这个方法有返回值（Pilot.Act 返回状态字符串），给个安全默认，
                // 免得那边拿到 null 在 UI 里炸掉。
                try
                {
                    var rt = (__originalMethod as MethodInfo)?.ReturnType;
                    if (rt != null && rt != typeof(void))
                    {
                        __result = rt == typeof(string) ? string.Empty
                                 : (rt.IsValueType ? Activator.CreateInstance(rt) : null);
                    }
                }
                catch { /* 忽略 */ }
                return false;
            }
        }
        catch { /* 判不出来就放行 */ }
        return true;
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

    /// <summary>这个参数里有没有我们的角色 Boss（影子玩家 / 她的生物 / 她持有的牌）。</summary>
    private static bool InvolvesBoss(object? arg)
    {
        try
        {
            if (arg == null) return false;

            if (arg is Player player)
            {
                var creature = player.Creature;
                if (creature?.Monster is Monsters.CharBossMonster) return true;
                return BossKillerTracker.BossOf(creature) != null;
            }

            if (arg is Creature c)
            {
                if (c.Monster is Monsters.CharBossMonster) return true;
                return BossKillerTracker.BossOf(c) != null;
            }

            if (arg is CardModel card)
            {
                var owner = card.Owner?.Creature;
                if (owner?.Monster is Monsters.CharBossMonster) return true;
                return BossKillerTracker.BossOf(owner) != null;
            }

            // 其它模型（RelicModel / PowerModel / 遭遇…）一律看它的 Owner 属性（反射拿，避免类型依赖）
            if (arg is AbstractModel)
            {
                try
                {
                    var ownerProp = arg.GetType().GetProperty("Owner",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var owner = ownerProp?.GetValue(arg);
                    if (owner != null && InvolvesBoss(owner)) return true;
                }
                catch { /* 忽略 */ }
            }

            // 集合（选牌候选 / 玩家列表）
            if (arg is IEnumerable seq && arg is not string)
            {
                foreach (var item in seq)
                {
                    if (InvolvesBoss(item)) return true;
                }
            }
        }
        catch { /* 忽略 */ }
        return false;
    }
}
