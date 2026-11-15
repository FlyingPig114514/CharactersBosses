#if STS2_111   // ⚠ 「愚者 / 塔罗牌」是 0.113.0 才有的 ⇒ 用户 2026-11-14：107 版本不加这个 Boss
using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Cards;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **塔罗愚的「月亮」固定生成 10 把小刀**（用户 2026-11-14）：
///
/// > 「保证第六回合的月亮能被正常打出并生成 10 张小刀，并且这 10 张小刀会被打出
/// >  （要在月亮下面显示这 10 把小刀的伤害意图）」
///
/// 反编译 `TheMoon.OnPlay`：
/// <code>
/// int count = CardPile.MaxCardsInHand - Hand.Cards.Count;   // 「用手牌填满」
/// await Shiv.CreateInHand(Owner, count, CombatState);
/// </code>
/// 也就是「**补到手牌上限**」——手牌里还剩几张就打几把，永远不一定是 10。
/// 他这一手的手牌是剧本给的（打出「节制」「女祭司」之后手里只剩「月亮」自己），
/// 按原版只会生成 9 把 ⇒ 这里对我们的 Boss 直接改成固定 10 把。
///
/// 生成的刀是真卡实例（进他的手牌），所以：
///   · 出牌阶段由 `CharBossMonster.PlayPlannedCards` 里那条
///     `if (play.GeneratedShivs &gt; 0) → SilentBlades.FrontloadRealShivs + PlayOne` 逐把打出去；
///   · 意图侧由 `BossIntentBuilder.ShivsFrom` 对 `THE_MOON` 返回 10（见那边的同名注释），
///     于是「月亮」下面会画出 10 把小刀各自的伤害。
///
/// 玩家的愚者打出这张牌时**完全不受影响**（`return true` 走原版）。
/// </summary>
[HarmonyPatch(typeof(TheMoon), "OnPlay")]
public static class FoolMoonShivPatch
{
    /// <summary>用户口径：月亮一共生成 10 把小刀。</summary>
    internal const int ShivCount = 10;

    [HarmonyPrefix]
    public static bool Prefix(TheMoon __instance, PlayerChoiceContext choiceContext, ref Task __result)
    {
        try
        {
            if (__instance?.Owner?.Creature?.Monster is not CharBossMonster) return true;
        }
        catch { return true; }

        __result = Play(__instance);
        return false;
    }

    private static async Task Play(TheMoon card)
    {
        try
        {
            var owner = card.Owner;
            if (owner == null) return;
            var made = await Shiv.CreateInHand(owner, ShivCount, card.CombatState);
            Logger.Log($"[CharBoss] 塔罗愚·月亮：生成 {ShivCount} 把小刀（原版是「补到手牌上限」，这里固定 10）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 塔罗愚·月亮：生成小刀失败: " + e);
        }
    }
}
#endif
