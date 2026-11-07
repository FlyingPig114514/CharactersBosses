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
/// 也就是说「两只 Boss」这件事**原版顶栏本来就支持**（还有 <c>DOUBLE_BOSS</c> 悬浮说明，
/// 会填 <c>BossName1/BossName2</c> = 两个遭遇的标题），我们的 <c>SecondBossEncounter</c> 读取兜底
/// 已经把第二只喂进去了 ⇒ 文字本来就列出全部 Boss。
///
/// 缺的只有贴图：我们的遭遇 id 在 <c>ui/run_history/</c> 下没有对应 png。
/// **用户要求**：就用「玩家界面左上角那个角色头像」——原版那条是
/// <c>CharacterModel.IconTexture</c>（<c>ui/top_panel/character_icon_&lt;角色&gt;.png</c>，另有一张 _outline），
/// 和顶栏玩家头像同源，所以这里直接取它。
///
/// 顺序：**我们的遭遇 → 角色头像**；别人的遭遇 → 原版图标（一个像素都不动）；
/// 实在取不到角色（理论上不会）→ 退回原版 Boss 的占位图标。
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

            // 第二只（多人「这一幕两只 Boss」时原版会额外画一颗）
            var secondIcon = traverse.Field("_secondBossIcon").GetValue<TextureRect>();
            var secondOutline = traverse.Field("_secondBossIconOutline").GetValue<TextureRect>();
            var secondEnc = act.SecondBossEncounter;
            if (secondEnc != null && !onlySecond) ApplyIcon(secondIcon, secondOutline, secondEnc);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 顶栏 Boss 图标兜底失败: " + e.Message);
        }
    }

    /// <summary>给一颗顶栏 Boss 图标选贴图：我们的遭遇用角色头像，其余保持原版。</summary>
    private static void ApplyIcon(TextureRect? icon, TextureRect? outline, EncounterModel encounter)
    {
        if (icon == null || !GodotObject.IsInstanceValid(icon)) return;

        // ① 我们的遭遇 → 角色头像（用户指定的那个「左上角头像」贴图）
        var character = CharacterFor(encounter);
        if (character != null)
        {
            try
            {
                // 贴图取不到就别硬塞（否则图标会变空白）—— 落到下面的占位图
                var portrait = character.IconTexture;
                if (portrait == null)
                {
                    Logger.Log($"[CharBoss] 顶栏 Boss 图标：{character.Id.Entry} 的 IconTexture 取不到 → 用占位图");
                }
                else
                {
                    icon.Texture = portrait;
                    // 角色头像的尺寸/宽高比和原版 ui/run_history 的 Boss 图**不一样**，
                    // 原版那颗 TextureRect 是按 Boss 图设的 → 这里改成「保持宽高比居中」，
                    // 免得头像被拉扁（占位图/原版图标那条路不碰，一个像素都不动）。
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
                    if (_logged++ < 8)
                    {
                        Logger.Log($"[CharBoss] 顶栏 Boss 图标：{encounter.Id.Entry} → 用角色头像 " +
                                   $"{character.Id.Entry}（ui/top_panel/character_icon_*.png）");
                    }
                    return;
                }
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 顶栏 Boss 图标：角色头像取不到，退回占位图: " + e.Message);
            }
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
