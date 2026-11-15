#if STS2_111   // ⚠ 「愚者 / 塔罗牌」是 0.113.0 才有的 ⇒ 用户 2026-11-14：107 版本不加这个 Boss
using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **塔罗愚打「恶魔」时不出牌**（用户 2026-11-14）：
///
/// > 「？？？你这恶魔怎么打出了神话形态，此时他的其他牌堆应该都是没有牌的
/// >  （唯一的三张能力牌已经被打出了）。拦掉恶魔的打牌效果」
///
/// 反编译 `TheDevil.OnPlay`：
/// <code>
/// 消耗你的手牌；每因此消耗一张牌，就打出你抽牌堆顶的一张牌
/// </code>
/// 他的抽牌堆里还剩着没打出去的神话形态（3 张里只打得出 1～2 张，其余一直躺在抽牌堆），
/// 于是「恶魔」把神话形态从抽牌堆顶抓出来又打了一遍 —— 而这完全不在剧本里。
///
/// 设计口径里「恶魔」本来就标了**（无效果）**：他的牌全是剧本印出来的，
/// 「消耗手牌 / 从抽牌堆打牌」这两件事对他都只会打乱剧本 ⇒ 直接整张空转。
///
/// 同一批被拦掉的还有「**世界**」（`TheWorld`：把弃牌堆里的随机 5 张放回手牌）——
/// 按用户 §3「拦掉所有原版抽牌、印牌、**捞牌**」的口径，它也是「从牌堆拿牌」，剧本里同样标「（无效果）」。
///
/// ⚠ 「愚者」那张（`TheFool`）**不能拦**：用户设计就是「愚者（自动打出后面 2 张牌）」。
/// </summary>
internal static class FoolNoopCard
{
    /// <summary>这张牌是不是「对我们的 Boss 应该整张空转」的牌。</summary>
    internal static bool ShouldNoop(CardModel? card)
    {
        try
        {
            if (card?.Owner?.Creature?.Monster is not CharBossMonster) return false;
            string entry = card.Id.Entry ?? string.Empty;
            return entry == "THE_DEVIL"      // 消耗手牌 + 从抽牌堆顶打牌
                   || entry == "THE_WORLD";  // 从弃牌堆捞牌回手
        }
        catch { return false; }
    }
}

/// <summary>「恶魔」：对我们的 Boss 整张不结算（手感上就是「打出但无事发生」）。</summary>
[HarmonyPatch(typeof(TheDevil), "OnPlay")]
public static class FoolDevilNoopPatch
{
    [HarmonyPrefix]
    public static bool Prefix(TheDevil __instance, ref Task __result)
    {
        if (!FoolNoopCard.ShouldNoop(__instance)) return true;
        __result = Task.CompletedTask;
        return false;
    }
}

/// <summary>「世界」：同上（从弃牌堆捞牌会把手牌打乱成剧本以外的样子）。</summary>
[HarmonyPatch(typeof(TheWorld), "OnPlay")]
public static class FoolWorldNoopPatch
{
    [HarmonyPrefix]
    public static bool Prefix(TheWorld __instance, ref Task __result)
    {
        if (!FoolNoopCard.ShouldNoop(__instance)) return true;
        __result = Task.CompletedTask;
        return false;
    }
}
#endif
