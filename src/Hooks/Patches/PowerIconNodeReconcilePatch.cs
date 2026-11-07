using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace CharacterBosses.Patches;

/// <summary>
/// **状态栏图标节点的权威对账**（用户 2026-10-29 遗留问题的方案 B：保留玩家侧图标，改渲染层回收残留节点）。
///
/// 反编译出来的原版实现（<c>NPowerContainer</c>，见 <c>docs/plans/_recon/NPowerContainer.txt</c>）：
/// <code>
/// private readonly List&lt;NPower&gt; _powerNodes = new();
///
/// private void Add(PowerModel power) {          // ← 由 Creature.PowerApplied 触发
///     if (power.IsVisible) {                      //   注意：**没有任何去重**
///         var n = NPower.Create(power);
///         _powerNodes.Add(n); AddChildSafely(n);
///     }
/// }
///
/// private void Remove(PowerModel power) {       // ← 由 Creature.PowerRemoved 触发
///     if (CombatManager.Instance.IsInProgress) {  //   ⚠ 战斗不在进行中就直接**什么都不做**
///         var n = _powerNodes.FirstOrDefault(x => x.Model == power);
///         if (n != null) { _powerNodes.Remove(n); n.QueueFreeSafely(); }
///     }
/// }
/// </code>
/// 两处不对称就是「一个人的状态栏上出现两个锁定图标」的全部成因：
///   · `Add` 不看「这个 Power 是不是已经有图标了」 ⇒ 同一个模型被加两次就是两个节点；
///   · `Remove` 在 `IsInProgress == false` 时直接返回（列表里那份也留着），
///     于是**清单（<c>creature.Powers</c>）里已经没有了、屏幕上还在**；
///   · `Remove` 按引用只摘**第一个**匹配项 ⇒ 一旦出现两个节点，Power 消失时只回收一个，
///     另一个变成**永久残留**（这就是用户看到的「摘掉了还挂着」）。
///
/// 本文件做两件事，都不改原版语义、只针对「我们自己也不该有第二个」的情况：
///   1. <see cref="PowerIconAddDedupePatch"/>：`Add` 之前先看一眼 —— 同类型的
///      **非实例化** Power（`InstanceType.None`，例如「跟踪锁定」）已经有节点了就不再新建；
///      若那个节点是**残留**（它的模型已经不在 `creature.Powers` 里）就**把它改绑到新模型上**，
///      屏幕上始终只有一个图标，连闪烁都没有。
///   2. <see cref="PowerIconNodeReconcile.Tick"/>：每帧节流地对账一次，把所有
///      「模型已经不在 <c>creature.Powers</c> 里」或「同一份模型的第二个节点」摘掉。
///      `creature.Powers` 是唯一真相（`RemovePowerInternal` 里就是 <c>_powers.Remove</c>）。
///
/// ⚠ 为什么不碰 `IsVisible`：它里面有 `LocalContext.IsMe(Target)`，联机时**队友身上那层
/// 图标要不要显示是原版的本地判定**。这里只回收「背后没有活 Power 的节点」，
/// 与可见性判定无关 ⇒ 不会改变原版/本模组已有的显示范围。
/// </summary>
internal static class PowerIconNodeReconcile
{
    // ───────────────────────────── 反射句柄 ─────────────────────────────

    private static readonly FieldInfo? NodesField =
        typeof(NPowerContainer).GetField("_powerNodes", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? CreatureField =
        typeof(NPowerContainer).GetField("_creature", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? UpdatePositionsMethod =
        typeof(NPowerContainer).GetMethod("UpdatePositions", BindingFlags.Instance | BindingFlags.NonPublic);

    // ───────────────────────────── 容器登记表 ─────────────────────────────

    /// <summary>
    /// 场上所有 <see cref="NPowerContainer"/>（每只生物的状态栏一个）。
    /// 由 <c>NPowerContainer.SetCreature</c> 的 Postfix 与 <c>Add</c> 的 Prefix 登记 ——
    /// 不自己扫场景树（那是每帧开销）。
    /// </summary>
    private static readonly List<NPowerContainer> Containers = new();

    private static readonly Dictionary<int, string> LastReport = new();

    internal static void Register(NPowerContainer? container)
    {
        try
        {
            if (container == null) return;
            if (!GodotObject.IsInstanceValid(container)) return;
            if (Containers.Contains(container)) return;
            Containers.Add(container);
        }
        catch { /* 忽略 */ }
    }

    // ───────────────────────────── 数据访问 ─────────────────────────────

    private static List<NPower>? NodesOf(NPowerContainer container)
    {
        try { return NodesField?.GetValue(container) as List<NPower>; }
        catch { return null; }
    }

    private static Creature? CreatureOf(NPowerContainer container)
    {
        try { return CreatureField?.GetValue(container) as Creature; }
        catch { return null; }
    }

    /// <summary>取节点上的模型（原版 `NPower.Model` 在没设过时会抛，这里兜住）。</summary>
    private static PowerModel? ModelOf(NPower? node)
    {
        if (node == null) return null;
        try
        {
            if (!GodotObject.IsInstanceValid(node)) return null;
            return node.Model;
        }
        catch { return null; }
    }

    /// <summary>`creature.Powers` 的**引用集合**（唯一真相；拿不到就返回 null = 本轮什么都不做）。</summary>
    private static HashSet<PowerModel>? LivePowers(Creature? creature)
    {
        try
        {
            if (creature?.Powers == null) return null;
            var set = new HashSet<PowerModel>(ReferenceComparer.Instance);
            foreach (var p in creature.Powers)
            {
                if (p != null) set.Add(p);
            }
            return set;
        }
        catch { return null; }
    }

    /// <summary>引用相等比较器（`PowerModel` 是模型对象，不能用默认比较器赌它没重写 Equals）。</summary>
    private sealed class ReferenceComparer : IEqualityComparer<PowerModel>
    {
        internal static readonly ReferenceComparer Instance = new();

        public bool Equals(PowerModel? a, PowerModel? b) => ReferenceEquals(a, b);

        public int GetHashCode(PowerModel obj) => RuntimeHelpers.GetHashCode(obj);
    }

    // ───────────────────────────── ① 新增时的去重 ─────────────────────────────

    /// <summary>
    /// 原版 <c>NPowerContainer.Add</c> 的**前置判定**：返回 <c>true</c> 交给原版新建节点，
    /// 返回 <c>false</c> 表示我们自己处理完了（不新建）。
    /// </summary>
    internal static bool HandleAdd(NPowerContainer? container, PowerModel? power)
    {
        Register(container);
        try
        {
            if (container == null || power == null) return true;

            // 实例化 Power（一件装备/一张牌各一份的那种）本来就允许同类型多个 ⇒ 一律放行。
            // 只有 `InstanceType.None`（同一生物上**不可能**有两份实例，否则
            // `Creature.ApplyPowerInternal` 会抛）才能断言「同类型的第二个节点一定是重复」。
            if (power.InstanceType != PowerInstanceType.None) return true;

            var nodes = NodesOf(container);
            if (nodes == null || nodes.Count == 0) return true;

            var live = LivePowers(CreatureOf(container));
            if (live == null) return true;

            NPower? aliveNode = null;      // 同类型、且模型**还活着**的节点
            NPower? residualNode = null;   // 同类型、但模型已经不在清单里（残留）
            foreach (var node in nodes)
            {
                var model = ModelOf(node);
                if (model == null) continue;
                if (model.GetType() != power.GetType()) continue;
                if (live.Contains(model)) aliveNode = node;
                else residualNode = node;
            }

            if (aliveNode != null)
            {
                // 这个模型（或同类型的另一份）已经有图标了 —— 再建一个就是用户看到的「2 个」。
                // 同一个「容器 + Power 类型」只报一次（这条路径可能每帧被撞到）。
                if (NoteOnce(container, power, "dupe"))
                {
                    Logger.Log($"[CharBoss] 状态栏图标：{EntryOf(power)} 已有节点，" +
                               $"拦下重复创建（原版 Add 不去重；模型 {Describe(aliveNode)}）");
                }
                return false;
            }

            if (residualNode != null)
            {
                // 有一个残留在屏幕上（它的模型已经没了）⇒ **改绑**到这个新模型，不新建节点。
                // 这样屏幕上从头到尾只有一个图标，不会出现「旧的没走、新的又来」的一瞬间。
                try
                {
                    residualNode.Model = power;
                    Logger.Log($"[CharBoss] 状态栏图标：{EntryOf(power)} 复用残留节点（改绑新模型，" +
                               "避免屏幕上出现第二个图标）");
                    return false;
                }
                catch (Exception e)
                {
                    Logger.Log($"[CharBoss] 状态栏图标：{EntryOf(power)} 改绑残留节点失败（{e.Message}），交给原版新建");
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 状态栏图标去重失败（交给原版）: " + e.Message);
        }
        return true;
    }

    /// <summary>
    /// 原版 <c>NPowerContainer.Remove</c> 漏掉的那一半：它在
    /// <c>CombatManager.Instance.IsInProgress == false</c> 时**整段跳过**
    /// （连 `_powerNodes` 里的那一项都不摘），于是清单里已经没有这份 Power、屏幕上却还在。
    /// 这里在原版之后补一刀：模型不在清单里而节点还在 ⇒ 直接摘掉。
    /// </summary>
    internal static void HandleRemoveFallback(NPowerContainer? container, PowerModel? power)
    {
        try
        {
            if (container == null || power == null) return;
            var nodes = NodesOf(container);
            if (nodes == null || nodes.Count == 0) return;

            var live = LivePowers(CreatureOf(container));
            if (live == null || live.Contains(power)) return;    // 清单里还有 ⇒ 不是这次要回收的

            bool removed = false;
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                var node = nodes[i];
                if (node == null) { nodes.RemoveAt(i); continue; }
                var model = ModelOf(node);
                if (!ReferenceEquals(model, power)) continue;

                nodes.RemoveAt(i);
                Free(node);
                removed = true;
            }

            if (!removed) return;
            try { UpdatePositionsMethod?.Invoke(container, null); } catch { /* 忽略 */ }
            Logger.Log($"[CharBoss] 状态栏图标：{EntryOf(power)} 已被摘除但原版没回收节点" +
                       "（战斗不在进行中时 Remove 会整段跳过）⇒ 我们自己摘掉");
        }
        catch { /* 忽略 */ }
    }

    // ───────────────────────────── ② 周期对账（回收残留） ─────────────────────────────

    /// <summary>每帧驱动里调（内部自带节流）；返回这一轮摘掉的节点总数。</summary>
    internal static void Tick(ulong nowUsec, ulong intervalUsec)
    {
        try
        {
            if (nowUsec - _lastUsec < intervalUsec) return;
            _lastUsec = nowUsec;
            ReconcileAll();
        }
        catch { /* 绝不影响战斗 */ }
    }

    private static ulong _lastUsec;

    /// <summary>把所有登记过的状态栏对账一遍（内部按容器逐个处理）。</summary>
    internal static void ReconcileAll()
    {
        if (Containers.Count == 0) return;

        // 清掉已经被释放的容器（生物节点销毁后）
        for (int i = Containers.Count - 1; i >= 0; i--)
        {
            NPowerContainer? c = null;
            try
            {
                c = Containers[i];
                if (c != null && GodotObject.IsInstanceValid(c)) continue;
            }
            catch { /* 失效 */ }
            Containers.RemoveAt(i);
            try
            {
                if (c != null) LastReport.Remove(RuntimeHelpers.GetHashCode(c));
            }
            catch { /* 忽略 */ }
        }

        int removedTotal = 0;
        foreach (var container in Containers)
        {
            try { removedTotal += Reconcile(container); }
            catch { /* 单个状态栏失败不影响其它 */ }
        }

        if (removedTotal > 0)
        {
            Logger.Log($"[CharBoss] 状态栏图标对账：回收 {removedTotal} 个残留/重复图标节点" +
                       $"（场上 {Containers.Count} 个状态栏）");
        }
    }

    /// <summary>对账一个状态栏：节点必须「背后有一份活着的 Power」，且同一份模型只允许一个节点。</summary>
    private static int Reconcile(NPowerContainer? container)
    {
        if (container == null || !GodotObject.IsInstanceValid(container)) return 0;

        var nodes = NodesOf(container);
        if (nodes == null || nodes.Count == 0) return 0;

        var creature = CreatureOf(container);
        var live = LivePowers(creature);
        if (live == null) return 0;      // 拿不到清单就不动（宁可留着也不要误删）

        int removed = 0;
        var kept = new HashSet<PowerModel>(ReferenceComparer.Instance);
        var reasons = new List<string>();

        // 倒着走：删元素不影响还没访问的下标；`kept` 记的是「已经保留下来的模型」，
        // 于是**第二个**同模型节点（下标更小的那个）会被认成重复 —— 与原版 Remove 摘第一个相反，
        // 但两者都是同一个 Power 的图标，摘哪个屏幕上都一样。
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            var node = nodes[i];
            string? why = null;
            PowerModel? model = null;

            if (node == null || !GodotObject.IsInstanceValid(node))
            {
                why = "节点已失效";
            }
            else
            {
                model = ModelOf(node);
                if (model == null) why = "取不到模型";
                else if (!live.Contains(model)) why = "背后已经没有这份 Power（残留）";
                else if (!kept.Add(model)) why = "同一份 Power 的第二个节点";
            }

            if (why == null) continue;

            nodes.RemoveAt(i);
            removed++;
            reasons.Add($"{EntryOf(model) ?? "?"}：{why}");
            Free(node);
        }

        if (removed > 0)
        {
            // 摘完要重排位置（原版 Remove 里也是摘完就 UpdatePositions）
            try { UpdatePositionsMethod?.Invoke(container, null); }
            catch { /* 重排失败只是位置难看 */ }

            // 同一个状态栏同样的原因只报一次，避免刷屏（对账是周期性的）
            string report = string.Join("；", reasons);
            int key = RuntimeHelpers.GetHashCode(container);
            if (!LastReport.TryGetValue(key, out var last) || last != report)
            {
                LastReport[key] = report;
                Logger.Log($"[CharBoss] 状态栏图标对账（{creature?.Name ?? "?"}）：{report}");
            }
        }

        return removed;
    }

    /// <summary>把节点从树上摘掉并释放（先 RemoveChild 让它**这一帧就消失**，再 QueueFree）。</summary>
    private static void Free(NPower? node)
    {
        if (node == null) return;
        try
        {
            if (!GodotObject.IsInstanceValid(node)) return;
            var parent = node.GetParent();
            if (parent != null) parent.RemoveChild(node);
        }
        catch { /* 已经不在树上 */ }
        try
        {
            if (GodotObject.IsInstanceValid(node)) node.QueueFree();
        }
        catch { /* 忽略 */ }
    }

    // ───────────────────────────── 日志小工具 ─────────────────────────────

    /// <summary>同一条「容器 + Power 类型 + 原因」只报一次（对账/去重都是高频路径，不能刷屏）。</summary>
    private static readonly HashSet<string> Noted = new();

    private static bool NoteOnce(NPowerContainer? container, PowerModel? power, string why)
    {
        try
        {
            string key = $"{RuntimeHelpers.GetHashCode(container!)}|{power?.GetType().Name ?? "?"}|{why}";
            return Noted.Add(key);
        }
        catch { return false; }
    }

    private static string? EntryOf(PowerModel? power)
    {
        try { return power?.Id.Entry; } catch { return null; }
    }

    private static string Describe(NPower node)
    {
        try
        {
            var model = ModelOf(node);
            return EntryOf(model) ?? "?";
        }
        catch { return "?"; }
    }

    /// <summary>新战斗 / 战斗结束：把容器登记表与日志去重表清干净（生物节点每场都是新的）。</summary>
    internal static void Reset()
    {
        try
        {
            Containers.Clear();
            LastReport.Clear();
            Noted.Clear();
            _lastUsec = 0;
        }
        catch { /* 忽略 */ }
    }
}

/// <summary>登记状态栏：`NCreatureStateDisplay.SetCreature` 会给每个生物的状态栏调一次。</summary>
[HarmonyPatch(typeof(NPowerContainer), "SetCreature")]
public static class PowerIconContainerRegisterPatch
{
    [HarmonyPostfix]
    public static void Postfix(NPowerContainer __instance)
    {
        PowerIconNodeReconcile.Register(__instance);
    }
}

/// <summary>新建图标之前去重（见 <see cref="PowerIconNodeReconcile.HandleAdd"/>）。</summary>
[HarmonyPatch(typeof(NPowerContainer), "Add")]
public static class PowerIconAddDedupePatch
{
    [HarmonyPrefix]
    public static bool Prefix(NPowerContainer __instance, PowerModel power)
    {
        try { return PowerIconNodeReconcile.HandleAdd(__instance, power); }
        catch { return true; }
    }
}

/// <summary>
/// 摘图标时把原版漏掉的那一半补上：`Remove` 里 `IsInProgress == false` 就整段跳过，
/// 于是**战斗刚结束/还没开始**时摘除会留下节点（清单没了、屏幕上还在）。
/// 这里只在原版**什么都没做**时接手（Postfix 里判断：模型已经不在清单里、节点还在）。
/// </summary>
[HarmonyPatch(typeof(NPowerContainer), "Remove")]
public static class PowerIconRemoveFallbackPatch
{
    [HarmonyPostfix]
    public static void Postfix(NPowerContainer __instance, PowerModel power)
    {
        try { PowerIconNodeReconcile.HandleRemoveFallback(__instance, power); }
        catch { /* 忽略 */ }
    }
}
