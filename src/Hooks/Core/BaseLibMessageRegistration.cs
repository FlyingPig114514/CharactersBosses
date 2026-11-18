#if STS2_111 || STS2_107
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace CharacterBosses.Core;

/// <summary>
/// **让 BaseLib 认出我们的自定义网络消息**（2026-11-15 首次发现，v0.2.772 才真正修好）。
///
/// 现象（玩家 `mod_log` 里反复出现，2026-11-18 14:26 那份日志里出现 **上百次**）：
/// <code>
/// 勾选同步：广播失败（客人将退回整张候补表）:
///   The given key 'CharacterBosses.Core.BossPicksMessage' was not present in the dictionary.
/// 观者 Boss：房主判定「可以出现」=False（询问）—— 本机实现=未装、期待客人上报 1 份、已收到 0 份
/// </code>
///
/// 反编译 BaseLib 的 `BaseLib.Abstracts.CustomMessageWrapper`：
/// <code>
/// private static List&lt;Type&gt;? _customMessages;
/// public static List&lt;Type&gt; CustomMessages
///     =&gt; _customMessages ??= ReflectionHelper.GetSubtypesInMods&lt;ICustomMessage&gt;()   // ← 只扫一次并缓存
///                                       .OrderBy(t =&gt; t.FullName)…;
/// public int MessageType =&gt; CustomMessageToId[Message.GetType()];   // ← 这里抛 KeyNotFound
/// public static Dictionary&lt;Type,int&gt; CustomMessageToId;             // Serialize 用
/// public static Dictionary&lt;int,Type&gt; IdToCustomMessage;             // Deserialize 用
/// public static void Initialize()   // i = FullName.ComputeBasicHash()；撞了就 +1 避让
/// </code>
/// 也就是说：BaseLib 的**扫描**看不见我们这个实现程序集（我们这套「外壳 + `Impl111.dll.bin`」
/// 的加载方式下，`ReflectionHelper.GetSubtypesInMods` 扫不到 `BossPicksMessage`），
/// 于是两张表里永远没有它 ⇒ **房主的结论发不出去、客人的观者身份也上报不了**。
///
/// ## 为什么 v0.2.771 那版「清缓存 + 重新 Initialize」没用
/// 玩家日志实证（2026-11-18 14:13:28）：
/// <code>
/// 勾选同步：已让 BaseLib 重新发现自定义消息（含 BossPicksMessage）
/// 勾选同步：广播失败（客人将退回整张候补表）: The given key … was not present in the dictionary.
/// </code>
/// —— 两行紧挨着。重新扫一遍还是扫不到同样的类型（扫描器看的是「模组程序集」清单，
/// 不是 `AppDomain`），所以这条路根本走不通。
///
/// ## 现在的做法：**按 BaseLib 自己的算法直接登记进它那两张表**
/// id 算法是确定性的、而且**只跟类型 FullName 有关**（`ComputeBasicHash` = MD5 → FNV-1a），
/// 同一套模组的两台机器算出来必然相同 ⇒ 两端不会因为「谁先登记」而分叉。
/// 唯一的不确定项是「撞了就 +1」，而撞车会不会发生取决于已有条目 —— 两端模组一致就一致。
///
/// 全程**反射**，不编译期引用 BaseLib（铁律：没装 BaseLib 也要能加载）。
/// </summary>
internal static class BaseLibMessageRegistration
{
    private static bool _done;
    private static int _logs;

    /// <summary>我们的消息类型（与 <c>BossPicksSync.BossPicksMessage</c> 同一个 FullName）。</summary>
    private const string OurTypeFullName = "CharacterBosses.Core.BossPicksMessage";

    /// <summary>幂等；由 <see cref="BossPicksSync"/> 在真正要发消息之前调一次。</summary>
    internal static void TryFix()
    {
        if (_done) return;
        try
        {
            var type = FindWrapperType();
            if (type == null) { _done = true; return; }      // 没装 BaseLib：本来就不发消息

            var ourType = FindOurType();
            if (ourType == null)
            {
                if (_logs++ < 2) Logger.Log("[CharBoss] 勾选同步：找不到自己的 BossPicksMessage 类型 ⇒ 跳过");
                _done = true;
                return;
            }

            var toId = GetStaticDict(type, "CustomMessageToId");     // Dictionary<Type,int>
            var toType = GetStaticDict(type, "IdToCustomMessage");   // Dictionary<int,Type>
            if (toId == null || toType == null)
            {
                if (_logs++ < 2) Logger.Log("[CharBoss] 勾选同步：BaseLib 的 CustomMessageWrapper 结构变了" +
                                            "（没有 CustomMessageToId / IdToCustomMessage）⇒ 跳过修复");
                _done = true;
                return;
            }

            if (toId.Contains(ourType)) { _done = true; return; }    // BaseLib 本来认识，什么都不用做

            // ① 先让 BaseLib 自己重扫一次（万一只是缓存成空/扫得早）
            try { type.GetField("_customMessages", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public)
                      ?.SetValue(null, null); } catch { /* 忽略 */ }
            try { type.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null); }
            catch (Exception e) { if (_logs++ < 3) Logger.Log("[CharBoss] 勾选同步：重算 BaseLib 消息表失败: " + e.Message); }

            if (toId.Contains(ourType))
            {
                Logger.Log("[CharBoss] 勾选同步：已让 BaseLib 重新发现自定义消息（含 BossPicksMessage）");
                _done = true;
                return;
            }

            // ② 还是不认识（扫描器看不见我们的实现程序集）⇒ 按 BaseLib 自己的算法直接登记
            //
            // ⚠ 只写这两张表，**不往 `_customMessages` 里加**：BaseLib 之后若再被谁调一次 `Initialize()`，
            //   它只会给 `_customMessages` 里的类型算 id —— 我们的类型不在里面 ⇒ 已经登记的条目原封不动
            //   （两端 id 一致）。反过来，如果把它塞进那个列表，`Initialize()` 会因为我们自己占着
            //   `hash` 这一格而把我们改登记到 `hash+1` → 两端幂等次数不同就会分叉。
            int? id = ComputeId(type, ourType);
            if (id == null)
            {
                if (_logs++ < 3) Logger.Log("[CharBoss] 勾选同步：算不出消息 id（BaseLib 的 ComputeBasicHash 拿不到）⇒ 放弃");
                _done = true;
                return;
            }
            toId[ourType] = id.Value;
            toType[id.Value] = ourType;

            Logger.Log($"[CharBoss] 勾选同步：BaseLib 之前不认识 BossPicksMessage（它的扫描看不见本实现程序集）" +
                       $"⇒ 已按 BaseLib 自己的算法直接登记 id={id.Value}；房主广播 / 客人上报恢复");
            _done = true;
        }
        catch (Exception e)
        {
            if (_logs++ < 3) Logger.Log("[CharBoss] 勾选同步：BaseLib 消息注册修复失败: " + e.Message);
        }
    }

    /// <summary>
    /// 用 BaseLib 的 `ComputeBasicHash`（`BaseLib.Extensions.StringExtensions` 上的扩展方法）算 id，
    /// 撞车就 +1 避让 —— 与 `CustomMessageWrapper.Initialize()` 完全同一套规则。
    /// 算不出来返回 null（调用方放弃，不写半截状态）。
    /// </summary>
    private static int? ComputeId(Type wrapperType, Type ourType)
    {
        try
        {
            string fullName = ourType.FullName ?? ourType.Name;
            var dicts = GetStaticDict(wrapperType, "IdToCustomMessage");
            if (dicts == null) return null;

            int id = 0;
            bool computed = false;

            // 优先直接调 BaseLib 自己的实现（跨版本最稳）
            try
            {
                var ext = FindTypeByFullName("BaseLib.Extensions.StringExtensions");
                var method = ext?.GetMethod("ComputeBasicHash", BindingFlags.Public | BindingFlags.Static,
                                            null, new[] { typeof(string) }, null);
                if (method != null)
                {
                    var raw = method.Invoke(null, new object[] { fullName });
                    if (raw is int i) { id = i; computed = true; }
                }
            }
            catch { /* 退到下面自己算 */ }

            // 兜底：MD5 → FNV-1a（BaseLib 源码里的算法，逐位照抄；107/111 两个版本算法相同）
            if (!computed)
            {
                byte[] digest;
                using (var md5 = System.Security.Cryptography.MD5.Create())
                    digest = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(fullName));
                int h = unchecked((int)0x811C9DC5);        // = -2128831035
                for (int i = 0; i < digest.Length; i++)
                    h = unchecked((h ^ digest[i]) * 16777619);
                id = h;
                computed = true;
            }
            if (!computed) return null;

            int guard = 0;
            while (dicts.Contains(id) && guard++ < 1024) id++;    // 撞了 +1（与 Initialize 一致）
            return id;
        }
        catch { return null; }
    }

    /// <summary>取 BaseLib 的静态字典字段（`Dictionary&lt;,&gt;` 也实现了非泛型 `IDictionary`，跨版本更稳）。</summary>
    private static IDictionary? GetStaticDict(Type wrapperType, string name)
    {
        try
        {
            var field = wrapperType.GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return field?.GetValue(null) as IDictionary;
        }
        catch { return null; }
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

    /// <summary>找 BaseLib 里某个类型（不编译期引用）。</summary>
    private static Type? FindTypeByFullName(string fullName)
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string? name = null;
                try { name = asm.GetName().Name; } catch { continue; }
                if (name == null || name.IndexOf("BaseLib", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var t = asm.GetType(fullName, false);
                if (t != null) return t;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>我们自己的消息类型（按 FullName 在本程序集里找，避免泛型/嵌套名的写法漂移）。</summary>
    private static Type? FindOurType()
    {
        try
        {
            var asm = typeof(BaseLibMessageRegistration).Assembly;
            return asm.GetType(OurTypeFullName, false) ?? asm.GetType("CharacterBosses.Core.BossPicksMessage", false);
        }
        catch { return null; }
    }
}
#endif
