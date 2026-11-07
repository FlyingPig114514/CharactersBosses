using System;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.UI;

/// <summary>
/// 手牌栏最上面那一条：遗物图标（从右往左，角色初始遗物在最右）+ 能量球 + 辉星。
///
/// **不用 HBoxContainer，改成手动从右往左摆**，原因（都实测过）：
///   · 原版 <c>NEnergyCounter.AnimIn()</c> 的实现是「把自身 Position 从 (-480,128) 补间到 (0,0)」，
///     放进容器里会被容器每帧改回容器位置 —— 结果只剩数字、球体跑到别处；
///     所以这里建完节点直接给终点位置，不调 AnimIn；
///   · 「能量球在所有遗物右侧、且和遗物排在同一行」用容器描述要绕一圈，手动排布就是几行代码。
///
/// 节点全部是原版的（见 <c>BossStatusRow.Icons.cs</c>）：遗物 <c>NRelicInventoryHolder</c>
/// （尺寸/悬浮说明/点击都和自己遗物栏一致，点击打开 <c>NInspectRelicScreen</c>）、
/// 能量 <c>NEnergyCounter</c>（原版能量球，数字在球内、字体一致）。
/// </summary>
public static partial class BossStatusRow
{
    private const float Gap = 6f;
    private const float MinHeight = 36f;
    private const float RelicFallback = 56f;   // 量不到原版图标尺寸时的兜底边长
    private const float RowBottomPad = 10f;    // 状态条与卡面之间的额外留白（防止遗物图标压到卡面）

    /// <summary>状态条整体再往上抬这么多（画在手牌栏上边界之外，进一步拉开与卡面的距离）。</summary>
    private const float RowLift = 12f;

    /// <summary>遗物图标与能量球的统一视觉边长（两者对齐成一行就靠它）。</summary>
    private const float IconSide = 56f;

    /// <summary>能量球显示盒的边长（用户要求「放大到和玩家那颗差不多大」，数字才不会挤）。</summary>
    private const float EnergyBox = 72f;

    /// <summary>叠在球心的能量数字字号。</summary>
    private const float EnergyFontSize = 26f;

    /// <summary>药水格子名的前缀（清空时按名字自检用，见 <see cref="Fill"/>）。</summary>
    internal const string PotionCellPrefix = "BossPotion_";

    public static Control Ensure(Control parent, Control? existing)
    {
        if (existing != null && GodotObject.IsInstanceValid(existing)) return existing;

        var row = new Control
        {
            Name = "BossStatusRow",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        parent.AddChild(row);
        return row;
    }

    /// <summary>
    /// 每一行上次填充的**整数指纹**（遗物数 / 药水数 / 药水类型求和 / 能量 / 辉星 / 药水版本 /
    /// 本回合药水计划 / 是否被击晕）。
    /// 不用字符串拼接 —— 每次拼字符串会每帧分配、把游戏拖到十几帧（用户 2026-10-26 实测）。
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<Control, (int Relics, int Potions, int PotionSum, int Energy, int Stars, int PotionVersion, int PotionPlan, int Stunned)> FillStamps = new();

    /// <summary>
    /// 每一行上次画出来的**遗物节点**（顺序 = 显示顺序，下标 0 在最右）。
    ///
    /// 为什么要缓存：原版 <c>NRelicInventoryHolder.Create</c> 是「实例化一个场景 + 挂贴图 + 装交互」，
    /// 一次 Fill 要建 8～10 个。以前每次刷新（出牌、喝药、能力变化……每秒好几次）全部重建，
    /// 这是状态条里唯一的「重活」（用户 2026-10-26：boss 战掉帧）。
    /// 遗物列表没变（顺序与 entry 完全一致）时直接把这些节点挂回去，一个都不新建。
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<Control, System.Collections.Generic.List<(string Entry, Control Node)>?> RelicCache = new();

    /// <summary>
    /// 重建内容：先药水、再遗物（反向 → 初始遗物最右），再能量球（遗物正右方），最后辉星。
    ///
    /// ⚠ **清空必须同步 <see cref="Node.RemoveChild"/>，不能只 QueueFree**（2026-10-26 定位到的坑）：
    /// <c>QueueFree</c> 只是「排队到本帧末删除」，节点此刻**仍然挂在树上**。于是：
    ///   · <c>row.GetChildCount()</c> 把「上一批正要被删的旧格子」也算进去（日志里 15/29/31 乱跳的真相）；
    ///   · 同一次 <c>PlaceBesideCards</c> 的排版会把旧格子算进宽度与下标 ⇒ 整条状态栏被算成一倍宽、
    ///     药水/遗物被挤到屏幕外（日志实测 rowW 从 872 跳到 2050、x 变成 -180）。
    /// 现在先 RemoveChild 再 QueueFree，排版看到的**只有**这一轮真正要画的格子。
    /// </summary>
    public static void Fill(Control row, CharBossMonster boss)
    {
        if (row == null || !GodotObject.IsInstanceValid(row)) return;

        // 内容没变就直接返回（Fill 要重建整排子节点，很贵）
        var shadow = boss?.Shadow;
        var pcs0 = shadow?.PlayerCombatState;
        if (shadow != null && pcs0 != null)
        {
            try
            {
                int potions = 0, sum = 0;
                foreach (var potion in Core.BossPotions.Held(shadow))
                {
                    potions++;
                    sum = unchecked(sum * 31 + (potion?.GetType().Name?.GetHashCode() ?? 0));
                }
                // 「本回合要喝哪几瓶」也要进指纹：每回合换剧本时药水栏本身没变，
                // 但意图要跟着换（否则上一回合的「强化」还挂在别的瓶子上）
                int plan = 0;
                try
                {
                    foreach (var type in boss?.PlannedPotions ?? new System.Collections.Generic.List<Type>())
                        plan = unchecked(plan * 31 + (type?.Name?.GetHashCode() ?? 0));
                }
                catch { /* 读不到就按 0 算 */ }

                // 被击晕（吹哨等）⇒ 药水栏要撤掉「本回合要喝」的箭头、能量球旁边也不再是「本回合」语义
                // ⇒ 这一位也必须进指纹，否则 Fill 直接 return、箭头顶着不走（用户 2026-11-04 的需求）。
                int stunned = 0;
                try { stunned = boss?.Stunned == true ? 1 : 0; } catch { stunned = 0; }

                var stamp = (Relics: shadow.Relics?.Count ?? 0, Potions: potions, PotionSum: sum,
                             Energy: pcs0.Energy, Stars: pcs0.Stars, PotionVersion: Core.BossPotions.Version,
                             PotionPlan: plan, Stunned: stunned);
                if (FillStamps.TryGetValue(row, out var old) && old == stamp) return;
                FillStamps[row] = stamp;
            }
            catch { /* 算不出来就照常重建 */ }
        }

        // 遗物的显示顺序（从右往左）：起始遗物在最右，**涅奥的祝福排第二**（用户设计），
        // 其余按定义顺序往左铺。
        var swFill = System.Diagnostics.Stopwatch.StartNew();
        var ordered = OrderRelics(shadow);

        // 这一轮能复用的节点：能量球（避免反复播粒子特效）+ 遗物图标（顺序没变就不用重建）
        var energy = EnergyOf(row);
        var reusableRelics = ReusableRelics(row, ordered);

        // ── 清空：**同步 RemoveChild 再 QueueFree** ──
        // 复用的节点（能量球 / 遗物图标）**留在树上不动**，最后统一用 MoveChild 排序 ——
        // 这样它们不会有「出树再入树」的抖动（NClickableControl 的点击/悬浮说明都靠树内状态）。
        int removed = 0;
        foreach (var child in row.GetChildren())
        {
            if (!GodotObject.IsInstanceValid(child)) continue;
            bool reuse = ReferenceEquals(child, energy)
                         || (reusableRelics != null && child is Control control && ContainsNode(reusableRelics, control));
            if (reuse) continue;
            row.RemoveChild(child);      // ← 关键：立刻离开场景树，本帧的排版不再看见它
            child.QueueFree();
            removed++;
        }

        var pcs = shadow?.PlayerCombatState;
        if (shadow == null || pcs == null)
        {
            RelicCache[row] = null;
            return;
        }

        // 药水（炼药猎）：先建格子，最后排到遗物左侧（子节点下标越小越靠左）
        var potionCells = BossPotionRow.AddTo(row, boss!);

        // 遗物：顺序没变就复用原节点，变了/新增的才新建
        var slot = new (string Entry, Control? Node)[ordered.Count];
        for (int i = 0; i < ordered.Count; i++)
        {
            var relic = ordered[i];
            string entry = SafeEntry(relic);
            Control? node = null;
            if (reusableRelics != null && i < reusableRelics.Count && reusableRelics[i].Entry == entry)
            {
                node = reusableRelics[i].Node;
            }
            if (node == null || !GodotObject.IsInstanceValid(node))
            {
                node = AddRelic(row, shadow, relic);   // 新建的节点追加在末尾，下面再挪到位
            }
            slot[i] = (entry, node);
        }

        // 期望的最终顺序：药水 → 遗物（下标 0 在最右，所以从后往前）→ 能量球
        var target = new System.Collections.Generic.List<Node>(potionCells.Count + ordered.Count + 1);
        target.AddRange(potionCells);
        for (int i = ordered.Count - 1; i >= 0; i--)
        {
            var node = slot[i].Node;
            if (node != null && GodotObject.IsInstanceValid(node)) target.Add(node);
        }

        var energyNode = MakeEnergyDisplay(shadow, pcs);
        if (energyNode != null)
        {
            // ⚠ 能量球是**按影武者缓存的**（一个影武者只有一个），所以它可能已经挂在别的那一行上
            // （多 Boss 的「补一条状态栏」共用同一个 Player）—— 那种情况不要抢、也不要参与本行排序。
            if (ReferenceEquals(energyNode.GetParent(), row)) target.Add(energyNode);
            else if (energyNode.GetParent() == null)
            {
                row.AddChild(energyNode);
                target.Add(energyNode);
            }
            SetEnergyOf(row, energyNode);
        }

        // 只调顺序、不重建：MoveChild 不会触发 _ExitTree/_EnterTree
        for (int i = 0; i < target.Count; i++)
        {
            if (i < row.GetChildCount() && ReferenceEquals(row.GetChild(i), target[i])) continue;
            row.MoveChild(target[i], i);
        }

        // 遗物节点缓存（有一个建不出来就整排作废，下次重建）
        var built = new System.Collections.Generic.List<(string Entry, Control Node)>(ordered.Count);
        foreach (var one in slot)
        {
            if (one.Node != null && GodotObject.IsInstanceValid(one.Node)) built.Add((one.Entry, one.Node));
        }
        RelicCache[row] = built.Count == ordered.Count ? built : null;

        // ── 诊断：这一轮到底画了几个药水格子、子节点数与期望是否一致 ──
        // 期望值 = 药水格子 + 遗物格子 + 能量球。不一致就是要查的信号，所以无条件打。
        try
        {
            int cells = CountPotionCells(row);
            int energyInRow = (energyNode != null && GodotObject.IsInstanceValid(energyNode)
                               && ReferenceEquals(energyNode.GetParent(), row)) ? 1 : 0;
            int want = potionCells.Count + ordered.Count + energyInRow;
            bool mismatch = cells != potionCells.Count || row.GetChildCount() != want;
            if (mismatch || _fillLogs < 40)
            {
                if (_fillLogs < 40) _fillLogs++;
                Logger.Log($"[CharBoss] 状态条 Fill: 药水={potionCells.Count}(格子 {cells}) 遗物={ordered.Count} " +
                           $"复用遗物={(reusableRelics != null ? "是" : "否")} 清掉={removed} " +
                           $"rowChildren={row.GetChildCount()}/{want} rowW={ContentWidth(row):0}");
            }
            Core.PerfLog.NoteFill(mismatch, swFill.Elapsed.TotalMilliseconds);
        }
        catch { /* 忽略 */ }

        // 辉星：**原版 <c>NStarCounter</c> 节点本身**（star_counter.tscn），贴在能量球左下角
        ShowStars(shadow, pcs, row);

        PruneIfNeeded();
    }

    /// <summary>退回用：无参照时贴着面板右缘排（子节点摆位见 <c>BossStatusRow.Pin.cs</c>）。</summary>
    public static void Place(Control row, float width)
    {
        float height = Height(row);
        row.Size = new Vector2(width, height);
        row.Position = new Vector2(0f, -RowLift);
        LayoutChildren(row, width);
    }

    /// <summary>这条目前实际需要的高度（含与卡面之间的留白）。</summary>
    public static float Height(Control row)
    {
        float height = MinHeight;
        foreach (var child in row.GetChildren())
        {
            if (child is Control control && GodotObject.IsInstanceValid(control))
            {
                height = Math.Max(height, SizeOf(control).Y);
            }
        }
        return height + RowBottomPad;
    }

    private static Vector2 SizeOf(Control control)
    {
        var min = control.CustomMinimumSize;
        if (min.X > 1f && min.Y > 1f) return min;      // 遗物这类：Fit() 已经写回缩放后的尺寸

        min = control.GetCombinedMinimumSize();
        if (min.X > 1f && min.Y > 1f) return min;

        // ⚠ 锚点撑满型的节点（原版 NEnergyCounter 就是）这两个都是 0，
        // 只有它自己的 Size 才是真实尺寸。早先直接兜底成 56，导致能量球被当成 56×56，
        // 行高按 56 算 → 128 的球从行顶往下画，压到卡面上（用户实测「高度不对」）。
        var actual = control.Size;
        if (actual.X > 1f && actual.Y > 1f) return actual;

        return new Vector2(RelicFallback, RelicFallback);
    }

    /// <summary>当前正在复用的原版能量球节点（刷新时不销毁，避免闪烁）。</summary>
    internal static Control? EnergyCounterNode;

    private static int _fillLogs;

    /// <summary>
    /// **每一行各自的**能量球节点（多 Boss 同场时会有两条以上状态栏）。
    /// 以前只有一个静态 <see cref="EnergyCounterNode"/>，第二条状态栏会去抢第一条那个节点
    /// （用户实测：第二只只显示能量和星、遗物图标全堆在原点）。
    /// 静态字段保留为「最近一次」的镜像，老代码路径不至于空引用。
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<Control, Control?> EnergyByRow = new();

    internal static void SetEnergyOf(Control? row, Control? node)
    {
        if (row == null) { EnergyCounterNode = node; return; }
        EnergyByRow[row] = node;
        EnergyCounterNode = node;      // 兼容镜像
    }

    internal static Control? EnergyOf(Control? row)
    {
        if (row != null && EnergyByRow.TryGetValue(row, out var node)) return node;
        return EnergyCounterNode;
    }

    /// <summary>
    /// 能量球**中心**在手牌栏坐标系里的 x（能量那一栏的正下方要放独立意图，靠它对位）。
    /// 必须在 <see cref="Place"/> 之后取，否则 Position 还是上一轮的值。
    /// </summary>
    public static float EnergyCenterX(Control? row)
    {
        // 用**这一行自己的**能量球（多面板时静态镜像可能指向别的那一行）
        var node = row != null ? EnergyOf(row) : EnergyCounterNode;
        if (node == null || !GodotObject.IsInstanceValid(node)) return 0f;
        var size = SizeOf(node);
        float center = node.Position.X + size.X * 0.5f;
        // 状态条自身整体上抬过（RowLift），x 不受影响，这里只防御 row 为空的情况
        if (row == null || !GodotObject.IsInstanceValid(row)) return center;
        return center + row.Position.X;
    }

    /// <summary>遗物的显示顺序：起始遗物在最右，涅奥的祝福第二，其余按定义顺序往左铺。</summary>
    private static System.Collections.Generic.List<RelicModel> OrderRelics(Player? shadow)
    {
        var ordered = new System.Collections.Generic.List<RelicModel>();
        var relics = shadow?.Relics;
        if (relics == null) return ordered;

        ordered.AddRange(relics);
        var neow = ordered.FirstOrDefault(r => SafeEntry(r) == "NEOW_BLESSING");
        if (neow != null && ordered.Count > 1 && ordered.IndexOf(neow) != 1)
        {
            ordered.Remove(neow);
            ordered.Insert(Math.Min(1, ordered.Count), neow);
        }
        return ordered;
    }

    /// <summary>上一次画的遗物节点能不能原样复用（数量、顺序、节点有效性全对上才算）。</summary>
    private static System.Collections.Generic.List<(string Entry, Control Node)>? ReusableRelics(
        Control row, System.Collections.Generic.List<RelicModel> ordered)
    {
        if (ordered.Count == 0) return null;
        if (!RelicCache.TryGetValue(row, out var cached) || cached == null) return null;
        if (cached.Count != ordered.Count) return null;

        for (int i = 0; i < cached.Count; i++)
        {
            if (cached[i].Node == null || !GodotObject.IsInstanceValid(cached[i].Node)) return null;
            if (cached[i].Entry != SafeEntry(ordered[i])) return null;
        }
        return cached;
    }

    private static bool ContainsNode(System.Collections.Generic.List<(string Entry, Control Node)> list, Control node)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i].Node, node)) return true;
        }
        return false;
    }

    /// <summary>数一下行里真正的药水格子（不是「本回合该有几瓶」）。</summary>
    private static int CountPotionCells(Control row)
    {
        int count = 0;
        foreach (var child in row.GetChildren())
        {
            if (child is not Node n || !GodotObject.IsInstanceValid(n)) continue;
            if (n.Name.ToString().StartsWith(PotionCellPrefix, StringComparison.Ordinal)) count++;
        }
        return count;
    }

    /// <summary>
    /// 这几张表都以「行节点」为键，面板每场战斗会重建（旧行被 QueueFree）⇒ 偶尔清一次，
    /// 免得整局跑下来把已经释放的节点一直攥在内存里。
    /// </summary>
    private static void PruneIfNeeded()
    {
        if (FillStamps.Count <= 8 && RelicCache.Count <= 8 && EnergyByRow.Count <= 8) return;
        try
        {
            PruneDead(FillStamps);
            PruneDead(RelicCache);
            PruneDead(EnergyByRow);
            RemoveDeadStarBindings();
        }
        catch { /* 忽略 */ }
    }

    private static void PruneDead<T>(System.Collections.Generic.Dictionary<Control, T> map)
    {
        var dead = new System.Collections.Generic.List<Control>();
        foreach (var key in map.Keys)
        {
            if (key == null || !GodotObject.IsInstanceValid(key)) dead.Add(key!);
        }
        foreach (var key in dead) map.Remove(key);
    }

    private static string SafeEntry(RelicModel relic)
    {
        try { return relic.Id.Entry; } catch { return string.Empty; }
    }
}
