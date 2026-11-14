using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace CharacterBosses.Core;

/// <summary>
/// **恐惧模式（Dread）** 的判定出口（用户 2026-11-13：「适配恐惧模式，不影响其他模式」）。
///
/// 游戏从 0.113.0 起有 `GameMode.Dread`（界面里叫「恐惧」），配一套「支配」（`DominionModel`）：
///   · `DreadRunModel.ApplyCurrentDominion()`：取 `Act.BossEncounter ?? SecondBossEncounter` 的
///     `GetDominion()`，塞进 `RunState.Dominion`；
///   · 进入 **Boss 房**时会被 `RemoveDominionIfRoomIsBoss` 清掉 ⇒ 支配只在**本幕的普通房间**生效。
/// 我们替换了每一幕的 Boss 遭遇，所以必须自己给遭遇挂支配，否则恐惧模式里游戏会打
/// `... which doesn't have any dominion set!`（等于没有支配）。
/// </summary>
internal static class DreadMode
{
    /// <summary>当前是否处于恐惧模式。</summary>
    public static bool IsActive
    {
        get
        {
#if STS2_111
            // ⚠ 107 的 `GameMode` 里根本没有 `Dread`（恐惧模式是 0.113.0 才有的）⇒ 只在 111 构建里判定。
            try
            {
                var run = MegaCrit.Sts2.Core.Runs.RunManager.Instance?.DebugOnlyGetState();
                return run != null && run.GameMode == GameMode.Dread;
            }
            catch { return false; }
#else
            return false;
#endif
        }
    }
}

/// <summary>玩家角色的下标（与 `BossKeys` 的 Character 字段一致）。</summary>
internal static class DreadCharacters
{
    public const int Ironclad = 0;
    public const int Silent = 1;
    public const int Defect = 2;
    public const int Necrobinder = 3;
    public const int Regent = 4;
}

#if STS2_111
// ⚠ 恐惧模式（`Models.Dread.DominionModel`）是 0.113.0 才有的类型 ⇒ 下面五个支配类
//   只在 111 构建里存在；107（正式版）没有恐惧模式，自然也不需要。
/// <summary>
/// **铁甲战士的支配**（用户 2026-11-13）：每场战斗开始时，所有敌人获得 1 层恶魔形态。
/// </summary>
public sealed class IroncladDominion : MegaCrit.Sts2.Core.Models.Dread.DominionModel
{
    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        try
        {
            if (room is not CombatRoom cr) return;
            var enemies = cr.Enemies;
            if (enemies == null) return;

            var targets = new List<Creature>();
            foreach (var c in enemies)
            {
                if (c == null || !c.IsAlive) continue;
                targets.Add(c);
            }
            if (targets.Count == 0) return;

            await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.DemonFormPower>(
                new ThrowingPlayerChoiceContext(), targets, 1m, null, null);
            Logger.Log($"[CharBoss] 恐惧模式·铁甲战士的支配：{targets.Count} 个敌人各获得 1 层恶魔形态");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 铁甲战士的支配失败: " + e.Message); }
    }
}

/// <summary>
/// **静默猎手的支配**（用户 2026-11-13）：每场战斗开始时，你失去 1 点力量并获得 1 层虚弱。
/// </summary>
public sealed class SilentDominion : MegaCrit.Sts2.Core.Models.Dread.DominionModel
{
    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        try
        {
            if (room is not CombatRoom cr) return;
            var players = cr.CombatState?.Players;
            if (players == null) return;

            foreach (var p in players)
            {
                var creature = p?.Creature;
                if (creature == null || !creature.IsAlive) continue;
                if (creature.Monster != null) continue;      // 影子玩家 / 怪物不算
                await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>(
                    new ThrowingPlayerChoiceContext(), creature, -1m, null, null);
                await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.WeakPower>(
                    new ThrowingPlayerChoiceContext(), creature, 1m, null, null);
            }
            Logger.Log("[CharBoss] 恐惧模式·静默猎手的支配：所有玩家 −1 力量、+1 虚弱");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 静默猎手的支配失败: " + e.Message); }
    }
}

/// <summary>
/// **储君的支配**（用户 2026-11-13 改口径）：
/// > 储君的支配的实现效果改成给所有敌人上一层「王之凝视」是 buff
/// > （是储君打能力牌王之凝视获得的那个正面效果，你做一下适配，效果应该是和我之前描述的是一样的）
///
/// 适配依据（反编译）：
/// <code>
/// // MonarchsGaze.OnPlay
/// PowerCmd.Apply&lt;MonarchsGazePower&gt;(ctx, Owner.Creature, DynamicVars["StrengthLoss"], Owner.Creature, this);
///
/// // MonarchsGazePower（PowerType.Buff, StackType.Counter）:
/// //   AfterDamageGiven: if (dealer == Owner &amp;&amp; props.IsPoweredAttack())
/// //       PowerCmd.Apply&lt;MonarchsGazeStrengthDownPower&gt;(ctx, target, Amount, Owner, null);
/// </code>
/// ⇒ 把「王之凝视」这层 **Buff** 挂到**所有敌人**身上：敌人每次用**带攻击力的攻击**打你，
/// 你就获得 `MonarchsGazeStrengthDownPower`（临时 −1 力量、回合结束自动还回）
/// —— 正是原来那条「每当敌人攻击你时，你在本回合失去 1 点力量」。
///
/// ⚠ 旧写法（自己接管 `AfterDamageReceived` 再手动施加 `MonarchsGazeStrengthDownPower`）**已删除**，
/// 否则会和原版这层 Buff 各算一次、变成 −2 力量。
/// </summary>
public sealed class RegentDominion : MegaCrit.Sts2.Core.Models.Dread.DominionModel
{
    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        try
        {
            if (room is not CombatRoom cr) return;
            var enemies = cr.Enemies;
            if (enemies == null) return;

            var targets = new List<Creature>();
            foreach (var c in enemies)
            {
                if (c == null || !c.IsAlive) continue;
                targets.Add(c);
            }
            if (targets.Count == 0) return;

            await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<
                MegaCrit.Sts2.Core.Models.Powers.MonarchsGazePower>(
                new ThrowingPlayerChoiceContext(), targets, 1m, null, null);
            Logger.Log($"[CharBoss] 恐惧模式·储君的支配：{targets.Count} 个敌人各获得 1 层「王之凝视」" +
                       "（它们用攻击打你时，你本回合 −1 力量）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 储君的支配失败: " + e.Message); }
    }
}

/// <summary>
/// **亡灵契约师的支配**（用户 2026-11-13 改口径）：改成给所有玩家挂一层
/// <see cref="Powers.NecrobinderDominionPower"/>（效果不变：你的回合开始时随机一张手牌本回合 +1 费）。
///
/// ⚠ 旧的 `AfterPlayerTurnStart` 实现**已删除**（效果已搬进 power，否则会打两次）。
/// </summary>
public sealed class NecrobinderDominion : MegaCrit.Sts2.Core.Models.Dread.DominionModel
{
    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        try
        {
            if (room is not CombatRoom cr) return;
            var players = cr.CombatState?.Players;
            if (players == null) return;

            var targets = new List<Creature>();
            foreach (var p in players)
            {
                var creature = p?.Creature;
                if (creature == null || !creature.IsAlive) continue;
                if (creature.Monster != null) continue;      // 影子玩家 / 怪物不算
                targets.Add(creature);
            }
            if (targets.Count == 0) return;

            await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<Powers.NecrobinderDominionPower>(
                new ThrowingPlayerChoiceContext(), targets, 1m, null, null);
            Logger.Log($"[CharBoss] 恐惧模式·亡灵契约师的支配：{targets.Count} 名玩家获得「亡灵契约师的支配」");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 亡灵契约师的支配失败: " + e.Message); }
    }
}

/// <summary>
/// **故障机器人的支配**（用户 2026-11-13 改口径）：改成给所有玩家挂一层
/// <see cref="Powers.DefectDominionPower"/>（效果不变：你的回合结束时，每有一名敌人你就受到 3 点伤害）。
///
/// ⚠ 旧的 `AfterSideTurnEnd` 实现**已删除**（效果已搬进 power，否则会打两次）。
/// </summary>
public sealed class DefectDominion : MegaCrit.Sts2.Core.Models.Dread.DominionModel
{
    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        try
        {
            if (room is not CombatRoom cr) return;
            var players = cr.CombatState?.Players;
            if (players == null) return;

            var targets = new List<Creature>();
            foreach (var p in players)
            {
                var creature = p?.Creature;
                if (creature == null || !creature.IsAlive) continue;
                if (creature.Monster != null) continue;      // 影子玩家 / 怪物不算
                targets.Add(creature);
            }
            if (targets.Count == 0) return;

            await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<Powers.DefectDominionPower>(
                new ThrowingPlayerChoiceContext(), targets, 1m, null, null);
            Logger.Log($"[CharBoss] 恐惧模式·故障机器人的支配：{targets.Count} 名玩家获得「故障机器人的支配」");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 故障机器人的支配失败: " + e.Message); }
    }
}

/// <summary>
/// **愚者的支配**（用户 2026-11-13 新增；目前还没有「愚者」的 Boss，先把类型写好）。
/// 效果：**每场战斗开始时，将 2 张「悲伤」加入你的抽牌堆**。
/// 同样只负责「进战斗房间时给所有玩家挂一层 <see cref="Powers.FoolDominionPower"/>」，
/// 真正的发牌在 power 里做。
/// </summary>
public sealed class FoolDominion : MegaCrit.Sts2.Core.Models.Dread.DominionModel
{
    public override async Task AfterRoomEntered(AbstractRoom room)
    {
        try
        {
            if (room is not CombatRoom cr) return;
            var players = cr.CombatState?.Players;
            if (players == null) return;

            var targets = new List<Creature>();
            foreach (var p in players)
            {
                var creature = p?.Creature;
                if (creature == null || !creature.IsAlive) continue;
                if (creature.Monster != null) continue;      // 影子玩家 / 怪物不算
                targets.Add(creature);
            }
            if (targets.Count == 0) return;

            await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<Powers.FoolDominionPower>(
                new ThrowingPlayerChoiceContext(), targets, 1m, null, null);
            Logger.Log($"[CharBoss] 恐惧模式·愚者的支配：{targets.Count} 名玩家获得「愚者的支配」");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 愚者的支配失败: " + e.Message); }
    }
}
#endif
