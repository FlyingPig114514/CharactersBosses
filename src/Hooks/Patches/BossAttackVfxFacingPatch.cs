using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **角色 Boss 的攻击特效要朝左**（面向玩家）。
///
/// 用户 2026-11-14：「目前 boss 的大部分牌的攻击特效都是反的，比如彗星、震荡波、冲刺、匕首雨等等
/// （但我记得君王之剑好像被我专门调过，不是反的）。你看看能不能让他们的攻击牌的攻击特效朝左？
/// （例外：轰击储醒来后即轰击打自己时轰击的动画保持原版，但轰击打玩家的时候要朝左）」。
///
/// ## 为什么是反的
///
/// 这批 Boss 复用的是**玩家角色**的立绘与卡牌，而玩家永远站在**左侧朝右**打：
///   · 立绘本身朝右画（所以 <see cref="BossVisuals.FaceThePlayer"/> 要把 `Body.Scale.X` 取负）；
///   · 卡牌的攻击特效也都写死「朝右」——
///     `NDaggerSprayFlurryVfx.Create(owner.Creature, color, goingRight: true)`、
///     `VfxCmd.PlayOnCreatureCenter(owner.Creature, "vfx/vfx_flying_slash")` ……
/// 放到敌方一侧（玩家在左、Boss 在右）就都背对玩家。
/// 「君王之剑」看着是对的，是因为它的特效（`WithHitVfxNode(NBigSlashVfx.Create)`）**落在被打的玩家身上**，
/// 玩家那一侧的方向本来就是对的。
///
/// ## 只管「画在她自己身上」的特效（v0.2.649 收窄）
///
/// v0.2.643～648 把「她出牌期间出现的**所有** `*Vfx`」都翻了，结果把**本来方向就对的**也翻反了 ——
/// 用户：「boss 打小刀的动画现在是反的」。小刀是
/// `NShivThrowVfx.Create(attacker, target, color)`：**生成时就同时拿到攻击者与目标**，
/// 方向是它自己算的，翻它等于翻错。
///
/// 所以现在只翻「**锚在她身上**」的特效：
///   · 父节点往上能找到**她自己的** `NCreature`；或
///   · 骨骼/粒子类画面特效（`NVfxSpine` / `NVfxParticleSystem`）且**位置就在她身边**（1.6 倍身位内）。
/// 外加一份**排除名单**（`Excluded`）挡住「自己知道方向」的那些（小刀投掷、巨型斩击、魔法导弹、命中火花…）。
///
/// ## 落地：两处补丁，一手一个幂等标记
///
/// ★ 教训：**不能补 `GodotTreeExtensions.AddChildSafely`** —— 它只有几行，JIT 会内联，
///   补丁装上却一次都不触发（v0.2.643/644 实测：日志里一条都没有）。现在补的是它内部真正落地的
///   **`Godot.Node.AddChild`**，外加**第二道保险 `VfxCmd.PlayVfx`**（所有字符串路径特效都由它落地、
///   体量大不会被内联，而且那时 `GlobalPosition` 已经设好，能判断「离她多远」）。
///   两个入口会用 Godot meta 标记去重，同一个节点只处理一次。
///
/// ## 位置问题（用户：「位置应该在储君左边」）
///
/// `PlayVfx` 把节点原点放在**生物中心**，美术在场景内部偏向一侧；翻 `Scale.X` 会把偏移一起镜像
/// ⇒ 特效从「她左边朝右」跳到「她右边朝左」。所以翻完之后再**往玩家那一侧平移一倍身位**
/// （尺度取她自己的 `Hitbox.Size.X`）。
/// </summary>
internal static class BossAttackVfxFacing
{
    /// <summary>「自己知道方向」的特效 —— 绝不翻（翻了就是反的）。</summary>
    private static readonly HashSet<string> Excluded = new(StringComparer.Ordinal)
    {
        "NShivThrowVfx",            // 小刀投掷（Create(attacker, target) 自己算方向）
        "NBigSlashVfx",             // 巨型横斩（Create(pos, creature.IsEnemy)）
        "NBigSlashImpactVfx",
        "NLargeMagicMissileVfx",    // 轰击的导弹
        "NHitSparkVfx",             // 命中火花（对称）
        "NDamageNumVfx",            // 伤害数字（文字，镜像会反着写）
    };

    /// <summary>骨骼/粒子类的「画面型」特效（没有贴图节点可 `FlipH`，只能翻 `Scale.X`）。</summary>
    private static bool IsArtVfx(string typeName)
        => typeName is "NVfxSpine" or "NVfxParticleSystem";

    private static int _depth;
    private static bool _quiet;

    private static readonly HashSet<string> _loggedTypes = new();
    private static readonly HashSet<string> _seenTypes = new();
    private static int _shiftLogs;

    internal static void Begin(string? cardEntry, bool quiet)
    {
        _depth++;
        if (_depth != 1) return;
        _quiet = quiet;
        if (quiet && _loggedTypes.Add("quiet:" + cardEntry))
        {
            Logger.Log($"[CharBoss] 攻击特效朝向：{cardEntry} 是她自己吃伤害 ⇒ 这一手保持原版方向（不镜像）");
        }
    }

    internal static void End()
    {
        if (_depth > 0) _depth--;
        if (_depth == 0) _quiet = false;
    }

    /// <summary>由 `Godot.Node.AddChild` 的后缀调用（覆盖攻击构建器的自定义节点）。</summary>
    internal static void OnNodeAdded(Node? parent, Node? child)
    {
        if (_depth <= 0 || _quiet) return;
        try
        {
            if (child is not Node2D node || !GodotObject.IsInstanceValid(node)) return;
            string typeName = TypeNameOf(node);
            bool underBoss = IsUnderBoss(parent);
            if (!underBoss) return;                       // 不在她身上 ⇒ 不动（多半方向本来就对）
            Apply(node, typeName, underBoss);
        }
        catch { /* 纯外观 */ }
    }

    /// <summary>由 `VfxCmd.PlayVfx` 的后缀调用：容器里最后那个子节点就是刚加进去的特效。</summary>
    internal static void MirrorLastChild(Node? container)
    {
        if (_depth <= 0 || _quiet) return;
        try
        {
            var children = container?.GetChildren();
            if (children == null || children.Count == 0) return;
            if (children[children.Count - 1] is not Node2D node) return;
            if (!GodotObject.IsInstanceValid(node)) return;

            string typeName = TypeNameOf(node);
            bool underBoss = IsUnderBoss(container);
            if (!underBoss && !(IsArtVfx(typeName) && NearBoss(node))) return;
            Apply(node, typeName, underBoss);
        }
        catch { /* 纯外观 */ }
    }

    private static void Apply(Node2D node, string typeName, bool underBoss)
    {
        try
        {
            if (Excluded.Contains(typeName)) return;

            // ★ v0.2.650（用户：「有没有类似小刀的本来方向就对的牌？」）：
            //   凡是有 `Create(攻击者, 目标, …)` 这种**同时拿到两端**的工厂方法的特效，
            //   方向都是它自己按两个生物的位置算的（`NShivThrowVfx.Create(attacker, target, color)`）
            //   ⇒ 一律不翻。这条用**反射自动识别**，不靠人工维护名单，
            //   原版以后新增的同型投射物也自动免疫。
            if (KnowsDirection(node.GetType()))
            {
                if (_loggedTypes.Add("skip:" + typeName))
                {
                    Logger.Log($"[CharBoss] 攻击特效跳过镜像（自带方向）：{typeName}");
                }
                return;
            }

            if (AlreadyDone(node)) return;

            bool mirrored = MirrorArt(node);
            if (!mirrored) return;
            MarkDone(node);

            if (IsArtVfx(typeName))
            {
                float shift = HorizontalShift();
                if (Math.Abs(shift) > 0.01f)
                {
                    try { node.Position = new Vector2(node.Position.X + shift, node.Position.Y); }
                    catch { /* 忽略 */ }
                }
                if (_shiftLogs++ < 8)
                {
                    Logger.Log($"[CharBoss] 攻击特效已镜像并朝玩家侧平移 {shift:0}px：{typeName}");
                }
            }

            if (_loggedTypes.Add(typeName))
            {
                Logger.Log($"[CharBoss] 攻击特效已水平镜像（朝左面向玩家）：{typeName}" +
                           $"{(underBoss ? "（她身上）" : "（她身边）")}");
            }
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 只翻「画面」：优先用贴图自带的 `FlipH`（位置完全不动），
    /// 没有贴图节点才翻**子 `Node2D`**；**绝不退到翻节点本身**（那会把美术偏移一起翻）。
    /// </summary>
    internal static bool MirrorArt(Node2D node)
    {
        bool any = false;
        try
        {
            foreach (var vis in VisualsOf(node, 3))
            {
                try
                {
                    if (vis is Sprite2D sp) { sp.FlipH = !sp.FlipH; any = true; }
                    else if (vis is AnimatedSprite2D asp) { asp.FlipH = !asp.FlipH; any = true; }
                }
                catch { /* 单个翻不动就跳过 */ }
            }

            if (!any)
            {
                var children = node.GetChildren();
                if (children != null)
                {
                    foreach (var child in children)
                    {
                        if (child is not Node2D inner || !GodotObject.IsInstanceValid(inner)) continue;
                        any |= Mirror(inner);
                    }
                }
            }
        }
        catch { /* 忽略 */ }
        return any;
    }

    /// <summary>把节点水平镜像（只翻朝右的，幂等）。</summary>
    internal static bool Mirror(Node2D? node)
    {
        try
        {
            if (node == null || !GodotObject.IsInstanceValid(node)) return false;
            var scale = node.Scale;
            if (scale.X <= 0f) return false;
            node.Scale = new Vector2(-scale.X, scale.Y);
            return true;
        }
        catch { return false; }
    }

    /// <summary>广度优先找 `Sprite2D` / `AnimatedSprite2D`（最多 <paramref name="depth"/> 层）。</summary>
    private static List<Node> VisualsOf(Node root, int depth)
    {
        var found = new List<Node>();
        try
        {
            var frontier = new List<Node> { root };
            for (int d = 0; d < depth && frontier.Count > 0; d++)
            {
                var next = new List<Node>();
                foreach (var n in frontier)
                {
                    if (!GodotObject.IsInstanceValid(n)) continue;
                    foreach (var child in n.GetChildren())
                    {
                        if (!GodotObject.IsInstanceValid(child)) continue;
                        if (child is Sprite2D || child is AnimatedSprite2D) found.Add(child);
                        else next.Add(child);
                    }
                }
                frontier = next;
            }
        }
        catch { /* 忽略 */ }
        return found;
    }

    private const string MetaKey = "charboss_vfx_mirrored";

    private static bool AlreadyDone(Node2D node)
    {
        try { return node.HasMeta(MetaKey); } catch { return false; }
    }

    private static void MarkDone(Node2D node)
    {
        try { if (!node.HasMeta(MetaKey)) node.SetMeta(MetaKey, true); } catch { /* 忽略 */ }
    }

    private static string TypeNameOf(Node2D node)
    {
        try { return node.GetType().Name; } catch { return string.Empty; }
    }

    /// <summary>每个特效类型「是不是自带方向」的缓存。</summary>
    private static readonly Dictionary<Type, bool> _knowsDirection = new();

    /// <summary>
    /// 这个特效类的静态工厂里有没有**同时接收两个 `Creature`** 的重载？
    /// 有 ⇒ 它生成时就知道「谁打谁」，方向自己算（小刀投掷、巨型横斩之类）⇒ 绝不能翻。
    /// </summary>
    private static bool KnowsDirection(Type type)
    {
        if (type == null) return false;
        if (_knowsDirection.TryGetValue(type, out bool cached)) return cached;

        bool result = false;
        try
        {
            var creatureType = typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature);
            foreach (var method in type.GetMethods(
                         System.Reflection.BindingFlags.Public
                         | System.Reflection.BindingFlags.NonPublic
                         | System.Reflection.BindingFlags.Static))
            {
                if (method.Name != "Create") continue;
                int creatures = 0;
                foreach (var p in method.GetParameters())
                {
                    if (p.ParameterType != null && creatureType.IsAssignableFrom(p.ParameterType)) creatures++;
                }
                if (creatures >= 2) { result = true; break; }
            }
        }
        catch { result = false; }

        if (_knowsDirection.Count > 512) _knowsDirection.Clear();
        _knowsDirection[type] = result;
        return result;
    }

    /// <summary>这个容器/节点是不是挂在**我们的角色 Boss** 身上。</summary>
    private static bool IsUnderBoss(Node? node)
    {
        try
        {
            for (int depth = 0; node != null && depth < 12; depth++)
            {
                if (node is MegaCrit.Sts2.Core.Nodes.Combat.NCreature creature)
                    return creature.Entity?.Monster is CharBossMonster;
                node = node.GetParent();
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    /// <summary>这个节点的位置是不是就贴在她身边（1.6 倍身位内）。</summary>
    private static bool NearBoss(Node2D node)
    {
        try
        {
            var creatureNode = ActingCreatureNode();
            if (creatureNode == null) return false;
            float dx = Math.Abs(node.GlobalPosition.X - creatureNode.VfxSpawnPosition.X);
            float width = HitboxWidth(creatureNode);
            return dx <= width * 1.6f;
        }
        catch { return false; }
    }

    /// <summary>镜像之后往玩家那一侧平移多少像素（玩家在左 ⇒ 负值）。</summary>
    private static float HorizontalShift()
    {
        var creatureNode = ActingCreatureNode();
        return -HitboxWidth(creatureNode);
    }

    private static float HitboxWidth(MegaCrit.Sts2.Core.Nodes.Combat.NCreature? creatureNode)
    {
        float width = 0f;
        try
        {
            var hitbox = creatureNode?.Hitbox;
            if (hitbox != null) width = hitbox.Size.X;
        }
        catch { /* 忽略 */ }
        return width > 1f ? width : 130f;
    }

    private static MegaCrit.Sts2.Core.Nodes.Combat.NCreature? ActingCreatureNode()
    {
        try
        {
            var boss = CharacterBosses.Core.BossCardSelector.ActingBoss;
            return MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.GetCreatureNode(boss?.Creature);
        }
        catch { return null; }
    }

    /// <summary>战斗结束/换场时清干净。</summary>
    internal static void Reset()
    {
        _depth = 0;
        _quiet = false;
        _seenTypes.Clear();
    }
}

/// <summary>
/// 真正生效的入口一：`Godot.Node.AddChild(Node, bool, InternalMode)`
/// （`GodotTreeExtensions.AddChildSafely` 太小会被 JIT 内联，补它等于没补 —— v0.2.643/644 的教训）。
/// </summary>
[HarmonyPatch(typeof(Node), nameof(Node.AddChild),
    new[] { typeof(Node), typeof(bool), typeof(Node.InternalMode) })]
internal static class BossAttackVfxAddChildPatch
{
    [HarmonyPostfix]
    public static void Postfix(Node __instance, Node node)
    {
        BossAttackVfxFacing.OnNodeAdded(__instance, node);
    }
}

/// <summary>
/// 入口二（保险）：`VfxCmd.PlayVfx(Vector2, string, Control)` ——
/// 所有**走字符串路径**的特效（`PlayOnCreature` / `PlayOnCreatureCenter` / `PlayOnSide`）都由它落地，
/// 体量大不会被内联；而且它返回时 `GlobalPosition` 已经设好，能判断「离她多远」。
/// </summary>
[HarmonyPatch(typeof(VfxCmd), nameof(VfxCmd.PlayVfx),
    new[] { typeof(Vector2), typeof(string), typeof(Control) })]
internal static class BossAttackVfxPlayVfxPatch
{
    [HarmonyPostfix]
    public static void Postfix(Control vfxContainer)
    {
        BossAttackVfxFacing.MirrorLastChild(vfxContainer);
    }
}
