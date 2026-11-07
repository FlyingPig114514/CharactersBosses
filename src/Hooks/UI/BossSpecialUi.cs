using System;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using CharacterBosses.Monsters;

namespace CharacterBosses.UI;

/// <summary>
/// Boss 特殊效果的**原版说明框**（`NHoverTipSet`），常驻显示在立绘右侧。
///
/// 内容直接用 <c>PowerModel.HoverTips</c> —— 也就是原版能力说明那一套（标题 + 正文，读
/// <c>content/localization/*/powers.json</c> 的 <c>DEVIATION_POWER.title/description</c>），
/// 所以字体、配色、换行、图标全部是原版，我们一个像素都不自绘。
///
/// 定位照抄原版 <c>NOrb.OnFocus</c> 的三件套：
///
/// <code>
/// NHoverTipSet.CreateAndShow(anchor, tips, HoverTipAlignment.Right)?.SetFollowOwner();
/// </code>
///
/// <c>SetFollowOwner()</c> 会把面板钉在「锚点的当前全局位置」上，并且在面板自己的
/// <c>_Process</c> 里跟着锚点走 —— 所以不需要我们写任何每帧逻辑（本项目手牌栏那条
/// <c>ProcessFrame</c> 实测没生效，见 §11.28b，这里刻意不依赖它）。
/// 位置仍然每次刷新都重对一次（<see cref="Place"/>），因为 Boss 可能被模组拖走。
///
/// 早先自己画 <c>PanelContainer + Label</c> 的版本定位算错、跑到左上角，已整段删除；
/// 这一版不再有任何自绘节点。
/// </summary>
internal static class BossSpecialUi
{
    private static Control? _anchor;
    private static NHoverTipSet? _tipSet;

    /// <summary>"暂时不可见"（菜单打开等）时的重试间隔，避免每帧创建/销毁。</summary>
    private const long RetryIntervalMs = 400;
    private static readonly System.Diagnostics.Stopwatch _retryClock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>手牌/意图每次刷新时调一次；幂等。</summary>
    public static void Ensure(CharBossMonster boss)
    {
        try
        {
            // 多人（同场两只 Boss）时**一律不显示**特殊效果详细说明框 —— 用户要求：显示空间太紧张。
            // 这里是唯一的咽喉点（手牌栏刷新等各处都走它），所以在这里拦最稳。
            try
            {
                var enemies = boss?.CombatState?.Enemies;
                if (enemies != null)
                {
                    int ours = 0;
                    foreach (var c in enemies)
                        if (c?.Monster is CharBossMonster) ours++;
                    if (ours >= 2) { Hide(); return; }
                }
            }
            catch { /* 忽略：拿不到人数就按单人处理 */ }

            var powerType = boss.Def.SpecialPowerForAct(boss.ActIndex);
            if (powerType == null)
            {
                Hide();     // 这一幕没有特殊效果
                return;
            }

            var power = FindPower(boss, powerType);
            if (power == null) return;      // Power 还没施加上（战斗开始后才施加）→ 下次刷新再试

            if (_anchor == null || !GodotObject.IsInstanceValid(_anchor))
            {
                _anchor = MakeAnchor();
                _tipSet = null;
                if (_anchor == null) return;
            }

            Place(boss);

            // ⚠ 以前这里是 `if (_tipSet != null) return;` —— 结果**打开菜单/界面时说明框被原版收掉以后
            // 就再也回不来了**（用户实测：回到战斗界面也不显示）。
            // 现在每帧检查：节点失效就立刻重建；只是"暂时不可见"（例如菜单盖在上面）则每 0.4 秒重试一次，
            // 避免每帧创建/销毁。
            if (_tipSet != null && GodotObject.IsInstanceValid(_tipSet))
            {
                bool visible = false;
                try { visible = _tipSet.IsVisibleInTree(); } catch { }
                if (visible) return;

                var since = _retryClock.ElapsedMilliseconds;
                if (since < RetryIntervalMs) return;      // 还没到重试间隔
                _retryClock.Restart();
                try { NHoverTipSet.Remove(_anchor); } catch { /* 忽略 */ }
                _tipSet = null;
            }

            var tips = power.HoverTips.ToList();
            _tipSet = NHoverTipSet.CreateAndShow(_anchor, tips, HoverTipAlignment.Right);

            // 图层压低：说明框只是"常驻提示"，不该盖住抽牌堆/牌堆详情这类交互界面。
            // （ZIndex 只在同一个 CanvasLayer 内比较；这里设成负数让它排在同层兄弟后面。）
            if (_tipSet != null && GodotObject.IsInstanceValid(_tipSet))
            {
                _tipSet.ZIndex = -1;
                _tipSet.MouseFilter = Control.MouseFilterEnum.Ignore;
            }
            _tipSet?.SetFollowOwner();
            Logger.Log($"[CharBoss] 特殊效果说明框已显示: {power.Id.Entry}（{tips.Count} 条）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 特殊效果说明框失败: " + e);
        }
    }

    /// <summary>战斗结束/离开房间时调用。</summary>
    public static void Hide()
    {
        try
        {
            if (_anchor != null && GodotObject.IsInstanceValid(_anchor))
            {
                NHoverTipSet.Remove(_anchor);
                _anchor.QueueFree();
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 特殊效果说明框清除失败: " + e.Message);
        }
        _anchor = null;
        _tipSet = null;
    }

    private static PowerModel? FindPower(CharBossMonster boss, Type powerType)
    {
        var creature = boss.Creature;
        if (creature == null) return null;
        var powers = creature.Powers;
        if (powers == null) return null;
        for (int i = 0; i < powers.Count; i++)
        {
            var power = powers[i];
            if (power != null && powerType.IsInstanceOfType(power)) return power;
        }
        return null;
    }

    /// <summary>锚点挂在战斗房 UI 层（屏幕坐标系，和手牌栏同一个父级）。</summary>
    private static Control? MakeAnchor()
    {
        var ui = NCombatRoom.Instance?.Ui;
        Node? parent = ui?.GetNodeOrNull<Control>("%PlayContainer") ?? (Node?)ui;
        if (parent == null) return null;

        var anchor = new Control
        {
            Name = "BossSpecialAnchor",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = Vector2.One,
        };
        parent.AddChild(anchor);
        return anchor;
    }

    /// <summary>摆在立绘右侧，并且再让开「说明框自己的宽度」那么多（否则会挡住 Boss）。</summary>
    private static void Place(CharBossMonster boss)
    {
        if (_anchor == null || !GodotObject.IsInstanceValid(_anchor)) return;

        BossUiTuning.Refresh();      // brewSpecialY 等微调值（改文件即生效）

        var node = boss.Creature != null ? NCombatRoom.Instance?.GetCreatureNode(boss.Creature) : null;
        if (node == null) return;

        var bounds = node.Visuals?.Bounds;
        Vector2 target;
        if (bounds != null && GodotObject.IsInstanceValid(bounds) && bounds.Size.X > 1f && bounds.Size.Y > 1f)
        {
            target = bounds.GetScreenPosition() + new Vector2(bounds.Size.X * 0.98f, bounds.Size.Y * 0.26f);
        }
        else
        {
            target = node.GetScreenPosition() + new Vector2(node.Size.X, 0f);
        }

        // 说明框是从锚点**向右**展开的；立绘包围盒的中心并不等于可见立绘的中心
        // （立绘是 scale 为负的镜像节点），只按包围盒右缘摆会压住 Boss。
        // 所以再右移一个说明框宽度 —— 这个宽度直接问原版节点要（组合最小尺寸，不用等排版帧）。
        target.X += BoxWidth();

        // 「炼药猎」的「调制」描述最长（5 行 + 警告），整块往上移一些才不顶到画面底部；
        // **其它 Boss 一律不动**（偏移 0）。用户 2026-10-26 要求。数值可热调：charbosses_ui.txt 的 brewSpecialY。
        if (UsesLongSpecialText(boss)) target.Y += BossUiTuning.BrewSpecialY;

        _anchor.Position = target;
    }

    /// <summary>这只 Boss 的特殊效果说明是不是「炼药猎」那条长文本（目前只有调制）。</summary>
    private static bool UsesLongSpecialText(CharBossMonster boss)
    {
        try { return boss.Def.SpecialPowerForAct(boss.ActIndex) == typeof(Powers.BrewModPower); }
        catch { return false; }
    }

    /// <summary>说明框宽度（量不到就给个经验值，下一次刷新会按真实宽度再对一次）。</summary>
    private static float BoxWidth()
    {
        try
        {
            if (_tipSet != null && GodotObject.IsInstanceValid(_tipSet))
            {
                float w = _tipSet.GetCombinedMinimumSize().X;
                if (w > 1f) return w;
                if (_tipSet.Size.X > 1f) return _tipSet.Size.X;
            }
        }
        catch { /* 忽略 */ }
        return 320f;
    }
}
