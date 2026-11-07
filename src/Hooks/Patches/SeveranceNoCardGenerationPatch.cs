using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **拦掉「切断」（<c>SEVERANCE</c>）的印牌**（用户 2026-10-29）。
///
/// 「切断会往手牌中添加灵魂，但这张灵魂已经被写在『剧本』里了。把切断的印牌拦了」
///
/// 反编译 `Severance.OnPlay`：
/// <code>
/// var souls = Soul.Create(Owner, 3, CombatState);                                  // 造 3 张灵魂
/// await CardPileCmd.AddGeneratedCardToCombat(souls[0], PileType.Draw,   Owner, CardPilePosition.Random);
/// await CardPileCmd.AddGeneratedCardToCombat(souls[1], PileType.Discard, Owner);
/// await CardPileCmd.AddGeneratedCardToCombat(souls[2], PileType.Hand,    Owner);   // ← 进手牌那张
/// </code>
/// 而我们的剧本里**已经把这张灵魂写好了**（`C<Soul>()`）—— 再让原版印一张，手牌里就多出一张
/// 剧本没算过的牌（意图/计划全对不上）。
///
/// 做法：给 `CardPileCmd.AddGeneratedCardToCombat` 挂前缀，**只在「正在打出的那张牌是切断」**
/// 这一个窗口里把生成整块吞掉（返回一个占位结果，调用方 `await` 完继续跑，不会卡住）。
/// 判据只看「谁的牌 + 正在打哪张」，真人玩家、别的牌、别的 Boss 一律不受影响。
///
/// 要拦别的牌的印牌：把它的 entry 加进 <see cref="CharBossMonster.BlocksGeneratedCardsEntries"/> 即可。
/// </summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.AddGeneratedCardToCombat))]
public static class SeveranceNoCardGenerationPatch
{
    private static int _blocked;

    [HarmonyPrefix]
    public static bool Prefix(CardModel card, PileType newPileType, Player creator,
                             ref Task<CardPileAddResult> __result)
    {
        try
        {
            if (creator?.Creature?.Monster is not CharBossMonster) return true;

            string? playing = CharBossMonster.CurrentPlayingEntry;
            if (string.IsNullOrEmpty(playing)) return true;
            if (!CharBossMonster.BlocksGeneratedCardsEntries.Contains(playing!)) return true;

            if (_blocked++ < 20)
            {
                Logger.Log($"[CharBoss] 拦掉 {playing} 的印牌：{SafeEntry(card)} → {newPileType}" +
                           "（这张牌剧本里已经写好了，再印一张会让手牌/计划对不上）");
            }
            __result = Task.FromResult<CardPileAddResult>(default!);
            return false;
        }
        catch
        {
            return true;      // 出问题就按原版走（多一张牌总比卡住强）
        }
    }

    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }
}
