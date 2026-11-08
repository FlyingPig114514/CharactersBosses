using System;
using Godot;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using CharacterBosses.Core;

namespace CharacterBosses.Monsters;

/// <summary>
/// 角色 Boss 的「外观适配」：都是因为**复用玩家角色的立绘当敌人**才需要做的事。
/// </summary>
public static class BossVisuals
{
    private static readonly System.Collections.Generic.HashSet<string> _warned = new();

    /// <summary>
    /// 建角色动画器（而不是怪物动画器）。
    ///
    /// 原因：<c>NCreature</c> 建动画器时是 <c>if (Entity.Player != null) 角色动画器 else 怪物动画器</c>，
    /// 而我们在 <c>AfterAddedToRoom</c> 才绑影子 Player —— 那一刻 <c>Entity.Player</c> 还是 null，
    /// 于是拿到了**怪物动画器**。两者状态名不一样：
    ///   角色：("cast","Cast") ("attack","Attack") ("hurt","Hit") ("cast","PowerUp")
    ///   怪物：状态名直接是 "attack" / "cast" / "hurt"
    /// 而 <c>AttackCommand.FromCard</c> 请求的是 **"Attack"**（大写）⇒ 怪物动画器里没有 → 不出攻击动画。
    /// 这里直接委托给角色模型自己的动画器，状态名就全对上了。
    /// </summary>
    public static CreatureAnimator? BuildCharacterAnimator(MonsterModel monster, BossDefinition def, MegaSprite controller)
    {
        try
        {
            var character = ModelDb.GetById<CharacterModel>(ModelDb.GetId(def.CharacterType));
            // ⚠ `MonsterModel.Creature` 在「还没挂进战斗」时**会抛异常**（"Creature was accessed before
            //   it was set."），而这里正好是建立绘的阶段 —— 直接读就会把整段 try 打断。
            //   以前每次建立绘都刷一行「角色动画器创建失败」，日志里 50+ 行全是噪音。
            MegaCrit.Sts2.Core.Entities.Creatures.Creature? creature = null;
            try { creature = monster.Creature; } catch { /* 还没挂上，退回怪物动画器即可 */ }
            if (character != null && creature != null)
            {
#if STS2_107
                return character.GenerateAnimator(controller);
#else
                return character.GenerateAnimator(controller, creature!);
#endif
            }
        }
        catch (Exception e)
        {
            // 同一只 Boss 只报一次，避免立绘反复创建时刷屏。
            if (_warned.Add(monster.GetType().Name))
                Logger.Log("[CharBoss] 角色动画器创建失败，退回怪物动画器: " + e.Message);
        }
        return null;
    }

    /// <summary>
    /// 把立绘水平镜像，让 Boss 面向玩家。
    ///
    /// 角色立绘是给「玩家站在左侧、朝右」画的；放到敌方一侧（玩家在左、Boss 在右）就会背对玩家。
    /// 镜像 `Visuals.Body`（而不是 `Visuals.Scale`）—— 因为游戏会反复把 `Visuals.Scale` 设成
    /// `Vector2.One * scale`（`SetScaleAndHue` / `DoScaleTween`），改它会被下一次缩放抹掉。
    /// </summary>
    public static void FaceThePlayer(MonsterModel monster, bool log = true)
    {
        try
        {
            var node = NCombatRoom.Instance?.GetCreatureNode(monster.Creature);
            var visuals = node?.Visuals;
            if (visuals == null) return;

#if STS2_107
            var body = visuals.GetCurrentBody();
#else
            var body = visuals.Body;
#endif
            if (body != null)
            {
                var scale = body.Scale;
                if (scale.X > 0f)
                {
                    body.Scale = new Vector2(-scale.X, scale.Y);
                    if (log) Logger.Log($"[CharBoss] 立绘已水平镜像（面向玩家）via Body，scale={body.Scale}");
                }
                return;
            }

            // 退路：角色场景里若没有 %Body，就翻 Visuals 本身（可能被下次缩放覆盖，每回合会重试）
            var vs = visuals.Scale;
            if (vs.X > 0f)
            {
                visuals.Scale = new Vector2(-vs.X, vs.Y);
                if (log) Logger.Log($"[CharBoss] 立绘已水平镜像 via Visuals，scale={visuals.Scale}");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 立绘镜像失败: " + e.Message);
        }
    }
}
