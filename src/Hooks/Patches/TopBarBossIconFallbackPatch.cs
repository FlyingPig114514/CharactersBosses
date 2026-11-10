using System;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using CharacterBosses.Core;

namespace CharacterBosses.Patches;

/// <summary>
/// **D 地图顶栏**：顶栏那颗「本幕 Boss」图标——我们的 Boss 直接用**角色头像**（用户指定）。
///
/// ## 原版怎么取图标（已反编译确认）
/// <code>
/// // NTopBarBossIcon.RefreshBossIcon()
/// var enc = ShouldOnlyShowSecondBossIcon ? Act.SecondBossEncounter : Act.BossEncounter;
/// _bossIcon.Texture = PreloadManager.Cache.GetTexture2D(
///     ImageHelper.GetRoomIconPath(MapPointType.Boss, RoomType.Boss, enc.Id));   // ui/run_history/&lt;id&gt;.png
/// if (Act.SecondBossEncounter != null &amp;&amp; !ShouldOnlyShowSecondBossIcon)   // 第二只的图标
/// { ... second_boss_icon.tscn ... 同一个 GetRoomIconPath ... }
/// </code>
/// 我们的遭遇 id 在 <c>ui/run_history/</c> 下没有对应 png ⇒ 用 <c>CharacterModel.IconTexture</c>
/// （<c>ui/top_panel/character_icon_&lt;角色&gt;.png</c>，和顶栏玩家头像同源）。
///
/// ## 第二颗头像会和第一颗一样的问题（v0.2.558）
/// 用户 2026-11-10：「剪影说明 boss 是一个亡灵契约师和一个储君，但最上方一栏却显示两个亡灵契约师的头像」。
/// 原因：两颗图标各自按**遭遇**解析角色，而第二槽的遭遇很容易与第一槽解析回同一个角色
/// （第二槽复用第一槽的遭遇、或 <c>SecondBossEncounter</c> 的读取兜底没换成伙伴那一套）。
/// 现在补一道：两颗解析出**同一只角色**时，第二颗改用**我们阵容里的第二只设计**：
///   · 多人同场（本场阵容 ≥2 条）= 本场阵容的 partner；
///   · 否则（进阶 10 第三幕连打两只）= 该幕第 2 场的阵容主角色。
/// </summary>
[HarmonyPatch(typeof(NTopBarBossIcon), nameof(NTopBarBossIcon.RefreshBossIcon))]
public static class TopBarBossIconFallbackPatch
{
    private static int _logged;

    [HarmonyPostfix]
    public static void Postfix(NTopBarBossIcon __instance)
    {
        try
        {
            var traverse = HarmonyLib.Traverse.Create(__instance);
            var runState = traverse.Field("_runState").GetValue<IRunState>();
            if (runState == null) return;

            bool onlySecond = false;
            try { onlySecond = traverse.Field("ShouldOnlyShowSecondBossIcon").GetValue<bool>(); }
            catch { /* 拿不到就按第一只处理 */ }

            var act = runState.Act;
            var enc = onlySecond ? act.SecondBossEncounter : act.BossEncounter;
            if (enc == null) return;

            var icon = traverse.Field("_bossIcon").GetValue<TextureRect>();
            var outline = traverse.Field("_bossIconOutline").GetValue<TextureRect>();
            ApplyIcon(icon, outline, enc);

            // 第二只（多人「这一幕两只 Boss」/ 进阶 10 连打两只时原版会额外画一颗）
            var secondIcon = traverse.Field("_secondBossIcon").GetValue<TextureRect>();
            var secondOutline = traverse.Field("_secondBossIconOutline").GetValue<TextureRect>();
            var secondEnc = act.SecondBossEncounter;
            if (secondEnc != null && !onlySecond)
            {
                ApplyIcon(secondIcon, secondOutline, secondEnc);

                var firstChar = CharacterFor(enc);
                var secondChar = CharacterFor(secondEnc);
                if (firstChar != null && secondChar != null
                    && string.Equals(secondChar.Id.Entry, firstChar.Id.Entry, StringComparison.Ordinal))
                {
                    string? slotKey = SecondSlotKey(act);
                    var alt = CharacterForKey(slotKey);
                    if (alt != null && !string.Equals(alt.Id.Entry, firstChar.Id.Entry, StringComparison.Ordinal))
                    {
                        ApplyCharacter(secondIcon, secondOutline, alt);
                        if (_logged++ < 12)
                        {
                            Logger.Log($"[CharBoss] 顶栏 Boss 图标：第二槽与第一槽都被解析成 {firstChar.Id.Entry} ⇒ " +
                                       $"按阵容第二只（{slotKey}）改成 {alt.Id.Entry}");
                        }
                    }
                    else if (_logged++ < 12)
                    {
                        Logger.Log($"[CharBoss] 顶栏 Boss 图标：第二槽与第一槽都是 {firstChar.Id.Entry}，" +
                                   $"而阵容给不出不同的第二只（key={slotKey ?? "null"}）");
                    }
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 顶栏 Boss 图标兜底失败: " + e.Message);
        }
    }

    /// <summary>
    /// **第二槽**该用哪一套设计（拿不到返回 null）：
    ///   · 多人同场（本场阵容 ≥2 条）⇒ 本场阵容的第 2 条（伙伴）；
    ///   · 否则 ⇒ 该幕第 2 场的阵容主角色（进阶 10 第三幕连打两只）。
    /// </summary>
    private static string? SecondSlotKey(ActModel? act)
    {
        try
        {
            var now = BossLineups.LineupNow();
            if (now != null && now.Length >= 2) return now[1];
        }
        catch { /* 继续看第二场 */ }

        try
        {
            var second = BossLineups.For(act?.Index ?? 0, 1);
            if (second != null && second.Length > 0) return second[0];
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>设计 Key（<c>NECROBINDER_FLESH</c> 之类）→ 角色模型（拿不到返回 null）。</summary>
    private static CharacterModel? CharacterForKey(string? designKey)
    {
        try
        {
            if (string.IsNullOrEmpty(designKey)) return null;
            var monsterType = BossKeys.MonsterTypeOf(designKey);
            if (monsterType == null) return null;

            string entry = ModelDb.GetId(monsterType).Entry;
            if (!BossDefs.All.TryGetValue(entry, out var def) || def?.CharacterType == null) return null;
            return ModelDb.GetById<CharacterModel>(ModelDb.GetId(def.CharacterType));
        }
        catch { return null; }
    }

    /// <summary>给一颗顶栏 Boss 图标选贴图：我们的遭遇用角色头像，其余保持原版。</summary>
    private static void ApplyIcon(TextureRect? icon, TextureRect? outline, EncounterModel encounter)
    {
        if (icon == null || !GodotObject.IsInstanceValid(icon)) return;

        // ① 我们的遭遇 → 角色头像（用户指定的那个「左上角头像」贴图）
        var character = CharacterFor(encounter);
        if (character != null && ApplyCharacter(icon, outline, character))
        {
            if (_logged++ < 8)
            {
                Logger.Log($"[CharBoss] 顶栏 Boss 图标：{encounter.Id.Entry} → 用角色头像 " +
                           $"{character.Id.Entry}（ui/top_panel/character_icon_*.png）");
            }
            return;
        }

        // ② 原版图标在 → 什么都不做
        string? path = null;
        try { path = ImageHelper.GetRoomIconPath(MapPointType.Boss, RoomType.Boss, encounter.Id); }
        catch { /* 忽略 */ }
        if (path != null && ResourceLoader.Exists(path)) return;

        // ③ 最后兜底：原版 Boss 的占位图标（与地图节点 BossNodePath 复用 TheKinBoss 美术是同一个思路）
        var vanilla = ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.TheKinBoss>();
        if (vanilla == null) return;

        var fallbackIcon = ImageHelper.GetRoomIconPath(MapPointType.Boss, RoomType.Boss, vanilla.Id);
        var fallbackOutline = ImageHelper.GetRoomIconOutlinePath(MapPointType.Boss, RoomType.Boss, vanilla.Id);
        if (fallbackIcon == null) return;

        try { icon.Texture = PreloadManager.Cache.GetTexture2D(fallbackIcon); } catch { /* 忽略 */ }
        if (outline != null && GodotObject.IsInstanceValid(outline) && fallbackOutline != null)
        {
            try { outline.Texture = PreloadManager.Cache.GetTexture2D(fallbackOutline); } catch { /* 忽略 */ }
        }
    }

    /// <summary>把「角色头像 + 描边」写到这两颗 TextureRect 上。返回是否真的写成功。</summary>
    private static bool ApplyCharacter(TextureRect? icon, TextureRect? outline, CharacterModel character)
    {
        if (icon == null || !GodotObject.IsInstanceValid(icon)) return false;
        try
        {
            // 贴图取不到就别硬塞（否则图标会变空白）—— 调用方会落到占位图
            var portrait = character.IconTexture;
            if (portrait == null)
            {
                Logger.Log($"[CharBoss] 顶栏 Boss 图标：{character.Id.Entry} 的 IconTexture 取不到 → 用占位图");
                return false;
            }

            icon.Texture = portrait;
            // 角色头像的尺寸/宽高比和原版 ui/run_history 的 Boss 图**不一样**，
            // 原版那颗 TextureRect 是按 Boss 图设的 → 这里改成「保持宽高比居中」，免得头像被拉扁。
            icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;

            if (outline != null && GodotObject.IsInstanceValid(outline))
            {
                var outlineTex = character.IconOutlineTexture;
                if (outlineTex != null)
                {
                    outline.Texture = outlineTex;
                    outline.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
                }
            }
            return true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 顶栏 Boss 图标：角色头像取不到，退回占位图: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// 这个遭遇是哪只「角色 Boss」（我们的遭遇才会命中）——通过它的怪物 id 反查 <see cref="BossDefs"/>。
    /// 例如 <c>IRONCLAD_BOSS</c> → <c>Models.Characters.Ironclad</c>。
    /// </summary>
    internal static CharacterModel? CharacterFor(EncounterModel encounter)
    {
        try
        {
            var monster = encounter?.AllPossibleMonsters?.FirstOrDefault();
            if (monster == null) return null;

            string key;
            try { key = monster.Id.Entry; } catch { return null; }

            if (!BossDefs.All.TryGetValue(key, out var def) || def?.CharacterType == null) return null;
            return ModelDb.GetById<CharacterModel>(ModelDb.GetId(def.CharacterType));
        }
        catch { return null; }
    }
}
