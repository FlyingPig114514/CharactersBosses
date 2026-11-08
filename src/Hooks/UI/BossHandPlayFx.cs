using System;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using CharacterBosses.Monsters;

namespace CharacterBosses.UI;

/// <summary>
/// 出牌动画：照着**原版** <c>NCard.AnimCardToPlayPile</c> 的节奏来（用户 2026-11-08：
/// 「这个动画并不够自然，并不像是真正的游戏动画…希望能够模仿原版的动画」）。
///
/// 原版参数（反编译 <c>MegaCrit.Sts2.Core.Nodes.Cards.NCard</c>）：
/// <code>
/// PlayPileTween = CreateTween().SetParallel();
/// TweenProperty(this, "position", playPileTarget, 0.2).SetEase(Out).SetTrans(Cubic);
/// TweenProperty(this, "scale", Vector2.One * 0.8f, 0.2).SetEase(Out).SetTrans(Cubic);
/// </code>
/// 出牌区 = <c>PlayContainer</c> 中心再往上 100px。牌到那儿之后停一下（等效果结算），
/// 然后淡出收走。所以这里也分三段：0.2s 飞过去 → 停 0.18s → 0.2s 淡出。
/// </summary>
internal static class BossHandPlayFx
{
    /// <summary>飞向出牌区的时长（原版 0.2s；用户 2026-11-08 要求可在模组配置里调）。</summary>
    private static double FlySeconds => CharacterBosses.Core.BossConfig.CricketFxFlyMs / 1000.0;

    /// <summary>停在出牌区的时长（原版结算窗口，可在配置里调）。</summary>
    private static double HoldSeconds => CharacterBosses.Core.BossConfig.CricketFxHoldMs / 1000.0;

    /// <summary>淡出时长（可在配置里调）。</summary>
    private static double FadeSeconds => CharacterBosses.Core.BossConfig.CricketFxFadeMs / 1000.0;

    /// <summary>落到出牌区时的缩放（原版 Vector2.One * 0.8）。</summary>
    private static readonly Vector2 LandScale = new(0.8f, 0.8f);

    /// <summary>
    /// 把 <paramref name="cell"/> 从手牌栏里搬出来，按原版节奏飞到出牌区并淡出。
    /// <paramref name="cell"/> 必须已挂在树上；本方法接管所有权（调用方不要再 QueueFree）。
    /// </summary>
    public static void Fly(Control cell, CharBossMonster boss, Creature? target)
    {
        try
        {
            if (cell == null || !GodotObject.IsInstanceValid(cell) || !cell.IsInsideTree())
            {
                FreeNow(cell);
                return;
            }

            BossHandCard.ReleaseCell(cell);

            // ★ v0.2.512：这段飞牌动画是**斗蛐蛐专属**的观感修补（那边没有原版手牌、牌会直接消失）。
            //   正常局保持本模组一直以来的样子（打出去的牌直接收走），所以先要求「装了斗蛐蛐」。
            if (!CharacterBosses.Core.CricketCompat.Installed)
            {
                FreeNow(cell);
                return;
            }

            // 配置里关掉动画 ⇒ 直接收走（牌还是会被正确移除，只是不飞）。
            if (!CharacterBosses.Core.BossConfig.CricketFxEnabled)
            {
                FreeNow(cell);
                return;
            }

            var parent = BossHudLayer.Resolve(out string why);
            if (parent == null)
            {
                FreeNow(cell);
                return;
            }

            Vector2 from = cell.GlobalPosition;
            Vector2 size = cell.Size;
            Vector2 startScale = cell.Scale;

            var intents = cell.GetNodeOrNull<Control>(BossHandCard.IntentsRowName);
            if (intents != null && GodotObject.IsInstanceValid(intents)) intents.Visible = false;

            cell.GetParent()?.RemoveChild(cell);
            parent.AddChild(cell);
            cell.GlobalPosition = from;
            cell.Size = size;
            cell.Scale = startScale;
            cell.MouseFilter = Control.MouseFilterEnum.Ignore;
            cell.ZIndex = 120;

            Vector2 to = PlayArea(cell);
            Logger.Log("[CharBoss] 出牌动画 self=" + boss.GetType().Name
                       + " target=" + (target != null ? target.Name : "(无)")
                       + " from=" + from + " to=" + to);

            var tween = cell.CreateTween();
            tween.SetParallel(true);
            // 第一段：飞进出牌区（Cubic Out，跟原版一致）
            tween.TweenProperty(cell, "global_position", to, FlySeconds)
                 .SetTrans(Tween.TransitionType.Cubic)
                 .SetEase(Tween.EaseType.Out);
            tween.TweenProperty(cell, "scale", startScale * LandScale, FlySeconds)
                 .SetTrans(Tween.TransitionType.Cubic)
                 .SetEase(Tween.EaseType.Out);
            // 手牌是扇形摆的（每张都带一点旋转），原版打出时会摆正，这里也要转正，
            // 否则飞出去的那张会斜着飘，看着就不像原版。
            tween.TweenProperty(cell, "rotation", 0.0f, FlySeconds)
                 .SetTrans(Tween.TransitionType.Cubic)
                 .SetEase(Tween.EaseType.Out);

            // 第二段：停一下（原版这段时间在结算效果、打伤害数字）
            tween.Chain().TweenInterval(HoldSeconds);

            // 第三段：淡出收走
            tween.Chain().SetParallel(true);
            tween.TweenProperty(cell, "modulate:a", 0.0f, FadeSeconds)
                 .SetTrans(Tween.TransitionType.Cubic)
                 .SetEase(Tween.EaseType.In);
            tween.TweenProperty(cell, "scale", startScale * LandScale * 0.92f, FadeSeconds)
                 .SetTrans(Tween.TransitionType.Cubic)
                 .SetEase(Tween.EaseType.In);

            tween.Chain().TweenCallback(Callable.From(() => FreeNow(cell)));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] play fx failed: " + e.Message);
            FreeNow(cell);
        }
    }

    /// <summary>出牌区中心：<c>PlayContainer</c> 能用就用它（原版就是这么算的），否则退到屏幕中央偏上。</summary>
    private static Vector2 PlayArea(Control cell)
    {
        try
        {
            var container = NCombatRoom.Instance?.Ui?.PlayContainer;
            if (container != null && GodotObject.IsInstanceValid(container) && container.IsInsideTree())
            {
                var rect = container.GetGlobalRect();
                if (rect.Size.X > 32f && rect.Size.Y > 32f)
                    return rect.Position + rect.Size * 0.5f - cell.Size * 0.5f + new Vector2(0f, -100f);
            }
        }
        catch { /* 忽略，走兜底 */ }

        if (Engine.GetMainLoop() is SceneTree tree)
        {
            var screen = tree.Root.GetVisibleRect().Size;
            return new Vector2(screen.X * 0.5f, screen.Y * 0.5f - 100f) - cell.Size * 0.5f;
        }

        return cell.GlobalPosition;
    }

    private static void FreeNow(Control? cell)
    {
        try
        {
            if (cell == null || !GodotObject.IsInstanceValid(cell)) return;
            BossHandCard.ReleaseCell(cell);
            cell.QueueFree();
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] play fx free failed: " + e.Message);
        }
    }
}
