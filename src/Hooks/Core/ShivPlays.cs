using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 静默猎手（刀猎）的「小刀」执行器。
///
/// 小刀（<c>Shiv</c>，0 费 token，4 伤）在两种场合出现：
///   1. **剧本/牌造出来的**（刀刃之舞 3、斗篷与匕首 1～2、隐秘匕首 2、先制打击 2、刀扇 4）
///      —— 这张牌结算时才 <c>Shiv.CreateInHand</c> 出手牌，所以规划阶段只知道"打几张"，
///      真打出去必须在出牌阶段打完之后做（<see cref="PlannedPlay.GeneratedShivs"/>）；
///   2. **袖里乾坤塞给她的**（玩家每回合首次打出耗能 ≥ 2 的牌 → 3 张）
///      —— 这些是玩家回合塞进来的，规划时还不存在 ⇒ **不进意图**（用户设计：实际伤害更高）。
///
/// 两种都用这里的 <see cref="PlayAsync"/> 打出去，统一 0 费、打玩家侧血最少的那个。
/// </summary>
public static class ShivPlays
{
    /// <summary>这张卡是不是小刀（token 牌，<c>Shiv</c>）。</summary>
    public static bool IsShiv(CardModel? card)
    {
        try
        {
            if (card == null) return false;
            if (card.Id.Entry == "SHIV") return true;
            return card.Rarity == CardRarity.Token && card.Id.Entry.Contains("SHIV");
        }
        catch { return false; }
    }

    /// <summary>
    /// 把手里的小刀打出去，最多 <paramref name="maxCount"/> 张（0 或负数 = 有多少打多少）。
    /// 返回实际打出的张数。
    /// </summary>
    public static async Task<int> PlayAsync(CharBossMonster boss, PlayerCombatState pcs,
                                            PlayerChoiceContext ctx, int maxCount, string reason)
    {
        int played = 0;
        try
        {
            var shivs = pcs.Hand.Cards.Where(IsShiv).ToList();
            foreach (var shiv in shivs)
            {
                if (maxCount > 0 && played >= maxCount) break;

                var target = BossPlanner.ChooseTarget(boss, shiv);
                if (target == null) break;

                try
                {
                    var playTask = CardCmd.AutoPlay(ctx, shiv, target, AutoPlayType.Default);
                    var finished = await Task.WhenAny(playTask, Task.Delay(5000));
                    if (!ReferenceEquals(finished, playTask))
                        Logger.Log("[CharBoss] 小刀超时(5s)，跳过继续");
                }
                catch (Exception e)
                {
                    Logger.Log($"[CharBoss] 小刀打出失败: {e.Message}");
                    continue;
                }

                played++;
                Logger.Log($"[CharBoss] 小刀已打出（{reason}）→ {target.Name}，" +
                           $"bossHp={boss.Creature?.CurrentHp}");

                // 打出去的刀必须从她手牌里消失（用户实测：AutoPlay 之后刀还留在手牌栏里）
                try
                {
                    if (pcs.Hand.Cards.Any(c => ReferenceEquals(c, shiv)))
                    {
                        pcs.Hand.RemoveInternal(shiv, true);
                        pcs.DiscardPile.AddInternal(shiv, -1, true);
                    }
                }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 小刀移出手牌失败: " + e.Message);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 小刀结算异常: " + e.Message);
        }
        return played;
    }
}
