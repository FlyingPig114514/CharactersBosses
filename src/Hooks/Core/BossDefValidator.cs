using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Core;

/// <summary>
/// 启动自检：把所有 <see cref="BossDefinition"/> 按「角色 × 幕」解析一遍并写进日志。
///
/// 用途：你填完卡组不用进战斗就能确认 ——
///   · 三类数值（HP / 能量 / 手牌数）是不是你写的；
///   · 每一幕实际拿到哪些牌（是不是回落到起始卡组占位了）；
///   · **卡名写错会标 `(!)`** —— 这是最容易犯又最难查的错（编译能过，进战斗才发现少几张牌）。
/// 在第一次拉房间时触发一次（早于任何战斗）。
/// </summary>
public static class BossDefValidator
{
    private static bool _done;

    public static void ValidateOnce()
    {
        if (_done) return;
        _done = true;
        try
        {
            ValidateAll();
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 定义自检异常（不影响游戏）: " + e);
        }
    }

    private static void ValidateAll()
    {
        Logger.Log("[CharBoss] ===== 定义自检开始 =====");
        for (int i = 0; i < BossDefs.ByCharacter.Length; i++)
        {
            var def = BossDefs.ByCharacter[i];
            Logger.Log($"[CharBoss] #{i} {def.Key}: 角色={def.CharacterType.Name} 立绘={def.VisualScene} " +
                       $"HP={Join(def.Hp)} 能量={Join(def.Energy)} 手牌={Join(def.HandSize)}");
            ValidateActs(def);
            ValidateSkips(def);
            ValidateVariants(def);
        }
        Logger.Log("[CharBoss] ===== 定义自检结束（卡名带 (!) 的表示找不到，请对照 docs\\plans\\cards-*.md 修正）=====");
    }

    private static void ValidateActs(BossDefinition def)
    {
        for (int act = 0; act < 3; act++)
        {
            var cards = BossLoadout.ResolveDeckTypes(def, act);
            int missingCards = 0;
            var names = new List<string>();
            foreach (var type in cards)
            {
                var entry = ResolveEntry(type, isCard: true);
                if (entry == null)
                {
                    missingCards++;
                    names.Add(type.Name + "(!)");
                }
                else
                {
                    names.Add(entry);
                }
            }

            var relics = BossLoadout.ResolveRelicTypes(def, act);
            int missingRelics = relics.Count(t => ResolveEntry(t, isCard: false) == null);

            var placeholder = (def.DeckByAct[act] == null || def.DeckByAct[act]!.Count == 0) && def.Deck.Count == 0;

            Logger.Log($"[CharBoss]   act{act + 1}: {cards.Count} 张" +
                       (missingCards > 0 ? $" ★{missingCards} 张找不到" : "") +
                       (placeholder ? "（未定义 → 按卡池自动配的占位卡组）" : "") +
                       $" 牌组=[{string.Join(",", names)}]");
            Logger.Log($"[CharBoss]   act{act + 1}: 遗物 {relics.Count} 个" +
                       (missingRelics > 0 ? $" ★{missingRelics} 个找不到" : "") +
                       $" [{string.Join(",", relics.Select(t => ResolveEntry(t, isCard: false) ?? t.Name + "(!)"))}]");
        }
    }

    /// <summary>
    /// **变体槽位自检**（v0.2.447 新增）。
    ///
    /// `VariantsByAct` 是「**下标 = 幕下标（0 起）**」的数组。这个坑已经踩过两次：
    ///   · v0.2.337 加「蛇眼猎」时把这一项插到了数组最前面 ⇒ 二层没有变体，「炼药猎」被挤到下标 3；
    ///   · v0.2.447 加「电球机」时多写了一个 `null,` ⇒ 「事件机」被挤到下标 3。
    /// 两次的**症状完全一样**：控制台点名 `fight XXX_BOSS_ENCOUNTER` 打出来还是主设计，
    /// 因为 `VariantsForAct()` 永远读不到那个槽位 —— 判定函数**连一次都不会被调用**，日志里也没有任何痕迹。
    ///
    /// 所以这里在开局自检时按 **`BossKeys` 里记的幕** 核对每个非空槽位：
    ///   · 下标 ≥ 3（整局只有 3 幕）⇒ 这套设计永远不会生效；
    ///   · 下标与 `BossKeys.ActOf(Key)` 不一致 ⇒ 槽位摆错，同样不会在该幕生效。
    /// </summary>
    private static void ValidateVariants(BossDefinition def)
    {
        for (int i = 0; i < def.VariantsByAct.Length; i++)
        {
            var variant = def.VariantsByAct[i];
            if (variant == null) continue;
            if (i > 2)
            {
                Logger.Log($"[CharBoss]   ★★ VariantsByAct[{i}] = {variant.Key} 落在**第 {i + 1} 幕**" +
                           "（整局只有 3 幕）⇒ 这套设计永远不会生效！下标必须是 0 起。");
                continue;
            }
            int keyAct = BossKeys.ActOf(variant.Key);
            if (keyAct >= 0 && keyAct != i)
            {
                Logger.Log($"[CharBoss]   ★★ VariantsByAct[{i}] = {variant.Key} 在 BossKeys 里属于**第 {keyAct + 1} 幕**" +
                           $"⇒ 槽位摆错了，它不会在第 {i + 1} 幕生效（也不会在第 {keyAct + 1} 幕生效）。");
            }
        }

        foreach (var pair in def.ExtraVariants)
        {
            int act = pair.Key;
            if (act < 0 || act > 2)
            {
                Logger.Log($"[CharBoss]   ★★ ExtraVariants 挂了「第 {act + 1} 幕」的键（整局只有 3 幕）⇒ 永远不会生效。");
            }
            foreach (var variant in pair.Value)
            {
                if (variant == null) continue;
                int keyAct = BossKeys.ActOf(variant.Key);
                if (keyAct >= 0 && keyAct != act)
                {
                    Logger.Log($"[CharBoss]   ★★ ExtraVariants[{act}] = {variant.Key} 在 BossKeys 里属于**第 {keyAct + 1} 幕**" +
                               "⇒ 摆错幕了。");
                }
            }
        }
    }

    private static void ValidateSkips(BossDefinition def)
    {
        foreach (var type in def.SkipCards)
        {
            if (ResolveEntry(type, isCard: true) == null)
            {
                Logger.Log($"[CharBoss]   ★SkipCards 里的 {type.Name} 找不到（拼错了？）");
            }
        }

        if (def.Priority.Count == 0) return;
        var priorityMissing = def.Priority.Where(t => ResolveEntry(t, isCard: true) == null).Select(t => t.Name).ToList();
        if (priorityMissing.Count > 0)
        {
            Logger.Log($"[CharBoss]   ★Priority 里找不到: {string.Join(",", priorityMissing)}");
        }
    }

    /// <summary>拿到模型 id（Entry，用于对照本地化 key）；找不到返回 null。</summary>
    private static string? ResolveEntry(Type type, bool isCard)
    {
        try
        {
            var id = ModelDb.GetId(type);
            var model = isCard
                ? (AbstractModel?)ModelDb.GetById<CardModel>(id)
                : ModelDb.GetById<RelicModel>(id);
            return model == null ? null : id.Entry;
        }
        catch
        {
            return null;
        }
    }

    private static string Join(int[] values) => string.Join("/", values);
}
