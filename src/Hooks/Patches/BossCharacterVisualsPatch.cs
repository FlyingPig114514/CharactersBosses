using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace CharacterBosses.Patches;

/// <summary>
/// 角色 Boss 的立绘**直接问角色模型要一个实例** —— 这是皮肤 mod 的第二层兼容。
///
/// 背景（用户 2026-10-24）：装了「空洞骑士亡灵契约师皮肤（小骑士与前辈）」后，
/// **奥斯提**换了、但我们的**亡灵契约师 Boss** 还是原版模型。
/// 说明那个皮肤不是改 `CharacterModel.VisualsPath`（那种已被
/// <c>CharBossMonster.VisualsPath</c> 的委托覆盖），而是在
/// <c>CharacterModel.CreateVisuals()</c> 里返回自己的场景（或用 Harmony 补这个方法）。
///
/// 原版链路是：`NCreature` → `MonsterModel.CreateVisuals()` → `PreloadManager.GetScene(VisualsPath)`。
/// 这里把「我们的 Boss」这一支改成 `角色模型.CreateVisuals()` —— 皮肤 mod 改哪个入口都能继承到。
/// 没装皮肤时读到的就是同一个 `ironclad.tscn` / `necrobinder.tscn`，行为不变。
/// </summary>
[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.CreateVisuals))]
public static class BossCharacterVisualsPatch
{
    [HarmonyPrefix]
    public static bool Prefix(MonsterModel __instance, ref NCreatureVisuals __result)
    {
        try
        {
            if (__instance is not Monsters.CharBossMonster boss) return true;

            var def = boss.Def;
            if (def?.CharacterType == null) return true;

            var character = ModelDb.GetById<CharacterModel>(ModelDb.GetId(def.CharacterType));
            if (character == null) return true;

            var visuals = character.CreateVisuals();
            if (visuals == null) return true;

            __result = visuals;
            Logger.Log($"[CharBoss] {def.Key} 立绘实例来自角色模型 CreateVisuals()（皮肤 mod 可通过这里替换）");
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 走角色 CreateVisuals 失败，退回原版立绘路径: " + e.Message);
            return true;
        }
    }
}
