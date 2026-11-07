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
/// 二层「彩虹机」（`DEFECT_RAINBOW`）的**模仿学习**落地 + 「拿到无尽刀刃后每回合一张小刀」。
///
/// 用户 2026-11-07 原话：
/// > 多人时，模仿学习选择目标的优先级从高到低：涂毒猎、铸剑储、放血战、王储、仆从储、灵魂骨、杂耍战、蛇眼猎。
/// > 因为理论上不会和物理机在同一场战斗，所以这里优先级在蛇眼猎之下。
/// > 其它幕的 boss 理论上也不会和它出现在同一场战斗，优先级也在蛇眼猎之下。
/// > 通过模仿涂毒猎获得「无尽刀刃」后，之后的每回合会打出一张小刀。
///
/// 为什么是「其它 Boss」而不是「其它玩家」：原版这张牌是「选择另一名**玩家**」，
/// 而我们的 Boss 用的是**影子玩家**，多人局里「另一位同类」就是**同一侧的其它 Boss**。
///
/// 实现拆成两半：
///   ① 她打出「模仿学习」⇒ <see cref="StartMimic"/> 按优先级挑一只在场的其它 Boss；
///   ② 被挑中的那只**接下来前 2 次打出能力牌**时，把那两张牌的类型记下来（<see cref="OnCardPlayed"/>）；
///   ③ 她下个回合开始时免费打出这些复制品（<see cref="PlayPendingAsync"/>）；
///      其中「无尽刀刃」还会给她一个永久标记 ⇒ 之后每回合先打一张小刀。
/// </summary>
internal static class RainbowMimic
{
    /// <summary>目标优先级（用户 2026-11-07）。不在表里的（含物理机与其它幕的 Boss）排在最后。</summary>
    private static readonly string[] Priority =
    {
        "SILENT_ENVENOM",        // 涂毒猎
        "REGENT_FORGE",          // 铸剑储
        "IRONCLAD_BLOOD",        // 放血战
        "REGENT_KING",           // 王储
        "REGENT_MINION",         // 仆从储
        "NECROBINDER_SOUL",      // 灵魂骨
        "IRONCLAD_JUGGLER",      // 杂耍战
        "SILENT_SNECKO",         // 蛇眼猎
    };

    private sealed class State
    {
        public int TargetCombatId = -1;
        public int CopiesLeft;
        public readonly List<Type> Pending = new();
        public bool EndlessBlades;
        public int ShivsPlayed;
    }

    private static readonly Dictionary<int, State> States = new();

    private static int IdOf(CharBossMonster boss)
    {
        try { var id = boss.Creature?.CombatId; return id.HasValue ? (int)id.Value : -1; } catch { return -1; }
    }

    /// <summary>这只 Boss 是不是「彩虹机」（按她这一场生效的设计 Key 判断）。</summary>
    internal static bool IsRainbow(CharBossMonster? boss)
    {
        try
        {
            if (boss == null) return false;
            if (!string.IsNullOrEmpty(boss.SpawnVariantKey)) return boss.SpawnVariantKey == "DEFECT_RAINBOW";
            return boss.Def?.SpecialPowerForAct(boss.ActIndex) == typeof(Powers.BossRainbowPower);
        }
        catch { return false; }
    }

    private static string KeyOf(CharBossMonster boss)
    {
        try { return boss.SpawnVariantKey ?? string.Empty; } catch { return string.Empty; }
    }

    /// <summary>
    /// 这张牌在本回合会不会**额外造成无来源群体伤害**（0 = 不会）。
    /// 数据来自剧本的 <c>ScriptCard.ExtraAoe</c>（目前只有彩虹机第 8 回合那张「彩虹+」= 30，
    /// 因为那一下会踩到遗物「节拍器」的第 5 颗球）。
    /// </summary>
    internal static int ExtraAoeFor(CharBossMonster boss, CardModel? card)
    {
        try
        {
            if (boss?.Def == null || card == null) return 0;
            var turn = boss.Def.ScriptForAct(boss.ActIndex)?.TurnAt(boss.TurnNumber);
            if (turn?.Cards == null) return 0;
            var type = card.GetType();
            foreach (var scriptCard in turn.Cards)
            {
                if (scriptCard == null || scriptCard.ExtraAoe <= 0) continue;
                if (scriptCard.Card != type) continue;
                return scriptCard.ExtraAoe;
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    /// <summary>她打出了「模仿学习」⇒ 按优先级选一只在场的其它 Boss 作为模仿目标（最多模仿 2 张）。</summary>
    internal static void StartMimic(CharBossMonster self)
    {
        try
        {
            var id = IdOf(self);
            if (id < 0) return;

            CharBossMonster? best = null;
            int bestRank = int.MaxValue;

            foreach (var creature in self.CombatState?.Enemies ?? new List<MegaCrit.Sts2.Core.Entities.Creatures.Creature>())
            {
                if (creature?.Monster is not CharBossMonster other) continue;
                if (ReferenceEquals(other, self)) continue;
                if (!creature.IsAlive || creature.IsDead) continue;

                string key = KeyOf(other);
                int rank = Array.IndexOf(Priority, key);
                if (rank < 0) rank = Priority.Length;        // 物理机 / 其它幕的 Boss：排在蛇眼猎之后

                // 同档按 CombatId 升序（各端一致、SL 不变）
                int otherId = IdOf(other);
                if (rank < bestRank || (rank == bestRank && best != null && otherId < IdOf(best)))
                {
                    bestRank = rank;
                    best = other;
                }
            }

            if (best == null)
            {
                Logger.Log("[CharBoss] 彩虹机·模仿学习：场上没有其它 Boss ⇒ 这张牌什么也不做（单人局本该已换成碎片整理）");
                return;
            }

            States[id] = new State { TargetCombatId = IdOf(best), CopiesLeft = 2 };
            Logger.Log($"[CharBoss] 彩虹机·模仿学习：目标 = {KeyOf(best)}（优先级第 {(bestRank == Priority.Length ? "最后（表外）" : (bestRank + 1).ToString())} 档），接下来 2 张能力牌会被复制");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 彩虹机·模仿学习选目标失败: " + e.Message);
        }
    }

    /// <summary>
    /// 某只 Boss 打出牌之后调用：
    ///   · 是彩虹机打出「模仿学习」⇒ 选目标；
    ///   · 否则如果她是别人模仿的目标 ⇒ 记录这张**能力牌**（最多 2 张）。
    /// </summary>
    internal static void OnCardPlayed(CharBossMonster speaker, CardModel? card)
    {
        try
        {
            if (speaker == null || card == null) return;

            if (IsRainbow(speaker) && card.Id.Entry == "IMITATION_LEARNING")
            {
                StartMimic(speaker);
                return;
            }

            bool isPower;
            try { isPower = card.Type == CardType.Power; } catch { isPower = false; }
            if (!isPower) return;

            int speakerId = IdOf(speaker);
            if (speakerId < 0) return;

            foreach (var state in States.Values)
            {
                if (state.CopiesLeft <= 0) continue;
                if (state.TargetCombatId != speakerId) continue;

                var type = card.GetType();
                state.Pending.Add(type);
                state.CopiesLeft--;
                Logger.Log($"[CharBoss] 彩虹机·模仿学习：记下 {card.Id.Entry} 的复制品" +
                           $"（还剩 {state.CopiesLeft} 次机会），她下回合打出");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 彩虹机·模仿学习记牌失败: " + e.Message);
        }
    }

    /// <summary>
    /// 她回合开始时：免费打出模仿到的复制品；若已拿到「无尽刀刃」，再多打**一张小刀**。
    /// （用户：「通过模仿涂毒猎获得『无尽刀刃』后，之后的每回合会打出一张小刀」。）
    /// </summary>
    internal static async Task PlayPendingAsync(CharBossMonster self, PlayerCombatState pcs, PlayerChoiceContext ctx)
    {
        try
        {
            var id = IdOf(self);
            if (id < 0 || !States.TryGetValue(id, out var state)) return;

            // ① 模仿到的能力牌复制品（免费打出）
            if (state.Pending.Count > 0)
            {
                var types = state.Pending.ToList();
                state.Pending.Clear();
                foreach (var type in types)
                {
                    await PlayCopyAsync(self, pcs, ctx, type, "模仿学习");
                }
            }

            // ② 「无尽刀刃」：之后每回合一张小刀
            if (state.EndlessBlades)
            {
                await PlayOneShivAsync(self, pcs, ctx, state);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 彩虹机·模仿学习结算失败: " + e.Message);
        }
    }

    private static async Task PlayCopyAsync(CharBossMonster self, PlayerCombatState pcs,
                                            PlayerChoiceContext ctx, Type cardType, string reason)
    {
        try
        {
            var shadow = self.Shadow;
            var combat = self.CombatState;
            if (shadow == null || combat == null) return;

            var canonical = ModelDb.GetById<CardModel>(ModelDb.GetId(cardType));
            if (canonical == null)
            {
                Logger.Log($"[CharBoss] 模仿学习：{cardType.Name} 取不到 canonical ⇒ 跳过");
                return;
            }

            var card = combat.CreateCard(canonical, shadow);
            if (card == null) return;

            // 「无尽刀刃」记一个永久标记：之后每回合一张小刀
            try
            {
                if (card.Id.Entry == "INFINITE_BLADES")
                {
                    if (States.TryGetValue(IdOf(self), out var st)) st.EndlessBlades = true;
                }
            }
            catch { /* 忽略 */ }

            var target = BossPlanner.ChooseTarget(self, card);
            try { pcs.DiscardPile.AddInternal(card, -1, true); } catch { /* 放不进就算了 */ }

            var task = CardCmd.AutoPlay(ctx, card, target, AutoPlayType.Default);
            var finished = await Task.WhenAny(task, Task.Delay(5000));
            if (!ReferenceEquals(finished, task))
            {
                Logger.Log("[CharBoss] 模仿学习的复制品超时(5s)，跳过");
                return;
            }
            Logger.Log($"[CharBoss] 彩虹机·{reason}：已免费打出 {card.Id.Entry}（目标 {target?.Name}）");
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 模仿学习复制品 {cardType.Name} 打出失败: {e.Message}");
        }
    }

    private static async Task PlayOneShivAsync(CharBossMonster self, PlayerCombatState pcs,
                                               PlayerChoiceContext ctx, State state)
    {
        try
        {
            var shadow = self.Shadow;
            var combat = self.CombatState;
            if (shadow == null || combat == null) return;

            var shiv = combat.CreateCard<MegaCrit.Sts2.Core.Models.Cards.Shiv>(shadow);
            if (shiv == null) return;

            var target = BossPlanner.ChooseTarget(self, shiv);
            try { pcs.Hand.AddInternal(shiv); } catch { /* 加不进手牌也试着打 */ }

            var task = CardCmd.AutoPlay(ctx, shiv, target, AutoPlayType.Default);
            var finished = await Task.WhenAny(task, Task.Delay(5000));
            if (!ReferenceEquals(finished, task))
            {
                Logger.Log("[CharBoss] 模仿学习的「小刀」超时(5s)，跳过");
                return;
            }

            try
            {
                if (pcs.Hand.Cards.Any(c => ReferenceEquals(c, shiv)))
                {
                    pcs.Hand.RemoveInternal(shiv, true);
                    pcs.DiscardPile.AddInternal(shiv, -1, true);
                }
            }
            catch { /* 忽略 */ }

            state.ShivsPlayed++;
            Logger.Log($"[CharBoss] 彩虹机·无尽刀刃：第 {state.ShivsPlayed} 张免费小刀 → {target?.Name}");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 模仿学习的小刀打出失败: " + e.Message);
        }
    }
}
