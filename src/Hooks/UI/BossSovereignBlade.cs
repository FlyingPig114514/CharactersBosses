using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using CharacterBosses.Monsters;

namespace CharacterBosses.UI;

/// <summary>
/// 铸剑储君那把浮在空中的「君王之剑」（<see cref="NSovereignBladeVfx"/>）的朝向修正。
///
/// 来历：原版把它作为 <c>NCreature</c> 的子节点挂上去（<c>SovereignBlade.GetVfxNode</c>
/// 取 <c>NCombatRoom.GetCreatureNode(player.Creature).GetChildren()</c> 里那张同名牌的节点），
/// 但整把剑的美术是照「玩家在左边、剑尖朝右」画的。Boss 站在右边（镜像侧）——
/// 剑尖就会朝**右**（背对玩家）。用户要求剑尖朝左。
///
/// 做法：只把内层骨架节点 <c>SpineSword</c> 的 <c>Scale.X</c> 取负（镜像美术），
/// 不去碰 <c>NSovereignBladeVfx</c> 本身的轨道（<c>%Path</c>）与位置 ——
/// 原版每帧写的是 <c>_spineNode.GlobalPosition</c> 和 <c>Scale.Y</c>，符号留给我们就行。
/// 原版在「铸造变大」时会重新赋一个正的 Scale，所以这里每帧幂等地确认一次。
/// </summary>
public static class BossSovereignBlade
{
    private static bool _logged;
    private static bool _dumped;

    /// <summary>每场战斗重新武装诊断日志（否则第二场看不到「找到/没找到」的证据）。</summary>
    public static void ResetDiagnostics()
    {
        _logged = false;
        _dumped = false;
    }

    /// <summary>每帧调用（幂等）：找到 Boss 身上那把剑，确认它朝左。</summary>
    public static void Ensure(CharBossMonster? boss)
    {
        try
        {
            if (boss?.Creature == null) return;
            var room = NCombatRoom.Instance;
            var bossNode = room?.GetCreatureNode(boss.Creature);
            if (bossNode == null || !GodotObject.IsInstanceValid(bossNode)) return;

            // 先保证那把浮空剑**存在**（用户实测：刚进战斗时看不见剑，要等他回合才开始飘）
            EnsureVfxExists(boss, bossNode);

            // 递归找（这把剑不一定直接挂在 NCreature 下，也可能挂在 Visuals/VfxContainer 里）
            var found = FindVfx(bossNode);
            if (found.Count == 0)
            {
                if (!_dumped)
                {
                    _dumped = true;
                    var kids = new System.Collections.Generic.List<string>();
                    foreach (var child in bossNode.GetChildren())
                    {
                        if (child is Node n) kids.Add($"{n.Name}[{n.GetType().Name}]");
                    }
                    Logger.Log($"[CharBoss] 没找到君王之剑 VFX，NCreature 直接子节点=[{string.Join(",", kids)}]");
                }
                return;
            }

            foreach (var vfx in found) Mirror(vfx);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 君王之剑朝向修正失败: " + e.Message);
        }
    }

    /// <summary>
    /// 保证「浮空的那把剑」节点存在**并且真的被"铸"出来**。
    ///
    /// 原版是把剑牌加进手牌时顺带把 VFX 挂到 <c>NCreature</c> 上的；而我们的剧本手牌是
    /// <c>BossScriptRunner.BuildHand</c> 直接 <c>CreateCard</c> + <c>Hand.AddInternal</c> 造出来的，
    /// 绕过了那条路径 ⇒ 战斗开始时场上只有卡面、没有浮空的剑。
    ///
    /// 而且**光 Create 是不够的**：<c>_Ready</c> 里是 <c>_spineNode.Scale = Vector2.Zero</c>，
    /// 骷髅本身尺寸为 0 ⇒ 节点存在但看不见（实测日志 `SpineSword.Scale=(-1E-05, 1E-05)`）。
    /// 让它变大的那一步是 <c>NSovereignBladeVfx.Forge(bladeDamage, showFlames)</c>：
    /// 它按伤害算 <c>_bladeSize</c> 再补间放大。
    ///
    /// 所以这里直接调原版的 <c>ForgeCmd.PlayCombatRoomForgeVfx(player, card)</c> —— 反编译确认它做的正是
    /// 「没有节点就 Create + AddChildSafely + Position=Zero + 播 regent_forge 音效，然后 Forge(damage, 是新建的)」，
    /// 是唯一"把剑铸出来"的正规入口。
    /// </summary>
    private static void EnsureVfxExists(CharBossMonster boss, Node bossNode)
    {
        try
        {
            var shadow = boss.Shadow;
            var pcs = shadow?.PlayerCombatState;
            if (shadow == null || pcs == null) return;

            var blade = FindBladeInPiles(pcs);
            if (blade == null) return;

            // ★ v0.2.319（用户 2026-11-02：「他没有击剑指南（只有铸剑储有），不应该在战斗开始时
            //   就把剑给铸造出来飘在他身边。应该在他第五回合打战火铸就的时候再铸造出来」）：
            //   **还没被铸造过的剑不补 VFX**。原版 `SovereignBlade` 的基础伤害是 10（`_baseDamage`），
            //   `ForgeCmd.Forge` 才会 `AddDamage` 并顺手调 `PlayCombatRoomForgeVfx` 把剑铸出来。
            //   所以「伤害 > 基础值」就等于「这把剑已经被铸过」：
            //     · 铸剑储君：击剑指南在战斗开始就铸了一次 ⇒ 仍然是进战斗就有剑（行为不变）；
            //     · 征服者储君：战斗开始/前几回合只有一张未铸造的剑牌，这里跳过 ⇒
            //       等到第 5 回合真的打出「战火铸就」时，由 `ForgeCmd.Forge` 自己把剑铸出来。
            if (!IsForged(blade)) return;

            // 已经有一把对应的浮空剑了就不再造（否则每帧都会播一遍精炼音效）
            if (MegaCrit.Sts2.Core.Models.Cards.SovereignBlade.GetVfxNode(shadow, blade) != null) return;

            MegaCrit.Sts2.Core.Commands.ForgeCmd.PlayCombatRoomForgeVfx(shadow, blade);
            Logger.Log($"[CharBoss] 战斗开始：铸出浮空君王之剑（原版 PlayCombatRoomForgeVfx，伤害 {SafeDamage(blade)}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 补建君王之剑 VFX 失败: " + e.Message);
        }
    }

    private static MegaCrit.Sts2.Core.Models.Cards.SovereignBlade? FindBladeInPiles(
        MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState pcs)
    {
        try
        {
            foreach (var pile in pcs.AllPiles)
            {
                if (pile == null) continue;
                foreach (var card in pile.Cards)
                {
                    if (card is MegaCrit.Sts2.Core.Models.Cards.SovereignBlade blade) return blade;
                }
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    private static string SafeDamage(MegaCrit.Sts2.Core.Models.Cards.SovereignBlade blade)
    {
        try { return blade.DynamicVars.Damage.IntValue.ToString(); } catch { return "?"; }
    }

    /// <summary>
    /// 君王之剑**未铸造**时的伤害（反编译 `SovereignBlade._baseDamage = 10`）。
    /// 用它判断「这把剑被铸过没有」：`ForgeCmd.Forge` 会 `AddDamage`，所以伤害 > 10 = 已铸造。
    /// </summary>
    private const int BaseBladeDamage = 10;

    /// <summary>这把剑已经铸造过（伤害高于基础值）。</summary>
    private static bool IsForged(MegaCrit.Sts2.Core.Models.Cards.SovereignBlade blade)
    {
        try { return blade.DynamicVars.Damage.BaseValue > BaseBladeDamage; }
        catch { return true; }   // 读不到就按「铸过」处理（宁可有剑也不要该有的剑不见了）
    }

    /// <summary>递归收集 <see cref="NSovereignBladeVfx"/>。</summary>
    private static System.Collections.Generic.List<NSovereignBladeVfx> FindVfx(Node root)
    {
        var list = new System.Collections.Generic.List<NSovereignBladeVfx>();
        foreach (var child in root.GetChildren())
        {
            if (!GodotObject.IsInstanceValid(child)) continue;
            if (child is NSovereignBladeVfx vfx) { list.Add(vfx); continue; }
            if (child is Node node) list.AddRange(FindVfx(node));
        }
        return list;
    }

    /// <summary>
    /// 直接镜像**某一个**剑节点（由 <c>NSovereignBladeVfx._Process</c> 的 postfix 每帧调用）。
    /// 这条路最可信：原版边跑边把 <c>Scale</c> / <c>GlobalPosition</c> 写回去，
    /// 所以镜像必须每帧幂等地重新确认一次。
    /// </summary>
    internal static void MirrorOne(NSovereignBladeVfx? vfx) => Mirror(vfx);

    private static void Mirror(NSovereignBladeVfx vfx)
    {
        if (vfx == null || !GodotObject.IsInstanceValid(vfx)) return;
        // **只翻我们的 Boss 那把剑**：玩家若玩储君、自己也有君王之剑，绝不能顺手把它掰反。
        if (!OwnedByBoss(vfx)) return;

        // 翻**这一层**（vfx 节点本身），不去翻内层 SpineSword。
        // 原因（用户实测：铸造/攻击动画仍然是反的）：原版动画是 `AnimationPlayer`/Tween 在写
        // 内层 `SpineSword.Scale`，它每帧**在我们 postfix 之后**再写一次正的 Scale ⇒ 我们翻内层等于白翻。
        // 翻外层则不会被动画覆盖（原版只写外层的 GlobalPosition），内层动画照旧播，方向跟着外层一起镜像。
        MirrorNode(vfx);
    }

    /// <summary>
    /// 把**原版自己**创建的那张剑牌说明框挪到剑的左边。
    ///
    /// 教训：之前我给剑另挂了一个热区 + 自己 CreateAndShow，结果**原版的热区本来就能收到鼠标事件**
    /// —— 于是同时存在两个框（左一个右一个，用户实测）。现在完全交回原版：只做「显示之后再挪一下位置」。
    ///
    /// 原版 <c>NSovereignBladeVfx.UpdateHoverTip</c> 里写的是
    /// <c>_hoverTip.SetGlobalPosition(_hitbox.GlobalPosition + Vector2.Right * _hitbox.Size.X)</c>
    /// —— 朝右偏移，而剑在屏幕最右侧时直接出屏。这里在它之后把框改成贴在热区**左侧**。
    /// </summary>
    internal static void RepositionHover(NSovereignBladeVfx vfx)
    {
        try
        {
            if (vfx == null || !GodotObject.IsInstanceValid(vfx)) return;
            if (!OwnedByBoss(vfx)) return;   // 玩家自己的剑保持原版行为

            var traverse = HarmonyLib.Traverse.Create(vfx);
            var tip = traverse.Field("_hoverTip").GetValue<MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet>();
            if (tip == null || !GodotObject.IsInstanceValid(tip)) return;

            var hit = traverse.Field("_hitbox").GetValue<Control>();
            if (hit == null || !GodotObject.IsInstanceValid(hit)) return;

            float width = tip.Size.X > 1f ? tip.Size.X : 420f;
            float x = hit.GlobalPosition.X - width - 16f;
            if (x < 8f) x = 8f;                    // 别钻出屏幕左边
            tip.SetGlobalPosition(new Vector2(x, hit.GlobalPosition.Y));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 君王之剑说明框定位失败: " + e.Message);
        }
    }

    /// <summary>
    /// 这把剑是不是我们 Boss 的。原版 <c>NSovereignBladeVfx</c> 上就有公开的 <c>Card</c>（<c>_Ready</c> 里
    /// <c>_owner = Card.Owner</c>），所以按 <c>Card.Owner.Creature.Monster</c> 判定即可 —— 精确，不用猜位置。
    /// </summary>
    private static bool OwnedByBoss(NSovereignBladeVfx vfx)
    {
        try
        {
            var card = vfx.Card;
            if (card == null) return false;
            return card.Owner?.Creature?.Monster is Monsters.CharBossMonster;
        }
        catch { return false; }
    }

    /// <summary>把一个 Node2D 的 X 轴翻转（幂等：已经是负的就不动）。</summary>
    private static void MirrorNode(Node2D? target)
    {
        if (target == null || !GodotObject.IsInstanceValid(target)) return;
        var scale = target.Scale;
        if (scale.X > 0f)
        {
            target.Scale = new Vector2(-scale.X, scale.Y);
            if (_loggedNames.Add(target.Name.ToString() + "@" + target.GetParent()?.Name))
            {
                Logger.Log($"[CharBoss] 君王之剑已镜像（剑尖朝左）：{target.GetParent()?.Name}/{target.Name}.Scale={target.Scale}");
            }
            _logged = true;
        }
    }

    private static readonly System.Collections.Generic.HashSet<string> _loggedNames = new();

    private static ulong _lastScanUsec;

    /// <summary>
    /// **兜底扫描**：铸造动画/攻击动画用的不是常驻那把剑的节点（用户实测：这两个动画仍然朝右）。
    ///
    /// ⚠ 只翻**内层骨架** `SpineSword`（美术本体）—— 绝不能按名字模糊匹配整棵树去翻外层节点：
    /// 外层 vfx 节点名里也有 "Blade"，翻完外层再翻内层等于两次取负，常驻的那把剑就又朝右了（踩过）。
    /// 只翻 X 轴符号、不动位置/旋转/轨道。
    /// </summary>
    internal static void MirrorAllInScene()
    {
        try
        {
            var now = Time.GetTicksUsec();
            // 性能（用户反馈「boss 战仍然卡」）：这条是**兜底**扫描，原来每 120ms 从
            // `SceneTree.Root` 递归扫整棵树（含手牌、UI、所有界面）。现在：
            //   ① 降频到 250ms；
            //   ② 只扫**战斗房间**这一棵子树（剑都是房间里的 NCreature 子节点）。
            if (now - _lastScanUsec < 250_000) return;
            _lastScanUsec = now;

            Node? root = NCombatRoom.Instance;
            if (root == null || !GodotObject.IsInstanceValid(root))
            {
                var tree = Engine.GetMainLoop() as SceneTree;
                root = tree?.Root;
            }
            if (root == null) return;

            Scan(root, 0);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 君王之剑兜底扫描失败: " + e.Message);
        }
    }

    private const int MaxScanDepth = 12;

    /// <summary>
    /// 只翻「属于我们 Boss 的 <c>NSovereignBladeVfx</c>」里的内层骨架。
    /// 不再按名字模糊匹配整棵树 —— 那样既会双重取负（外层 + 内层），也会把玩家的剑一起掰反。
    /// 铸造/攻击动画如果是**另一个** vfx 实例，它同样会被这里找到并按归属判定。
    /// </summary>
    private static void Scan(Node node, int depth)
    {
        if (depth > MaxScanDepth || node == null) return;
        foreach (var child in node.GetChildren())
        {
            if (child == null || !GodotObject.IsInstanceValid(child)) continue;
            if (child is NSovereignBladeVfx vfx) { Mirror(vfx); continue; }   // 里面已含归属判定
            if (child is Node n) Scan(n, depth + 1);
        }
    }

    private static Node2D? FindNamed(Node root, string name)
    {
        foreach (var child in root.GetChildren())
        {
            if (!GodotObject.IsInstanceValid(child)) continue;
            if (child is Node2D n2 && n2.Name.ToString() == name) return n2;
            if (child is Node n)
            {
                var hit = FindNamed(n, name);
                if (hit != null) return hit;
            }
        }
        return null;
    }
}
