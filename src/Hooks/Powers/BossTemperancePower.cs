#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Powers;

/// <summary>
/// 三层 3 号观者 Boss「**红蓝观**」（`WATCHER_RUSHDOWN`）的特殊效果「**节制**」（用户 2026-11-18）：
///
/// > 每当观者打出 12 张牌，她就会强制结束自己的回合并使她和她的其他友方获得 2 点力量。
/// > PS：机制源于塔1 三层 Boss 时间吞噬者，不过这里是她限制自己而非玩家。
/// > 恐惧增强：特殊效果不变，把塔1 的时间扭曲搬过来 —— 两个 buff 同时挂在观者身上，
/// > 1 个计**她自己的**出牌并终止**她**的回合（节制），1 个计**被其锁定玩家**的出牌并终止**该玩家**的回合
/// > （来自塔1 的「时间扭曲」）；两个独立计数、互不干扰，触发时都要播那个钟的特效。
///
/// ## 实现
/// · **普通模式**：本 power 挂在**她**身上，`Amount` = 她本场已打出的牌数（0..11），
///   满 12 ⇒ 清零 + 她与所有友方 +2 力量 + **强制结束她自己的回合**。
/// · **恐惧模式**：额外给她挂一份 <see cref="BossTimeWarpPower"/>（计被锁定玩家的出牌、终止那个玩家的回合）。
/// · 计数在 buff 栏可见（`DisplayAmount` = 已打出张数，满格 12）。
///
/// ⚠ 待办（分阶段清单第 4 项）：塔1 时间扭曲触发时的**时钟动画 + 音效**还没接
/// （要从 StS1 资源里取 `TimeWarp` 的贴图/音效 → 导入 → 进 pck_extras），
/// 现在只打日志 <c>[CharBoss] 节制：满 12 张 ⇒ 结束她的回合 + 全体 +2 力量</c>。
/// </summary>
public sealed class BossTemperancePower : PowerModel
{
    /// <summary>触发阈值：打出 12 张牌（用户口径）。</summary>
    public const int Threshold = 12;

    public override PowerType Type => PowerType.Buff;

    /// <summary>计数型显示（真正的数字走 `DisplayAmount`）。</summary>
    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldPlayVfx => false;

    /// <summary>
    /// 本场已打出的牌数。⚠ **不能用 `Amount` 存计数**（v0.2.760 用户实测「她身上没有特殊效果节制」）：
    /// `PowerCmd.ModifyAmount` 把数量改成 0 时，原版 `ShouldRemoveDueToAmount()` 会把**整条 power 移除**
    /// （v0.2.541 在「引雷针」上踩过同一个坑）⇒ 第一次满 12 清零后她就永久没有「节制」了。
    /// </summary>
    private int _played;

    /// <summary>buff 栏上显示的数字 = 已打出张数（0..12）。</summary>
    public override int DisplayAmount => Math.Clamp(_played, 0, Threshold);

    private static int _logs;

    /// <summary>
    /// 恐惧模式：**同时**再挂一份「时间扭曲」（BossTimeWarpPower）—— 用户口径：两个 buff 同时挂在
    /// 观者身上，一个计她的出牌（节制）、一个计被锁定玩家的出牌（时间扭曲），两个独立计数、互不干扰。
    /// </summary>
    public override async Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        try
        {
            if (Owner == null) return;
            if (!Core.DreadMode.IsActive) return;
            var ctx = new BlockingPlayerChoiceContext();
            await PowerCmd.Apply<BossTimeWarpPower>(ctx, Owner, 1m, Owner, null);
            Logger.Log("[CharBoss] 时间扭曲（恐惧模式）：已挂在她身上，单独计被锁定玩家的出牌");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 时间扭曲：挂载失败: " + e.Message); }
    }

    /// <summary>
    /// ★ v0.2.779（用户 2026-11-19：「我刚刚测完红蓝观去测预见观，结果预见观的特殊效果被换成了红蓝观的特殊效果」）：
    ///   这个 power 只属于**特殊效果就是「节制」的那只 Boss**。以前 `PlayOne` 对**每一只** Boss 都会调
    ///   <see cref="NoteCardPlayedAsync"/>，而它「她身上没有这层 buff 就自己补挂」⇒ **任何** Boss 打完第一张牌
    ///   都会被塞上「节制」，于是预见观也跟着按 12 张牌结算。日志实证（15:32:49 / 15:34:20，预见观那一场）：
    ///   <code>
    ///   节制：她身上原来没有这层 buff ⇒ 已补挂
    ///   节制：满 12 张 ⇒ 结束她的回合 + 她与友方 +2 力量
    ///   </code>
    /// </summary>
    private static bool IsTemperanceBoss(Monsters.CharBossMonster? boss)
    {
        try
        {
            var def = boss?.Def;
            if (def == null || boss == null) return false;
            return def.SpecialPowerForAct(boss.ActIndex) == typeof(BossTemperancePower);
        }
        catch { return false; }
    }

    /// <summary>
    /// **由 `CharBossMonster.PlayOne` 直接调用**（用户 2026-11-18：「特殊效果没有正确的按她自己打牌计数」）。
    ///
    /// ⚠ v0.2.771 更正：v0.2.768 当时判断「`PowerModel.AfterCardPlayed` 在她这只 Boss 身上根本不派发」——
    ///   **那个判断是错的**，它其实会派发（证据：一度「每张牌被数两次」，见
    ///   `docs/plans/watcher-rushdown-fix-2026-11-19.md` §1）。现在保留这条显式记账作为**唯一**计数路径，
    ///   并把 `AfterCardPlayed` 里那半段计数删掉。
    /// </summary>
    internal static async Task NoteCardPlayedAsync(PlayerChoiceContext ctx, Monsters.CharBossMonster? boss,
                                                   CardModel? card = null)
    {
        try
        {
            if (boss?.Creature == null) return;
            // ★ v0.2.779：不是「节制」那只 Boss（例如预见观 / Ω观）⇒ 不数、更不许补挂
            if (!IsTemperanceBoss(boss)) return;
            var power = boss.Creature.GetPower<BossTemperancePower>();
            if (power == null)
            {
                // ★ v0.2.769（用户 2026-11-18：「特殊效果还是没有给他计数」）：她身上可能压根没挂上
                //   「节制」（变体判定/施加时机一旦错过就再也没有）⇒ 第一次记账时自己补挂一次。
                await PowerCmd.Apply<BossTemperancePower>(ctx, boss.Creature, 1m, boss.Creature, null);
                power = boss.Creature.GetPower<BossTemperancePower>();
                if (power == null) return;
                Logger.Log("[CharBoss] 节制：她身上原来没有这层 buff ⇒ 已补挂");
            }
            int before = power._played;
            power._played++;
            // ★ v0.2.771：`NPower.RefreshAmount` 只在 `StackType == Counter` 时把 **`DisplayAmount`**
            //   画到图标上（反编译实证），而且只在**收到 `DisplayAmountChanged` 事件**时重画
            //   ⇒ 不调这一句，图标永远停在挂载那一刻的数字（用户 2026-11-19：「还是没有计数」）。
            //   同款写法见 `IroncladRagePower` / `KnowThyPlacePower`。
            try { power.InvokeDisplayAmountChanged(); } catch { /* 只是图标 */ }
            if (_countLogs++ < 48)
                Logger.Log($"[CharBoss] 节制：计数 {before} → {power._played}/{Threshold}（{EntryOf(card)}）");
            if (power._played < Threshold) return;
            power._played = 0;
            try { power.InvokeDisplayAmountChanged(); } catch { /* 只是图标 */ }
            await TriggerAsync(ctx, boss!.Creature);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 节制：计数失败（不影响战斗）: " + e.Message); }
    }

    private static int _countLogs;

    private static string EntryOf(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }

    /// <summary>
    /// **只做一件事**：把「真人玩家（被她锁定的那位）打出攻击牌」记下来（第 12 回合起换计划用）。
    ///
    /// ⚠ v0.2.771（用户 2026-11-19：「第二回合 6 张牌就给自己加力量（明明是 12 张）」）：
    ///   这里以前**还会 `_played++`** —— 而日志实证这条 `PowerModel.AfterCardPlayed` 在她身上
    ///   **是会派发的**（同一个根因反过来证明它：她这 3 个回合一共只打了 7 张牌就触发了一次，
    ///   只有「`PlayOne` 记一次 + 这里再记一次」才对得上 12 ⇒ 每张牌数两遍）。
    ///   现在计数**只走** <see cref="NoteCardPlayedAsync"/> 一条路，这里不再数。
    ///
    /// 顺带把同一个信号也给 `Core.PlayerAttackWatch.Flag`（`PlayOne` 里那记「不惧妖邪 ⇒ 平静」
    /// 的兜底分支读它；那条 Harmony 挂在 `CardCmd.AutoPlay` 上，而真人从手里出牌并不走那个入口，
    /// 所以这里补一份更可靠的来源）。
    /// </summary>
    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var boss = Owner?.Monster as Monsters.CharBossMonster;
            var shadow = boss?.Shadow;
            if (shadow == null || cardPlay?.Card == null) return Task.CompletedTask;

            if (ReferenceEquals(cardPlay.Card.Owner, shadow)) return Task.CompletedTask;   // 她的牌：不在这里数
            if (!IsTemperanceBoss(boss)) return Task.CompletedTask;   // 别的 Boss 不打「换不惧妖邪无限」这套

            bool attack = false;
            try { attack = cardPlay.Card.Type == CardType.Attack; } catch { /* 忽略 */ }
            if (!attack) return Task.CompletedTask;

            Core.BossScript.SwitchRequested = true;
            Core.PlayerAttackWatch.Flag = true;
            // ★ v0.2.773（用户 2026-11-19：「打了攻击牌要**立刻**替换**当回合**的意图，
            //   现在替换的是下回合的」）：她的计划/手牌是 `PrepareTurn`（玩家回合**开始**那一刻）
            //   就算死的 ⇒ 只置标记的话切换要等下一个 `PrepareTurn` 才生效。
            //   这里再喊帧回调把**这一回合**的手牌/计划/意图当场重建（见 `CharBossMonster.RebuildForSwitch`）。
            try
            {
                var script = boss.Def?.ScriptForAct(boss.ActIndex);
                if (script?.SwitchTurn != null && boss.TurnNumber >= script.SwitchFromTurn)
                    boss.SwitchReplanPending = true;
            }
            catch { /* 忽略：只影响「立刻换」这半拍 */ }
            if (_hookLogs++ < 12)
                Logger.Log($"[CharBoss] 节制钩子：真人玩家打出了攻击牌「{EntryOf(cardPlay.Card)}」" +
                           "⇒ 已记下（第 12 回合起本回合换成「不惧妖邪无限」）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 节制钩子记账失败（不影响战斗）: " + e.Message); }
        return Task.CompletedTask;
    }

    private static int _hookLogs;

    /// <summary>触发一次：她与所有友方 +2 力量，然后**强制结束她自己的回合**。</summary>
    internal static async Task TriggerAsync(PlayerChoiceContext ctx, Creature? self)
    {
        try
        {
            if (self?.CombatState == null) return;

            // ① 她和她的其他友方各 +2 力量
            var allies = new List<Creature> { self };
            try
            {
                foreach (var e in self.CombatState.Enemies)
                    if (e != null && !ReferenceEquals(e, self) && e.IsAlive) allies.Add(e);
            }
            catch { /* 忽略 */ }

            await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>(ctx, allies, 2m, self, null);

            if (_logs++ < 20)
                Logger.Log("[CharBoss] 节制：满 12 张 ⇒ 结束她的回合 + 她与友方 +2 力量" +
                           "（塔1 时间扭曲的时钟特效/音效待接，见 docs/plans/boss-watcher-rushdown.md）");

            // ② 强制结束她自己的回合（影子玩家 => PlayerCmd.EndTurn）
            var player = self.Player;
            if (player != null)
            {
                try { PlayerCmd.EndTurn(player, false, null); }
                catch (Exception e) { Logger.Log("[CharBoss] 节制：结束她的回合失败（不影响战斗）: " + e.Message); }
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 节制：结算失败（不影响战斗）: " + e.Message); }
    }
}

/// <summary>
/// **恐惧模式的「时间扭曲」**（用户 2026-11-18：把塔1 三层 Boss 时间吞噬者的时间扭曲搬过来）：
/// 计**被这只 Boss 锁定玩家的**出牌，满 12 张 ⇒ 终止**那个玩家**的回合（并播同一个钟）。
/// 与她自己的「节制」是两个独立计数、互不干扰。
/// </summary>
public sealed class BossTimeWarpPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldPlayVfx => false;

    /// <summary>⚠ 同样不能用 `Amount` 存计数（会被整条移除），见 <see cref="BossTemperancePower"/>。</summary>
    private int _warped;

    public override int DisplayAmount => Math.Clamp(_warped, 0, BossTemperancePower.Threshold);

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            if (Owner == null || cardPlay?.Card == null) return;
            var shooter = cardPlay.Card.Owner;
            var myShadow = (Owner.Monster as Monsters.CharBossMonster)?.Shadow;
            if (shooter == null) return;
            if (myShadow != null && ReferenceEquals(shooter, myShadow)) return;     // 只数玩家那边的牌
            // 只数**被这只 Boss 锁定**的那位玩家（多人时其余玩家不受影响）
            var boss = Owner.Monster as Monsters.CharBossMonster;
            var victim = Core.BossLockOn.PureVictim(boss, Owner.CombatState);
            if (victim == null || !ReferenceEquals(victim, shooter)) return;

            _warped++;
            // 同「节制」：不调这一句图标上的数字永远不刷新（`NPower.RefreshAmount` 只听这个事件）
            try { InvokeDisplayAmountChanged(); } catch { /* 只是图标 */ }
            if (_warped < BossTemperancePower.Threshold) return;
            _warped = 0;
            try { InvokeDisplayAmountChanged(); } catch { /* 只是图标 */ }
            Logger.Log($"[CharBoss] 时间扭曲：{shooter.Creature?.Name} 满 12 张 ⇒ 终止他的回合" +
                       "（塔1 时间扭曲的时钟特效/音效待接）");
            PlayerCmd.EndTurn(shooter, false, null);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 时间扭曲：结算失败（不影响战斗）: " + e.Message); }
    }
}
#endif
