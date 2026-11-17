#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Patches;

/// <summary>
/// **Ω观的死亡提示按死因特判**（用户 2026-11-17）：
///
/// > 欧米伽观默认用①「{character} 未能走出观者的阿尔法、贝塔与欧米伽。」，
/// > 然后特判一下，**如果被点穴打死改为用③**「{character} 的每一寸都被观者的点穴点得爆开。」
///
/// 机制（反编译 0.111 的 `EncounterModel`）：
/// <code>
/// public LocString GetLossMessageFor(CharacterModel character) {
///     LocString locString = L10NLookup(base.Id.Entry + ".loss");
///     character.AddDetailsTo(locString);
///     locString.Add("encounter", Title);
///     return locString;
/// }
/// </code>
/// ⇒ 死因判定做在 `WatcherOmegaMechanics` 里（点穴那一下把玩家打死时记下角色模型），
/// 这里只把返回的 `LocString` 换成 `WATCHER_OMEGA_BOSS_ENCOUNTER.loss_pressure`，
/// 并照原样补上「角色详情」和「遭遇标题」两个变量，其它遭遇一个字节都不碰。
/// </summary>
[HarmonyPatch(typeof(EncounterModel), nameof(EncounterModel.GetLossMessageFor))]
public static class WatcherOmegaLossPatch
{
    private static int _logs;

    [HarmonyPostfix]
    public static void Postfix(EncounterModel __instance, CharacterModel character, ref LocString __result)
    {
        try
        {
            var killedBy = Core.WatcherOmegaMechanics.PressurePointsKillCharacter;
            if (killedBy == null) return;                                   // 这一场没人被点穴打死
            if (character == null || !ReferenceEquals(character, killedBy)) return;   // 多人：只改那一位

            string? entry = __instance?.Id.Entry;
            if (!string.Equals(entry, "WATCHER_OMEGA_BOSS_ENCOUNTER", StringComparison.Ordinal)) return;

            string key = entry + ".loss_pressure";
            if (!LocString.Exists("encounters", key))
            {
                if (_logs++ < 4)
                    Logger.Log("[CharBoss] Ω观：缺少点穴专属死亡提示（" + key + "）⇒ 沿用默认那句");
                return;
            }

            var loc = new LocString("encounters", key);
            try { character.AddDetailsTo(loc); } catch { /* 忽略 */ }
            try { loc.Add("encounter", __instance!.Title); } catch { /* 忽略 */ }
            __result = loc;

            if (_logs++ < 4)
                Logger.Log("[CharBoss] Ω观：玩家被[点穴]打死 ⇒ 死亡提示换成点穴专属文案");
        }
        catch (Exception e)
        {
            if (_logs++ < 4) Logger.Log("[CharBoss] Ω观：死亡提示替换失败: " + e.Message);
        }
    }
}
#endif
