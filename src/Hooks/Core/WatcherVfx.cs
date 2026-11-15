#if STS2_111   // ⚠ 「愚者 / 塔罗牌」是 0.113.0 才有的 ⇒ 用户 2026-11-14：107 版本不加这个 Boss
using System;
using System.Reflection;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace CharacterBosses.Core;

/// <summary>
/// **借用「观者（Watcher）mod」的特效/动画**（用户 2026-11-14）：
///
/// > 「在装载观者 mod 时，让这里衍生版本的天人形态、斋戒、审判打出时也触发对应的特效和动画。
/// >   审判既是原版衍生牌塔罗牌，也是一张观者的稀有技能牌…如果装载了观者模组，
/// >   就让这张审判也使用观者模组中审判的特效和动画」
///
/// 反编译 `Watcher.dll` 拿到它的特效中心：`internal static class WatcherMod.WatcherAttackVfxHelper`，
/// 里面正好有：
///   · `StartDevaEyeSweep(Creature owner)` / `StopDevaEyeSweep(Creature owner)` —— 天人形态的「开眼」扫场；
///   · `PlayJudgment(Creature caster, Creature target, string title)` —— 审判的处决演出。
///（`WatcherMod.WatcherFasting2.OnPlay` 里**没有任何特效调用** ⇒ 斋戒本身没有专属动画，不用管。）
///
/// 那个类型是 **internal**，跨程序集只能反射；而且观者 mod 可能没装 ⇒
/// 全部走「找不到就静默跳过」，我们对它**零硬依赖**。
/// </summary>
internal static class WatcherVfx
{
    private const string HelperTypeName = "WatcherMod.WatcherAttackVfxHelper";

    /// <summary>观者 mod 在不在（按程序集名找，找不到就什么都不做）。</summary>
    private static Type? HelperType()
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name;
                try { name = asm.GetName().Name ?? string.Empty; } catch { continue; }
                if (!name.Contains("Watcher", StringComparison.OrdinalIgnoreCase)) continue;
                var type = asm.GetType(HelperTypeName, throwOnError: false);
                if (type != null) return type;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    private static void Invoke(string methodName, object?[] args)
    {
        try
        {
            var type = HelperType();
            if (type == null) return;
            foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(m.Name, methodName, StringComparison.Ordinal)) continue;
                var ps = m.GetParameters();
                if (ps.Length != args.Length) continue;
                m.Invoke(null, args);
                Logger.Log($"[CharBoss] 观者特效：{methodName} 已播放");
                return;
            }
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 观者特效 {methodName} 失败（忽略）: {e.Message}");
        }
    }

    /// <summary>天人形态：开眼扫场动画（放一小段就收，免得眼睛一直挂在屏幕上）。</summary>
    internal static async Task PlayDevaEyeSweepAsync(Creature? owner)
    {
        if (owner == null) return;
        Invoke("StartDevaEyeSweep", new object?[] { owner });
        try
        {
            await Task.Delay(4000);   // 用户 2026-11-15：1.2 秒太短，扫场还没展开就被收了
            Invoke("StopDevaEyeSweep", new object?[] { owner });
        }
        catch { /* 忽略 */ }
    }

    /// <summary>审判：处决演出（`title` 是卡名，观者 mod 会把它显示在画面上）。</summary>
    internal static void PlayJudgment(Creature? caster, Creature? target, string title)
    {
        if (caster == null) return;
        Invoke("PlayJudgment", new object?[] { caster, target, title });
    }
}
#endif
