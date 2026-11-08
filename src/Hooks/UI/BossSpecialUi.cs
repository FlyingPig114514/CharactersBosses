using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using CharacterBosses.Monsters;

namespace CharacterBosses.UI;

/// <summary>
/// Boss 特殊效果说明——**自绘的可拖动面板**（<see cref="BossSpecialPanel"/>）。
///
/// 历史：最早用的是原版 <c>NHoverTipSet</c>，靠锚点跟随。斗蛐蛐里红蓝两队各一只我们的
/// Boss 时，共用锚点会互相把对方收掉（闪烁）；改成每只一份后不闪了，但**多个说明框会重叠**，
/// 而且原版说明框挪不开。用户 2026-11-08 要求：换成能自由摆放、记住位置、更小更靠边的小面板。
///
/// 现在的规则：
///   · 每只 Boss 一块面板，内容 = <c>PowerModel.HoverTips</c>（原版文案）；
///   · 玩家拖动后位置写进 <c>hud_layout.json</c>，下次同一种 Boss 直接沿用；
///   · 没拖过的按阵营自动排版：玩家侧靠**左上**，敌方侧靠**右上**，依次往下叠，不互相压。
/// </summary>
internal static class BossSpecialRichUi
{
    private sealed class State
    {
        public BossSpecialPanel? Panel;
        public string? PowerEntry;
    }

    private static readonly Dictionary<ulong, State> _states = new();

    /// <summary>自动排版时，每一边已经摆了几块（战斗结束清空）。</summary>
    private static readonly Dictionary<string, int> _slots = new();

    private const float MarginX = 14f;
    private const float TopY = 212f;   // 让开斗蛐蛐自己的「红队/蓝队」标签
    private const float StackStep = 104f;

    public static void Ensure(CharBossMonster boss)
    {
        try
        {
            if (boss?.Creature == null) return;
            ulong key = KeyOf(boss);

            // 同阵营多 Boss 时一律不显示（用户要求：显示空间太紧张）。红蓝各一只不算。
            int sameSide = 0;
            try
            {
                var teammates = boss.CombatState?.GetTeammatesOf(boss.Creature);
                if (teammates != null)
                    foreach (var c in teammates)
                        if (c?.Monster is CharBossMonster) sameSide++;
            }
            catch { sameSide = 1; }
            if (sameSide >= 2) { Hide(boss); return; }

            var powerType = boss.Def.SpecialPowerForAct(boss.ActIndex);
            if (powerType == null) { Hide(boss); return; }

            var power = FindPower(boss, powerType);
            if (power == null) return;      // Power 还没施加 → 下次刷新再试

            if (!_states.TryGetValue(key, out var st))
            {
                st = new State();
                _states[key] = st;
            }

            if (st.Panel == null || !GodotObject.IsInstanceValid(st.Panel))
            {
                string layoutKey = LayoutKeyOf(boss);
                st.Panel = new BossSpecialPanel(layoutKey, BossHudLayout.Load(layoutKey), BossHudLayout.LoadFontSize(layoutKey));
                var parent = BossHudLayer.Resolve(out string why);
                if (parent == null) { st.Panel = null; _states.Remove(key); return; }
                parent.AddChild(st.Panel);
                st.Panel.ApplyPendingPosition();
                if (!st.Panel.Placed) PlaceDefault(boss, st.Panel, layoutKey);
                Logger.Log($"[CharBoss] 特殊效果面板创建: {layoutKey} parent={why} 位置={st.Panel.Position}");
            }

            st.Panel.SetTips(power.HoverTips);
            st.PowerEntry = power.Id.Entry;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 特殊效果面板失败: " + e);
        }
    }

    /// <summary>没拖过时的自动排版：玩家侧左上、敌方侧右上，依次往下叠。</summary>
    private static void PlaceDefault(CharBossMonster boss, BossSpecialPanel panel, string layoutKey)
    {
        try
        {
            bool playerSide;
            try { playerSide = boss.Creature?.Side == MegaCrit.Sts2.Core.Combat.CombatSide.Player; }
            catch { playerSide = true; }

            int index = 0;
            if (_slots.TryGetValue(layoutKey, out int existing)) index = existing;
            else
            {
                index = _slots.Count(kv => kv.Key.StartsWith(playerSide ? "P:" : "E:", StringComparison.Ordinal));
                _slots[layoutKey] = index;
            }

            float screenW = 1920f;
            try { screenW = panel.GetViewport()?.GetVisibleRect().Size.X ?? 1920f; } catch { }
            float x = playerSide ? MarginX : Math.Max(MarginX, screenW - BossSpecialPanel.MinWidth - MarginX);
            panel.Position = new Vector2(x, TopY + index * StackStep);
            panel.Placed = false;   // 没拖过：允许下次重排
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 说明框排版失败: " + e.Message);
        }
    }

    public static void Hide(CharBossMonster boss)
    {
        try
        {
            if (boss?.Creature == null) return;
            ulong key = KeyOf(boss);
            if (!_states.TryGetValue(key, out var st)) return;
            _states.Remove(key);
            if (st.Panel != null && GodotObject.IsInstanceValid(st.Panel)) st.Panel.QueueFree();
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 说明框清除失败: " + e.Message);
        }
    }

    public static void HideAll()
    {
        foreach (var st in _states.Values)
        {
            try { if (st.Panel != null && GodotObject.IsInstanceValid(st.Panel)) st.Panel.QueueFree(); }
            catch { /* 忽略 */ }
        }
        _states.Clear();
        _slots.Clear();
    }

    private static ulong KeyOf(CharBossMonster boss)
    {
        try { return boss.Creature?.CombatId ?? 0; }
        catch { return 0; }
    }

    /// <summary>位置存档用的键：怪物类名（例如 <c>SilentShivBoss</c>），同一种 Boss 跨战斗沿用。</summary>
    private static string LayoutKeyOf(CharBossMonster boss)
    {
        try { return boss.GetType().Name; }
        catch { return "Boss"; }
    }

    private static PowerModel? FindPower(CharBossMonster boss, Type powerType)
    {
        var creature = boss.Creature;
        var powers = creature?.Powers;
        if (powers == null) return null;
        for (int i = 0; i < powers.Count; i++)
        {
            var power = powers[i];
            if (power != null && powerType.IsInstanceOfType(power)) return power;
        }
        return null;
    }
}

/// <summary>
/// **特殊效果说明框的分发口**（v0.2.512）。
///
/// 两套实现并存，按「有没有装斗蛐蛐 / 战斗 UI 是否可见」选：
///   · 正常局（原版战斗 UI 正常显示）⇒ <see cref="BossSpecialTip"/>：原版 `NHoverTipSet` 说明框，
///     跟随立绘右侧、字体配色全是原版 —— **观感与本模组 v0.2.511 完全一致**；
///   · 斗蛐蛐（`CricketFightMod` 装了 / 或整层 CombatUi 被隐藏）⇒ <see cref="BossSpecialRichUi"/>：
///     自绘可拖动小面板（原版说明框在被隐藏的 UI 层里不显示、还会每 400ms 重建闪烁）。
///
/// 为什么不直接把自绘面板给所有人用：那是 PR #1 的做法，会改掉正常局的说明框外观与位置
///（且我们当初正是因为自绘定位错才换回原版框的）。所以这里按环境分流，正常局零变化。
/// </summary>
internal static class BossSpecialUi
{
    /// <summary>要不要走自绘面板（= 斗蛐蛐环境，或原版战斗 UI 被整层隐藏）。</summary>
    private static bool UseRich
    {
        get
        {
            try { if (Core.CricketCompat.Installed) return true; } catch { /* 忽略 */ }
            try { return !BossHudLayer.CombatUiVisible(); } catch { return false; }
        }
    }

    public static void Ensure(CharBossMonster boss)
    {
        try
        {
            if (UseRich)
            {
                BossSpecialTip.Hide();               // 换到自绘面板时先把原版说明框收掉
                BossSpecialRichUi.Ensure(boss);
            }
            else
            {
                BossSpecialRichUi.HideAll();         // 退回原版说明框时把自绘面板收干净
                BossSpecialTip.Ensure(boss);
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 特殊效果说明分发失败: " + e.Message); }
    }

    public static void Hide(CharBossMonster boss)
    {
        try
        {
            if (UseRich) BossSpecialRichUi.Hide(boss);
            else BossSpecialTip.Hide();
        }
        catch { /* 忽略 */ }
    }

    public static void HideAll()
    {
        try { BossSpecialRichUi.HideAll(); } catch { /* 忽略 */ }
        try { BossSpecialTip.Hide(); } catch { /* 忽略 */ }
    }
}