using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Core;

namespace CharacterBosses.Patches;

/// <summary>
/// **战斗开场就把「角色 Boss 那一套美术」预载好** —— 修「每次第一回合打血墙都会卡一下」。
///
/// ## 症状与实证（用户 2026-10-28）
/// 二层「放血战」第 1 回合打「血墙」时会顿一下。`godot.log` 开战那一刻：
/// <code>
/// Monster IRONCLAD_BOSS performing move CHARBOSS_PLAY
/// [WARN] Asset not cached: res://scenes/vfx/card_trail_ironclad.tscn
/// [WARN] Asset not cached: res://images/powers/rupture_power.png
/// [WARN] Asset not cached: res://images/powers/self_forming_clay_power.png
/// [WARN] Asset not cached: res://scenes/vfx/vfx_blood_wall.tscn      ← 血墙的 VFX 场景
/// [WARN] Asset not cached: res://images/powers/strength_power.png
/// [INFO] Unloading 8 missed cache assets
/// </code>
/// 也就是说：**这张牌的 VFX 场景直到第一次打出来才同步从磁盘读**，所以就卡那一下。
///
/// 为什么原版玩家不会卡：原版在开局用 `PreloadManager.LoadRunAssets(角色)` 把**玩家角色**的
/// 全部卡牌美术（`CardModel.RunAssetPaths`：立绘 / 卡框 / 能量图标 / `scenes/vfx/*`）一次性载入。
/// 而我们的 Boss 是**怪物**，结算战斗预载时走的是 `EncounterModel.GetAssetPaths()` ——
/// 里面只有「战斗房间 + 怪物立绘」，**没有任何卡牌美术**（怪物本来不打牌）。
///
/// ## 做法
/// 在 `PreloadManager.GetCombatAssetPaths(encounter, runState)`（玩家进战斗房间前那段
/// **异步加载**，有加载界面）的返回值后面，追加「这只角色 Boss 的美术清单」：
///   · 角色本身：`CharacterModel.AssetPaths`（含立绘/头像/卡框材质）+ `TrailPath`（卡牌拖尾）；
///   · 卡池里每一张牌：`CardModel.RunAssetPaths`（卡面、能量图标、`scenes/vfx/vfx_xxx.tscn`）。
/// 别的遭遇（原版 / 别的模组）一律原样返回，**一个字节都不加**。
///
/// ⚠ 只加不删：`LoadAssets` 会把清单里的都缓存住，于是进战斗后不再有「第一次打出才读盘」的卡顿。
/// 每只角色的清单算一次就缓存（同一个 Boss 反复遇到不会重复算）。
/// </summary>
[HarmonyPatch(typeof(PreloadManager), "GetCombatAssetPaths")]
internal static class BossCombatPreloadPatch
{
    [HarmonyPostfix]
    public static void Postfix(EncounterModel? encounter, ref IEnumerable<string> __result)
    {
        try
        {
            var extra = BossPreloadAssets.For(encounter);
            Logger.Log($"[CharBoss] 战斗预载钩子（GetCombatAssetPaths）：{encounter?.Id.Entry} → 追加 {extra.Count} 个");
            if (extra.Count == 0) return;
            __result = __result == null ? extra : __result.Concat(extra);
            if (Logged.Add(encounter?.Id.Entry ?? "?"))
            {
                Logger.Log($"[CharBoss] 战斗预载：为 {encounter?.Id.Entry} 额外预载 {extra.Count} 个" +
                           "角色 Boss 美术文件（卡牌 VFX / 立绘 / 拖尾），避免第一次打出时才读盘卡顿");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 战斗预载失败（不影响开打）: " + e.Message);
        }
    }

    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);
}

/// <summary>
/// **备用通道**：上面那个 `GetCombatAssetPaths` 是 `PreloadManager` 里的私有小方法，
/// 万一被 JIT 内联或补丁没挂上（实测 2026-10-28：日志里 `Preloading 'Combat Room' assets... count=8`
/// 一个都没多），就靠这里兜底 ——
/// 在 <see cref="PreloadManager.LoadRoomCombatAssets"/>（public）之前**同步**把这批美术读进缓存。
/// 这一步发生在「玩家进战斗房间」的加载阶段（有加载界面），所以就算慢也看不见；
/// 进了战斗就不再出现「第一次打出血墙才读盘」的卡顿。
/// </summary>
[HarmonyPatch(typeof(PreloadManager), "LoadRoomCombatAssets")]
internal static class BossCombatWarmPatch
{
    [HarmonyPrefix]
    public static void Prefix(EncounterModel? encounter)
    {
        try
        {
            var paths = BossPreloadAssets.For(encounter);
            if (Warmed.Add(encounter?.Id.Entry ?? "?"))
                Logger.Log($"[CharBoss] 战斗预载（同步兜底钩子触发）：{encounter?.Id.Entry} 清单 {paths.Count} 条");
            if (paths.Count == 0) return;
            int ok = 0;
            foreach (var path in paths)
            {
                try
                {
                    var cache = PreloadManager.Cache;
                    if (cache == null) break;
                    if (cache.ContainsKey(path)) { ok++; continue; }
                    var res = cache.GetAsset<Godot.Resource>(path);
                    if (res != null) ok++;
                }
                catch { /* 单个失败不影响别的 */ }
            }
            if (Warmed.Add(encounter?.Id.Entry ?? "?"))
            {
                Logger.Log($"[CharBoss] 战斗预载（同步兜底）：{encounter?.Id.Entry} 预热 {ok}/{paths.Count} 个美术文件");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 战斗预载兜底失败（不影响开打）: " + e.Message);
        }
    }

    private static readonly HashSet<string> Warmed = new(StringComparer.Ordinal);
}

/// <summary>「这个遭遇对应哪只角色 Boss，它需要预载哪些美术」。</summary>
internal static class BossPreloadAssets
{
    private static readonly Dictionary<string, List<string>> Cache = new(StringComparer.Ordinal);

    internal static List<string> For(EncounterModel? encounter)
    {
        if (encounter == null) return new List<string>();
        string encounterEntry;
        try { encounterEntry = encounter.Id.Entry; } catch { return new List<string>(); }

        if (Cache.TryGetValue(encounterEntry, out var cached)) return cached;

        var paths = new List<string>();
        Cache[encounterEntry] = paths;   // 先登记，避免重复算（算失败也留空表，下一场不再重试）

        try
        {
            var key = BossKeys.KeyOfEncounter(encounter);
            if (key == null) { Note(encounterEntry, "不是我们的档位遭遇（KeyOfEncounter=null）"); return paths; }

            // ⚠ `BossDefs.All` 的 key 是**基名**（`IRONCLAD_BOSS` / `SILENT_BOSS` …），
            //   档位 key（`IRONCLAD_BLOOD` / `SILENT_BREWER` …）只登记在 `BossKeys` 里
            //   —— 2026-10-28 第一版就是拿档位 key 直接去 BossDefs 查，结果永远查不到（预载一场空）。
            if (!BossKeys.TryGet(key, out var info))
            {
                Note(encounterEntry, $"KeyOfEncounter={key} 但 BossKeys 里没有这一档");
                return paths;
            }

            var def = DefFor(info.Character);
            if (def?.CharacterType == null)
            {
                Note(encounterEntry, $"{key} 的角色下标 {info.Character} 没有对应的 BossDefs 定义");
                return paths;
            }

            var character = ModelDb.GetById<CharacterModel>(ModelDb.GetId(def.CharacterType));
            if (character == null) { Note(encounterEntry, $"{key} 的角色模型取不到"); return paths; }

            AddAssetPaths(paths, character.AssetPaths);
            AddOne(paths, character.TrailPath);
            int afterCharacter = paths.Count;

            // 优先只预载**他真正会用的那套牌**（放血战 21 张）—— 整池 80 张会让战斗加载界面变长。
            var deck = DeckOf(def, ActOf(encounter));
            int fromDeck = 0, rawDeckPaths = 0, missingCards = 0;
            foreach (var cardType in deck)
            {
                if (cardType == null) continue;
                var card = ModelDb.GetById<CardModel>(ModelDb.GetId(cardType));
                if (card == null) { missingCards++; continue; }
                // RunAssetPaths 已经包含卡面/能量图标/scenes/vfx/*（ExtraRunAssetPaths 是 protected，不用）
                var cardPaths = card.RunAssetPaths?.ToList();
                if (cardPaths != null) rawDeckPaths += cardPaths.Count;
                AddAssetPaths(paths, cardPaths);
                fromDeck++;
            }

            // 牌表拿不到（理论上不会）⇒ 退回整个卡池
            if (fromDeck == 0 && character.CardPool != null)
            {
                foreach (var card in character.CardPool.AllCards)
                {
                    if (card == null) continue;
                    AddAssetPaths(paths, card.RunAssetPaths);
                }
            }

            Note(encounterEntry, $"key={key} 角色资产={afterCharacter} 牌表={deck.Count}张" +
                                 $"（解析到 {fromDeck}、取不到 {missingCards}）牌面候选路径={rawDeckPaths}" +
                                 $" → 过滤后合计 {paths.Count} 条");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 战斗预载清单生成失败: " + e.Message);
        }
        return paths;
    }

    private static readonly HashSet<string> Noted = new(StringComparer.Ordinal);

    /// <summary>每个遭遇只记一次「为什么没算出预载清单」，便于排查。</summary>
    private static void Note(string encounterEntry, string why)
    {
        if (!Noted.Add(encounterEntry)) return;
        Logger.Log($"[CharBoss] 战斗预载：{encounterEntry} 没有额外美术（{why}）");
    }

    /// <summary>角色下标（0 铁甲 / 1 静默 / 2 故障 / 3 亡灵 / 4 储君）→ 那一套主定义的基名。</summary>
    private static BossDefinition? DefFor(int character)
    {
        try
        {
            string? baseKey = character switch
            {
                0 => "IRONCLAD_BOSS",
                1 => "SILENT_BOSS",
                2 => "DEFECT_BOSS",
                3 => "NECROBINDER_BOSS",
                4 => "REGENT_BOSS",
                _ => null,
            };
            if (baseKey != null && BossDefs.All.TryGetValue(baseKey, out var def)) return def;
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>这个遭遇钉死的幕下标（我们自己的档位遭遇有 `TierAct`；读不到返回 −1）。</summary>
    private static int ActOf(EncounterModel encounter)
    {
        try
        {
            var prop = encounter.GetType().GetProperty("TierAct");
            if (prop?.GetValue(encounter) is int act) return act;
        }
        catch { /* 忽略 */ }
        return -1;
    }

    /// <summary>这一档他会用哪套牌（变体优先 → 按幕 → 主定义）。</summary>
    private static List<Type> DeckOf(BossDefinition def, int act)
    {
        try
        {
            // ★ v0.2.419：一幕可以有多套备选设计（三层故障 = 事件机 / 状态机）⇒ 取第一个生效的
            foreach (var variant in def.VariantsForAct(act))
            {
                BossVariant? active = null;
                try { if (variant.Enabled()) active = variant; } catch { /* 试下一套 */ }
                if (active?.Deck != null && active.Deck.Count > 0) return active.Deck;
            }
            if (act >= 0 && act < def.DeckByAct.Length && def.DeckByAct[act] != null)
                return def.DeckByAct[act]!;
        }
        catch { /* 忽略 */ }
        return def.Deck;
    }

    private static void AddAssetPaths(List<string> into, IEnumerable<string>? paths)
    {
        if (paths == null) return;
        foreach (var p in paths) AddOne(into, p);
    }

    private static void AddOne(List<string> into, string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            if (!path!.StartsWith("res://", StringComparison.Ordinal)) return;
            if (!Godot.ResourceLoader.Exists(path)) return;   // 不存在的别塞进去（会让预载报错）
            if (!into.Contains(path)) into.Add(path);
        }
        catch { /* 忽略 */ }
    }
}
