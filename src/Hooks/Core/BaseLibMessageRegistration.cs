#if STS2_111 || STS2_107
using System;
using System.Collections.Generic;
using System.Reflection;

namespace CharacterBosses.Core;

/// <summary>
/// **让 BaseLib 认出我们的自定义网络消息**（2026-11-15，玩家联机反馈的日志实证）。
///
/// 现象（`mod_log` 里反复出现）：
/// <code>
/// 勾选同步：广播失败（客人将退回整张候补表）:
///   The given key 'CharacterBosses.Core.BossPicksMessage' was not present in the dictionary.
/// </code>
///
/// 反编译 BaseLib 的 `BaseLib.Abstracts.CustomMessageWrapper`：
/// <code>
/// private static List&lt;Type&gt;? _customMessages;
/// public static List&lt;Type&gt; CustomMessages
///     =&gt; _customMessages ??= ReflectionHelper.GetSubtypesInMods&lt;ICustomMessage&gt;()   // ← 只扫一次并缓存
///                                       .OrderBy(t =&gt; t.FullName)…;
/// public int MessageType =&gt; CustomMessageToId[Message.GetType()];   // ← 这里抛 KeyNotFound
/// public static void Initialize()      // 把 CustomMessages 逐个算 id（FullName 的哈希）填进两张表
/// </code>
/// 也就是说：BaseLib 在我们这套「外壳 + 两份实现 DLL」（`CharacterBosses.dll` + `Impl107/111.dll.bin`）
/// 里**没扫到** `BossPicksMessage` —— 要么扫描发生在实现程序集加载之前（`_customMessages` 被缓存成
/// 空/不含我们的类型），要么实现程序集没被登记成「这个 mod 的程序集」。
///
/// 兜底做法：**发消息之前**把自己的类型补进去（并且让 BaseLib 重新算一遍 id）——
/// `_customMessages` 是私有静态缓存，清掉它再调一次公开的 `Initialize()` 即可。
/// 全程**反射**，不编译期引用 BaseLib（铁律：没装 BaseLib 也要能加载）。
///
/// ⚠ 两端必须得出同一套 id：BaseLib 的算法是「按 FullName 排序后顺序编号 + 哈希避让」，**确定性**的
/// ⇒ 同一个模组集合的两台机器重算结果一致，不会因为「谁先重算」而分叉。
/// </summary>
internal static class BaseLibMessageRegistration
{
    private static bool _done;
    private static int _logs;

    /// <summary>幂等；由 <see cref="BossPicksSync"/> 在真正要发消息之前调一次。</summary>
    internal static void TryFix()
    {
        if (_done) return;
        try
        {
            var type = FindWrapperType();
            if (type == null) { _done = true; return; }      // 没装 BaseLib：本来就不发消息

            // 我们的消息类型在不在 BaseLib 的缓存里？
            var field = type.GetField("_customMessages",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
            if (field == null)
            {
                if (_logs++ < 2) Logger.Log("[CharBoss] 勾选同步：BaseLib 的 CustomMessageWrapper 结构变了（没有 _customMessages）⇒ 跳过修复");
                _done = true;
                return;
            }

            bool known = false;
            try
            {
                var list = field.GetValue(null) as System.Collections.IEnumerable;
                if (list != null)
                {
                    foreach (var item in list)
                    {
                        if (item is Type t && t.FullName == "CharacterBosses.Core.BossPicksMessage") { known = true; break; }
                    }
                }
            }
            catch { /* 读不到就当不知道 */ }

            if (!known)
            {
                try { field.SetValue(null, null); } catch (Exception e) { Logger.Log("[CharBoss] 勾选同步：清 BaseLib 消息缓存失败: " + e.Message); }
                var init = type.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
                try { init?.Invoke(null, null); } catch (Exception e) { Logger.Log("[CharBoss] 勾选同步：重算 BaseLib 消息表失败: " + e.Message); }
                Logger.Log("[CharBoss] 勾选同步：已让 BaseLib 重新发现自定义消息（含 BossPicksMessage）");
            }

            _done = true;
        }
        catch (Exception e)
        {
            if (_logs++ < 3) Logger.Log("[CharBoss] 勾选同步：BaseLib 消息注册修复失败: " + e.Message);
        }
    }

    /// <summary>反射找 BaseLib 的 `CustomMessageWrapper`（不编译期引用）。</summary>
    private static Type? FindWrapperType()
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string? name = null;
                try { name = asm.GetName().Name; } catch { continue; }
                if (name == null || name.IndexOf("BaseLib", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var t = asm.GetType("BaseLib.Abstracts.CustomMessageWrapper", false);
                if (t != null) return t;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }
}
#endif
