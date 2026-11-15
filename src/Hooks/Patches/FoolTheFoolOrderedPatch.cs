#if STS2_111   // ⚠ 「愚者 / 塔罗牌」是 0.113.0 才有的 ⇒ 用户 2026-11-14：107 版本不加这个 Boss
using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **塔罗愚的「愚者」按剧本顺序打牌，不随机**（用户 2026-11-14）：
///
/// > 「第十回合让愚者优先打女祭司，从而让女祭司被回响形态重打而非魔术师」
///
/// 反编译 `TheFool.OnPlay`：循环 `Cards` 次，每次从手牌里 **`StableShuffle(Rng.CombatCardSelection)`
/// 之后取第一张可打出的牌**再 `CardCmd.AutoPlay` —— 也就是随机挑。他的牌是剧本印的，
/// 手牌顺序 = 剧本顺序，随机挑会让「回响形态到底重打哪一张」每局都不一样（用户看到的随机性就来自这里）。
///
/// 现在对我们的 Boss 改成：**按手牌顺序（= 剧本顺序）依次打出**，可预期的同时
/// 也让「第 10 回合愚者先打女祭司」这种设计成立。
/// 玩家的愚者完全不受影响（`return true` 走原版随机）。
/// </summary>
[HarmonyPatch(typeof(TheFool), "OnPlay")]
public static class FoolTheFoolOrderedPatch
{
    [HarmonyPrefix]
    public static bool Prefix(TheFool __instance, PlayerChoiceContext choiceContext, ref Task __result)
    {
        try
        {
            if (__instance?.Owner?.Creature?.Monster is not CharBossMonster) return true;
        }
        catch { return true; }

        __result = PlayInOrder(__instance, choiceContext);
        return false;
    }

    private static async Task PlayInOrder(TheFool card, PlayerChoiceContext ctx)
    {
        int count = 2;
        try { count = Math.Max(0, card.DynamicVars.Cards.IntValue); } catch { count = 2; }

        for (int i = 0; i < count; i++)
        {
            CardModel? pick = null;
            try
            {
                var hand = PileType.Hand.GetPile(card.Owner).Cards;
                foreach (var candidate in hand)
                {
                    if (candidate == null) continue;
                    if (ReferenceEquals(candidate, card)) continue;              // 自己已经打出去了
                    if (candidate.Keywords.Contains(CardKeyword.Unplayable)) continue;
                    pick = candidate;
                    break;                                                       // 手牌顺序 = 剧本顺序
                }
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 塔罗愚·愚者：读手牌失败: " + e.Message);
            }

            if (pick == null) return;
            try
            {
                await CardCmd.AutoPlay(ctx, pick, (Creature?)null);
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 塔罗愚·愚者：按顺序打出 " + pick.Id.Entry + " 失败: " + e.Message);
                return;
            }
        }
    }
}
#endif
