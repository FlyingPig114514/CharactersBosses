using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using CharacterBosses.Monsters;

namespace CharacterBosses.UI;

/// <summary>
/// 让 Boss 的充能球槽/球按「本地玩家」的样式画 —— 原版 <c>NOrb</c> 完全由私有字段 <c>_isLocal</c> 决定：
///
/// <code>
/// // 空槽（Model == null）
/// _outline.Visible = _isLocal;                         // ← 非本地：空槽的圈根本不画（所以"看不见球位"）
/// // 有球
/// _labelContainer.Visible = _isLocal;                   // ← 非本地：数值不常显
/// if (!_isLocal) Modulate = Model.DarkenedColor;        // ← 非本地：整颗球压暗
/// </code>
///
/// 而槽是 <c>NOrbManager.AddSlotAnim</c> 造的，它调 <c>NOrb.Create(LocalContext.IsMe(Player))</c>；
/// <c>LocalContext.IsMe</c> 的实现是 <c>player.NetId == LocalContext.NetId</c>，
/// 影子 Player 的 NetId 是我们虚构的（<c>0xFFFFFFFF_0000_xxxx</c>），永远不相等 →
/// 造出来的槽全是「非本地」：空槽不画、数值要悬停才出、球还压暗。
///
/// 这里只把 <c>_isLocal</c> 掰成 true 并补上原版 <c>_Ready</c> 里对本地槽做的 0.85 缩放，
/// 然后让原版自己 <c>UpdateVisuals</c> 重画 —— 不自绘任何东西。
/// </summary>
internal static class OrbLocalVisuals
{
    private static readonly FieldInfo? LocalField =
        typeof(NOrb).GetField("_isLocal", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? SlotsField =
        typeof(NOrbManager).GetField("_orbs", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? CreatureField =
        typeof(NOrbManager).GetField("_creatureNode", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>幂等：已经是本地样式的槽直接跳过，所以可以每帧调。</summary>
    public static void Force(NOrbManager? manager)
    {
        if (manager == null || !GodotObject.IsInstanceValid(manager)) return;
        if (LocalField == null) return;
        if (SlotsField?.GetValue(manager) is not List<NOrb> orbs) return;

        bool changed = false;
        for (int i = 0; i < orbs.Count; i++)
        {
            var orb = orbs[i];
            if (orb == null || !GodotObject.IsInstanceValid(orb)) continue;
            if (LocalField.GetValue(orb) is true) continue;

            LocalField.SetValue(orb, true);
            // 原版 NOrb._Ready 里：if (_isLocal) Scale *= 0.85f —— 我们掰字段时 _Ready 已经跑过了，补上
            orb.Scale *= 0.85f;
            // 变亮：原版 UpdateVisuals 只在 !_isLocal 时把 Modulate 设成 DarkenedColor，**没有 else 分支设回白**，
            // 所以不补这一句就会一直暗着，只有 OnFocus（鼠标碰一下）才变白 —— 原版 OnFocus 里就是设白。
            orb.Modulate = Colors.White;
            changed = true;
        }
        if (changed) Logger.Log($"[CharBoss] 球槽已切成常显样式（{orbs.Count} 个槽）");

        // 让原版按新的 _isLocal 重画（空槽圈可见性、数值可见性、压暗/白都在它里面），
        // 再按「本回合哪些球会被激发」逐球点亮（原版 NOrbManager 会在每次 CombatStateChanged 里
        // UpdateVisuals(None) 把高亮清掉，见 OnCombatStateChanged，所以每次都顺手恢复）。
        BossOrbDisplay.ReapplyHighlight(manager);
    }

    /// <summary>
    /// 只对「我们自己的 Boss」动手 —— 用管理器挂着的那个 <c>NCreature</c> 判断，
    /// 玩家自己的球位管理器走原版逻辑，一个字段都不碰。
    /// </summary>
    public static void ForceIfOurBoss(NOrbManager? manager)
    {
        try
        {
            if (manager == null || !GodotObject.IsInstanceValid(manager)) return;
            if (CreatureField?.GetValue(manager) is not NCreature node) return;
            if (node.Entity?.Monster is not CharBossMonster) return;
            Force(manager);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 球槽样式失败: " + e.Message);
        }
    }
}
