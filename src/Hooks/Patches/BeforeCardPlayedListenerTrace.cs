#if STS2_107
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Patches;

/// <summary>
/// **逐个「BeforeCardPlayed 监听者」的探针（只在 v0.107 上编译）**。
///
/// 2026-11-05 定位：「羽化」那一手卡在 `Hook.BeforeCardPlayed` 的**广播链**里
/// （trace 显示进入 BeforeCardPlayed 之后什么都没有）。这条链会依次 await 场上每个
/// 遗物 / 能力 / 牌的 `AbstractModel.BeforeCardPlayed` —— 只要其中一个永不返回，整手就卡住。
///
/// 光打 `Hook.BeforeCardPlayed` 这一层看不出是**谁**，所以这里在启动时把「所有声明了
/// `BeforeCardPlayed(CardPlay)` 的 AbstractModel 子类」逐个挂上日志前缀/后缀：
/// **有 enter 没有 leave 的那个类型就是卡住的人**。
///
/// ⚠ 只在 107 调试用：能摘掉就摘掉（挂了 100 来个模型，日志会变多，但只在事件机这只 Boss 上打）。
/// </summary>
internal static class BeforeCardPlayedListenerTrace
{
    private const string HarmonyId = "CharacterBosses.TraceBeforeCardPlayed";
    private static Harmony? _harmony;
    private static int _logs;

    internal static void Install()
    {
        try
        {
            if (_harmony != null) return;
            var prefix = AccessTools.Method(typeof(BeforeCardPlayedListenerTrace), nameof(Pre));
            var postfix = AccessTools.Method(typeof(BeforeCardPlayedListenerTrace), nameof(Post));
            if (prefix == null || postfix == null) return;

            var harmony = new Harmony(HarmonyId);
            int n = 0;
            var modelBase = typeof(AbstractModel);
            foreach (var type in AccessTools.GetTypesFromAssembly(modelBase.Assembly))
            {
                try
                {
                    if (type == null || type.IsAbstract && type.IsSealed) continue;
                    if (!modelBase.IsAssignableFrom(type)) continue;
                    var m = type.GetMethod("BeforeCardPlayed", BindingFlags.Instance | BindingFlags.Public,
                                           null, new[] { typeof(CardPlay) }, null);
                    if (m == null || m.DeclaringType != type) continue;     // 只挑**自己声明**的（覆写）
                    harmony.Patch(m, new HarmonyMethod(prefix), new HarmonyMethod(postfix));
                    n++;
                }
                catch { /* 单类失败不影响别的 */ }
            }
            _harmony = harmony;
            Logger.Log($"[TRACE] BeforeCardPlayed 探针已挂：{n} 个监听者实现");
        }
        catch (Exception e)
        {
            Logger.Log("[TRACE] BeforeCardPlayed 探针安装失败: " + e.Message);
        }
    }

    public static void Pre(object __instance)
    {
        try
        {
            if (_logs++ > 200) return;
            Logger.Log($"[TRACE] listener ENTER {__instance?.GetType().Name}");
        }
        catch { /* 忽略 */ }
    }

    public static void Post(object __instance)
    {
        try
        {
            if (_logs++ > 400) return;
            Logger.Log($"[TRACE] listener LEAVE {__instance?.GetType().Name}");
        }
        catch { /* 忽略 */ }
    }
}
#endif
