#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Powers;

/// <summary>
/// 三层 3 号观者 Boss「**红蓝观**」（`WATCHER_RUSHDOWN`）的特殊效果「**节制**」（用户 2026-11-18）：
///
/// > 每当观者打出 12 张牌，她就会强制结束自己的回合，**并在玩家的下回合开始时**
/// > 使她和她的其他友方获得 2 点力量。（v0.2.1002 起力量延后到账，用户口径）
/// > PS：机制源于塔1 三层 Boss 时间吞噬者，不过这里是她限制自己而非玩家。
/// > 恐惧增强：特殊效果不变，把塔1 的时间扭曲搬过来 —— 两个 buff 同时挂在观者身上，
/// > 1 个计**她自己的**出牌并终止**她**的回合（节制），1 个计**被其锁定玩家**的出牌并终止**该玩家**的回合
/// > （来自塔1 的「时间扭曲」）；两个独立计数、互不干扰，触发时都要播那个钟的特效。
///
/// ## 实现
/// · **普通模式**：本 power 挂在**她**身上，`Amount` = 她本场已打出的牌数（0..11），
///   满 12 ⇒ 清零 + **强制结束她自己的回合** + 记一笔「+2 力量欠账」（玩家回合开始时到账）。
/// · **恐惧模式**：额外给她挂一份 <see cref="BossTimeWarpPower"/>（计被锁定玩家的出牌、终止那个玩家的回合）。
/// · 计数在 buff 栏可见（`DisplayAmount` = 已打出张数，满格 12）。
///
/// ⚠ v0.2.999 起**塔1 的时钟特效已接上**（`Core.TimeWarpFx`：金边闪光 + 那个钟 + 音效），
///   「节制」与「时间扭曲」两处触发都会播。
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

    /// <summary>
    /// ★ v0.2.1002：**还没到账的「+2 力量」笔数**。
    /// 用户口径：「强制结束自己的回合并**在玩家的下回合开始时**使她和她的其他友方获得 2 点力量」
    /// ⇒ 触发时只把这里 +1，等玩家侧回合开始（<see cref="AfterSideTurnStart"/>）再真正施加。
    /// </summary>
    private int _pendingStrengthGrants;

    /// <summary>延迟到账的力量点数（用户口径 2）。</summary>
    private const decimal PendingStrength = 2m;

    /// <summary>
    /// **玩家侧回合开始**时把「节制」欠的那笔力量补上（用户 2026-11-28 的新口径）。
    ///
    /// 为什么用 `AfterSideTurnStart(side == Player)` 而不是 `AfterPlayerTurnStart`：
    ///   · 后者是**每个玩家**各派发一次（多人局会触发 N 次），得额外去重；
    ///   · 前者是「玩家这一方回合开始」派发一次，与「玩家的下回合开始时」完全对应。
    /// 施给谁：她 + 她的其他友方（= 玩家视角的全部敌方），与旧版当场结算时的范围一致。
    /// </summary>
    public override async Task AfterSideTurnStart(CombatSide side,
                                                  IReadOnlyList<Creature> parties,
                                                  ICombatState combatState)
    {
        try
        {
            if (side != CombatSide.Player) return;
            if (_pendingStrengthGrants <= 0) return;
            _pendingStrengthGrants--;

            var self = Owner;
            if (self?.CombatState == null) return;

            var allies = new List<Creature> { self };
            try
            {
                foreach (var e in self.CombatState.Enemies)
                    if (e != null && !ReferenceEquals(e, self) && e.IsAlive) allies.Add(e);
            }
            catch { /* 忽略 */ }

            var ctx = new BlockingPlayerChoiceContext();
            await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>(
                ctx, allies, PendingStrength, self, null);

            Logger.Log($"[CharBoss] 节制：玩家的回合开始 ⇒ 她与友方 +2 力量到账（{allies.Count} 个单位）" +
                       $"（还剩 {_pendingStrengthGrants} 笔欠账）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 节制：延迟 +2 力量失败（不影响战斗）: " + e.Message); }
    }

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

    /// <summary>触发一次：**只结束她自己的回合**（+2 力量延后到玩家回合开始，见 <see cref="AfterSideTurnStart"/>）。</summary>
    internal static async Task TriggerAsync(PlayerChoiceContext ctx, Creature? self)
    {
        try
        {
            if (self?.CombatState == null) return;

            // ★ v0.2.1002（用户 2026-11-28）：「每当观者打出 12 张牌，她就会强制结束自己的回合
            //   **并在玩家的下回合开始时**使她和她的其他友方获得 2 点力量」
            //   ⇒ 力量不再当场到账，只记一笔「欠账」；玩家侧回合一开始由 AfterSideTurnStart 结算。
            try
            {
                var power = self.GetPower<BossTemperancePower>();
                if (power != null) power._pendingStrengthGrants++;
            }
            catch (Exception e) { Logger.Log("[CharBoss] 节制：登记 +2 力量欠账失败: " + e.Message); }

            if (_logs++ < 20)
                Logger.Log("[CharBoss] 节制：满 12 张 ⇒ 结束她的回合（+2 力量延后到玩家的下回合开始时到账）" +
                           "（同塔1 时间扭曲：金边闪光 + 大钟 + 音效）");

            // ★ v0.2.999（用户 2026-11-28）：「不论是她的特殊效果节制打满 12 张牌强制结束她的回合，
            //   还是玩家打满 12 张牌回合被她强制结束，都应该有塔1 时间扭曲触发时的那个钟的特效」
            //   ⇒ 两处触发点共用 `Core.TimeWarpFx`（塔1 的金边闪光 + 那个钟 + 音效）。
            try { Core.TimeWarpFx.Play(self, "节制：她打满 12 张牌"); }
            catch (Exception e) { Logger.Log("[CharBoss] 节制：特效播放失败（不影响结算）: " + e.Message); }

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
///
/// ★ v0.2.999（用户 2026-11-28）：「这个 buff 应该和塔1 的时间扭曲完全相同」——
///   照塔1 `TimeWarpPower.onAfterUseCard` 三件事全搬：
///     ① `callEndTurnEarlySequence()` ⇒ 你的回合强制结束（本来就有）；
///     ② `BorderFlashEffect(GOLD)` + `TimeWarpTurnEndEffect()` ⇒ 金边闪光 + 那个钟（`Core.TimeWarpFx`）；
///     ③ `ApplyPowerAction(monster, monster, new StrengthPower(monster, STR_AMT), STR_AMT)` ⇒ 力量。
///
/// ★ v0.2.1002（用户 2026-11-28）：「让观者的时间扭曲不会在强制终止玩家回合时让所有 boss 获得 2 力量」
///   ⇒ **③ 那一半撤掉**：现在时间扭曲触发只做两件事 —— 强制结束你的回合 + 播塔1 那套特效。
///     说明文案同步改成「你每打出 12 张牌，观者就会强制结束你的回合。」（见 16 语言 powers.json）。
///
/// ★ v0.2.1001（用户 2026-11-28）：「确保恐惧模式多人联机时时间扭曲只记录被其锁定的玩家的出牌」——
///   以前这里直接拿 `BossLockOn.PureVictim` 的结果跟出牌者比**引用**，两个隐患：
///     · 多人局花名册还没到齐时 `PureVictim` 会返回 null ⇒ 那段时间**一张都不记**（与「记错人」同样是 bug）；
///     · `Player` 实例在**不同来源**（RunState / CombatState / 卡牌的 Owner）里可能不是同一个对象
///       ⇒ 引用比较会把「本该记的那位」也判掉，或反过来谁也判不住。
///   现在改成：
///     · 用**全项目统一**的权威取法 `BossLockOn.TargetFor(boss)`（单人/多人/死者重派都在里面），
///       取不到再退 `PureVictim`，再取不到而场上只有**一位**真人时就用那一位（单人兜底）；
///     · 比较改成「引用相同 **或** NetId 相同」（`SamePlayer`）—— 跨实例也认得出是同一个人；
///     · 影子玩家（Boss 自己/别的 Boss）的牌一律不算：它们的 `Creature.Monster != null`；
///     · 记/不记都打一行诊断（带 `NetId`），多人局一眼能看出到底记的是谁。
/// </summary>
public sealed class BossTimeWarpPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldPlayVfx => false;

    /// <summary>⚠ 同样不能用 `Amount` 存计数（会被整条移除），见 <see cref="BossTemperancePower"/>。</summary>
    private int _warped;

    public override int DisplayAmount => Math.Clamp(_warped, 0, BossTemperancePower.Threshold);

    private static int _countLogs;
    private static int _skipLogs;

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            if (Owner == null || cardPlay?.Card == null) return;
            var shooter = cardPlay.Card.Owner;
            if (shooter?.Creature == null) return;

            var boss = Owner.Monster as Monsters.CharBossMonster;
            if (boss == null) return;                     // 这层 buff 只挂在我们自己的 Boss 身上

            // ① 她自己的牌、以及**别的 Boss 的影子玩家**打的牌，都不算「玩家出牌」
            //    （影子玩家的 Creature 一定带 Monster，见 BossLockOn.RealPlayers）
            if (shooter.Creature.Monster != null) return;

            // ② 只记**被她锁定**的那位玩家（多人局：其余玩家的出牌完全不进这个计数）
            var victim = ResolveVictim(boss);
            if (victim == null)
            {
                if (_skipLogs++ < 6)
                    Logger.Log("[CharBoss] 时间扭曲：还没确定锁定目标（玩家尚未到齐）⇒ 这一张先不记");
                return;
            }
            if (!SamePlayer(victim, shooter))
            {
                if (_skipLogs++ < 12)
                    Logger.Log($"[CharBoss] 时间扭曲：不记 {Core.BossLockOn.DescribeTarget(shooter)} 的牌" +
                               $"（她锁定的是 {Core.BossLockOn.DescribeTarget(victim)}）");
                return;
            }

            _warped++;
            // 同「节制」：不调这一句图标上的数字永远不刷新（`NPower.RefreshAmount` 只听这个事件）
            try { InvokeDisplayAmountChanged(); } catch { /* 只是图标 */ }
            if (_countLogs++ < 48)
                Logger.Log($"[CharBoss] 时间扭曲：记 {Core.BossLockOn.DescribeTarget(shooter)} 的" +
                           $"第 {_warped}/{BossTemperancePower.Threshold} 张" +
                           $"（锁定目标={Core.BossLockOn.DescribeTarget(victim)}）");
            if (_warped < BossTemperancePower.Threshold) return;
            _warped = 0;
            try { InvokeDisplayAmountChanged(); } catch { /* 只是图标 */ }

            // ★ v0.2.1002（用户 2026-11-28）：「让观者的时间扭曲不会在强制终止玩家回合时让所有 boss 获得 2 力量」
            //   ⇒ 这里**不再施加任何力量**。塔1 原版 `TimeWarpPower` 会给怪 +STR_AMT，
            //     但在这个模组里那会让全场 Boss 一起变强（多人局尤其夸张），用户明确要求去掉。
            Logger.Log($"[CharBoss] 时间扭曲：{Core.BossLockOn.DescribeTarget(shooter)} 满 12 张" +
                       " ⇒ 终止他的回合（不加力量；同塔1：金边闪光 + 大钟 + 音效）");

            // ② 塔1 的金边闪光 + 转上来的那个钟 + 音效（和「节制」共用同一套）
            try { Core.TimeWarpFx.Play(Owner, "时间扭曲：玩家打满 12 张牌"); }
            catch (Exception e) { Logger.Log("[CharBoss] 时间扭曲：特效播放失败（不影响结算）: " + e.Message); }

            // ① 强制结束那个玩家的回合
            PlayerCmd.EndTurn(shooter, false, null);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 时间扭曲：结算失败（不影响战斗）: " + e.Message); }
    }

    /// <summary>
    /// **这只 Boss 现在锁定的是谁**（时间扭曲只记这个人的牌）。
    ///
    /// 取法优先级（都能在多人局各端算出同一个答案）：
    ///   ① <see cref="Core.BossLockOn.TargetFor"/>：全项目统一入口，内部已处理
    ///      「多人走纯函数 / 单人走本地表 / 被锁玩家死亡后重派 / SL 后生物实例换了」；
    ///   ② <see cref="Core.BossLockOn.PureVictim"/>：多人局的纯函数本体（TargetFor 返回 null 时再问一次）；
    ///   ③ 场上只有**一位**真人玩家 ⇒ 就是那一位（单人局兜底，保证不会因为花名册没建好而漏记）。
    /// </summary>
    private static Player? ResolveVictim(Monsters.CharBossMonster boss)
    {
        try
        {
            var victim = Core.BossLockOn.TargetFor(boss);
            if (victim != null) return victim;
        }
        catch { /* 继续往下兜 */ }

        try
        {
            var victim = Core.BossLockOn.PureVictim(boss, boss?.CombatState);
            if (victim != null) return victim;
        }
        catch { /* 继续往下兜 */ }

        try
        {
            var real = Core.BossLockOn.RealPlayers(boss?.CombatState);
            if (real.Count == 1) return real[0];
        }
        catch { /* 忽略 */ }

        return null;
    }

    /// <summary>
    /// 这两个 <see cref="Player"/> 是不是**同一个玩家**。
    ///
    /// ⚠ 只比引用不够：同一个玩家在 `RunState` / `CombatState` / `CardModel.Owner` 里的实例
    /// 未必是同一个对象（SL 与联机同步后尤其如此）⇒ 再比一次 `NetId`。
    /// </summary>
    private static bool SamePlayer(Player? a, Player? b)
    {
        if (a == null || b == null) return false;
        if (ReferenceEquals(a, b)) return true;
        try { return a.NetId != 0UL && a.NetId == b.NetId; }
        catch { return false; }
    }
}
#endif
