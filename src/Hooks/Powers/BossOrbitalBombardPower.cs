using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using CharacterBosses.Monsters;

namespace CharacterBosses.Powers;

/// <summary>
/// 一层「**轰击储**」的特殊效果「**环绕轨道**」（用户 2026-11-04 设计）。
///
/// 原话：
/// > 环绕轨道：你以一层光谱偏移开始战斗。储君第一回合被击晕，在打出轰击后被击晕
/// > 直到你在打出过无色牌的第五个回合，此后储君反而受到轰击的伤害。
///
/// 拆成三块：
///  1. **开局给玩家 1 层「光谱偏移」**（`SpectrumShiftPower`：每回合摸 1 张随机无色牌）
///     —— 落地在 `BossHookBridge` 的战斗开始段（`SpecialPowerForAct == 本类型` 时给真人玩家上）。
///  2. **计数**：玩家**打出过无色牌**的回合数（同一回合打多张只算 1 回合）。
///     这里挂在 `Hook.AfterCardPlayed` 上自己数（与 `PlayerCardPlayedTracker` 同一个钩子），
///     数到 <see cref="TurnsToWake"/> 就把 <see cref="WakeTurn"/> 定成「下一次规划」的回合号。
///  3. **反噬**：唤醒之后轰击的伤害改成打**她自己**。
///     落地在 `Patches/AutoPlayTargetForRealPlayerPatch`（轰击是靠 `CardCmd.AutoPlay(card, null)`
///     自动打出的 ⇒ 在那里把目标从「真人玩家」改成她本人）。
///
/// ⚠ 联机口径：这是「数玩家打出的牌」——两端**都**从同步的 `AfterCardPlayed` 里数同一批出牌，
///   所以结果天然一致（和本项目其它「数出牌」的记账同一套依据）。真正影响战斗状态的是
///   `Awake`（只喂给剧本选择与轰击目标），不做任何本机独有的分支。
/// </summary>
public sealed class BossOrbitalBombardPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    /// <summary>
    /// **计数是倒数**（用户 2026-11-05：「因为计数好像不支持 0，改成倒数吧，5 开始，在变为 1 后再 -1 就直接不显示计数了」）：
    /// 层数 = **还差几个「打出过无色牌的回合」他就醒**（5 → 4 → … → 1）。
    /// 醒来之后（层数会变成 0）就切回 `Single` ⇒ 图标上**不再显示数字**。
    /// </summary>
    public override PowerStackType StackType =>
        Awake ? PowerStackType.Single : PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => false;

    /// <summary>「打出过无色牌的回合」要攒到几个才唤醒她（用户：第五个回合）。</summary>
    internal const int TurnsToWake = 5;

    // ── 本场战斗的环绕轨道状态（静态：一个进程里同一时刻只有一场战斗） ──

    /// <summary>已经有多少个「玩家打出过无色牌的回合」。</summary>
    internal static int ColorlessTurns;

    /// <summary>她**真正开始行动**的那个剧本回合号（0 = 还没唤醒）。</summary>
    internal static int WakeTurn;

    internal static bool Awake => WakeTurn > 0;

    internal static void ResetForNewFight()
    {
        ColorlessTurns = 0;
        WakeTurn = 0;
        CountedTurn = -1;
    }

    /// <summary>还差几个「打出过无色牌的回合」他就醒（5 → 4 → … → 1；到 0 就醒了）。</summary>
    internal static int Remaining(int colorlessTurns) => Math.Max(0, TurnsToWake - colorlessTurns);

    /// <summary>
    /// 把图标层数同步成**倒数**。0 层 Counter 不显示/会被移除 ⇒ 用 `SetAmount(1)` 兜底，
    /// 而 `StackType` 已经是 `Single` ⇒ 不画数字（用户口径：倒数到 0 就干脆不显示计数）。
    /// </summary>
    private static void SyncAmount(Monsters.CharBossMonster? boss)
    {
        try
        {
            var power = boss?.Creature?.GetPower<BossOrbitalBombardPower>();
            if (power == null) return;
            power.SetAmount(Math.Max(1, Remaining(ColorlessTurns)));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 环绕轨道：同步计数层数失败（不影响唤醒）: " + e.Message);
        }
    }

    /// <summary>
    /// 记一个「玩家打出过无色牌的回合」。同一回合重复调用只算一次。
    /// 由 <see cref="CountColorlessTurnPatch"/> 调。
    /// </summary>
    internal static void NoteColorlessPlay(int playerTurnNumber, CharBossMonster boss)
    {
        try
        {
            if (Awake) return;                       // 已经唤醒：不再数
            if (CountedTurn == playerTurnNumber) return;   // 这个回合已经算过
            CountedTurn = playerTurnNumber;

            ColorlessTurns++;
            Logger.Log($"[CharBoss] 环绕轨道：玩家第 {playerTurnNumber} 回合打出了无色牌 ⇒ " +
                       $"已累计 {ColorlessTurns}/{TurnsToWake} 个回合（计数倒数到 {Remaining(ColorlessTurns)}）");

            // 层数是**倒数**：5 → 4 → … → 1（用户 2026-11-05：「计数好像不支持 0，改成倒数吧」）。
            SyncAmount(boss);

            if (ColorlessTurns < TurnsToWake) return;

            // 唤醒：她的下一份计划（= 下一个玩家回合开始时的规划）就用「唤醒后 A」。
            // 计划是按 `TurnNumber` 取的，而这个数在她回合结束前不会变 ⇒ +1 正好是下一份计划。
            WakeTurn = Math.Max(1, boss.TurnNumber) + 1;
            Logger.Log($"[CharBoss] 环绕轨道：无色牌回合攒够 {TurnsToWake} 个 ⇒ **她醒了**" +
                       $"（下一份计划起用唤醒后循环；此后轰击反过来打她自己）");

            // 倒数减到 0 ⇒ **不再显示数字**（`StackType` 已经由 `Awake` 切回 `Single`）。
            // 同时刷一次左侧意图栏：**这一回合**轰击就已经反过来打她自己了，那条「轰击」意图必须立刻消失
            //（用户 2026-11-05：「计数满了 5 的那回合，轰击的意图应该消失」）。
            SyncAmount(boss);
            try { UI.BossHandUi.RefreshForce(boss); }
            catch (Exception e) { Logger.Log("[CharBoss] 环绕轨道：刷新意图栏失败（忽略）: " + e.Message); }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 环绕轨道计数失败（忽略）: " + e.Message);
        }
    }

    /// <summary>上一次计数的玩家回合号（同一回合重复出无色牌只算 1 次）。</summary>
    private static int CountedTurn = -1;

    /// <summary>
    /// `Hook.AfterCardPlayed` 前缀：**真人玩家**打出**无色牌**时记一个回合。
    /// 只对我们这只「轰击储」生效（按 `SpecialPowerForAct` 判断，其它 Boss 零影响）。
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardPlayed))]
    internal static class CountColorlessTurnPatch
    {
        [HarmonyPrefix]
        public static void Prefix(CardPlay cardPlay)
        {
            try
            {
                var card = cardPlay?.Card;
                if (card == null) return;

                var owner = card.Owner?.Creature;
                if (owner == null) return;
                if (owner.Monster != null) return;            // 她（影子玩家）自己打的不算，宠物也不算

                // 只认「无色牌」：卡池是 `ColorlessCardPool`
                bool colorless = false;
                try { colorless = card.Pool is ColorlessCardPool; } catch { colorless = false; }
                if (!colorless) return;

                var boss = FindBombardBoss(owner);
                if (boss == null) return;                    // 场上没有「轰击储」⇒ 什么都不做

                // ★ 用户 2026-11-05：「轰击储的特殊效果也是单体的」⇒ 只数**被她锁定的**那位玩家的无色牌
                if (!Core.BossLockOn.SpecialEffectAppliesTo(boss, owner.Player)) return;

                int turn = 0;
                try { turn = owner.Player?.PlayerCombatState?.TurnNumber ?? 0; } catch { turn = 0; }
                NoteColorlessPlay(turn, boss);
            }
            catch { /* 绝不影响战斗 */ }
        }

        /// <summary>场上有没有「轰击储」（带有 <see cref="BossOrbitalBombardPower"/> 的我们的 Boss）。</summary>
        private static CharBossMonster? FindBombardBoss(Creature anyPlayerCreature)
        {
            try
            {
                var combat = anyPlayerCreature.CombatState;
                if (combat == null) return null;
                foreach (var enemy in combat.Enemies)
                {
                    if (enemy?.Monster is not CharBossMonster boss) continue;
                    if (boss.Creature == null) continue;
                    if (boss.Creature.GetPower<BossOrbitalBombardPower>() == null) continue;
                    return boss;
                }
            }
            catch { /* 忽略 */ }
            return null;
        }
    }
}
