using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Powers;

namespace CharacterBosses.Patches;

/// <summary>
/// Boss **特殊效果**的能力图标：一律借用原版「好奇」（<see cref="CuriousPower"/>）的灯泡图标。
///
/// <c>PowerModel.PackedIconPath</c> 不是 virtual，所以只能打补丁：原版的图标是按 model id 拼路径，
/// 而我们的 mod 没有自己的贴图资源 —— 直接换成原版好奇的图标，既不缺图也不用新造美术。
///
/// ★ v0.2.339（用户 2026-11-03：「特殊效果『不死』的图标没有改成『好奇』的。
///   **以后所有特殊效果的图标都要改成「好奇」的**」）：
///   判据从「**逐个类型点名**」改成「**是不是本 mod 的 Power**」——
///   我们程序集里的每一个 <see cref="PowerModel"/> 都是某个 Boss 的特殊效果（或它派生的状态图标），
///   一律用好奇的灯泡。以后再加新 Boss / 新特殊效果**自动生效**，
///   不会再出现「新效果忘了加名单」（这次「不死」`BossOstyUndyingPower` 就是漏了）。
///
/// 两个例外仍然用 mod 自带的专门图标（用户点名要的）：
///   · 「麻痹」（<c>ParalysisPower</c>）→ `images/powers/paralysis.png`（黄色闪电，要能与「下回合能量」区分）；
///   · 「跟踪锁定」（<c>LockOnPower</c>，多人）→ `images/powers/lock_on.png`（塔1 的 lock-on 图标）。
/// </summary>
[HarmonyPatch(typeof(PowerModel), "get_PackedIconPath")]
public static class BossSpecialPowerIconPatch
{
    [HarmonyPrefix]
    public static bool Prefix(PowerModel __instance, ref string __result)
    {
        // ★ 2026-11-13（用户）：「这 3 个 buff 的图标改成对应角色的头像。这三个 buff 不属于 boss 的
        //   特殊效果（和那些不是一类东西），所以不用好奇图标。其它特殊效果还是用好奇图标」
        //   ⇒ 恐惧模式的三个「支配」power 用**角色头像**（`CharacterModel.IconTexturePath`）。
        var dominionIcon = DominionIconOf(__instance);
        if (!string.IsNullOrEmpty(dominionIcon))
        {
            __result = dominionIcon!;
            return false;
        }

        // 「麻痹」（二层「王储」施加给玩家的减益）：mod 自带的黄色闪电
        // （`tools/art/import_proj/.../images/powers/paralysis.png`，随 PCK 打包，见 pck_extras.txt）。
        if (__instance is Powers.ParalysisPower)
        {
            __result = "res://CharacterBosses/images/powers/paralysis.png";
            return false;
        }

        // 「跟踪锁定」（多人）用塔1 的 lock-on 图标：从 SlayTheSpire/desktop-1.0.jar 的
        // powers/powers.atlas 里裁出 `128/lockon` 区域（xy 979,933 size 89,89），经 Godot 导入后随 PCK 发布。
        if (__instance is Powers.LockOnPower)
        {
            __result = "res://CharacterBosses/images/powers/lock_on.png";
            return false;
        }

        // 其余：**只有本 mod 的 Power** 才接管（原版/别的 mod 的 Power 完全不受影响）
        try
        {
            if (__instance == null) return true;
            if (!IsOurPowerType(__instance.GetType())) return true;

            var curiosity = ModelDb.Power<CuriousPower>();
            if (curiosity != null)
            {
                __result = curiosity.PackedIconPath;
                return false;
            }
        }
        catch
        {
            // 拿不到就交给原版
        }
        return true;
    }

    /// <summary>
    /// 恐惧模式的「支配」power 图标 = **对应角色的头像**（`CharacterModel.IconTexturePath`）。
    ///
    /// 用户 2026-11-13：「这 3 个 buff 的图标改成对应角色的头像。这三个 buff 不属于 boss 的特殊效果
    /// （和那些不是一类东西），所以不用好奇图标。其它特殊效果还是用好奇图标」
    ///
    /// 目前用到它的三个（都在 `Powers/DominionPowers.cs`）：
    ///   · `DefectDominionPower`（故障机器人的支配）→ 故障机器人头像；
    ///   · `NecrobinderDominionPower`（亡灵契约师的支配）→ 亡灵契约师头像；
    ///   · `FoolDominionPower`（愚者的支配）→ 愚者头像（`Models.Characters.Fool`，仅 0.113 有）。
    ///
    /// 取不到（角色没注册 / 路径为空）时返回 null ⇒ 继续走下面的逻辑（最终落到好奇灯泡），
    /// 绝不会出现「图标空白」。
    /// </summary>
    private static string? DominionIconOf(PowerModel? power)
    {
        try
        {
            if (power == null) return null;
            if (power is Powers.DefectDominionPower)
                return ExistingOrNull(CharacterIconPath(typeof(MegaCrit.Sts2.Core.Models.Characters.Defect)));
            if (power is Powers.NecrobinderDominionPower)
                return ExistingOrNull(CharacterIconPath(typeof(MegaCrit.Sts2.Core.Models.Characters.Necrobinder)));
#if STS2_111
            if (power is Powers.FoolDominionPower)
                return ExistingOrNull(CharacterIconPath(typeof(MegaCrit.Sts2.Core.Models.Characters.Fool)));
#endif
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 支配图标：取角色头像失败（退回好奇）: " + e.Message);
        }
        return null;
    }

    /// <summary>
    /// 某个原版角色的**头像贴图路径**（取不到返回 null）。
    ///
    /// ⚠ `CharacterModel.IconTexturePath` 是 **private**，所以这里按原版同一套拼法自己拼：
    /// <code>
    /// // 反编译 CharacterModel：
    /// private string IconTexturePath =&gt; ImageHelper.GetImagePath(
    ///     "ui/top_panel/character_icon_" + base.Id.Entry.ToLowerInvariant() + ".png");
    /// public  Texture2D IconTexture  =&gt; PreloadManager.Cache.GetTexture2D(IconTexturePath);
    /// </code>
    /// 也就是**顶栏那颗角色头像**用的那张图（`ui/top_panel/character_icon_defect.png` 之类），
    /// 与原版自己显示的完全是同一个文件。
    /// </summary>
    private static string? CharacterIconPath(Type characterType)
    {
        try
        {
            var id = ModelDb.GetId(characterType);
            string entry = id?.Entry ?? string.Empty;
            if (string.IsNullOrEmpty(entry)) return null;

            return MegaCrit.Sts2.Core.Helpers.ImageHelper.GetImagePath(
                "ui/top_panel/character_icon_" + entry.ToLowerInvariant() + ".png");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 支配图标：拼角色头像路径失败: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// 这个 res:// 资源**真的存在**吗（防止「角色没注册 / 该角色没有顶栏头像图」时画出空白图标）。
    /// 查不到资源就返回 null ⇒ 退回好奇灯泡。
    /// </summary>
    private static string? ExistingOrNull(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            if (Godot.ResourceLoader.Exists(path)) return path;
            Logger.Log($"[CharBoss] 支配图标：{path} 不存在 ⇒ 退回好奇灯泡");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 支配图标：检查资源存在性失败（退回好奇）: " + e.Message);
        }
        return null;
    }

    /// <summary>
    /// 这个 Power 类型是不是**本 mod 定义**的 —— 沿基类链走一遍，只要有一层落在本程序集就算。
    ///
    /// 为什么要走基类链：游戏给 Power 造「可变实例」时可能是运行时生成的子类
    /// （`ToMutable()` 那套），它的 `Assembly` 不一定是我们的 ⇒ 只看最外层的类型会漏判，
    /// 漏判就退回原版图标（正是「不死」没变成灯泡的那种表象）。
    /// </summary>
    private static bool IsOurPowerType(Type type)
    {
        var ours = typeof(BossSpecialPowerIconPatch).Assembly;
        for (var cur = type; cur != null; cur = cur.BaseType)
        {
            if (cur.Assembly == ours) return true;
        }
        return false;
    }
}
