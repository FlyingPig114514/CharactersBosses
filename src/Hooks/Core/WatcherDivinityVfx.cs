using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace CharacterBosses.Core;

/// <summary>
/// **生物进入「神格」时，把观者 mod 的那套神格特效放出来**（用户 2026-11-24 → 2026-11-25）。
///
/// 用户原话：
/// > 「让怪物进入神格时周围也浮现观者处于神格的特效（周围有紫雾、紫色眼睛）
/// >   装了哪个观者 mod 就用哪个 mod 的对应美术，同时装就 A 版优先」
/// > 「你这画的什么玩意，不要自己乱画，用原本的素材」
///
/// ⇒ **只用两个观者 mod 自己的美术**，一个像素都不自己画：
///
/// | 实现 | 用的东西（原件） |
/// |---|---|
/// | **A 版（Boninall，3747526116，优先）** | `WatcherMod.StanceVfxHelper.CreateDivinityVfx()` —— 返回一个 `Node2D`，里面就是观者神格那两层紫色 `CpuParticles2D`（雾 + 上升粒子）；再顺手调 `WatcherMod.WatcherAttackVfxHelper.StartDivinityEye(owner)`（紫色眼睛） |
/// | **B 版（lamali，3747492505）** | 场景原件 `res://Watcher/scenes/watcher_mod/vfx/divinity_aura.tscn`（`DivinityStance.VfxConfig` 里写的就是它）+ 眼睛节点 `Watcher.Code.Stances.Vfx.DivinityEyeSpawner` |
///
/// 走过的坑（别再走一遍）：
///   1. 入口写过 `CharBossMonster?` —— 恐惧模式的「观者的支配」给的是**本幕所有敌人**（普通怪物也算），
///      转换结果是 null ⇒ 直接 return，一次都没执行。
///   2. 改成 `Creature` 后 A 版 `StartDivinityEye` 调用成功但画面上什么都没有（它内部按**玩家**摆位）。
///   3. 我自己用 `Sprite2D` 手写了一版「一圈眼睛 + 雾」，用户：**不要自己乱画** ⇒ 已全部删掉。
///
/// 现在：直接用 A 版的 `CreateDivinityVfx()` / B 版的原场景，挂到生物节点上；
/// 只按生物大小做一次**整体缩放**（原件是按玩家身高设计的，怪物大得多，不放大就看不见）。
/// </summary>
internal static class WatcherDivinityVfx
{
    private static int _logs;

    private const string AuraSceneB = "res://Watcher/scenes/watcher_mod/vfx/divinity_aura.tscn";

    /// <summary>生物 → 我们挂上去的节点（退神格 / 生物死亡时收掉）。</summary>
    private static readonly Dictionary<Creature, Node> Attached = new();

    /// <summary>进神格：挂上观者 mod 原版的神格特效（紫雾 + 紫色眼睛）。</summary>
    internal static void Play(Creature? creature)
    {
        try
        {
            if (creature == null || !creature.IsAlive) return;
            if (Attached.ContainsKey(creature)) return;      // 已经在显示

            var host = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.GetCreatureNode(creature);
            if (host == null) { Log("[CharBoss] 神格特效：找不到生物节点 ⇒ 跳过"); return; }

            float scale = BodyScale(host as MegaCrit.Sts2.Core.Nodes.Combat.NCreature);

            var root = new Node2D { Name = "CharBossDivinityVfx" };
            root.Scale = Vector2.One * scale;

            int added = 0;

            // ① A 版：它自己的神格雾 + 粒子（原版整套构造）
            try
            {
                var type = ModTypeFinder.Find("WatcherMod.StanceVfxHelper");
                var method = type?.GetMethod("CreateDivinityVfx",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (method?.Invoke(null, null) is Node2D aura)
                {
                    root.AddChild(aura);
                    added++;
                }
            }
            catch { /* 试下一套 */ }

            // ①b A 版：紫色眼睛（它在怪物身上可能什么都不画，调一下没有副作用）
            try { InvokeStatic("WatcherMod.WatcherAttackVfxHelper", "StartDivinityEye", creature); }
            catch { /* 纯表现 */ }

            // ② B 版：它自己的神格光环场景 + 眼睛节点
            if (added == 0)
            {
                try
                {
                    if (ResourceLoader.Exists(AuraSceneB)
                        && ResourceLoader.Load<PackedScene>(AuraSceneB) is { } packed
                        && packed.Instantiate() is Node auraB)
                    {
                        root.AddChild(auraB);
                        added++;
                    }
                }
                catch { /* 试下一个 */ }

                try
                {
                    var eyeType = ModTypeFinder.Find("Watcher.Code.Stances.Vfx.DivinityEyeSpawner");
                    if (eyeType != null && typeof(Node).IsAssignableFrom(eyeType)
                        && Activator.CreateInstance(eyeType) is Node eyes)
                    {
                        root.AddChild(eyes);
                        added++;
                    }
                }
                catch { /* 忽略 */ }
            }

            if (added == 0)
            {
                Log("[CharBoss] 神格特效：两个观者 mod 都取不到原版神格素材 ⇒ 跳过（不影响战斗）");
                return;
            }

            host.AddChild(root);
            Attached[creature] = root;
            Log($"[CharBoss] 神格特效：已用观者 mod 原版素材挂上（{creature.Name}，套数={added}，缩放={scale:0.00}）");
        }
        catch (Exception e) { Log("[CharBoss] 神格特效挂载失败（忽略）: " + e.Message); }
    }

    /// <summary>退神格：收掉。</summary>
    internal static void Stop(Creature? creature)
    {
        try
        {
            if (creature == null) return;

            // A 版的眼睛要它自己关
            try { InvokeStatic("WatcherMod.WatcherAttackVfxHelper", "StopDivinityEye", creature); }
            catch { /* 纯表现 */ }

            if (!Attached.TryGetValue(creature, out var root)) return;
            Attached.Remove(creature);
            try
            {
                if (root != null && GodotObject.IsInstanceValid(root))
                {
                    // B 版眼睛节点有 StopSpawning()，先叫它停再摘
                    foreach (var child in root.GetChildren())
                    {
                        if (child == null) continue;
                        child.GetType().GetMethod("StopSpawning", BindingFlags.Public | BindingFlags.Instance)
                            ?.Invoke(child, null);
                    }
                    root.QueueFree();
                }
            }
            catch { /* 忽略 */ }
            Log($"[CharBoss] 神格特效：{creature.Name} 的神格特效已收");
        }
        catch (Exception e) { Log("[CharBoss] 神格特效收尾失败（忽略）: " + e.Message); }
    }

    /// <summary>生物死亡 / 离场时兜底收一次（由每帧慢车道调用）。</summary>
    internal static void StopIfDead()
    {
        try
        {
            if (Attached.Count == 0) return;
            List<Creature>? dead = null;
            foreach (var pair in Attached)
            {
                if (pair.Key == null || !pair.Key.IsAlive) (dead ??= new List<Creature>()).Add(pair.Key!);
            }
            if (dead == null) return;
            foreach (var c in dead) Stop(c);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 整体缩放：原件是按**玩家**身高（约 260px）设计的，敌人（机甲骑士这种）个头大得多
    /// ⇒ 按体高比例放大，避免「贴在身上看不见」。只动缩放，不重画素材。
    /// </summary>
    private static float BodyScale(MegaCrit.Sts2.Core.Nodes.Combat.NCreature? node)
    {
        try
        {
            var bounds = node?.Visuals?.Bounds;
            if (bounds != null && GodotObject.IsInstanceValid(bounds) && bounds.Size.Y > 1f)
            {
                float scale = bounds.Size.Y / 260f;
                return Mathf.Clamp(scale, 0.8f, 2.6f);
            }
        }
        catch { /* 用默认 */ }
        return 1f;
    }

    private static void InvokeStatic(string typeFullName, string methodName, Creature creature)
    {
        var type = ModTypeFinder.Find(typeFullName);
        var method = type?.GetMethod(methodName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            null, new[] { typeof(Creature) }, null);
        method?.Invoke(null, new object[] { creature });
    }

    private static void Log(string message)
    {
        if (_logs++ > 30) return;
        try { Logger.Log(message); } catch { /* 忽略 */ }
    }
}
