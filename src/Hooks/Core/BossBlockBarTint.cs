using System;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// **她有格挡时，血条要变成「银蓝色」**（用户 2026-11-02：「拥有格挡时，boss 的血条应该是和格挡一样的银蓝色的」）。
///
/// 原版实现（反编译 `NHealthBar.RefreshBlockUi`）：
/// <code>
/// if (_creature.Block &lt;= 0 &amp;&amp; (tracked == null || tracked.Block &lt;= 0))
/// {
///     _blockContainer.Visible = false;
///     _hpForeground.SelfModulate = _redForegroundColor;      // 没格挡 → 红色
///     return;
/// }
/// _hpForeground.SelfModulate = _blockHpForegroundColor;      // 有格挡 → 银蓝 #3B6FA3
/// </code>
/// 也就是说这件事**完全靠 `RefreshBlockUi` 被调**。而我们的 Boss 是**影子玩家**：
///   · 战斗开始那一刻她还没和血条节点绑定好（钻石头冠的 20 点格挡那时就加上去了）；
///   · 之后格挡再变化（护喉甲、奥利哈钢、无惧疼痛…）也不一定经过原版那条
///     「本机玩家格挡变化 → 刷血条」的链路；
/// ⇒ 血条一直停在红色。
///
/// 所以这里在 4Hz 慢车道里兜一次：她的 `Block &gt; 0` 但血条前景还是红的，就替它调一次
/// `RefreshBlockUi` / `RefreshText`（都是 private，反射调；两个都调，数字颜色也跟着对）。
/// </summary>
internal static class BossBlockBarTint
{
    private static readonly FieldInfo? HealthBarField =
        typeof(NCreature).GetField("_healthBar", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? HpForegroundField =
        typeof(NHealthBar).GetField("_hpForeground", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? RefreshBlockUiMethod =
        typeof(NHealthBar).GetMethod("RefreshBlockUi", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? RefreshTextMethod =
        typeof(NHealthBar).GetMethod("RefreshText", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>原版「有格挡」时血条前景用的颜色（反编译 `_blockHpForegroundColor = 3B6FA3`）。</summary>
    private static readonly Color BlockForeground = new Color(0x3B / 255f, 0x6F / 255f, 0xA3 / 255f);

    private static int _logs;

    /// <summary>4Hz 调一次：把她的血条前景调成「有格挡」的样子。</summary>
    internal static void Enforce(CharBossMonster? boss)
    {
        try
        {
            var creature = boss?.Creature;
            if (creature == null || !creature.IsAlive) return;
            if (creature.Block <= 0m) return;                    // 没格挡：原版本来就会是红的

            var room = NCombatRoom.Instance;
            var node = room?.GetCreatureNode(creature) as NCreature;
            if (node == null) return;

            var bar = HealthBarField?.GetValue(node) as NHealthBar;
            if (bar == null) return;

            var foreground = HpForegroundField?.GetValue(bar) as CanvasItem;
            if (foreground == null) return;

            // 已经是对的（银蓝附近）就不动它，避免每 4Hz 刷一次造成重绘
            var current = foreground.SelfModulate;
            bool looksRight = Math.Abs(current.R - BlockForeground.R) < 0.02f
                              && Math.Abs(current.G - BlockForeground.G) < 0.02f
                              && Math.Abs(current.B - BlockForeground.B) < 0.02f;
            if (looksRight) return;

            RefreshBlockUiMethod?.Invoke(bar, null);
            RefreshTextMethod?.Invoke(bar, null);

            var after = (HpForegroundField?.GetValue(bar) as CanvasItem)?.SelfModulate ?? default;
            if (_logs++ < 5)
            {
                Logger.Log($"[CharBoss] 血条配色：{boss!.Def?.Key} 有 {creature.Block} 点格挡，" +
                           $"血条前景 {current} → {after}（银蓝 = 有格挡）");
            }
        }
        catch { /* 纯显示，失败就算了 */ }
    }
}
