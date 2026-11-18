#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Core;

/// <summary>
/// **「玩家这回合打过攻击牌」的两处接线**（用户 2026-11-18，三层 3 号「红蓝观」）：
/// · 第 4 回合：玩家打出攻击牌 ⇒ 她那记「不惧妖邪」应当让她进入「平静」；
/// · 第 12 回合起：玩家打出攻击牌 ⇒ 本回合计划切换成「不惧妖邪无限」那一套。
///
/// 为什么不再依赖 `WatcherFearNoEvilAimPower.AfterCardPlayed`：那条是 **Power 钩子**，
/// 实测在她身上不派发（同一个根因也害得「节制」计数一次都没动过）⇒ 现在统一在
/// **出牌入口** `CardCmd.AutoPlay` 上判，谁打的、什么类型一目了然。
/// </summary>
[HarmonyPatch]
public static class PlayerAttackWatch
{
    /// <summary>本回合玩家打出过攻击牌。</summary>
    internal static bool Flag;

    [HarmonyPrepare]
    public static bool Prepare() => TargetMethod() != null;

    internal static System.Reflection.MethodBase? TargetMethod()
        => GameCompat.DeclaredMethod(typeof(MegaCrit.Sts2.Core.Commands.CardCmd), "AutoPlay");

    [HarmonyPostfix]
    public static void Postfix(object[] __args)
    {
        try
        {
            // ★ v0.2.769：不再靠参数名绑定（绑定不上就永远收不到 ⇒ 第 4 回合不惧妖邪 / 第 12 回合换计划全失效）
            CardModel? card = null;
            if (__args != null)
                foreach (var a in __args)
                {
                    if (a is CardModel cm) { card = cm; break; }
                    if (a is System.Collections.IEnumerable en)
                        foreach (var b in en) if (b is CardModel c2) { card = c2; break; }
                }
            if (card?.Owner == null) return;
            // 只看**真人玩家**的牌：影子玩家的牌 Owner 是她的 Shadow（`Creature.Monster != null`）
            if (card.Owner.Creature?.Monster != null) return;
            if (card.Type != CardType.Attack) return;
            Flag = true;
            BossScript.SwitchRequested = true;         // 红蓝观第 12 回合起：本回合换成不惧妖邪那套
        }
        catch { /* 忽略 */ }
    }

    /// <summary>她回合开始时清掉（玩家回合内的攻击才算「本回合」）。</summary>
    internal static void Reset() => Flag = false;
}
#endif