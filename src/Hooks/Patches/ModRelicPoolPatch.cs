using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Patches;

/// <summary>
/// 给「不属于任何遗物池」的 mod 遗物兜底。
///
/// 原版实现是：<c>public RelicPoolModel Pool =&gt; ModelDb.AllRelicPools.First(p =&gt; p.AllRelicIds.Contains(Id))</c>
/// —— 我们自己加的「涅奥的祝福」不在任何池子里，于是 `First(...)` 抛
/// <c>InvalidOperationException: Sequence contains no matching element</c>。
/// 这个属性会在**生成遗物说明/悬浮提示**时被调用（说明文本里的能量图标要问它），
/// 一旦抛异常，遗物悬浮说明就出不来（godot.log 里能看到整串栈）。
///
/// 这里只在「确实找不到池子」时兜底返回第一个池子，原版遗物完全走原逻辑。
/// </summary>
[HarmonyPatch(typeof(RelicModel), "get_Pool")]
public static class ModRelicPoolPatch
{
    [HarmonyPrefix]
    public static bool Prefix(RelicModel __instance, ref RelicPoolModel __result)
    {
        try
        {
            var pools = ModelDb.AllRelicPools?.ToList();
            if (pools == null || pools.Count == 0) return true;

            var id = __instance.Id;
            foreach (var pool in pools)
            {
                if (pool.AllRelicIds.Contains(id)) return true;   // 原版遗物：交给原实现
            }

            __result = pools[0];   // mod 遗物：给一个占位池，避免整条说明崩掉
            return false;
        }
        catch
        {
            return true;
        }
    }
}
