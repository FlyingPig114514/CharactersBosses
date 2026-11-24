using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Core;
using CharacterBosses.Encounters;

namespace CharacterBosses.Patches;

/// <summary>
/// 记录「这个玩家是被哪一只 Boss 打死的」——原版 `Hook.AfterDamageGiven` 是每次伤害结算后的钩子，
/// 参数里同时有 dealer / target / cardSource，正好够判断归属。见 <see cref="BossKillerTracker"/>。
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageGiven))]
public static class BossKillerTrackPatch
{
    [HarmonyPostfix]
    public static void Postfix(Creature dealer, DamageResult results, Creature target, CardModel cardSource)
    {
        try { BossKillerTracker.Note(dealer, target, cardSource); }
        catch { /* 绝不影响战斗 */ }
    }
}

/// <summary>
/// 死亡提示按**凶手**显示（用户 2026-10-23）。
///
/// 原版 `EncounterModel.GetLossMessageFor(CharacterModel)` 的实现就是
/// <code>
/// var s = new LocString("encounters", Id.Entry + ".loss");
/// character.AddDetailsTo(s);
/// s.Add("encounter", Title);
/// return s;
/// </code>
/// 也就是说文案挂在**房间的遭遇**上。多人同场两只 Boss 时房间只有一个遭遇
/// ⇒ 死在第二只手里也会报主 Boss 的名字。
/// 这里在它返回之后换一份：用**打死你的那只 Boss 自己的遭遇**重造同样的 LocString。
/// 单人（凶手就是这一场的 Boss）时 `enc.Id == __instance.Id`，一个字节都不改 —— 零回归。
/// </summary>
[HarmonyPatch(typeof(EncounterModel), nameof(EncounterModel.GetLossMessageFor))]
public static class BossLossMessageKillerPatch
{
    [HarmonyPostfix]
    public static void Postfix(EncounterModel __instance, CharacterModel character, ref LocString __result)
    {
        try
        {
            var boss = BossKillerTracker.KillerOf(character);
            if (boss == null) return;
            // ★ v0.2.788（用户 2026-11-19）：只有「这一格提示对应的就是记录凶手那一场战斗」才按凶手改。
            //   结算/历史界面会给本局**每一场**战斗各问一次，以前会把它们全部改写成同一个凶手
            //   ⇒ 「不同的几局都显示灵魂被亡灵契约师收走了」。
            if (!BossKillerTracker.IsDeathCombat(__instance?.Id)) return;

            var encounter = CharBossEncounters.EncounterForBoss(boss);
            if (encounter == null) return;
            if (Equals(encounter.Id, __instance?.Id)) return;      // 同一只：保持原版结果

            var message = new LocString("encounters", encounter.Id.Entry + ".loss");
            character?.AddDetailsTo(message);
            try { message.Add("encounter", encounter.Title); } catch { /* 拿不到标题就不加，文案本身已写死名字 */ }

            __result = message;
            Logger.Log($"[CharBoss] 死亡提示按凶手显示：{encounter.Id.Entry}" +
                       $"（本场遭遇是 {__instance?.Id.Entry}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 死亡提示按凶手显示失败（忽略）: " + e.Message);
        }
        finally
        {
            // ★ 用户 2026-11-14：「死于塔罗愚时，如果玩家角色的名字属于那张塔罗牌对应的角色，
            //   就把死亡提示改成 `[character]成为了愚者牌组中新的[对应卡牌]`」。
            //   这里统一做一次收尾（不管上面走的是哪条分支），只对**愚者的遭遇**生效。
            try { ApplyTarotMessage(__instance, character, ref __result); }
            catch (Exception e) { Logger.Log("[CharBoss] 塔罗死亡提示失败（忽略）: " + e.Message); }
        }
    }

    /// <summary>
    /// 把「死于塔罗愚」的提示换成「成为了愚者牌组中新的 XX」。
    /// 文案 key 是同表的 `.loss_tarot`（16 语言），里面用 `{Card}` 引用 `cards` 表的塔罗牌名
    ///（牌名本来就带 16 语言，不用重复翻译）。
    /// </summary>
    private static void ApplyTarotMessage(EncounterModel? encounter, CharacterModel character, ref LocString result)
    {
        if (encounter == null) return;
        string entry;
        try { entry = encounter.Id.Entry ?? string.Empty; } catch { return; }
        if (!entry.StartsWith("FOOL_", StringComparison.Ordinal)) return;   // 只认愚者的遭遇
        // ★ v0.2.788：同上 —— 只有「死亡那一场」才换文案，历史里别的格子保持原版
        if (!BossKillerTracker.IsDeathCombat(encounter.Id)) return;

#if STS2_111
        string? card = Core.FoolTarotDeaths.CardEntryFor(character);
#else
        string? card = null;
#endif
        if (card == null) return;

        string key = entry + ".loss_tarot";
        var message = new LocString("encounters", key);
        character?.AddDetailsTo(message);
        try { message.Add("Card", new LocString("cards", card + ".title")); }
        catch { Logger.Log("[CharBoss] 塔罗死亡提示：取不到牌名 " + card); return; }

        result = message;
        Logger.Log($"[CharBoss] 塔罗死亡提示：{entry} + {card}（角色 {character?.Id.Entry}）");
    }
}

/// <summary>
/// **死亡提示彩蛋**（用户 2026-11-24）：
/// > 「为部分 boss 添加彩蛋死亡提示（低概率遇到，大概率遇到的还是原本的提示）
/// >   巨石战：铁甲战士发现{character} 对石头过敏。
/// >   炼药猎：{character}没能击穿静默猎手的龟壳。」
///
/// 机制：**纯数据驱动** —— 只要某个遭遇在 `encounters` 表里有 `<遭遇>_BOSS_ENCOUNTER.loss_egg`
/// 这个键（目前只有巨石战 `IRONCLAD_BOULDER` 与炼药猎 `SILENT_BREWER`），就有
/// <see cref="Chance"/>（10%）的概率把死亡提示换成彩蛋那句；其余情况原样返回（= 原本那句）。
///
/// 两条护栏：
///   · 只改**真正死掉的那一场**（<see cref="BossKillerTracker.IsDeathCombat"/>）——
///     结算 / 历史界面会给本局每一场战斗各问一次，不全挡住的话历史里每一格都会变成彩蛋；
///   · **一次死亡只掷一次骰子**：同一个战斗（<see cref="BossKillerTracker.CombatToken"/>）
///     里的结果会被缓存，结算界面与历史界面看到的是同一句（否则两边会显示不同的话）。
/// </summary>
[HarmonyPatch(typeof(EncounterModel), nameof(EncounterModel.GetLossMessageFor))]
public static class BossLossEggPatch
{
    /// <summary>彩蛋出现概率（10%）。要调就改这一个数。</summary>
    private const double Chance = 0.10;

    /// <summary>上次掷骰子时那一场战斗；换战斗就清缓存。</summary>
    private static object? _token;

    /// <summary>「这一场这一位玩家」的掷骰结果（键 = 遭遇 + 角色）。</summary>
    private static readonly Dictionary<string, bool> Rolls = new(StringComparer.Ordinal);

    private static int _logs;

    [HarmonyPriority(Priority.Last)]        // 排在「按凶手换文案」之后：彩蛋优先级最高
    [HarmonyPostfix]
    public static void Postfix(EncounterModel __instance, CharacterModel character, ref LocString __result)
    {
        try
        {
            string? entry = __instance?.Id.Entry;
            if (string.IsNullOrEmpty(entry)) return;

            string key = entry + ".loss_egg";
            if (!LocString.Exists("encounters", key)) return;        // 没写彩蛋的 Boss 直接放过
            if (!BossKillerTracker.IsDeathCombat(__instance!.Id)) return;

            object? token = BossKillerTracker.CombatToken;
            if (!ReferenceEquals(token, _token)) { _token = token; Rolls.Clear(); }

            string rollKey = entry + "|" + (character?.Id.Entry ?? "?");
            if (!Rolls.TryGetValue(rollKey, out bool egg))
            {
                egg = RollOnce();
                Rolls[rollKey] = egg;
                if (_logs++ < 12)
                    Logger.Log($"[CharBoss] 死亡提示彩蛋掷骰：{entry}（角色 {character?.Id.Entry}）⇒ " +
                               (egg ? "**中了**" : "没中，用原本那句"));
            }
            if (!egg) return;

            var loc = new LocString("encounters", key);
            try { character?.AddDetailsTo(loc); } catch { /* 忽略 */ }
            try { loc.Add("encounter", __instance!.Title); } catch { /* 忽略 */ }
            __result = loc;

            if (_logs++ < 12) Logger.Log("[CharBoss] 死亡提示彩蛋：" + key);
        }
        catch (Exception e)
        {
            if (_logs++ < 4) Logger.Log("[CharBoss] 死亡提示彩蛋失败（忽略）: " + e.Message);
        }
    }

    private static bool RollOnce()
    {
        try { return Random.Shared.NextDouble() < Chance; }
        catch { return false; }
    }
}
