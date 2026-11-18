using System;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using CharacterBosses.Monsters;

namespace CharacterBosses.UI;

// 说明：这个文件是**正常局（没装斗蛐蛐）**用的原版说明框实现，从 v0.2.511 的 BossSpecialUi.cs 原样搬过来，
// 只改了类名（BossSpecialUi → BossSpecialTip）；斗蛐蛐走另一套自绘面板（BossSpecialRichUi），由 BossSpecialUi 分发。

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
internal static class BossSpecialTip
{
    private static Control? _anchor;
    /// <summary>塔罗愚说明框定位日志上限（2 条）。</summary>
    private static int _foolPlaceLogs;
    private static NHoverTipSet? _tipSet;

    /// <summary>"暂时不可见"（菜单打开等）时的重试间隔，避免每帧创建/销毁。</summary>
    private const long RetryIntervalMs = 400;
    private static readonly System.Diagnostics.Stopwatch _retryClock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>
    /// **此刻该不该显示这条说明框**（用户 2026-11-16 第六轮：「特殊效果描述的层级不对，
    /// 切到地图页面了还能看见」）。
    ///
    /// 以前只判「有没有战斗房」。而这块框挂在**全局**提示层（`NGame.HoverTipsContainer`）上，
    /// 层级比地图 / 图例 / 牌堆详情这些界面还高 ⇒ 只要战斗房还活着（战斗中打开地图、图例、
    /// 牌堆详情都算），框就压在这些界面上面 —— 症状正是「切到地图页面还能看见」。
    ///
    /// 现在四条一起判，任一不满足就收掉（`Ensure` 下一帧会按需重建，不需要别的出口）：
    ///   ① 有战斗房且实例有效；
    ///   ② 没有任何浮层打开（`NOverlayStack.ScreenCount == 0`：地图 / 牌堆 / 选牌 / 图例都走它）；
    ///   ③ 地图屏没开着（`NMapScreen.IsOpen`，它有时不算浮层）；
    ///   ④ 当前屏幕就是战斗房（`ActiveScreenContext.IsCurrent(room)`，与 `NCombatRoom` 自己用的判据一致）。
    /// </summary>
    internal static bool CombatScreenActive()
    {
        string reason;
        bool ok = CombatScreenActive(out reason);
        if (!ok && reason != null && _screenLogs++ < 8)
        {
            Logger.Log($"[CharBoss] 特殊效果说明框：当前不该显示（{reason}）⇒ 收掉");
        }
        return ok;
    }

    private static int _screenLogs;

    private static bool CombatScreenActive(out string? reason)
    {
        reason = null;
        try
        {
            var room = NCombatRoom.Instance;
            if (room == null || !GodotObject.IsInstanceValid(room))
            {
                reason = "没有战斗房";
                return false;
            }

            try
            {
                var overlays = NOverlayStack.Instance;
                if (overlays != null && GodotObject.IsInstanceValid(overlays) && overlays.ScreenCount > 0)
                {
                    reason = $"有 {overlays.ScreenCount} 层浮层压着（地图/牌堆/图例一类）";
                    return false;
                }
            }
            catch { /* 拿不到就按没开 */ }

            try
            {
                var map = NMapScreen.Instance;
                if (map != null && GodotObject.IsInstanceValid(map) && map.IsOpen)
                {
                    reason = "地图屏开着";
                    return false;
                }
            }
            catch { /* 拿不到就按没开 */ }

            try
            {
                if (!ActiveScreenContext.Instance.IsCurrent(room))
                {
                    reason = "当前屏幕不是战斗房";
                    return false;
                }
            }
            catch { /* 拿不到判据就放行 */ }

            return true;
        }
        catch { reason = "判据本身抛异常"; return false; }
    }

    /// <summary>手牌/意图每次刷新时调一次；幂等。</summary>
    public static void Ensure(CharBossMonster boss)
    {
        try
        {
            // ★ 2026-11-15（用户：「切到地图页面都还能看到」）+ 2026-11-16（「层级不对」）：
            //   原版 `NHoverTipSet` 挂在**全局提示层**上，常驻之后**跨界面存活、且压在别的界面上面**。
            //   所以只要「当前不是战斗屏」就一律收掉（判据见 `CombatScreenActive`）。
            if (!CombatScreenActive())
            {
                if (_tipSet != null || _anchor != null) Hide();
                return;
            }

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
            Patches.BossSpecialTipRoomGuardPatch.Register(_tipSet);   // 交给「离房即收」的守卫

            // 图层压低：说明框只是"常驻提示"，不该盖住抽牌堆/牌堆详情这类交互界面。
            // （ZIndex 只在同一个 CanvasLayer 内比较；这里设成负数让它排在同层兄弟后面。）
            if (_tipSet != null && GodotObject.IsInstanceValid(_tipSet))
            {
                _tipSet.ZIndex = -1;
                _tipSet.MouseFilter = Control.MouseFilterEnum.Ignore;
            }
            _tipSet?.SetFollowOwner();
            try { Logger.Log($"[CharBoss] 说明框实际位置: {_tipSet!.GlobalPosition} 尺寸={_tipSet.Size} 锚点={_anchor.Position}"); } catch { }
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
        // ★ 2026-11-15（用户：「切到地图页面还是能看到」）：说明框挂在**全局**提示层上，
        //   而以前只通过锚点 `NHoverTipSet.Remove(_anchor)` 去收 —— 离开战斗房时锚点是随房间
        //   一起被释放的，`IsInstanceValid(_anchor)` 已经为 false ⇒ **一次都没收过**，
        //   接着又把 `_tipSet` 清空 ⇒ 那块框永久留在了屏幕上层。
        //   现在对**框本身**动手：先按锚点 Remove，再对框兜底 Visible=false + QueueFree。
        var tip = _tipSet;
        try
        {
            if (_anchor != null && GodotObject.IsInstanceValid(_anchor))
            {
                try { NHoverTipSet.Remove(_anchor); } catch { /* 兜底在下面 */ }
                _anchor.QueueFree();
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 特殊效果说明框清除失败: " + e.Message);
        }
        try
        {
            if (tip != null && GodotObject.IsInstanceValid(tip))
            {
                tip.Visible = false;
                tip.QueueFree();
            }
        }
        catch { /* 纯外观 */ }
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
    private static Control? MakeAnchor(bool atUiRoot = false)
    {
        var ui = NCombatRoom.Instance?.Ui;
        // ★ 2026-11-15：`%PlayContainer` 自己带变换（局部坐标 ≠ 屏幕坐标），塔罗愚那条定位怎么算都不对
        //   ⇒ 他改用 **ui 根节点**（变换为单位矩阵，Position 就是屏幕坐标）。其它 Boss 仍用 PlayContainer。
        Node? parent = atUiRoot
            ? (Node?)ui
            : (ui?.GetNodeOrNull<Control>("%PlayContainer") ?? (Node?)ui);
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

        // ★ 2026-11-15（用户第三次订正：「用原版的框啊」）——
        //   撤回自绘面板，**塔罗愚也用原版 `NHoverTipSet`**；只做一件事：
        //   原公式算出来 `target.X = 立绘右缘 + 框宽 = 1982` 会顶出 1920 的屏幕 ⇒ 把它收到屏内。
        //   `target` 是**框的右缘**（其它 Boss 实测：框占 [target−框宽, target]），
        //   所以这里只需 `target.X = min(target.X, 屏宽 − 8)`，Y 完全不碰。
        //   其它 Boss 本来就不越界 ⇒ 这个 min 对它们没有任何影响。
        if (IsFoolBoss(boss))
        {
            try
            {
                var viewport = _anchor.GetViewportRect().Size;
                float maxRight = Math.Max(8f, viewport.X - 8f);
                if (target.X > maxRight)
                {
                    Logger.Log($"[CharBoss] 塔罗愚的说明框右缘越界 ⇒ 收到屏内: X {target.X:0} → {maxRight:0}" +
                               $"（屏幕 {viewport}，框宽 {BoxWidth():0}）");
                    target.X = maxRight;
                }
            }
            catch { /* 量不到就原样放 */ }
        }
        _anchor.Position = target;
    }

    /// <summary>这只 Boss 的特殊效果说明是不是「炼药猎」那条长文本（目前只有调制）。</summary>
    private static bool UsesLongSpecialText(CharBossMonster boss)
    {
        try
        {
            // ★ 2026-11-15（用户：「由于这个特殊效果太长了，往上移动一些，其它boss说明框的位置不改变」）：
            //   神格观（塔罗牌之后第二长的说明：虔信 4 行 + 空行 + 神格 2 行）和炼药猎的「调制」一样，
            //   套用同一个偏移（`BossUiTuning.BrewSpecialY`，默认 −120，`charbosses_ui.txt` 可热调）。
            //   **只加这一只，其它 Boss 的说明框位置一个像素都不动。**
            //
            // ★ v0.2.713（用户 2026-11-16：「这个特殊效果的描述并不长，你描述框用正常的位置就行，
            //   目前的位置有些偏高」）：印牌观（`WATCHER_CREATE`）是**同一个观者角色的第三套设计**，
            //   它的 `Def.Key` 也是 `WATCHER_DIVINITY` ⇒ 以前会白吃神格观那个 −120 偏移。
            //   但它那条说明只有两行 ⇒ 必须排除掉，用正常位置。
            //
            // ★ v0.2.733（用户 2026-11-17）：**保留观**（`WATCHER_RETAIN`）同理 —— 同一个 `Def.Key`、
            //   说明也只有两行 ⇒ 同样用正常位置。
            // ★ v0.2.754（用户 2026-11-18：「预见观的特殊效果描述框偏高了，它很短不用特意调高
            //   （别动别的 boss）」）：预见观的「通晓万物」只有两行 ⇒ 用正常位置。
            //   判据用**特殊效果类型**而不是阵容 latch —— 全模组只有预见观带 `BossOmnisciencePower`，
            //   所以这条绝不会误伤别的 Boss（神格观的 −120 偏移照旧）。
            try
            {
                if (boss.Def.SpecialPowerForAct(boss.ActIndex) == typeof(Powers.BossOmnisciencePower)) return false;
            }
            catch { /* 取不到就按原逻辑走 */ }

            if (boss.Def.Key == "WATCHER_DIVINITY"
                && !Core.BossLineups.WatcherCreateActive()
                && !Core.BossLineups.WatcherRetainActive()
                && !Core.BossLineups.WatcherForesightActive()) return true;
            return boss.Def.SpecialPowerForAct(boss.ActIndex) == typeof(Powers.BrewModPower);
        }
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

    /// <summary>说明框高度（量不到就给个经验值）。</summary>
    private static float BoxHeight()
    {
        try
        {
            var box = _tipSet ?? throw new InvalidOperationException();
            return box.Size.Y > 1f ? box.Size.Y : 160f;
        }
        catch { return 160f; }
    }

    /// <summary>是不是「塔罗愚」（愚者）。他的说明框固定贴屏幕右上角（用户 2026-11-15）。</summary>
    private static bool IsFoolBoss(CharBossMonster? boss)
    {
        try { return boss?.Def?.Key == "FOOL_BOSS"; } catch { return false; }
    }
}
