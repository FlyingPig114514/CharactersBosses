using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using CharacterBosses.Monsters;

namespace CharacterBosses.UI;

/// <summary>
/// Boss 的充能球显示 —— 节点、槽位、布局、动画**全部用原版**，我们只补原版不替我们做的事：
///
///   1. 补建 <c>NOrbManager</c>：我们的 Boss 被判定成「不是玩家」，原版不会给它建；
///   2. 补建槽位：原版是靠 <c>CombatManager.CombatSetUp</c> 事件调 <c>AddSlotAnim</c> 建空槽，
///      而我们在 <c>AfterAddedToRoom</c> 才挂 manager（事件已经过了）→ 必须自己补一次，
///      否则「管理器在、槽位没有、球也画不出来」；
///   3. 摆位置：直接调原版私有的 <c>NCreature.SetOrbManagerPosition()</c>（用角色自己的 OrbPosition 标记）。
///      **不要自己算偏移** —— 之前算到立绘上方，超出 NCreature 的矩形被裁掉，于是「球位全看不见」；
///   4. 手动往队列塞球（裂核兜底）后补调 <c>AddOrbAnim</c>，让 UI 长出球节点。
/// </summary>
public static partial class BossOrbDisplay
{
    private static readonly FieldInfo? ManagerField =
        typeof(NCreature).GetField("<OrbManager>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? SetPositionMethod =
        typeof(NCreature).GetMethod("SetOrbManagerPosition", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? SlotsField =
        typeof(NOrbManager).GetField("_orbs", BindingFlags.Instance | BindingFlags.NonPublic);

    // NOrbManager.Player 不是 public，只能拿私有字段 _creatureNode 再问 Entity.Player
    private static readonly FieldInfo? CreatureField =
        typeof(NOrbManager).GetField("_creatureNode", BindingFlags.Instance | BindingFlags.NonPublic);

    private static int _lastOrbCount = -1;

    public static void Attach(CharBossMonster boss)
    {
        try
        {
            var node = NodeOf(boss);
            if (node == null) return;
            if (node.OrbManager != null) return;

            var manager = NOrbManager.Create(node, true);   // isLocal:true → 显示被动/激发数字
            ManagerField?.SetValue(node, manager);
            node.AddChildSafely(manager);

            // 槽位：原版由 CombatSetUp 建，我们挂得晚，自己补
            int capacity = CapacityOf(boss);
            int slots = SlotCount(manager);
            if (slots == 0 && capacity > 0)
            {
                manager.AddSlotAnim(capacity);
                Logger.Log($"[CharBoss] 球位：补建 {capacity} 个槽位（原版事件已过）");
            }

            // 槽是按「非本地」样式造的（影子 Player 的 NetId 不等于本地）→ 空槽不画、数值不常显，
            // 这里掰成本地样式（原版 NOrb 的 _isLocal 一个字段决定这三件事）
            OrbLocalVisuals.Force(manager);

            Reposition(node);
            _lastOrbCount = OrbCount(boss);
            Logger.Log($"[CharBoss] 球位：已挂上原版 NOrbManager（容量 {capacity}，槽位 {SlotCount(manager)}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 球位挂载失败: " + e);
        }
    }

    /// <summary>每帧：只调原版定位 + 球的增删同步（不改任何布局/颜色/缩放）。</summary>
    public static void Follow(CharBossMonster? boss)
    {
        try
        {
            var node = boss?.Creature != null ? NCombatRoom.Instance?.GetCreatureNode(boss.Creature) : null;
            var manager = node?.OrbManager;
            if (node == null || manager == null || !GodotObject.IsInstanceValid(manager)) return;

            Reposition(node);

            // ⚠ 这里**绝不主动 AddOrbAnim**。
            //
            // 反编译 <c>NOrbManager.AddOrbAnim()</c>：取队列里**最后一个**球模型，找**第一个空槽**造球；
            // 而「没有空槽」时它会先 <c>EvokeOrbAnim(...)</c> 激发掉一个已有球再填。
            // 原版 Channel/激发路径自己就会维护这套视觉 —— 我们再按「数据球数 > UI 已填充槽数」补，
            // 会在原版动画还没把 Model 设上时误判成"缺一个"，抢先填掉最后一个空槽；
            // 于是原版下一次引导发现没空槽 → **激发掉那个电球**再填冰球
            //（用户实测：战斗开始 1 个电球，打冰川后电球被激发、画面剩 3 个冰球，而数据是 [L,F,F]）。
            //
            // 只有「我们手动往队列塞球」那条路（破损核心）需要自己补视觉 —— 那由
            // <see cref="AfterChannel"/> 负责，它有明确的前后时序，不会和原版动画打架。
            if (boss != null)
            {
                _lastOrbCount = OrbCount(boss);

                // 槽位被别处清掉时补回来
                int capacity = CapacityOf(boss);
                if (SlotCount(manager) < capacity && capacity > 0)
                {
                    manager.AddSlotAnim(capacity - SlotCount(manager));
                }

                // AddSlotAnim / AddOrbAnim 造出来的新 NOrb 又是「非本地」的 → 每帧幂等掰回来
                OrbLocalVisuals.Force(manager);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 球位跟随失败: " + e.Message);
        }
    }

    /// <summary>
    /// 手动往队列塞了球之后调用。
    ///
    /// ⚠ 这里**同样**要先比「数据球数 vs UI 已填充槽数」再决定要不要 <c>AddOrbAnim</c>：
    /// 手动入队是 `await` 过的，中间会插进若干帧，<see cref="Follow"/> 那时已经看到
    /// 「数据 1 个 / UI 已填充 0 个」补过一次了；这里再无脑补一次就是**同一个模型两个球节点**
    ///（用户实测：破损核心画面上出现 2 个电球，而数据里只有 1 个）。
    /// </summary>
    public static void AfterChannel(CharBossMonster boss)
    {
        try
        {
            var manager = NodeOf(boss)?.OrbManager;
            if (manager == null || !GodotObject.IsInstanceValid(manager)) return;

            int count = OrbCount(boss);
            int filled = FilledSlotCount(manager);
            if (count > filled)
            {
                manager.AddOrbAnim();
                Logger.Log($"[CharBoss] 球位 UI 已同步（AddOrbAnim）：数据 {count} / 已填充 {filled}");
            }
            OrbLocalVisuals.Force(manager);   // AddOrbAnim 新造的球节点也是非本地样式
            _lastOrbCount = count;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 球位 UI 同步失败: " + e.Message);
        }
    }

    /// <summary>原版定位（用角色自己的 OrbPosition 标记，别自己算 —— 算错会被裁出可视区）。</summary>
    private static void Reposition(NCreature node)
    {
        try { SetPositionMethod?.Invoke(node, null); }
        catch (Exception e) { Logger.Log("[CharBoss] 球位定位失败: " + e.Message); }
    }

    private static int SlotCount(NOrbManager manager)
    {
        try { return SlotsField?.GetValue(manager) is List<NOrb> list ? list.Count : 0; }
        catch { return 0; }
    }

    /// <summary>管理器的槽节点表（下标与 orb 队列顺序一致：队首在最右）。</summary>
    private static List<NOrb>? SlotList(NOrbManager manager)
    {
        try { return SlotsField?.GetValue(manager) as List<NOrb>; }
        catch { return null; }
    }

    /// <summary>
    /// UI 里**已经填了球**的槽数（<c>NOrb.Model != null</c>）。
    /// 用来判断「原版是不是已经把这次引导画出来了」——见 Follow 里那段注释。
    /// </summary>
    private static int FilledSlotCount(NOrbManager manager)
    {
        var list = SlotList(manager);
        if (list == null) return 0;
        int filled = 0;
        foreach (var orb in list)
        {
            try
            {
                if (orb != null && GodotObject.IsInstanceValid(orb) && orb.Model != null) filled++;
            }
            catch { /* 忽略单个节点 */ }
        }
        return filled;
    }

    private static int OrbCount(CharBossMonster boss)
        => boss.Shadow?.PlayerCombatState?.OrbQueue.Orbs.Count ?? 0;

    /// <summary>
    /// 诊断用：把 Boss 的充能球队列写成 <c>L,F,F (3/3)</c>（L=闪电 F=冰霜 D=黑暗 P=等离子 G=玻璃）。
    /// 用途：确认「某张牌到底引导了几个球、有没有被重复引导」
    ///（用户实测：冰川应该给 2 个冰霜球，实际看到 3 个）。
    /// </summary>
    internal static string DescribeOrbs(CharBossMonster? boss)
    {
        try
        {
            var queue = boss?.Shadow?.PlayerCombatState?.OrbQueue;
            if (queue == null) return "无球位";
            var parts = new List<string>();
            foreach (var orb in queue.Orbs)
            {
                parts.Add(orb switch
                {
                    MegaCrit.Sts2.Core.Models.Orbs.LightningOrb => "L",
                    MegaCrit.Sts2.Core.Models.Orbs.FrostOrb => "F",
                    MegaCrit.Sts2.Core.Models.Orbs.DarkOrb => "D",
                    MegaCrit.Sts2.Core.Models.Orbs.PlasmaOrb => "P",
                    MegaCrit.Sts2.Core.Models.Orbs.GlassOrb => "G",
                    null => "?",
                    _ => orb.GetType().Name,
                });
            }
            return $"{string.Join(",", parts)} ({queue.Orbs.Count}/{queue.Capacity})";
        }
        catch (Exception e)
        {
            return "描述失败: " + e.Message;
        }
    }

    private static NCreature? NodeOf(CharBossMonster boss)
    {
        var creature = boss.Creature;
        if (creature == null) return null;
        return NCombatRoom.Instance?.GetCreatureNode(creature);
    }

    private static int CapacityOf(CharBossMonster boss)
        => boss.Shadow?.PlayerCombatState?.OrbQueue.Capacity ?? 0;
}
