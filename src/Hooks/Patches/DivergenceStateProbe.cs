using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Core;
using CharacterBosses.Monsters;
namespace CharacterBosses.Patches;

/// <summary>
/// **不同步发生那一刻，把我们自己的状态打进 `mod_log.txt`**（用户 2026-10-29 第二次反馈「有不同步」）。
///
/// ## 为什么要这个
/// `godot.log` 里原版的 `State divergence detected!` 只会给两个**校验和数字**，
/// 另一侧的 `LOCAL STATE DUMP` **只在本机日志里**（我们拿到的 `godot (4).log` 是房主的，
/// 看不到客人 7656119… 的 dump）⇒ 无法逐项对比是哪一份状态不一样。
///
/// 所以这里挂在原版 `ChecksumTracker.LogStateDivergence`（**房主和客人都会走到**）上，
/// 打一行「我们关心的状态」摘要：
///   · 每名真人玩家（按 `NetId` 排序）身上的**锁定图标实例数**、以及每份的施加者（Boss 标识）；
///   · 每只我们的 Boss 的关键 power 层数（袖里乾坤 / 群情激奋 / 偏差认知 / 共鸣 / 带毒刺击 /
///     原始力量 / 命运同担…）+ HP/格挡；
///   · 我们算出来的锁定分配表。
///
/// 两边各自发一份 `mod_log.txt`，把这些行放在一起比，就能立刻看出是哪一项不一样
/// （**锁定图标**最可疑：它是唯一被我们反复 apply/remove 的同步 power）。
/// </summary>
[HarmonyPatch]
internal static class DivergenceStateProbe
{
    private static int _logged;

    /// <summary>目标方法（私有）：`ChecksumTracker.LogStateDivergence(TrackedChecksum, StateDivergenceMessage, ulong, int)`。</summary>
    private static MethodBase? Target()
    {
        try
        {
            var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Multiplayer.Game.ChecksumTracker");
            if (t == null) return null;
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (m.Name == "LogStateDivergence") return m;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    private static bool Prepare() => Target() != null;

    private static MethodBase? TargetMethod() => Target();

    [HarmonyPrefix]
    public static void Prefix()
    {
        try
        {
            if (_logged++ > 12) return;
            Dump("检测到不同步");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 不同步状态摘要失败（忽略）: " + e.Message);
        }
    }

    /// <summary>
    /// **回合快照环形缓冲**（v0.2.448）。
    ///
    /// 为什么要加：RitsuLib 的诊断包只能给出「不同步那一刻」的一张对照表，
    /// 而真正要查的是「**从哪一回合开始不一样**」。有了这条时间线，一份 `mod_log.txt`
    /// 就能看出第一次偏离发生在哪一侧的哪一回合（两端的快照行按时间对齐逐字比对即可）。
    ///
    /// ⚠ 只**读**状态、绝不改状态 —— 写在帧回调里改状态正是历史上几次联机不同步的根因
    /// （见 `ShadowDiamondDiademOncePatch` 的注释）。
    /// </summary>
    private static readonly List<string> History = new();

    private const int HistoryCap = 40;

    /// <summary>上一次记快照时的 CombatState（引用变了 = 换了战斗 ⇒ 清空环）。</summary>
    private static ICombatState? _lastCombat;

    /// <summary>一个**两端都会被 await 的同步时点**：某一方回合开始（含「敌方回合结束之后」）。</summary>
    internal static void RecordSnapshot(ICombatState? combatState, CombatSide side)
    {
        try
        {
            if (combatState == null) return;

            // ★ v0.2.464（2026-11-07，玩家上报的那条不同步摘要）：`History` 是**跨战斗**累积的静态环，
            //   所以摘要里最先出现的 `#1 轮次 1` / `#2 轮次 2` 其实是**上一场**（例如 Boss 前的精英战）
            //   的快照 —— 真正要看的那几个回合被挤到后面，再被字数上限切掉 ⇒ 报告等于没用。
            //   现在引用一换（＝换了一场战斗）就把环清掉，摘要里的快照必然属于本场。
            try
            {
                if (!ReferenceEquals(_lastCombat, combatState))
                {
                    _lastCombat = combatState;
                    lock (History) History.Clear();
                }
            }
            catch { /* 忽略 */ }

            var sb = new StringBuilder();
            sb.Append($"轮次 {combatState.RoundNumber} 侧={side} |");

            // ── 怪物（含我们的 Boss）──
            foreach (var creature in combatState.Enemies)
            {
                if (creature == null) continue;
                string name = SafeEntry(creature.Monster) ?? SafeCharacterName(creature) ?? "?";
                sb.Append($" E:{name}={creature.CurrentHp}/{creature.MaxHp}+{creature.Block}");
                AppendPowers(creature, sb);
            }

            // ── 真人玩家（按 NetId 排序 ⇒ 两端顺序一致）──
            var players = BossLockOn.RealPlayers(combatState);
            players.Sort((a, b) =>
            {
                ulong na = 0, nb = 0;
                try { na = a?.NetId ?? 0; } catch { }
                try { nb = b?.NetId ?? 0; } catch { }
                return na.CompareTo(nb);
            });
            foreach (var p in players)
            {
                var creature = p?.Creature;
                if (creature == null) continue;
                sb.Append($" P:{SafeNetId(p)}={creature.CurrentHp}/{creature.MaxHp}+{creature.Block}");
                AppendPowers(creature, sb);
            }

            lock (History)
            {
                History.Add(sb.ToString());
                while (History.Count > HistoryCap) History.RemoveAt(0);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 回合快照失败（忽略）: " + e.Message);
        }
    }

    /// <summary>把这只生物身上的 power 都写进快照（`名称:层数`，**按名字排序**保证两端逐字一致）。</summary>
    private static void AppendPowers(Creature creature, StringBuilder sb)
    {
        try
        {
            var parts = new List<string>();
            foreach (var slot in creature.Powers)
            {
                if (slot == null) continue;
                string entry;
                decimal amount;
                try { entry = slot.Id.Entry; } catch { continue; }
                try { amount = slot.Amount; } catch { amount = 0m; }
                parts.Add($"{entry}:{amount}");
            }
            if (parts.Count == 0) return;
            parts.Sort(StringComparer.Ordinal);
            sb.Append('[').Append(string.Join(",", parts)).Append(']');
        }
        catch { /* 忽略 */ }
    }

    private static string? SafeEntry(AbstractModel? model)
    {
        try { return model?.Id.Entry; } catch { return null; }
    }

    /// <summary>生物没有 <c>Monster</c> 时（影子玩家/宠物）用它的角色名当标签。</summary>
    private static string? SafeCharacterName(Creature creature)
    {
        try
        {
            var p = creature.Player;
            if (p == null) return null;
            var ch = p.Character;
            return ch != null ? ch.GetType().Name : "Player";
        }
        catch { return null; }
    }

    /// <summary>把最近若干条回合快照倒出来（不同步摘要里用）。</summary>
    private static void AppendHistory(StringBuilder sb)
    {
        try
        {
            string[] copy;
            lock (History) copy = History.ToArray();
            if (copy.Length == 0) return;

            int from = Math.Max(0, copy.Length - 12);
            sb.Append("\n  ── 最近回合快照（两端逐字比对，第一处不同就是分叉点）──");
            for (int i = from; i < copy.Length; i++)
            {
                sb.Append($"\n  #{i + 1 - from,-2} {copy[i]}");
            }
        }
        catch { /* 忽略 */ }
    }

    /// <summary>把「我们关心的状态」打一行（多行文本，前缀统一便于 grep）。</summary>
    internal static void Dump(string reason)
    {
        try
        {
            var combat = CombatManager.Instance?.DebugOnlyGetState();
            if (combat == null)
            {
                Logger.Log($"[CharBoss] ⚠ 不同步状态摘要（{reason}）：拿不到战斗状态");
                return;
            }

            var sb = new StringBuilder();
            sb.Append($"[CharBoss] ⚠ 不同步状态摘要（{reason}）：轮次 {combat.RoundNumber}");

            // ── 这一场到底是什么遭遇 + 本机是房主还是客人（v0.2.237 追加）──
            // 联机分叉最常见的一种就是「两端根本不是同一场战斗」（客人打了原版 Boss、房主打角色 Boss），
            // 以前摘要里没有遭遇 id，只能靠猜。两个客户端的日志一比这对就出来了。
            try
            {
                string enc = "<空>";
                try { enc = combat.Encounter?.Id.Entry ?? "<空>"; } catch { /* 忽略 */ }
                string role = "?";
                try { role = Core.NetRole.IsGuest ? "客人" : Core.NetRole.Type.ToString(); } catch { /* 忽略 */ }
                string actBoss = "<空>";
                try { actBoss = MultiBossPlan.RunStateOrNull()?.Act?.BossEncounter?.Id.Entry ?? "<空>"; } catch { /* 忽略 */ }
                sb.Append($"\n  遭遇={enc} 本机={role} 本幕存档 Boss 槽={actBoss}");
            }
            catch { /* 忽略 */ }

            // ── 多人阵容：阵容指纹 + 场上有几只我们的 Boss（v0.2.526 追加）──
            // 为什么加：不同步最常见的一种是「两端生成的多 Boss 数量/顺序不同」
            // （2026-11-08 那次：房主 2 只、客人 1 只）。可摘要里原来只有**本机**的 Boss 列表，
            // 「该上几只」这件事根本看不到，所以只有一份日志时没法判断 ——
            // 上一轮只能要求玩家「每人各发一份」。现在把**阵容指纹**也写进摘要：
            // 两端这一行不同 ⇒ 就是阵容分叉；相同而 Boss 数不同 ⇒ 是生成阶段分叉。
            try
            {
                int players2 = 0, act2 = 0;
                try
                {
                    var state2 = MultiBossPlan.RunStateOrNull();
                    players2 = state2?.Players?.Count ?? 0;
                    act2 = state2?.CurrentActIndex ?? 0;
                }
                catch { /* 用 0 */ }

                int bosses2 = 0;
                try
                {
                    foreach (var c2 in combat.Enemies)
                        if (c2?.Monster is CharBossMonster) bosses2++;
                }
                catch { /* 用 0 */ }

                string fp = "?";
                try
                {
                    if (act2 >= 0 && act2 <= 2)
                    {
                        var f0 = Core.BossLineups.FingerprintOf(Core.BossLineups.For(act2, 0));
                        var f1 = Core.BossLineups.FingerprintOf(Core.BossLineups.For(act2, 1));
                        fp = $"第1场={f0} 第2场={f1}";
                    }
                }
                catch { fp = "?"; }

                sb.Append($"\n  多人阵容：玩家={players2} 第 {act2 + 1} 层 阵容指纹[{fp}] " +
                          $"场上我方 Boss={bosses2} 只（两端这一行必须一致）");
            }
            catch { /* 忽略 */ }

            // ── 每名真人玩家的锁定图标实例 ──
            var players = BossLockOn.RealPlayers(combat);
            players.Sort((a, b) =>
            {
                ulong na = 0, nb = 0;
                try { na = a?.NetId ?? 0; } catch { }
                try { nb = b?.NetId ?? 0; } catch { }
                return na.CompareTo(nb);
            });

            foreach (var p in players)
            {
                var creature = p?.Creature;
                if (creature == null) continue;

                int count = 0;
                var appliers = new List<string>();
                try
                {
                    foreach (var slot in creature.Powers)
                    {
                        if (slot is not Powers.LockOnPower lockOn) continue;
                        count++;
                        Creature? applier = null;
                        try { applier = lockOn.Applier; } catch { applier = null; }
                        appliers.Add(applier?.Monster is CharBossMonster b ? b.Id.Entry : "<无施加者>");
                    }
                }
                catch { /* 忽略 */ }

                string key = creature.Monster?.Id.Entry ?? "Player";
                sb.Append($"\n  玩家 {key}/NetId={SafeNetId(p)}：锁定图标 {count} 个" +
                          (appliers.Count > 0 ? $" [{string.Join(",", appliers)}]" : string.Empty) +
                          $"；HP {creature.CurrentHp}/{creature.MaxHp} 格挡 {creature.Block}");
            }

            // ── 我们的 Boss：关键 power 层数 ──
            foreach (var creature in combat.Enemies)
            {
                if (creature?.Monster is not CharBossMonster boss) continue;
                var parts = new List<string>();
                AddPower<Powers.SleeveTrickPower>(creature, "袖里乾坤", parts);
                AddPower<Powers.IroncladRagePower>(creature, "群情激奋", parts);
                AddPower<Powers.DeviationPower>(creature, "偏差认知", parts);
                AddPower<Powers.ResonancePower>(creature, "共鸣", parts);
                AddPower<Powers.PoisonStrikePower>(creature, "带毒刺击", parts);
                AddPower<Powers.BossPrimalForcePower>(creature, "原始力量", parts);
                AddPower<Powers.SharedFatePower>(creature, "命运同担", parts);
                AddPower<Powers.BossStellarGatheringPower>(creature, "群星荟萃", parts);
                AddPower<Powers.KnowThyPlacePower>(creature, "何人僭越", parts);
                AddPower<MegaCrit.Sts2.Core.Models.Powers.AccelerantPower>(creature, "触媒", parts);

                // ── 出牌 AI 的可观察状态（v0.2.237 追加）──
                // 两端的剧本（`boss.Plan`）/ 手牌 / 能量只要有一项不一样，下一张牌就会分叉，
                // 而以前摘要里只有 HP 和 power 层数 ⇒ 看不出来。这里把这三样都打上。
                try
                {
                    var pcs = boss.Shadow?.PlayerCombatState;
                    if (pcs != null)
                    {
                        parts.Add($"手牌={pcs.Hand.Cards.Count}/抽={pcs.DrawPile.Cards.Count}" +
                                  $"/弃={pcs.DiscardPile.Cards.Count}/耗={pcs.ExhaustPile.Cards.Count}");
                        parts.Add($"能量={pcs.Energy}/星={pcs.Stars}");
                    }
                    else
                    {
                        parts.Add("手牌=<无影子玩家>");
                    }
                }
                catch { /* 忽略 */ }
                try
                {
                    if (boss.Plan.Count > 0)
                    {
                        parts.Add("计划=[" + string.Join(",", boss.Plan.ConvertAll(p => p.Card?.Id.Entry ?? "?")) + "]");
                    }
                }
                catch { /* 忽略 */ }

                sb.Append($"\n  Boss {boss.Id.Entry}：HP {creature.CurrentHp}/{creature.MaxHp} 格挡 {creature.Block}" +
                          (parts.Count > 0 ? $"；{string.Join("，", parts)}" : "；（无本模组 power）"));
            }

            // ── 我们算出来的锁定分配表（Boss → 玩家）──
            var assign = new List<string>();
            try
            {
                foreach (var creature in combat.Enemies)
                {
                    if (creature?.Monster is not CharBossMonster b) continue;
                    var victim = BossLockOn.TargetFor(b);
                    string name = "?";
                    try
                    {
                        name = victim?.Creature?.Monster != null
                            ? (victim!.Creature!.Monster!.Id.Entry ?? "?")
                            : (victim?.Character?.GetType().Name ?? "?");
                    }
                    catch { name = "?"; }
                    assign.Add($"{b.Id.Entry}→{name}/NetId={SafeNetId(victim)}");
                }
            }
            catch { /* 忽略 */ }
            if (assign.Count > 0) sb.Append($"\n  锁定分配：{string.Join(" | ", assign)}");

            AppendHistory(sb);

            Logger.Log(sb.ToString());
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 不同步状态摘要生成失败: " + e.Message);
        }
    }

    /// <summary>
    /// 每「某一方回合开始」记一条快照（**两端都会 await 的同步钩子**）。
    /// 107 / 111 都有 `Hook.AfterSideTurnStart(ICombatState, CombatSide, IReadOnlyList&lt;Creature&gt;)`
    /// （`AfterSideTurnEnd` 只有 111 有，所以不含侧别地只用这一个）。
    /// </summary>
    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), nameof(MegaCrit.Sts2.Core.Hooks.Hook.AfterSideTurnStart),
                  new[] { typeof(ICombatState), typeof(CombatSide), typeof(IReadOnlyList<Creature>) })]
    internal static class SideTurnSnapshotPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ICombatState combatState, CombatSide side)
        {
            try { RecordSnapshot(combatState, side); }
            catch { /* 忽略：快照绝不能影响战斗 */ }
        }
    }

    private static void AddPower<T>(Creature creature, string label, List<string> parts) where T : PowerModel
    {
        try
        {
            var power = creature.GetPower<T>();
            if (power == null) return;
            parts.Add($"{label}={power.Amount}");
        }
        catch { /* 忽略 */ }
    }

    private static string SafeNetId(MegaCrit.Sts2.Core.Entities.Players.Player? player)
    {
        try { return player?.NetId.ToString() ?? "?"; } catch { return "?"; }
    }
}
