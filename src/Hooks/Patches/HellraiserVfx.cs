using System;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**地狱狂徒**」自动打出打击时的演出（用户 2026-11-08）。
///
/// 反编译实证：
///   · 打出**地狱狂徒这张牌**：`NHellraiserVfx`（范围很大的刀阵）+ PowerUp 动作 —— 用户说**用错了**；
///   · 地狱狂徒**自动打出打击**：攻击指令 `WithHitFx("vfx/hellraiser_attack_vfx")`
///     `.WithHitVfxSpawnedAtBase()` ⇒ **脚下一小片刀阵**，对应节点
///     `MegaCrit.Sts2.Core.Nodes.Vfx.Cards.NHellraiserAttackVfx`（`Node2D`，`_Ready` 里播 `%Sword` 的补间）。
///
/// 那个节点**没有静态 Create**，只能从场景实例化 ⇒ 这里按几个常见的 res:// 路径**探测**哪一个是它，
/// 找到就实例化并挂到 `NCombatRoom.CombatVfxContainer`（和原版命中特效同一个容器），
/// 位置对准她的脚下（`WithHitVfxSpawnedAtBase` 的语义）。都不存在就只打一行日志，不影响出牌。
///
/// ⚠ v0.2.487 那次「压 TriggerAnim 只留铁山靠」已经证明会打断出牌动画的 await 链（卡牌卡空中、不结算伤害），
///   所以**不再压制任何动画**；铁山靠用 `TriggerAnim(her, "Cast")` 额外补一次。
/// </summary>
internal static class HellraiserVfx
{
    private static readonly string[] Candidates =
    {
        "res://scenes/vfx/hellraiser_attack_vfx.tscn",
        "res://vfx/hellraiser_attack_vfx.tscn",
        "res://nodes/vfx/hellraiser_attack_vfx.tscn",
        "res://scenes/nodes/vfx/hellraiser_attack_vfx.tscn",
        "res://scenes/vfx/cards/hellraiser_attack_vfx.tscn",
        "res://vfx/hellraiser_attack_vfx/hellraiser_attack_vfx.tscn",
    };

    private static bool _reported;

    internal static void Spawn(Creature? her, Creature? target = null)
    {
        if (her == null) return;

        try { _ = CreatureCmd.TriggerAnim(her, "Cast", 0.6f); }
        catch (Exception e) { Logger.Log("[CharBoss] 地狱狂徒动作（Cast）失败（忽略）: " + e.Message); }

        // ★ v0.2.490（用户 2026-11-08：「那个小刀阵应该在他攻击的敌人脚下」）：
        //   刀阵挂在**被打的敌人**脚下（对应原版 `HitVfxSpawnedAtBase` 的语义），不是她自己脚下。
        SpawnSmallBlades(target ?? her);
    }

    private static void SpawnSmallBlades(Creature at)
    {
        try
        {
            // 已经找到过就把路径记住，之后不再探测
            string? path = _foundPath;
            if (path == null)
            {
                foreach (var candidate in Candidates)
                {
                    bool exists = false;
                    try { exists = ResourceLoader.Exists(candidate); } catch { exists = false; }
                    if (exists) { path = candidate; break; }
                }
                if (path == null)
                {
                    if (!_reported)
                    {
                        _reported = true;
                        Logger.Log("[CharBoss] 地狱狂徒：没探到 hellraiser_attack_vfx 的场景路径 ⇒ 这一版只有铁山靠、没有脚下刀阵");
                    }
                    return;
                }
                _foundPath = path;
                Logger.Log($"[CharBoss] 地狱狂徒：脚下一小片刀阵的场景路径 = {path}");
            }

            var scene = GD.Load<PackedScene>(path);
            if (scene == null) return;
            var node = scene.Instantiate();
            if (node == null) return;

            var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
            var container = room?.CombatVfxContainer;
            if (container == null) return;

            // 位置：`WithHitVfxSpawnedAtBase` = 打在**被打的那个敌人**脚下
            try
            {
                if (node is Node2D node2d)
                {
                    var visual = room?.GetCreatureNode(at);
                    if (visual != null) node2d.GlobalPosition = visual.GlobalPosition;
                }
            }
            catch { /* 位置失败就放原点 */ }

            container.AddChild(node);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 地狱狂徒脚下刀阵失败（忽略）: " + e.Message); }
    }

    private static string? _foundPath;

    /// <summary>兼容旧调用。</summary>
    internal static void Flash(Creature? victim) => Spawn(victim, victim);
}