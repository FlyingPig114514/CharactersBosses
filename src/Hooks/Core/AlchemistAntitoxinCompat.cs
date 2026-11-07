using System;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.ValueProps;

namespace CharacterBosses.Core;

/// <summary>
/// **炼金术士模组（The Alchemist，工坊 v0.14.29）「解毒剂」兼容**。
///
/// ## 症状（用户 2026-10-25 实测）
/// 二层「涂毒猎」对炼金术士：中毒层数**没超过解毒剂**时，毒本该完全无效，
/// 实际却照样结算、被格挡吃掉（「偷护甲」）。
///
/// ## 根因（反编译 `Alchemist.dll` 定位）
/// 那套解毒剂是靠**伤害属性（ValueProp）的形状**来认「这是不是中毒跳伤」的：
/// <code>
/// // AlchemistCode.Powers.AntitoxinRules
/// HasPoisonTickShape(props, dealer, cardSource) =>
///     dealer == null &amp;&amp; cardSource == null
///     &amp;&amp; props.HasFlag(ValueProp.Unblockable) &amp;&amp; props.HasFlag(ValueProp.Unpowered);
///
/// IsPoisonTick(...) => HasPoisonTickShape(...) &amp;&amp; amount > 0 &amp;&amp; 目标中毒层数 &gt;= amount;
///
/// // AlchemistCode.Powers.AntitoxinPower
/// ModifyDamageAdditive(...) => IsPoisonTick(...) ? -min(解毒剂层数, amount) : 0;
/// </code>
/// 而「带毒刺击」的设计要求中毒**能被打挡**，所以我们的结算用的是 `ValueProp.Unpowered`（**没有** Unblockable，
/// 见 <c>Powers/PoisonStrikePower.TriggerBlockableAsync</c>）⇒ 解毒剂判定为「不是中毒 tick」⇒ 一点不吸收。
///
/// ## 本文件做什么
/// 结算毒伤**之前**，先拿「原版中毒 tick 的属性」（<see cref="VanillaPoisonTickProps"/> =
/// `Unblockable | Unpowered`、dealer / cardSource 都为 null）去**问一次**她身上的 `AntitoxinPower`：
/// 调它的 `ModifyDamageAdditive`，返回值是负的就等于「解毒剂吃掉了这么多」。
/// 于是：吸收完还剩多少，就用**我们自己的**可格挡属性去结算 —— 两个设计都不破。
///
/// · 只用反射，**不引用 Alchemist.dll**（没装这个模组时这段代码什么都不做，零影响）；
/// · 反射失败时退回「按它的公式自己算」（`min(解毒剂层数, 伤害)`，且要求中毒层数 ≥ 伤害），
///   所以它以后改实现也不会把毒伤算错成 0。
/// </summary>
internal static class AlchemistAntitoxinCompat
{
    /// <summary>原版中毒跳伤用的伤害属性（`PoisonPower.Trigger` 里写死的就是这两个）。</summary>
    internal const ValueProp VanillaPoisonTickProps = ValueProp.Unblockable | ValueProp.Unpowered;

    /// <summary>目标 Power 类型名后缀（工坊包名变了也认得出来：`*.Powers.AntitoxinPower`）。</summary>
    private const string AntitoxinSuffix = ".AntitoxinPower";

    /// <summary>日志上限（每个进程最多记这么多条，别把 mod_log 刷爆）。</summary>
    private const int MaxLogs = 20;
    private static int _logs;

    /// <summary>
    /// 这段毒伤里有多少会被「解毒剂」吃掉。返回 <c>0</c> = 没有解毒剂 / 不适用（绝大多数情况）。
    /// </summary>
    /// <param name="target">正在挨毒的那个生物。</param>
    /// <param name="damage">这一次跳伤原本要打多少（= 当前中毒层数）。</param>
    /// <param name="poisonStacks">它现在的中毒层数（解毒剂要求「中毒层数 ≥ 伤害」才算一次完整的毒跳）。</param>
    internal static decimal AbsorbedBy(Creature? target, decimal damage, int poisonStacks)
    {
        if (target == null || damage <= 0m) return 0m;

        decimal absorbed = 0m;
        try
        {
            foreach (var power in target.Powers)
            {
                if (power == null) continue;
                if (!IsAntitoxin(power.GetType())) continue;

                // ① 首选：让它自己算（它的公式以后改了也不用跟着改）
                absorbed += AskPower(power, target, damage);

                // ② 反射不到方法：按反编译出来的公式自己算一版
                if (absorbed <= 0m) absorbed += FormulaFallback(power, damage, poisonStacks);
            }
        }
        catch (Exception e)
        {
            Log($"[CharBoss] 解毒剂兼容：判定失败（按无解毒剂处理）: {e.Message}");
            return 0m;
        }

        if (absorbed <= 0m) return 0m;

        decimal result = Math.Min(absorbed, damage);
        Log($"[CharBoss] 解毒剂兼容：{target.Name} 的这次毒伤 {damage} 点里，解毒剂吸收 {result} 点" +
            $"（剩 {damage - result} 点照常按「可格挡」结算）");
        return result;
    }

    private static bool IsAntitoxin(Type type)
    {
        try
        {
            string? full = type.FullName;
            if (string.IsNullOrEmpty(full)) return false;
            return full!.EndsWith(AntitoxinSuffix, StringComparison.Ordinal)
                   && full.IndexOf("Alchemist", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { return false; }
    }

    /// <summary>调它自己的 <c>ModifyDamageAdditive</c>，传入「原版中毒 tick 的形状」，取回负数。</summary>
    private static decimal AskPower(object power, Creature target, decimal damage)
    {
        try
        {
            var method = power.GetType().GetMethod(
                "ModifyDamageAdditive",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[]
                {
                    typeof(Creature), typeof(decimal), typeof(ValueProp), typeof(Creature),
                    typeof(MegaCrit.Sts2.Core.Models.CardModel),
                    typeof(MegaCrit.Sts2.Core.Entities.Cards.CardPlay),
                },
                null);
            if (method == null) return 0m;

            object? result = method.Invoke(
                power, new object?[] { target, damage, VanillaPoisonTickProps, null, null, null });

            if (result is decimal delta && delta < 0m) return -delta;
        }
        catch (Exception e)
        {
            Log("[CharBoss] 解毒剂兼容：调用 ModifyDamageAdditive 失败，改用公式: " + e.Message);
        }
        return 0m;
    }

    /// <summary>反编译出来的公式：<c>min(解毒剂层数, 伤害)</c>，前提是中毒层数 ≥ 伤害。</summary>
    private static decimal FormulaFallback(object power, decimal damage, int poisonStacks)
    {
        try
        {
            var amountProp = power.GetType().GetProperty("Amount", BindingFlags.Public | BindingFlags.Instance);
            if (amountProp == null) return 0m;

            decimal stacks = Convert.ToDecimal(amountProp.GetValue(power));
            if (stacks <= 0m) return 0m;
            if (poisonStacks < damage) return 0m;      // 它的门槛：毒还没到这一次跳伤的量，不认

            return Math.Min(stacks, damage);
        }
        catch { return 0m; }
    }

    private static void Log(string line)
    {
        if (_logs >= MaxLogs) return;
        _logs++;
        Logger.Log(line);
    }
}
