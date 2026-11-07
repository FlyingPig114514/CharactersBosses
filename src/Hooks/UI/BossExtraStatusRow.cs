using System;
using System.Collections.Generic;
using Godot;
using CharacterBosses.Monsters;

namespace CharacterBosses.UI;

/// <summary>
/// 多人「多 Boss 同场」时，给**主面板之外的那只** Boss 补一条状态栏（遗物 / 能量 / 辉星）。
///
/// 关键前提（已在本轮改造）：<see cref="BossStatusRow"/> 的能量球节点已经改成「按行各记一份」
/// （`SetEnergyOf` / `EnergyOf`），排版 `Pin.cs` 也改成读当前行自己的节点 ——
/// 所以现在两条状态栏可以同时存在、各用各的能量球，不会互相抢节点。
///
/// 位置：作为**主面板的兄弟节点**、用 local 坐标相对主面板往上让一格
/// （房间是 0.8 缩放的，用全局坐标会飘，用户实测过）。
///
/// 手牌（卡牌排）仍然只有主面板那只显示 —— 那是 <see cref="BossHandUi"/> 单例结构的后续工作。
/// </summary>
public static class BossExtraStatusRow
{
    private static readonly Dictionary<CharBossMonster, Control> Rows = new();

    /// <summary>节流：`Fill` 会销毁并重建整排遗物图标，每帧跑会明显掉帧（用户实测「boss 战好卡」）。</summary>
    private static ulong _lastUsec;
    private const ulong MinIntervalUsec = 200_000;   // 5Hz 足够

    /// <summary>上一帧填过的内容指纹，没变就不重填。</summary>
    private static readonly Dictionary<CharBossMonster, string> Fingerprints = new();

    public static void Update(List<CharBossMonster> bosses, CharBossMonster? primary)
    {
        try
        {
            if (bosses == null || bosses.Count < 2) { HideAll(); return; }

            var now = Time.GetTicksUsec();
            if (now - _lastUsec < MinIntervalUsec) return;
            _lastUsec = now;

            var panel = BossHandUi.Panel;
            if (panel == null || !GodotObject.IsInstanceValid(panel)) return;
            var parent = panel.GetParent() as Control;
            if (parent == null) return;

            foreach (var boss in bosses)
            {
                if (boss == null || ReferenceEquals(boss, primary)) continue;   // 主面板那只不重复画

                if (!Rows.TryGetValue(boss, out var row) || row == null || !GodotObject.IsInstanceValid(row))
                {
                    row = BossStatusRow.Ensure(parent, null);
                    if (row == null) continue;
                    Rows[boss] = row;
                    Logger.Log($"[CharBoss] 多 Boss：给 {boss.Id.Entry} 补一条状态栏（遗物/能量/辉星）");
                }

                // 内容指纹没变就不重填（Fill 会销毁重建整排遗物图标，很贵）
                var pcs = boss.Shadow?.PlayerCombatState;
                string fp = $"{pcs?.Energy}|{pcs?.Stars}|{pcs?.Hand.Cards.Count}|{boss.Shadow?.Relics?.Count}";
                if (Fingerprints.TryGetValue(boss, out var old) && old == fp) { /* 位置仍要跟一下 */ }
                else
                {
                    BossStatusRow.Fill(row, boss);
                    BossStatusRow.Place(row, panel.Size.X);
                    Fingerprints[boss] = fp;
                }

                // 位置：摆在主面板**左边**（之前放上方会顶到屏幕外/被顶栏挡住，用户实测看不到）。
                // 同一父节点 → 用 local 坐标；再夹一次非负，保证不会跑到屏幕外。
                float rowH = BossStatusRow.Height(row);
                var pos = panel.Position + new Vector2(-panel.Size.X - 16f, Math.Max(0f, panel.Size.Y * 0.5f - rowH * 0.5f));
                row.Position = new Vector2(Math.Max(0f, pos.X), Math.Max(0f, pos.Y));
                row.Visible = true;
                if (row.Size.Y < 1f) row.Size = new Vector2(panel.Size.X, Math.Max(1f, rowH));
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 多 Boss 额外状态栏失败: " + e.Message);
        }
    }

    public static void HideAll()
    {
        foreach (var kv in Rows)
        {
            try
            {
                if (kv.Value != null && GodotObject.IsInstanceValid(kv.Value)) kv.Value.QueueFree();
            }
            catch { /* 忽略 */ }
        }
        Rows.Clear();
    }
}
