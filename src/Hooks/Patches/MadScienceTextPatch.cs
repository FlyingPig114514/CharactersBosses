using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**疯狂科学**」（<c>MadScience</c>）卡面上那三个问号（`？？？`）→ 换成混乱 rider 的真实文案。
///
/// 反编译 `MAD_SCIENCE.description`（`.recon/pck/localization/zhs/cards.json`）：
/// <code>
/// {CardType:choose(Attack|Skill|Power):
///     造成{Damage}点伤害…。|获得{Block}点[gold]格挡[/gold]。||？？？}
/// {HasRider:{Sapping:…}{Choking:…}{Energized:…}{Wisdom:…}{Chaos:…}{Expertise:…}{Curious:…}{Improvement:…}|}
/// {CardType:choose(Attack|Skill|Power):\n？？？|\n？？？|？？？|？？？}
/// </code>
/// 最后那一段是**写死在本地化文本里的**「rider 未知」占位符（`AddExtraArgsToDescription`
/// 只往里塞 `CardType` 和 `HasRider` 两个参数，没有可以替换它的变量）⇒ 无论什么类型都会显示 `？？？`。
///
/// 她这张牌在我们剧本里的 rider **永远是 Chaos**（`EventMachineScript` 第 3/7 回合的疯狂科学
/// 都在花 1 费印一张 0 费随机牌），用户 2026-11-04 要求：
/// 「三个问号应该替换成：将一张随机牌放入你的手牌，这张牌在本回合可以免费打出。」
///
/// 做法：挂 `CardModel.GetDescriptionForPile` 的 **postfix**（卡面文本就来自它 ——
/// 反编译 `NCard`：`Model.GetDescriptionForPile(pileType, target)` → `_descriptionLabel.SetTextAutoSize`），
/// **只对「她的那张疯狂科学」**把 `？？？` 换掉；玩家自己的疯狂科学完全不受影响。
///
/// ⚠ 替换用的文案取自**游戏自带本地化**（`events` 表的
/// `TINKER_TIME.pages.CHOOSE_RIDER.options.CHAOS.description`），所以 16 种语言都对，
/// 不会在我们自己这边硬编码中文。
/// </summary>
[HarmonyPatch(typeof(CardModel), nameof(CardModel.GetDescriptionForPile),
              new[] { typeof(PileType), typeof(Creature) })]   // ⚠ 必须钉死参数类型：同名方法有两个（另一个是 3 参私有重载）
public static class MadScienceRiderTextPatch
{
    private const string UnknownRider = "？？？";
    private const string ChaosTextTable = "events";
    private const string ChaosTextKey = "TINKER_TIME.pages.CHOOSE_RIDER.options.CHAOS.description";

    /// <summary>只记一次日志（卡面每次刷新都会走这里）。</summary>
    private static bool _logged;

    [HarmonyPostfix]
    public static void Postfix(CardModel __instance, ref string __result)
    {
        try
        {
            if (string.IsNullOrEmpty(__result)) return;
            if (__result.IndexOf(UnknownRider, StringComparison.Ordinal) < 0) return;
            if (__instance is not MadScience) return;
            if (__instance.Owner?.Creature?.Monster is not CharBossMonster) return;

            string chaos = ChaosRiderText();
            if (string.IsNullOrEmpty(chaos)) return;

            __result = __result.Replace(UnknownRider, chaos);
            if (!_logged)
            {
                _logged = true;
                Logger.Log("[CharBoss] 疯狂科学卡面：已把「？？？」换成混乱 rider 文案（" + chaos + "）");
            }
        }
        catch { /* 绝不因为改文案打断游戏 */ }
    }

    /// <summary>混乱 rider 的本地化文案（取不到就返回空串，保持原样）。</summary>
    private static string ChaosRiderText()
    {
        try
        {
            if (LocString.GetIfExists(ChaosTextTable, ChaosTextKey) == null) return string.Empty;
            return new LocString(ChaosTextTable, ChaosTextKey).GetFormattedText();
        }
        catch { return string.Empty; }
    }
}
