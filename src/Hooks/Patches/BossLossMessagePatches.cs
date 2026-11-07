using System;
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
    }
}
