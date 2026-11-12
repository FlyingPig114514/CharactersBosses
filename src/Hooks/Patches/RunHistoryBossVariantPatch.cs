using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs.History;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 跑团历史里那行 **`Boss: …`** 显示**设计名（变体名）**，而不是角色名。
///
/// 用户 2026-11-11：
/// > 「能不能在历史记录这里额外显示出来该 boss 是哪个变体（如黑球机）？局内最上方仍显示储君、静默猎手这样。」
///
/// 为什么需要补丁、而不是改文案：
///   那一行读的是**遭遇的 `.title`**（反编译 `NMapPointHistoryHoverTip._Ready`）：
///   <code>
///   _mapPointRoomStatsLoc.Add("ModelTitle", SaveUtil.EncounterOrDeprecated(First(Rooms).ModelId).Title);
///   </code>
///   而我们的遭遇 `.title` 一律是**角色名**（静默猎手 / 储君 …）—— 那是为了修 v0.2.578 之前
///   「名字直接显示成 `encounters.SILENT_GRAND_FINALE_BOSS_ENCOUNTER.title`」的问题而定的，
///   而且局内顶栏也用它 ⇒ **不能改文案**，否则局内也会变成「黑球机」。
///   所以这里只在**历史悬浮框**渲染完之后把那一段替换掉：
///   遭遇 id → Key（`BossKeys.KeyOfEncounterEntry`）→ `<KEY>_BOSS.name`（monsters.json，各语言自带
///   的设计名，例如 zhs 黑球机 / eng Dark Orb Defect）。
///
/// 只对**我们的档位遭遇**生效：`KeyOfEncounterEntry` 对非档位键（基础遭遇 `SILENT_BOSS_ENCOUNTER`
/// 之类）返回 null，原版行为一个字都不动。
/// 取不到本地化名字时（例如 zhs 里 `NECROBINDER_FLESH_BOSS.name` 是空的）退回
/// `BossKeys.DesignNameOf`（中文设计短名），至少不会是键名。
/// </summary>
[HarmonyPatch(typeof(NMapPointHistoryHoverTip), nameof(NMapPointHistoryHoverTip._Ready))]
internal static class RunHistoryBossVariantPatch
{
    [HarmonyPostfix]
    public static void Postfix(NMapPointHistoryHoverTip __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var entry = traverse.Field("_entry").GetValue<MapPointHistoryEntry>();
            var rooms = entry?.Rooms;
            if (rooms == null || rooms.Count == 0) return;

            // 和原版同一个取法：看第一个房间
            var room = rooms[0];
            if (room == null || room.RoomType != RoomType.Boss) return;

            string? entryId = null;
            try { entryId = room.ModelId?.Entry; } catch { /* 忽略 */ }
            string? key = Core.BossKeys.KeyOfEncounterEntry(entryId);
            if (key == null) return;                 // 不是我们的档位遭遇 ⇒ 不碰

            string label = VariantLabel(key);
            if (string.IsNullOrEmpty(label)) return;

            var loc = traverse.Field("_mapPointRoomStatsLoc").GetValue<LocString>();
            var stats = traverse.Field("_roomStats").GetValue<RichTextLabel>();
            if (loc == null || stats == null) return;

            // 覆盖 `ModelTitle` 这一格，再重新渲染一次（原版 _Ready 已经渲染过一次）
            loc.Add("ModelTitle", label);
            stats.Text = loc.GetFormattedText();

            Logger.Log($"[CharBoss] 历史记录：{entryId} 的 Boss 名改用设计名「{label}」（{key}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 历史记录改设计名失败（忽略）: " + e.Message);
        }
    }

    /// <summary>变体显示名：优先 `<KEY>_BOSS.name`（各语言自带），取不到就用中文设计短名。</summary>
    private static string VariantLabel(string key)
    {
        try
        {
            var ls = new LocString("monsters", key + "_BOSS.name");
            string txt = ls.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(txt)
                && txt.IndexOf(".name", StringComparison.Ordinal) < 0   // 本地化缺失时会把键名原样吐回来
                && !string.Equals(txt, key + "_BOSS", StringComparison.Ordinal))
            {
                return txt;
            }
        }
        catch { /* 忽略 */ }
        return Core.BossKeys.DesignNameOf(key);
    }
}
