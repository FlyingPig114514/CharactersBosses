using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**从摸牌堆顶自动打出**」（<c>CardPileCmd.AutoPlayFromDrawPile</c>）期间，强制按**玩家侧**
/// 解析 <c>HittableEnemies</c> —— 只对我们自己的 Boss 生效。
///
/// ★ v0.2.308（玩家反馈：「**倾泻**打出**巨石**会打自己」）：
/// - 倾泻 `Cascade.OnPlay` 的实现就是
///   `CardPileCmd.AutoPlayFromDrawPile(ctx, Owner, n, CardPilePosition.Top, forceExhaust:false)`；
/// - 巨石 `GiantRock` 是 `TargetType.AnyEnemy` 的 token，`OnPlay` 直接打 `cardPlay.Target`，
///   而 `AutoPlay(…, target: null)` 的目标是从 `HittableEnemies` 里挑的；
/// - 这条链是**自己 await 的一段**，可能在她回合边界之后才结算 ⇒ 那时 `CurrentSide` 已经是 Player，
///   我们「敌方回合才翻侧」的判据不成立 ⇒ 目标按**敌方阵营（= 她自己）**解析 ⇒ 打自己。
///
/// 这与「地狱狂徒 + 百年积木」是**同一类**问题（自动出牌时目标从被翻过的 `HittableEnemies` 里挑），
/// 只是方向相反：那次是**玩家**的牌被翻成玩家侧，这次是**她的**牌没被翻成玩家侧。
///
/// 做法：给她这条入口套一个**计数**（`HittableEnemiesSidePatch.ForceRelativeHold`），
/// 整条任务跑完（`ContinueWith`）再减回去；计数而不是 bool，避免嵌套/并发把它提前清掉。
/// </summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.AutoPlayFromDrawPile),
              new[]
              {
                  typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),
                  typeof(MegaCrit.Sts2.Core.Entities.Players.Player),
                  typeof(int),
                  typeof(MegaCrit.Sts2.Core.Entities.Cards.CardPilePosition),
                  typeof(bool),
              })]
internal static class AutoPlayFromDrawPileSideHoldPatch
{
    [HarmonyPrefix]
    public static void Prefix(PlayerChoiceContext choiceContext, Player player, int count,
                              CardPilePosition position, bool forceExhaust)
    {
        try
        {
            if (player?.Creature?.Monster is not Monsters.CharBossMonster boss) return;
            HittableEnemiesSidePatch.ForceRelativeHold++;
            Logger.Log($"[CharBoss] 摸牌堆自动打出（{boss.Def?.Key}）：{count} 张，" +
                       "这段时间「可命中敌人」强制按玩家侧解析（避免巨石打到自己）");
        }
        catch { /* 忽略 */ }
    }

    [HarmonyPostfix]
    public static void Postfix(PlayerChoiceContext choiceContext, Player player, int count,
                               CardPilePosition position, bool forceExhaust, ref Task __result)
    {
        try
        {
            if (player?.Creature?.Monster is not Monsters.CharBossMonster) return;
            if (__result == null)
            {
                Pop();
                return;
            }
            // 任务真正跑完再放掉计数（async：Postfix 只拿到 Task，所以要挂续延）
            _ = __result.ContinueWith(_ => Pop(), TaskScheduler.Default);
        }
        catch { Pop(); }
    }

    private static void Pop()
    {
        try
        {
            if (HittableEnemiesSidePatch.ForceRelativeHold > 0) HittableEnemiesSidePatch.ForceRelativeHold--;
        }
        catch { /* 忽略 */ }
    }
}
