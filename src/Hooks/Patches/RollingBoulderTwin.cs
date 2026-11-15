#if STS2_111   // 「滚石」目前只有塔罗愚（愚者，0.113+）会打 ⇒ 只在 111 里编译
using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace CharacterBosses.Patches;

/// <summary>
/// **「滚石」的滚向**（用户 2026-11-15：「他的滚石的动画目前完全是反的（和玩家的方向一样），
/// 翻转过来（不影响玩家自己的滚石）」）。
///
/// ## 为什么翻不动（反编译 `NRollingBoulderVfx.PlayAnim`）
///
/// 那条动画的几何是**写死的「玩家在左、敌人在右」**：
/// <code>
/// impactPoint = new Vector2(viewport.Size.X * 0.5f, 目标平均 Y);
/// start       = new Vector2(impactPoint.X - xOffset, -150);
/// velocity.X  = (impactPoint.X - start.X) / timeToImpact;   // 恒为正 ⇒ 永远向右滚
/// </code>
/// 也就是说它总是「从屏幕中线左边掉下来、然后一路往右滚」。玩家打出来时右边是敌人 ⇒ 正确；
/// 我们的 Boss 打出来时玩家在**左边** ⇒ 石头从玩家头上掉下来往右滚走，看着完全反了。
///
/// ## 为什么不能直接翻节点（踩过的坑）
///
/// `GlobalPosition` 是**绝对坐标**，动画每帧自己写一次（`GlobalPosition += velocity * dt`）⇒
///   ① 给它套一个 `Scale.X = -1` 的镜像父节点没用（子节点写绝对坐标，父级变换会和它正好抵消）；
///   ② 每帧把它的位置镜像一次也没用 —— 下一帧动画会**读回被镜像过的值**再加位移，变成来回抽搐。
///
/// ## 落地：影子替身（不动原版管线）
///
/// 原版节点照旧跑（伤害是靠它的 `HitCreature` 信号结算的，绝不能拦掉），只是把它的**石头贴图藏起来**，
/// 另建一个 `Sprite2D` 替身：每帧按 `x → 2*屏幕中线 − x` 摆到镜像位置、`Rotation` 取反、
/// `Scale` 取绝对值 ⇒ 观众看到的是一颗**从右往左滚**的石头。
/// 替身只在「目标全是玩家」（= 我们的 Boss 打出来的那一下）时才建 ⇒ **玩家自己的滚石完全不受影响**。
/// </summary>
internal static class RollingBoulderFacing
{
    private sealed class Shot
    {
        public NRollingBoulderVfx Vfx = null!;
        public Sprite2D? Twin;
        public Sprite2D? Source;
        public Node2D? Shadow;
        public bool Hidden;
    }

    private static readonly List<Shot> _shots = new();
    private static AccessTools.FieldRef<NRollingBoulderVfx, Sprite2D>? _boulderField;
    private static AccessTools.FieldRef<NRollingBoulderVfx, Node2D>? _shadowField;
    private static int _logs;

    /// <summary>`NRollingBoulderVfx.Create` 的后缀：目标是玩家 ⇒ 记一笔，交给每帧同步。</summary>
    internal static void OnCreated(NRollingBoulderVfx? vfx, IEnumerable<Creature>? targets)
    {
        try
        {
            if (vfx == null || !GodotObject.IsInstanceValid(vfx)) return;
            if (!TargetsArePlayers(targets)) return;      // 玩家自己的滚石：一个字都不动
            _shots.Add(new Shot { Vfx = vfx });
        }
        catch (Exception e) { Logger.Log("[CharBoss] 滚石镜像登记失败: " + e.Message); }
    }

    /// <summary>每帧一次（由 `BossFrameTickPatch` 的每帧段调用）。只读原版节点、只写替身。</summary>
    internal static void Tick()
    {
        if (_shots.Count == 0) return;
        try
        {
            for (int i = _shots.Count - 1; i >= 0; i--)
            {
                var shot = _shots[i];
                if (shot.Vfx == null || !GodotObject.IsInstanceValid(shot.Vfx))
                {
                    FreeTwin(shot);
                    _shots.RemoveAt(i);
                    continue;
                }
                try { Sync(shot); } catch { /* 单帧失败不影响其它 */ }
            }
        }
        catch { /* 纯外观 */ }
    }

    private static void Sync(Shot shot)
    {
        var vfx = shot.Vfx;

        if (shot.Twin == null)
        {
            // 原版节点进树之后才有意义（位置/贴图都在 `_Ready` 之后才就绪）
            if (!vfx.IsInsideTree()) return;
            var parent = vfx.GetParent();
            if (parent == null) return;

            _boulderField ??= AccessTools.FieldRefAccess<NRollingBoulderVfx, Sprite2D>("_boulder");
            _shadowField ??= AccessTools.FieldRefAccess<NRollingBoulderVfx, Node2D>("_shadow");
            shot.Source = _boulderField(vfx);
            shot.Shadow = _shadowField(vfx);
            if (shot.Source == null || shot.Source.Texture == null) return;

            var twin = new Sprite2D
            {
                Name = "CharBossBoulderTwin",
                Texture = shot.Source.Texture,
                ZIndex = vfx.ZIndex + 1,
                ZAsRelative = false,
            };
            parent.AddChild(twin);
            shot.Twin = twin;
        }

        float cx = vfx.GetViewportRect().Size.X * 0.5f;
        var source = shot.Source;
        var twinNode = shot.Twin;

        if (!shot.Hidden)
        {
            shot.Hidden = true;
            try { if (source != null) source.Visible = false; } catch { /* 忽略 */ }
            try { if (shot.Shadow != null) shot.Shadow.Visible = false; } catch { /* 忽略 */ }
            if (_logs++ < 3)
                Logger.Log("[CharBoss] 滚石：她打出的那颗改成朝左滚（隐藏原贴图 + 镜像替身）");
        }

        // 位置镜像（屏幕中线左右对调）
        twinNode.GlobalPosition = new Vector2(2f * cx - vfx.GlobalPosition.X, vfx.GlobalPosition.Y);
        // 自转反向：原版是「向右滚」的转法，镜像之后取反才像「向左滚」
        if (source != null)
        {
            twinNode.Rotation = -source.Rotation;
            // 尺寸 = 节点动画缩放 × 石头贴图自身缩放（少乘节点那份就会大得离谱）
            var s = source.Scale;
            var vs = vfx.Scale;
            twinNode.Scale = new Vector2(Math.Abs(vs.X * s.X), vs.Y * s.Y);
            twinNode.Modulate = source.Modulate;
        }
    }

    private static void FreeTwin(Shot shot)
    {
        try
        {
            if (shot.Twin != null && GodotObject.IsInstanceValid(shot.Twin)) shot.Twin.QueueFree();
        }
        catch { /* 忽略 */ }
        shot.Twin = null;
    }

    /// <summary>目标是不是全在玩家一侧（= 这一颗是我们的 Boss 打出来的）。</summary>
    private static bool TargetsArePlayers(IEnumerable<Creature>? targets)
    {
        try
        {
            if (targets == null) return false;
            bool any = false;
            foreach (var c in targets)
            {
                if (c == null) continue;
                any = true;
                if (c.Monster != null) return false;      // 有怪物 ⇒ 是玩家打的
            }
            return any;
        }
        catch { return false; }
    }

    /// <summary>重开战斗/切房间时清干净（替身节点自己会随原版节点一起消失）。</summary>
    internal static void Reset()
    {
        foreach (var shot in _shots) FreeTwin(shot);
        _shots.Clear();
    }
}

[HarmonyPatch(typeof(NRollingBoulderVfx), nameof(NRollingBoulderVfx.Create),
    new[] { typeof(IEnumerable<Creature>), typeof(decimal), typeof(Vector2?) })]
internal static class RollingBoulderVfxCreatePatch
{
    [HarmonyPostfix]
    public static void Postfix(NRollingBoulderVfx? __result,
                              IEnumerable<Creature> creatures)
    {
        RollingBoulderFacing.OnCreated(__result, creatures);
    }
}
#endif
