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
