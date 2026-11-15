#if STS2_111   // ⚠ 「愚者 / 塔罗牌」是 0.113.0 才有的 ⇒ 用户 2026-11-14：107 版本不加这个 Boss
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 「回响形态」的**每回合重打额度清零**（用户 2026-11-14）：
///
/// > 「你让额外回合打出的第一张牌会真的被打出 2 次」
///
/// 反编译 `EchoFormPower`：`ModifyCardPlayCount` 判的是
/// `GetInternalData&lt;Data&gt;().cardsPlayedThisTurn &gt;= Amount` —— 也就是**本回合**前 `Amount` 张重打。
/// 而 `Data` 是 `private class`、计数也是 `public int` 字段但没有对外接口，
/// 所以「额外回合」这种我们自己造的回合边界只能靠反射把它清回 0。
///
/// 反射路径：`PowerModel` 内部把 Data 挂在某个实例字段上（类型是 `EchoFormPower+Data`），
/// 这里扫描该 power 的所有实例字段，找到「类型里含 `cardsPlayedThisTurn` 字段」的那个对象再写 0。
/// 取不到就静默跳过（最多是「额外回合第一张不重打」，不会崩）。
/// </summary>
internal static class EchoFormTurnReset
{
    internal static void ResetForNewTurn(CharBossMonster? boss)
    {
        try
        {
            var power = boss?.Creature?.GetPower<EchoFormPower>();
            if (power == null) return;
            if (ResetCounter(power))
            {
                Logger.Log("[CharBoss] 回响形态：换回合 ⇒ 重打额度清零（额外回合的第一张也会被打两次）");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 回响形态清零失败: " + e.Message);
        }
    }

    private static bool ResetCounter(PowerModel power)
    {
        // ① 先试：Data 直接挂成实例字段
        if (ResetOnFieldValue(power)) return true;
        // ② 再试：走基类的 `GetInternalData<T>()`（Data 是 private 嵌套类，只能靠反射拿到类型再泛型实例化）
        return ResetViaGetInternalData(power);
    }

    private static bool ResetOnFieldValue(PowerModel power)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var field in power.GetType().GetFields(Flags))
        {
            object? data;
            try { data = field.GetValue(power); } catch { continue; }
            if (data == null) continue;

            var counter = data.GetType().GetField("cardsPlayedThisTurn", Flags);
            if (counter == null || counter.FieldType != typeof(int)) continue;
            try { counter.SetValue(data, 0); return true; } catch { return false; }
        }
        return false;
    }

    /// <summary>反射调 `PowerModel.GetInternalData&lt;T&gt;()`：先找嵌套的 `Data` 类型，再把方法泛型化调用。</summary>
    private static bool ResetViaGetInternalData(PowerModel power)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        try
        {
            Type? dataType = null;
            foreach (var nested in power.GetType().GetNestedTypes(Flags))
            {
                if (nested.GetField("cardsPlayedThisTurn", Flags) != null) { dataType = nested; break; }
            }
            if (dataType == null) return false;

            for (var t = power.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (var m in t.GetMethods(Flags))
                {
                    if (!m.Name.StartsWith("GetInternalData", StringComparison.Ordinal) || !m.IsGenericMethodDefinition) continue;
                    var data = m.MakeGenericMethod(dataType).Invoke(power, null);
                    if (data == null) continue;
                    var counter = dataType.GetField("cardsPlayedThisTurn", Flags);
                    if (counter == null) return false;
                    counter.SetValue(data, 0);
                    return true;
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 回响形态清零（GetInternalData 路径）失败: " + e.Message);
        }
        return false;
    }
}

/// <summary>
/// **手动结算一次充能球的被动**（用户 2026-11-14）：
/// 「正常回合和额外回合结束时都结算 1 次充能球」——
/// 额外回合是对我们自己造出来的回合边界，原版的「回合结束」钩子不会为它跑第二遍，
/// 所以在那个边界上手搓一次：先 `BeforeTurnEndOrbTrigger`（原版回合结束的入口），
/// 再逐颗 `TriggerPassive`（真正造成伤害/给格挡的那一下）。
/// </summary>
internal static class OrbPassiveTick
{
    internal static async Task RunAsync(CharBossMonster? boss, PlayerChoiceContext ctx)
    {
        try
        {
            var queue = boss?.Shadow?.PlayerCombatState?.OrbQueue;
            if (queue == null || queue.Orbs.Count == 0) return;

            var orbs = new List<OrbModel>(queue.Orbs);
            foreach (var orb in orbs)
            {
                if (orb == null) continue;
                try { await orb.BeforeTurnEndOrbTrigger(ctx); }
                catch { /* 单颗失败不影响其它球 */ }
                try { await orb.TriggerPassive(ctx, null); }
                catch (Exception e) { Logger.Log("[CharBoss] 球被动触发失败: " + e.Message); }
            }
            Logger.Log($"[CharBoss] 额外回合：结算了 {orbs.Count} 颗充能球的被动");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 额外回合结算球被动失败: " + e.Message);
        }
    }
}
#endif
