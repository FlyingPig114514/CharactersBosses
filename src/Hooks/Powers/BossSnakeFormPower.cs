using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Powers;

/// <summary>
/// 一层「蛇咬猎」的特殊效果「**群蛇形态**」（单体；用户 2026-11-08）。
///
/// 用户原话 + 补充口径：
/// > 「群蛇形态」：静默猎手的手牌数没有上限。
/// >   每当你或静默猎手洗牌时，将一张蛇咬洗入自己的抽牌堆。
/// > ① 她每洗牌一次（剧本第 6 回合）就给自己抽牌堆加 1 张「蛇咬」，**不用表现动画**，
/// >    于是第 9 回合手牌里的蛇咬比之前多一张；
/// > ② **被她锁定的玩家**洗牌时，往**那个玩家的**抽牌堆塞 1 张蛇咬，**用怪物塞牌的动画**。
///
/// 实现分两半：
///   · ① 由剧本的 `ShuffleDrawPile` 驱动（`CharBossMonster.DoScriptedShuffleAsync` 里调
///     <see cref="AddSnakebiteToHerAsync"/>）；
///   · ② 由 <see cref="AfterShuffle"/> 钩子驱动（真人玩家洗牌时触发；只认被她锁定的那位）。
/// 手牌上限那条对我们没有实际影响（她的手牌由剧本重建），但玩家侧的原版上限也不动 ⇒ 保持原版行为。
/// </summary>
public sealed class BossSnakeFormPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    private static int _logs;

    /// <summary>「蛇咬」的 canonical（取不到就一切都是空操作）。</summary>
    private static CardModel? SnakebiteCanonical()
    {
        try
        {
            return ModelDb.GetById<CardModel>(
                ModelDb.GetId(typeof(MegaCrit.Sts2.Core.Models.Cards.Snakebite)));
        }
        catch { return null; }
    }

    /// <summary>① 她自己洗牌后：往**她自己**的抽牌堆顶部加 1 张蛇咬（无动画）。</summary>
    internal static void AddSnakebiteToHer(CharBossMonster boss)
    {
        try
        {
            var pcs = boss?.Shadow?.PlayerCombatState;
            var combat = boss?.CombatState;
            var canonical = SnakebiteCanonical();
            if (pcs == null || combat == null || canonical == null) return;

            var card = combat.CreateCard(canonical, boss!.Shadow!);
            if (card == null) return;
            pcs.DrawPile.AddInternal(card, 0, true);       // 0 = 牌堆顶
            if (_logs++ < 20)
                Logger.Log($"[CharBoss] 群蛇形态：她洗牌 ⇒ 自己抽牌堆 +1 张蛇咬（第 {boss.TurnNumber} 回合）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 群蛇形态：给自己加蛇咬失败: " + e.Message); }
    }

    /// <summary>
    /// ② 被她锁定的玩家洗牌时：往**那个玩家**的抽牌堆塞 1 张蛇咬，并播「怪物塞牌」动画。
    /// </summary>
    public override async Task AfterShuffle(PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            if (player == null) return;
            var me = Owner?.Monster as CharBossMonster;
            if (me == null) return;

            // 她自己洗牌由剧本那条路处理，别重复加
            if (ReferenceEquals(player, Owner?.Player)) return;
            // 只认被她**锁定**的那位（单体特殊效果）
            if (!Core.BossLockOn.IsLocked(me, player)) return;

            var canonical = SnakebiteCanonical();
            var combat = Owner?.CombatState;
            if (canonical == null || combat == null) return;

            // 2026-11-13（恐惧模式「群蛇形态」）：玩家洗牌 ⇒ 塞 **3 张**蛇咬（正常 1 张）
            int copies = Core.DreadMode.IsActive ? 3 : 1;
            for (int i = 0; i < copies; i++)
            {
                var card = combat.CreateCard(canonical, player);
                if (card == null) break;

                var added = await MegaCrit.Sts2.Core.Commands.CardPileCmd.AddGeneratedCardToCombat(
                    card, PileType.Draw, player);
                try { MegaCrit.Sts2.Core.Commands.CardCmd.PreviewCardPileAdd(added); }
                catch { /* 忽略：动画失败不影响塞牌 */ }
            }

            if (_logs++ < 40)
                Logger.Log($"[CharBoss] 群蛇形态：被锁定的玩家洗牌 ⇒ 往他的抽牌堆塞 {copies} 张蛇咬（{player.NetId}）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 群蛇形态：给玩家塞蛇咬失败: " + e.Message); }
    }
}
