using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace CharacterBosses.Core;

/// <summary>
/// 伤害预测：给意图（卡牌下方图标）算出「这一击到底打多少」。
///
/// 为什么不能直接读 <c>DynamicVars.Damage</c>：
///   · 「欺凌」这类牌的伤害在 <c>CalculatedDamage</c> 里，而且**只有走预览通道才会按目标易伤层数重算**；
///   · 目标身上的易伤/虚弱、Boss 自己的力量等，都要过 <c>Hook.ModifyDamage</c> 才是最终数字；
///   · 命中次数根本不在数据里（原版也是硬编码表 + 少数例外）。
/// 配方参考同一个作者的上一个模组（Critical Cinematic 大招手演出）的 <c>DamageEstimator</c>：
///   <c>DynamicVars.ClearPreview()</c> → <c>card.UpdateDynamicVarPreview(CardPreviewMode.Normal, target, vars)</c>
///   → 取 <c>DamageVar</c>/<c>CalculatedDamageVar</c> 的 <c>BaseValue</c> → <c>Hook.ModifyDamage(..., CardPreviewMode.Normal, ...)</c>。
/// </summary>
public static class BossDamagePredictor
{
    /// <summary>这些牌的 <c>Repeat</c> 变量<b>不是</b>命中次数（如冰之长枪是「生成 3 个冰球 + 打一次」）。</summary>
    private static readonly HashSet<string> RepeatIsNotHits = new(StringComparer.Ordinal)
    {
        "CHAOS", "CONSUMING_SHADOW", "HIBERNATE", "ICE_LANCE", "REFRACT", "CAPACITOR",
        "MODDED", "QUADCAST", "DEATHS_DOOR", "DECISIONS_DECISIONS",
    };

    /// <summary>数据里看不出来、只能写死的命中次数（取自上一个模组实测结论）。</summary>
    private static readonly Dictionary<string, int> FixedHitCounts = new(StringComparer.Ordinal)
    {
        ["REFRACT"] = 2,
        ["ASTRAL_PULSE"] = 2,
        ["DAGGER_SPRAY"] = 2,
        ["MAUL"] = 2,
        ["RIP_AND_TEAR"] = 2,
        ["THRASH"] = 2,
        ["TWIN_STRIKE"] = 2,
        ["UPROAR"] = 2,
    };

    /// <summary>这张牌打几次（同一目标）。</summary>
    public static int Hits(CardModel card) => Hits(card, null);

    /// <summary>
    /// 这张牌对某个目标打几次。
    ///
    /// 命中次数**大多不在数据里**，要靠三种来源：
    ///   1. <c>CalculatedHits</c> 这类**计算出来的**变量（弹幕齐射 = 充能球数量、
    ///      终结技 = 本回合打出的攻击牌数、散射炮 = 消耗的状态牌数、螺旋钻机 = 打出的牌数…）
    ///      —— 它们只有走预览通道才会算出来，所以要跟伤害一样先 <c>UpdateDynamicVarPreview</c>；
    ///   2. <c>Repeat</c> 变量（少数牌直接用它当命中数，排除表里的那些不是）；
    ///   3. 写死的例外表（数据里完全看不出来的）。
    /// 早先只读 <c>Repeat</c>，于是弹幕齐射这类牌只显示 1 段（用户实测）。
    /// </summary>
    public static int Hits(CardModel card, Creature? target)
    {
        var entry = card.Id.Entry;
        int hits = 1;

        // 「拆卸」（Dismantle）：卡面是「造成 8 点伤害；**如果该敌人有易伤**，则攻击两次」——
        // 段数是**运行时**按目标状态决定的，数据里没有（`Repeat`/`CalculatedHits` 都不会告诉意图），
        // 所以意图一直只显示一段（用户 2026-10-24：「拆卸在目标有易伤的时候没有显示为二段伤害」）。
        if (entry == "DISMANTLE" && HasVulnerable(target)) return 2;

        int calculated = (int)Math.Round(PreviewVar(card, target, "CalculatedHits", 0));
        if (calculated > 1) hits = calculated;

        if (hits <= 1 && !RepeatIsNotHits.Contains(entry))
        {
            int repeat = (int)Math.Round(PreviewVar(card, target, "Repeat", 1));
            if (repeat > hits) hits = repeat;
        }

        if (FixedHitCounts.TryGetValue(entry, out var fixedHits)) hits = Math.Max(hits, fixedHits);
        return Math.Max(1, hits) * ReplayMultiplier(card);
    }

    /// <summary>目标身上有没有易伤（拆卸的「攻击两次」条件）。</summary>
    public static bool HasVulnerable(Creature? target)
    {
        try { return target != null && target.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.VulnerablePower>() > 0; }
        catch { return false; }
    }

    // ───────────────────── 「回响斩击」的斩杀链段数 ─────────────────────

    /// <summary>
    /// 「回响斩击」（<c>EchoingSlash</c>）这一手**实际会打几段**（用户 2026-11-04 要求：提前算出来、
    /// 并且随玩家这边的血量/格挡/奥斯提「动态改变」）。
    ///
    /// 反编译 `EchoingSlash.OnPlay`：
    /// <code>
    /// int attackCount = 1;
    /// while (attackCount &gt; 0) {
    ///     var results = await CreatureCmd.Damage(ctx, CombatState?.HittableEnemies,
    ///                                            DynamicVars.Damage, Owner.Creature, this, cardPlay);
    ///     ctx.AddHit(results);
    ///     attackCount += results.Count(r =&gt; r.WasTargetKilled);   // 每杀掉一个就**多打一整遍**
    ///     attackCount--;
    /// }
    /// </code>
    /// 所以要提前算段数，就得把「每一次挥击会杀死谁」推演出来。三件必须建模的事：
    ///   · **打谁**：只打**真人玩家**（她的群攻只打真人玩家 —— 见 `HittableEnemiesSidePatch.PlayerCharacters`
    ///     与 `BossAttackRealPlayersOnlyPatch`；玩家的奥斯提/召唤物不进这一手的目标列表）；
    ///   · **格挡**：反编译 `CreatureCmd.Damage` 里 `creature = originalTarget.PetOwner?.Creature ?? originalTarget;
    ///     var blockedDamage = creature.DamageBlockInternal(...)` ⇒ 打宠物时格挡取自**主人**；
    ///   · **为你而死**（`DieForYouPower`）：玩家的**未被格挡的攻击伤害**会改指向它那只宠物
    ///     （`if (!props.IsPoweredAttack()) return target;`），而 `DamageResult` 是拿**改派后的目标**
    ///     建的（`unblockedDamageTarget.LoseHpInternal(...)`）⇒ 宠物死 = `WasTargetKilled` 为真
    ///     ⇒ **照样多打一段**。用户 2026-11-04 明确要求把「替主人承伤而死的奥斯提」算进去。
    /// </summary>
    /// <param name="baseDamage">牌面伤害（已经到了「该不该算她力量」的那一步，与单段意图同一个值）。</param>
    public static int EchoingSlashHits(CardModel card, int baseDamage, Creature? localVictim = null)
    {
        try
        {
            var dealer = card?.Owner?.Creature;
            var combat = dealer?.CombatState ?? localVictim?.CombatState;
            if (dealer == null || combat == null) return 1;
            if (baseDamage <= 0) return 1;

            var rows = EchoRowsOf(card, dealer, combat, baseDamage, localVictim);
            if (rows.Count == 0) return 1;

            // ★ 2026-11-04（用户实测纠错）：「回响斩击**之前**那张攻击牌就把奥斯提打死了，所以并没有 2 段」
            //   —— 上面建的是**此刻**的状态，可意图是「本回合接下来会发生什么」：
            //   她同一回合排在回响斩击**前面**的攻击牌会先落地（例如 `STRIKE_SILENT` 先把玩家那只
            //   只剩几滴血的奥斯提打死）⇒ 轮到回响斩击时已经没有「替主人承伤」的宠物了 ⇒ 只能是 1 段。
            //   这里把这些**还没打出来的**先行伤害预扣到模拟状态上，再跑斩杀链。
            //   ⚠ 天然不会双算：`PlayPlannedCards` 打出一张就 `Plan.Remove(play)`，
            //   所以「已经打过的牌」不在 `Plan` 里，它们的伤害已经体现在真实血量上了。
            int prior = PriorPlannedAttackDamage(card, rows, dealer);
            if (prior > 0) ApplyLumpDamage(rows, prior);

            // 日志用的是**推演之前**的状态快照（以前打印的是跑完之后的值，
            // 「奥斯提=0」到底是一开始就没有、还是被这一手打死的就分不清了 —— 用户实测时被这一点误导过）
            string preState = DescribeRows(rows);

            // 完全照抄原版的循环：预算 1 次；每杀死一个 +1 次
            int hits = 0;
            int budget = 1;
            while (budget > 0 && hits < 20)          // 20 是显示上限，防止极端局面算到天荒地老
            {
                hits++;
                budget--;
                int kills = 0;
                foreach (var row in rows)
                {
                    if (row.Hp <= 0) continue;       // 已经死了：原版 `if (originalTarget.IsDead) continue;`
                    int dmg = row.PerHit;
                    if (dmg <= 0) continue;

                    // ① 先扣**格挡**。反编译 `CreatureCmd.Damage`：
                    //    `creature = originalTarget.PetOwner?.Creature ?? originalTarget;
                    //     var blockedDamage = creature.DamageBlockInternal(...)`
                    //    —— 打的是真人玩家时格挡就是他自己那份；改派到宠物是**扣完格挡之后**的事。
                    if (row.Block > 0)
                    {
                        int absorbed = Math.Min(row.Block, dmg);
                        row.Block -= absorbed;
                        dmg -= absorbed;
                    }
                    if (dmg <= 0) continue;          // 被格挡吃干净了：既不掉血，也不会死

                    // ② 再看有没有「为你而死」的宠物顶锅（它死 ⇒ 算一次击杀 ⇒ 多打一段）
                    if (row.PetHp > 0)
                    {
                        row.PetHp -= dmg;
                        if (row.PetHp <= 0) { row.PetHp = 0; kills++; }
                        continue;
                    }
                    // ③ 自己挨剩下的
                    row.Hp -= dmg;
                    if (row.Hp <= 0) kills++;
                }
                budget += kills;
            }
            int result = Math.Max(1, hits);
            LogEchoSim(card, baseDamage, preState, prior, result);
            return result;
        }
        catch { return 1; }
    }

    /// <summary>
    /// 回响斩击推演日志：意图数字本身进不了日志，所以把「每段多少、玩家血量/格挡、奥斯提血量、算出来几段」
    /// 原样打一行 —— 用户报「显示段数不对」时能直接对账。
    /// 推演参数（血量/格挡/宠物血量/每段伤害/段数）**没变就不打**，否则意图每帧重算会刷屏。
    /// </summary>
    private static readonly HashSet<string> EchoSimLogs = new(StringComparer.Ordinal);

    /// <summary>把模拟状态拼成一行（日志用）。</summary>
    private static string DescribeRows(List<EchoRow> rows)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var row in rows)
            sb.Append('[').Append("hp=").Append(row.Hp)
              .Append(" block=").Append(row.Block)
              .Append(" 奥斯提=").Append(row.PetHp)
              .Append(" 每段=").Append(row.PerHit).Append(']');
        return sb.ToString();
    }

    private static void LogEchoSim(CardModel card, int baseDamage, string state, int prior, int hits)
    {
        try
        {
            string key = baseDamage + "|" + prior + "|" + hits + "|" + state;
            if (!EchoSimLogs.Add(key)) return;
            if (EchoSimLogs.Count > 200) EchoSimLogs.Clear();
            Logger.Log($"[CharBoss] 回响斩击推演：牌面 {baseDamage}，" +
                       (prior > 0 ? $"先扣掉本回合前面那些牌 {prior} 点，" : string.Empty) +
                       $"{state} ⇒ 显示 {hits} 段");
        }
        catch { /* 诊断失败不影响显示 */ }
    }

    /// <summary>模拟用的一行状态（真人玩家本人 + 他那只「为你而死」的宠物）。</summary>
    private sealed class EchoRow
    {
        public int Hp;
        public int Block;
        public int PetHp;
        public int PerHit;
    }

    /// <summary>
    /// 本回合**排在 <paramref name="card"/> 前面、还没打出来**的那些计划牌，会对玩家侧造成多少伤害
    /// （取它们各自已经算好的攻击意图总伤相加）。
    ///
    /// 用途见调用处注释：回响斩击的段数取决于「轮到它时还有没有可杀的敌人」，
    /// 而前面那张攻击牌可能已经把「替主人承伤」的奥斯提打死了。
    /// 只读 <see cref="CharBossMonster.Plan"/>，不改变任何战斗数据。
    /// </summary>
    private static int PriorPlannedAttackDamage(CardModel card, List<EchoRow> rows, Creature dealer)
    {
        try
        {
            if (dealer?.Monster is not Monsters.CharBossMonster boss || boss.Plan == null) return 0;
            if (rows == null || rows.Count == 0) return 0;

            var targets = new List<Creature>();
            try
            {
                foreach (var c in dealer.CombatState?.PlayerCreatures ?? Array.Empty<Creature>())
                {
                    if (c == null || c.Monster != null || c.Player == null) continue;
                    bool pet = false;
                    try { pet = c.IsPet; } catch { /* 忽略 */ }
                    if (pet) continue;
                    targets.Add(c);
                }
            }
            catch { /* 忽略 */ }

            int total = 0;
            foreach (var play in boss.Plan)
            {
                if (play == null) continue;
                if (ReferenceEquals(play.Card, card)) break;      // 只看排在它前面的
                if (play.StampedeOnly) continue;                  // 惊逃是回合结束才打，不算先行伤害
                if (play.Intents == null) continue;
                foreach (var intent in play.Intents)
                {
                    if (intent == null) continue;
                    try
                    {
                        if (intent is MegaCrit.Sts2.Core.MonsterMoves.Intents.MultiAttackIntent multi)
                            total += Math.Max(0, multi.GetTotalDamage(targets, dealer));
                        else if (intent is MegaCrit.Sts2.Core.MonsterMoves.Intents.SingleAttackIntent single)
                            total += Math.Max(0, single.GetTotalDamage(targets, dealer));
                    }
                    catch { /* 单条算不出来就跳过 */ }
                }
            }
            return total;
        }
        catch { return 0; }
    }

    /// <summary>把一笔「先行伤害」按原版顺序扣到模拟状态上：格挡 → 「为你而死」的宠物 → 本人。</summary>
    private static void ApplyLumpDamage(List<EchoRow> rows, int amount)
    {
        if (amount <= 0 || rows == null) return;
        foreach (var row in rows)
        {
            if (row == null || row.Hp <= 0) continue;
            int dmg = amount;
            if (row.Block > 0)
            {
                int absorbed = Math.Min(row.Block, dmg);
                row.Block -= absorbed;
                dmg -= absorbed;
            }
            if (dmg <= 0) continue;
            if (row.PetHp > 0)
            {
                row.PetHp -= dmg;                 // 顶不住就归 0（待会儿跑斩杀链时它已经不在了）
                if (row.PetHp < 0) row.PetHp = 0;
                continue;
            }
            row.Hp -= dmg;
        }
    }

    /// <summary>按「这一手会打到谁」建立模拟状态（只认真人玩家；每人的每段伤害走各自的伤害管道）。</summary>
    private static List<EchoRow> EchoRowsOf(CardModel card, Creature dealer, ICombatState combat,
                                            int baseDamage, Creature? localVictim)
    {
        var rows = new List<EchoRow>();
        try
        {
            var victims = new List<Creature>();
            foreach (var c in combat.PlayerCreatures)
            {
                if (c == null || c.Monster != null || c.Player == null) continue;   // 宠物/召唤物跳过
                bool pet = false;
                try { pet = c.IsPet; } catch { /* 忽略 */ }
                if (pet) continue;
                if (!c.IsAlive || c.IsDead) continue;
                victims.Add(c);
            }
            // 一人玩家都没有（极端局面，例如本地玩家已经倒下）→ 退回意图节点给的那个目标
            if (victims.Count == 0 && localVictim != null && localVictim.Monster == null) victims.Add(localVictim);

            foreach (var victim in victims)
            {
                rows.Add(new EchoRow
                {
                    Hp = victim.CurrentHp,
                    Block = victim.Block,
                    PetHp = DieForYouPetHp(victim),
                    PerHit = FinalDamage(card, baseDamage, victim),
                });
            }
        }
        catch { /* 算不出来就当没有 */ }
        _ = dealer;
        return rows;
    }

    /// <summary>这名玩家身上「为你而死」宠物的可承受生命（没有就是 0）。</summary>
    private static int DieForYouPetHp(Creature victim)
    {
        try
        {
            foreach (var pet in victim.Pets)
            {
                if (pet == null || !pet.IsAlive) continue;
                if (!pet.HasPower<MegaCrit.Sts2.Core.Models.Powers.DieForYouPower>()) continue;
                return Math.Max(0, pet.CurrentHp);
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    /// <summary>
    /// 「重放」：<c>CardModel.BaseReplayCount</c>（+1 = 这张牌会打出两次）。
    /// 原版结算时是 <c>GetEnchantedReplayCount() + 1</c> 次，每次再按 <c>Repeat</c> 段算命中；
    /// 早先意图只读 <c>Repeat</c> ⇒ 铸剑储君打掉剑圣后，君王之剑仍显示 1 段（用户实测要求改成 2 段）。
    /// </summary>
    private static int ReplayMultiplier(CardModel card)
    {
        try { return 1 + Math.Max(0, card.GetEnchantedReplayCount()); }
        catch { return 1; }
    }

    /// <summary>走预览通道读某个动态变量的值（前提：预览会把它算出来）。</summary>
    private static decimal PreviewVar(CardModel card, Creature? target, string key, decimal fallback)
    {
        var vars = card.DynamicVars;
        try
        {
            vars.ClearPreview();
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, target, vars);
            if (vars.TryGetValue(key, out var v) && v != null)
            {
                decimal value = v.PreviewValue;
                if (value <= 0m) value = v.BaseValue;
                return value;
            }
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 命中数预览失败（{card.Id.Entry}/{key}）: {e.Message}");
        }
        finally
        {
            try { vars.ClearPreview(); } catch { /* 忽略 */ }
        }
        return fallback;
    }

    /// <summary>
    /// 原版的「小数伤害 → 整数」口径：**向下取整**（对正数＝截断），不是四舍五入。
    ///
    /// 两处实证（反编译 `sts2.dll`）：
    ///   · 意图显示的 <c>AttackIntent.GetSingleDamage</c> 结尾就是 `return Math.Max(0, (int)num);`；
    ///   · 实际扣血的 <c>Creature.LoseHpInternal</c> 是 `int num = (int)Math.Clamp(amount, 0m, …);`。
    /// ⇒ 33 × 0.5 = 16.5 显示并实扣 **16**；11.5 是 **11**（用户 2026-10-27 指出，之前这里用了
    /// <c>Math.Round</c>，会在 .5 的情况下比原版多 1 点，属于我们自己的显示错误）。
    /// </summary>
    private static int FloorToInt(decimal value) => (int)Math.Floor(value);

    /// <summary>对某个目标的**单次**伤害。注意返回的是**未被力量/易伤等加成的基础值** ——
    /// 因为意图节点自己会再过一次 <c>Hook.ModifyDamage</c>（见下）。
    /// </summary>
    public static int HitDamage(CardModel card, Creature? target)
        => Math.Max(0, FloorToInt(ReadBaseDamage(card, target)));

    /// <summary>
    /// 读「未经全局钩子加成」的伤害。
    ///
    /// ⚠ 这一条是 R31 的核心修正。原版 <c>AttackIntent.GetSingleDamage</c> 的原文是：
    ///
    /// <code>
    /// decimal num = DamageCalc.Invoke();
    /// Player me = LocalContext.GetMe(owner.CombatState);
    /// if (me != null)
    ///     num = Hook.ModifyDamage(me.RunState, combatState, me.Creature, owner, DamageCalc.Invoke(),
    ///                             ValueProp.Move, null, null, ModifyDamageHookType.All, CardPreviewMode.None, out _);
    /// return Math.Max(0, (int)num);
    /// </code>
    ///
    /// 也就是说**意图节点会在我们给的数字上再跑一次力量/易伤等修正**（target = 玩家、dealer = Boss）。
    /// 早先我们把「已经加成过的 PreviewValue」喂进去 → 力量被算了两遍；激发意图那边
    /// 又因为 <c>EvokeVal</c> 自带集中、再被加一次力量，于是看起来「力量和集中都在加成」。
    ///
    /// 现在改成：喂基础值，让原版意图节点只加一次。取基础值的办法是
    /// <c>DynamicVar.UpdateCardPreview(..., runGlobalHooks: false)</c> —— 它和
    /// <c>card.UpdateDynamicVarPreview</c> 唯一的区别就是不跑 <c>Hook.ModifyDamage</c>，
    /// 但「欺凌」这类算出来的伤害照样会算完。
    /// </summary>
    /// <summary>
    /// 读某个 DynamicVar 的**升级后**数值（走和卡面预览同一条管线）。
    ///
    /// 为什么不能直接读 `BaseValue`（用户 2026-11-02：「进阶 9~10 升级预备打击，
    /// 预备打击+ 会提供 4 点临时力量而非 3 点」）：`BaseValue` 是**未套用升级**的基数，
    /// 升级值只有预览管线跑过之后才在 `PreviewValue` 里 ⇒ 规划期读 BaseValue 会少算 1。
    /// </summary>
    public static int VarPreviewInt(CardModel? card, string key, int fallback)
    {
        if (card == null) return fallback;
        var vars = card.DynamicVars;
        try
        {
            vars.ClearPreview();
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, null, vars);
            foreach (var v in vars.Values)
            {
                try { v.UpdateCardPreview(card, CardPreviewMode.Normal, null, false); }
                catch { /* 单个变量算不出来就跳过 */ }
            }
            if (vars.TryGetValue(key, out var target) && target != null)
            {
                int value = (int)Math.Round((double)target.PreviewValue);
                if (value > 0) return value;
            }
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 读变量失败（{key}）：{e.Message}");
        }
        finally
        {
            try { vars.ClearPreview(); } catch { /* 忽略 */ }
        }
        return fallback;
    }
    private static decimal ReadBaseDamage(CardModel card, Creature? target)
    {
        var vars = card.DynamicVars;
        try
        {
            // 先走一遍完整预览，保证 CalculatedDamage / Enchantment 这些按卡牌状态算的值被填好
            vars.ClearPreview();
            card.UpdateDynamicVarPreview(CardPreviewMode.Normal, target, vars);
            // 再把每个变量按「不跑全局钩子」重算一次 → PreviewValue 回到未加成的基础值
            foreach (var v in vars.Values)
            {
                try { v.UpdateCardPreview(card, CardPreviewMode.Normal, target, false); }
                catch { /* 单个变量算不出来就跳过 */ }
            }

            decimal best = 0m;
            foreach (var v in vars.Values)
            {
                if (!v.GetType().Name.Contains("Damage")) continue;
                if (v.PreviewValue > best) best = v.PreviewValue;
            }
            return best;
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 基础伤害失败（{card.Id.Entry}），退回变量直读: {e.Message}");
            return VarValue(card, "Damage", VarValue(card, "CalculatedDamage", 0));
        }
        finally
        {
            // 别把预览值留在卡上，卡面自己会重新算
            try { vars.ClearPreview(); } catch { /* 忽略 */ }
        }
    }

    /// <summary>
    /// 奥斯提牌（「出击」这类）的**实际**伤害。
    ///
    /// 原版这类牌用的不是 <c>DynamicVars.Damage</c>，而是 <c>CalculatedDamage</c>，它走
    /// <c>CalculatedVar.Calculate</c>：
    /// <code>
    /// GetBaseVar().BaseValue + GetExtraVar().BaseValue * multiplier(card, target)
    ///     // 出击：GetBaseVar() = CalculationBase(6，升级 +3)、GetExtraVar() = ExtraDamage(1)、
    ///     //       multiplier = 奥斯提当前生命（奥斯提不在时 0）
    /// </code>
    /// 而预览通道**算不出 multiplier**（`Calculate` 里遇到「战斗未开始 / 模版卡」时直接返回 0），
    /// 于是意图只显示基础值 —— 用户实测：显示 6、实际打 7（奥斯提 1 血）；
    /// 显示 9、实际打 17（奥斯提 8 血）。这里按同一个公式重算。
    /// </summary>
    public static int OstyScaledDamage(CardModel card)
    {
        try
        {
            var osty = card.Owner?.Osty;
            int hp = (osty != null && osty.IsAlive) ? osty.CurrentHp : 0;

            decimal calculationBase = VarValue(card, "CalculationBase", VarValue(card, "Damage", 0));
            decimal extra = VarValue(card, "ExtraDamage", 0);
            // 钙化（CalcifyPower）挂在她自己身上：原版 <c>CalcifyPower.ModifyDamageAdditive</c> 直接返回
            // <c>Amount</c>，也就是**奥斯提的每次攻击伤害 +N**（用户订正：给奥斯提增伤的是钙化）。
            // 「出击」走 Raw 意图不吃 Hook.ModifyDamage ⇒ 必须自己加，否则显示的就是低估的数字（用户实测）。
            decimal calcify = 0m;
            try
            {
                calcify = card.Owner?.Creature?
                    .GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.CalcifyPower>() ?? 0;
            }
            catch { calcify = 0m; }
            int value = FloorToInt(calculationBase + extra * hp + calcify);
            // 诊断：奥斯提伤害是"显示低于实际"的老问题（用户实测报过），把输入摊开记一行，
            // 下次不一致就能直接看出是 HP 取错还是公式不对。
            try
            {
                Logger.Log($"[CharBoss] 奥斯提伤害预览 {card.Id.Entry}: base={calculationBase} extra={extra} " +
                           $"ostyHp={hp} calcify={calcify} → {value}");
            }
            catch { /* 忽略 */ }
            if (value > 0) return value;
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 奥斯提伤害重算失败（{card.Id.Entry}），退回基础值: {e.Message}");
        }
        return HitDamage(card, null);
    }

    /// <summary>
    /// 这张牌打出后会给**奥斯提加多少生命**。
    ///
    /// 「出击」（<c>Unleash</c>）的伤害 = `CalculationBase + ExtraDamage × 奥斯提当前生命`，
    /// 而 `OstyScaledDamage` 读的是**真实**当前生命 ⇒ 规划期/她的回合开始时，
    /// 这回合前面那些「召唤」还没发生，预览就会偏低。用户 2026-10-23 要求：
    /// **出击要提前算上「吞噬生命」和「灵魂」给奥斯提带来的生命**。
    ///
    /// 两类来源（都反编译确认过）：
    ///   1. **召唤牌**：`Summon` 变量（增生 3、死者苏生 20、骸骨军团 6/8 …）。
    ///      `OstyCmd.Summon`：已有奥斯提 → `CreatureCmd.GainMaxHp(osty, amount)`（最大生命与当前生命同涨）；
    ///      没有 → `SetMaxHp(osty, amount) + Heal(osty, amount)` ⇒ 生命就等于召唤值。
    ///   2. **「灵魂」× 吞噬生命**（<c>DevourLifePower</c>）：
    ///      `AfterCardPlayed`：`cardPlay.Card is Soul &amp;&amp; cardPlay.Card.Owner.Creature == Owner`
    ///      → `OstyCmd.Summon(ctx, owner, power.Amount, power)` ⇒ 每打一张灵魂 +`Amount` 生命。
    /// </summary>
    /// <summary>旧写法里可能出现的「奥斯提生命」变量名（`OstyHpGainOf` 的兜底）。</summary>
    private static readonly string[] OstyHpVarKeys = { "OstyMaxHp", "OstyHp", "OstyHealth", "OstyLife" };

    public static int OstyHpGainOf(CardModel card, Monsters.CharBossMonster? boss)
        => OstyHpGainOf(card, DevourAmountOf(boss));

    /// <summary>她身上「吞噬生命」当前的召唤值（每打一张灵魂给奥斯提加多少生命）；没有就是 0。</summary>
    public static int DevourAmountOf(Monsters.CharBossMonster? boss)
    {
        try
        {
            var creature = boss?.Creature;
            if (creature == null) return 0;
            return Math.Max(0, creature.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.DevourLifePower>());
        }
        catch { return 0; }
    }

    /// <summary>
    /// 「吞噬生命」这张牌本身会给多少（= 打出去之后 `DevourLifePower` 的层数）。
    /// 规划期这张牌还没打出去、能力也还没上身，所以只能读牌面：`PowerVar&lt;DevourLifePower&gt;`
    /// 的 key 是类型名去掉 `Power`（和 `Shame` 的 `DynamicVars["Frail"]` 同一套规则），
    /// 读不到就退回「未升级 1 / 升级 2」。
    /// </summary>
    public static int DevourLifeCardAmount(CardModel card)
    {
        try
        {
            foreach (var key in new[] { "DevourLife", "DevourLifePower" })
            {
                int v = (int)Math.Round(VarValue(card, key, 0m));
                if (v > 0) return v;
            }
            bool upgraded;
            try { upgraded = card.IsUpgraded; } catch { upgraded = false; }
            return upgraded ? 2 : 1;
        }
        catch { return 1; }
    }

    /// <param name="devourAmount">「吞噬生命」当前的召唤值（打一张灵魂给奥斯提加多少生命）。</param>
    public static int OstyHpGainOf(CardModel card, int devourAmount)
    {
        try
        {
            // ① 召唤牌
            int summon = (int)Math.Round(VarValue(card, "Summon", 0m));
            if (summon > 0) return summon;

            // ①b 兼容旧写法：有些牌用的是 Osty* 这类变量名
            foreach (var key in OstyHpVarKeys)
            {
                int v = (int)Math.Round(VarValue(card, key, 0m));
                if (v > 0) return v;
            }

            // ② 灵魂 + 吞噬生命
            string entry;
            try { entry = card.Id.Entry; } catch { return 0; }
            if (entry != "SOUL") return 0;

            return Math.Max(0, devourAmount);
        }
        catch { return 0; }
    }

    /// <summary>
    /// 这张牌打出后**给她加多少力量**（正数才算；减力量的牌返回 0）。
    ///
    /// 用途（用户 2026-10-23）：一层愤怒战第 7/9 回合是「**烙印**（0 费，失去1生命、消耗1张牌、
    /// **获得 1 点力量**）→ 愤怒+ → 痛击…」，烙印之后那些攻击的**伤害预览**要先把这 +1 力量算进去，
    /// 等烙印真打出去、力量到账后再按同样数量撤回（不给的话预览会偏低）。
    ///
    /// 读法：力量牌用的是 `PowerVar&lt;StrengthPower&gt;`，其 DynamicVar key 是类型名去掉 `Power`
    /// （`Brand` / `Inflame` 反编译确认都是 `new PowerVar&lt;StrengthPower&gt;(n)` ⇒ key = `Strength`）。
    /// </summary>
    public static int StrengthGainOf(CardModel card)
    {
        try
        {
            // ⚠ 「撕裂」（Rupture）的 `Strength` 变量**不是力量**，而是 `RupturePower` 的层数
            //（反编译：`PowerCmd.Apply<RupturePower>(…, DynamicVars.Strength.BaseValue, …)`）。
            //   当成 +1 力量会让它后面的攻击预览凭空多 1（用户 2026-10-28 报的偏差之一）。
            //   它真正的力量来自「以后每次失去生命」——那部分由
            //   <see cref="LosesHp"/> × 撕裂层数在 `BladesGrowth` 里记账。
            if (string.Equals(EntryOf(card), "RUPTURE", StringComparison.Ordinal)) return 0;

            foreach (var key in StrengthVarKeys)
            {
                int v = (int)Math.Round(VarValue(card, key, 0m));
                if (v > 0) return v;
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    /// <summary>
    /// 这张牌打出后会让**本回合后续攻击**多吃多少力量 —— <see cref="StrengthGainOf"/> 的扩展版。
    ///
    /// ★ v0.2.357（用户 2026-11-04：「伤害预览没有计算它通过把弃牌堆的牌返回手牌、由特殊效果获得的力量」）：
    ///   三层「事件机」的特殊效果「一心化万」= 万物一心每返手一张牌 ⇒ **所有单位** +1 力量。
    ///   这份力量**不在牌的任何 DynamicVar 里**（`ALL_FOR_ONE` 的卡面只有 `Damage`），
    ///   所以 `StrengthGainOf` 读不到、规划器另外硬编码了 `entry.ReturnsToHand.Length`。
    ///   但**出牌期的重算**（`RefreshPlannedIntents`）只认 `StrengthGainOf` ⇒ 她回合一开始重算，
    ///   这份预补就被抹掉了（有球 Boss 的路径尤其明显）⇒ 预览比实伤低。
    ///   这里把它并进同一个入口，规划期/重算期/撤回期三处用同一把尺子。
    /// </summary>
    public static int StrengthGainOfPlanned(CardModel card, Monsters.CharBossMonster? boss)
    {
        int gain = StrengthGainOf(card);
        gain += DiscardReturnStrengthOf(card, boss);
        return gain;
    }

    /// <summary>
    /// 这张牌会让「一心化万」给所有单位加多少力量
    /// （= 它会把几张牌从**弃牌堆**搬回手牌）。
    ///
    /// ★ v0.2.359（用户 2026-11-04：「特殊效果一心化万会在本回合提供的力量好像还是没被提前计算」）：
    ///   以前只算了「万物一心」那 2 张（`ReturnsToHand`），漏了**全息影像** ——
    ///   它同样会从弃牌堆捞 1 张 ⇒ 同样触发 +1 力量。第 7 回合的剧本正是
    ///   `疯狂科学 → 全息影像 → 冰之长枪 → 双重释放`，所以那两张的攻击预览一直少 1 点。
    ///   （全息影像的**搬运本身**已经被 `Patches/HologramNoReturnPatch` 拦掉，但力量照给。）
    /// </summary>
    public static int DiscardReturnStrengthOf(CardModel? card, Monsters.CharBossMonster? boss)
    {
        if (card == null) return 0;
        try
        {
            string entry = EntryOf(card);
            if (string.Equals(entry, "ALL_FOR_ONE", StringComparison.Ordinal))
                return AllForOneReturnCount(card, boss);
            if (string.Equals(entry, "HOLOGRAM", StringComparison.Ordinal))
                return 1;      // 全息影像：从弃牌堆捞 1 张
        }
        catch { /* 忽略 */ }
        return 0;
    }

    /// <summary>「万物一心」在**这一回合的剧本**里点名要返手几张牌（= 给所有单位几点力量）。</summary>
    public static int AllForOneReturnCount(CardModel? card, Monsters.CharBossMonster? boss)
    {
        if (card == null || boss == null) return 0;
        try
        {
            var entry = BossScriptRunner.FindEntry(boss, card);
            return entry?.ReturnsToHand?.Length ?? 0;
        }
        catch { return 0; }
    }

    /// <summary>
    /// 这张牌会不会让**自己**失去生命（`HpLoss` 变量 &gt; 0）：血墙 / 御血术 / 突破 / 烙印…
    /// 反编译确认这几张都是「**先**扣自己的血、**再**打伤害」⇒ 撕裂带来的力量连**它自己**那一下也算得上。
    /// </summary>
    public static bool LosesHp(CardModel card)
    {
        try { return VarValue(card, "HpLoss", 0m) > 0m; }
        catch { return false; }
    }

    /// <summary>
    /// 「撕裂」这张牌会给几层 `RupturePower`（层数 = 每次失去生命涨多少力量）。
    /// 反编译：`new PowerVar&lt;StrengthPower&gt;(1m)` 但 apply 的是 `RupturePower`，取升级后的牌面值。
    /// ⚠ key 两个都要试（`PowerVar&lt;StrengthPower&gt;` 的默认名在 0.111.0 上是 `StrengthPower`，
    ///   早期版本是 `Strength`）—— 只试一个的话规划期会读到 0，肌肉记忆里的「撕裂+ = 2 力量」就丢了。
    /// </summary>
    public static int RuptureAmountOf(CardModel card)
    {
        try
        {
            if (!string.Equals(EntryOf(card), "RUPTURE", StringComparison.Ordinal)) return 0;
            foreach (var key in RuptureVarKeys)
            {
                int v = (int)Math.Round(VarValue(card, key, 0m));
                if (v > 0) return v;
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    /// <summary>`PowerVar&lt;StrengthPower&gt;` 可能落成的 key：新旧版本不同，全试一遍。</summary>
    private static readonly string[] RuptureVarKeys = { "Strength", "StrengthPower", "Rupture", "RupturePower" };

    /// <summary>牌的安全 entry（读不到返回空串）。</summary>
    public static string EntryOf(CardModel? card)
    {
        try { return card?.Id.Entry ?? string.Empty; }
        catch { return string.Empty; }
    }

    /// <summary>力量变量可能的名字（`PowerVar&lt;StrengthPower&gt;` 的 key）。</summary>
    private static readonly string[] StrengthVarKeys = { "Strength", "StrengthPower" };

    /// <summary>
    /// 这张牌会给**集中**带来多少变化（正数 = 本回合获得，负数 = 本回合失去）；没有这类变量返回 0。
    ///
    /// 用途：手牌栏那条「本回合充能球被动会造成的伤害」——球的 `PassiveVal` 含集中，
    /// 而本回合**还没打出去**的「热修复」（+2 集中本回合）/「超能光束」（−3 集中本回合）
    /// 会改变回合结束那一刻的被动伤害（用户 2026-10-24 实测：第 3 回合预告 8，实际 12，
    /// 差的正是热修复那 2 点 × 2 个闪电球）。
    ///
    /// ⚠ **正负不能看变量值**：热修复和超能光束的变量都是 `PowerVar&lt;FocusPower&gt;(正数)`，
    /// 真正加减集中由它们施加的 `TemporaryFocusPower` 子类决定
    /// （`HotfixPower` = 增益 +2，`HyperbeamFocusDownPower` = 减益 −3）⇒ 减集中这张按 entry 认。
    /// </summary>
    public static int FocusDeltaOf(CardModel card)
    {
        int amount = RawFocusVarOf(card);
        if (amount <= 0) return 0;

        string entry = SafeEntryOf(card);
        if (entry == "HYPERBEAM") return -amount;   // 超能光束：本回合失去 amount 点集中
        return amount;                              // 热修复 / 碎片整理这类：本回合或永久获得
    }

    /// <summary>牌面上「集中」那个变量的数值（不分正负）。</summary>
    private static int RawFocusVarOf(CardModel card)
    {
        try
        {
            foreach (var v in card.DynamicVars.Values)
            {
                if (v == null) continue;
                var type = v.GetType();
                if (!type.IsGenericType) continue;
                if (type.GetGenericTypeDefinition() != typeof(PowerVar<>)) continue;
                if (type.GetGenericArguments()[0] != typeof(MegaCrit.Sts2.Core.Models.Powers.FocusPower)) continue;
                return Math.Abs((int)Math.Round((double)v.BaseValue));
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    /// <summary>这张牌有没有某个动态变量（判断要不要走奥斯提公式）。</summary>
    public static bool HasVar(CardModel card, string key)
    {
        try
        {
            return card.DynamicVars.TryGetValue(key, out var v) && v != null;
        }
        catch { return false; }
    }

    /// <summary>
    /// 「精确切击」（<c>PreciseCut</c>）打出那一刻的真实伤害。
    ///
    /// 原版反编译（`PreciseCut.CanonicalVars`）：
    /// <code>
    /// new CalculationBaseVar(13m),
    /// new ExtraDamageVar(2m),
    /// new CalculatedDamageVar(ValueProp.Move).WithMultiplier((card, _) =&gt; {
    ///     int num = Hand.Cards.Count;
    ///     if (card 自己在手牌里) num--;          // ⇒ num = 手牌里的**其他**牌数
    ///     return -num;                            // ⇒ 伤害 = 13 + 2 × (−num) = 13 − 2×num
    /// })
    /// </code>
    /// 所以「这张牌打多少」完全取决于**打出那一刻**的手牌数 —— 而意图是在玩家回合一次算死的，
    /// 那时手牌还是满的 ⇒ 意图一直显示得很低（用户 2026-10-26 要求按真实手牌数提前算）。
    /// </summary>
    /// <param name="handCountAtPlay">打出这张牌时**手里一共有几张牌**（含它自己）。</param>
    public static int PreciseCutDamage(CardModel card, int handCountAtPlay)
    {
        try
        {
            decimal baseVar = VarValue(card, "CalculationBase", 13m);
            decimal extra = VarValue(card, "ExtraDamage", 2m);
            int others = Math.Max(0, handCountAtPlay - 1);
            int value = FloorToInt(baseVar - extra * others);
            return Math.Max(0, value);
        }
        catch { return Math.Max(0, FloorToInt(VarValue(card, "CalculatedDamage", 0m))); }
    }

    /// <summary>
    /// 这张牌是否有伤害（用于决定要不要显示攻势意图）。
    /// 只看**基础**伤害就够了：加成只会在基础值为 0 时仍然为 0（力量再高也救不了 0 伤牌）。
    /// </summary>
    public static bool DealsDamage(CardModel card) => HitDamage(card, null) > 0;

    /// <summary>
    /// 「铭记死亡」（<c>MementoMori</c>）打出那一刻的真实伤害。
    ///
    /// 原版反编译（`MementoMori.CanonicalVars`）：
    /// <code>
    /// new CalculationBaseVar(9m),          // 升级 +2
    /// new ExtraDamageVar(4m),              // 升级 +1
    /// new CalculatedDamageVar(Move).WithMultiplier((card, _) =&gt;
    ///     History.OfType&lt;CardDiscardedEntry&gt;().Count(e =&gt; e.HappenedThisTurn(...) &amp;&amp; e.Actor == card.Owner.Creature))
    /// </code>
    /// 也就是**伤害 = 9 + 4 × 本回合她已丢弃的张数**（升级后 11 + 5 × N）。
    ///
    /// 为什么不能靠预览通道：意图是在**玩家回合**一次算死的，那时她一张牌都还没弃 ⇒ 倍率恒为 0，
    /// 于是「第 6 回合弃了 2 张、实际 17」而意图只显示 9。现在由 <c>BladesGrowth</c> 沿计划
    /// 累加「本回合会弃几张」传进来（一层「奇巧猎」的第 5/6 回合都吃这个）。
    /// </summary>
    /// <param name="discardsThisTurn">打出这张牌之前，她本回合已经（将会）丢弃的张数。</param>
    public static int MementoMoriDamage(CardModel card, int discardsThisTurn)
    {
        try
        {
            decimal baseVar = VarValue(card, "CalculationBase", 9m);
            decimal extra = VarValue(card, "ExtraDamage", 4m);
            int value = FloorToInt(baseVar + extra * Math.Max(0, discardsThisTurn));
            return Math.Max(0, value);
        }
        catch { return Math.Max(0, FloorToInt(VarValue(card, "CalculatedDamage", 0m))); }
    }

    /// <summary>
    /// 把「牌面伤害」过一遍**原版伤害管道** <c>Hook.ModifyDamage</c>，得到最终每段伤害。
    ///
    /// 为什么必须走它（用户 2026-10-23 根除骨截图：玩家用崩坠「哀嚎」减了她 11 点力量，
    /// 意图还是显示 10×4，应该 0×4）：
    ///   · Raw 意图（<see cref="RawAttackIntent"/> / <see cref="RawMultiAttackIntent"/>）
    ///     **不吃**意图节点自己的 <c>Hook.ModifyDamage</c>，所以以前在 <c>BossIntentBuilder</c> 里
    ///     手算「牌面值 + `StrengthPower` 层数」；
    ///   · 手算只看得见**原版力量**。崩坠「哀嚎」（`Collector...CursedWail`）挂的是
    ///     `CursedWailPower : TemporaryDebuffPowerWrapper&lt;CursedWail, StrengthPower&gt;`
    ///     （「本回合失去 N 点力量」），`StrengthPower` 的层数根本不变 ⇒ 手算永远是 0。
    /// 原版 <c>AttackIntent.GetSingleDamage</c> 的做法就是拿基础值跑
    /// <c>Hook.ModifyDamage(..., ModifyDamageHookType.All, ...)</c>，于是**所有** power
    /// （原版的、别的 mod 的、永久的、临时的）的 ModifyDamageAdditive/Multiplicative 都会算进来。
    /// 这里照抄同一套：dealer = 出牌的生物（她），target = 被打的那位玩家。
    /// </summary>
    public static int FinalDamage(CardModel card, int baseDamage, Creature? target)
        => FinalDamageFor(card?.Owner?.Creature, card, baseDamage, target);

    /// <summary>
    /// 同上，但**显式指定 dealer / cardSource**（可以没有 cardSource）。
    ///
    /// 用途：小刀那一路（`BossIntentBuilder.ShivDamagePerHit`）既要吃她的力量/虚弱、也要吃**目标易伤**，
    /// 而它手上不一定有个真的小刀卡实例（`combat.CreateCard&lt;Shiv&gt;()` 可能拿不到）
    /// —— 那就没法再走 `FinalDamage(card, …)` 那条「dealer 从 card.Owner 推」的路。
    ///
    /// ★ v0.2.365（用户 2026-11-04：「玩家有易伤，刀刃之舞应该显示 6×3 而非 4×3」）：
    ///   以前小刀只要拿不到卡实例就**直接返回牌面值**（4）⇒ 易伤、力量、虚弱全丢。
    /// </summary>
    public static int FinalDamageFor(Creature? dealer, CardModel? cardSource, int baseDamage, Creature? target)
    {
        if (baseDamage <= 0) return 0;
        try
        {
            var combat = dealer?.CombatState ?? target?.CombatState ?? cardSource?.Owner?.Creature?.CombatState;
            if (dealer == null || combat == null) return baseDamage;

            var run = combat.RunState;
            if (run == null) return baseDamage;

            // 意图的目标是「本机玩家」；拿不到就退回本机玩家，再拿不到就当打她自己（只为把力量算进来）
            var victim = target
                         ?? MegaCrit.Sts2.Core.Context.LocalContext.GetMe(combat)?.Creature
                         ?? dealer;

            decimal value = CharacterBosses.Core.GameCompat.ModifyDamage(run, combat, victim, dealer, baseDamage,
                ValueProp.Move, cardSource,
                ModifyDamageHookType.All, CardPreviewMode.None, out _);
            int result = Math.Max(0, FloorToInt(value));

            string entry = cardSource != null ? SafeEntryOf(cardSource) : "(无卡源)";
            if (result != baseDamage && LogDamagePipeline(cardSource, baseDamage, result))
            {
                // 把「到底是谁在改这个数字」摊开记一行 —— 以后再有「预览和实伤不一致」的报告，
                // 一眼就能看出是虚弱(×0.75)、摧残(DEBILITATE：虚弱/易伤效果翻倍 ⇒ ×0.5)、
                // 还是易伤(×1.5)/力量在起作用（用户 2026-10-27 的彗星 33→16 就是 虚弱+摧残）。
                Logger.Log($"[CharBoss] 意图伤害管道 {entry}: 牌面 {baseDamage} → {result}" +
                           $"（力量={StrengthOf(dealer)}，她虚弱={PowerOf(dealer, "WEAK_POWER")}，" +
                           $"她摧残={PowerOf(dealer, "DEBILITATE_POWER")}，" +
                           $"目标易伤={PowerOf(victim, "VULNERABLE_POWER")}，目标={SafeName(victim)}）");
            }
            return result;
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 意图伤害管道失败: {e.Message}");
            return baseDamage;
        }
    }

    /// <summary>
    /// **只跑原版伤害管道的「上限」阶段**（<c>ModifyDamageHookType.Cap</c>），也就是
    /// <c>min(牌面/已估算的每段伤害, 所有 ModifyDamageCap)</c>。
    ///
    /// 为什么单独有这个函数（用户 2026-10-24 截图：玩家身上有「无实体」，奥斯提牌的攻击仍显示 17）：
    ///   · 奥斯提的牌（戳击 / 重压 / 出击 / 骨碎片）在我们这里走 **Raw 意图**，
    ///     Raw 意图**不吃**原版意图节点那句 <c>Hook.ModifyDamage</c>（见 <see cref="FinalDamage"/> 的注释），
    ///     所以任何「上限」都不会自己生效；
    ///   · 反编译 <c>IntangiblePower</c>（无实体）：
    ///     <code>
    ///     public override decimal ModifyDamageCap(target, props, dealer, cardSource, cardPlay)
    ///         => target != Owner ? decimal.MaxValue : 1m;        // 打到她自己身上 → 1
    ///     public override decimal ModifyHpLostAfterOsty(target, amount, props, dealer, cardSource)
    ///         => target != Owner || amount &lt; 1m ? amount : 1m;     // 奥斯提出手的那条路也是 1
    ///     </code>
    ///     —— 也就是「无实体」下奥斯提的每段攻击真实只有 1 点，预览必须也是 1；
    ///   · **只跑 Cap 阶段**是有意的：Additive/Multiplicative 我们不跑，因为
    ///     钙化（<c>CalcifyPower</c>）已经由 <see cref="BossIntentBuilder"/> 手工加过一次，
    ///     再跑一遍加法就会高估（奥斯提牌也不吃她的力量）。
    /// </summary>
    public static int CapDamage(CardModel card, int baseDamage, Creature? target)
    {
        if (baseDamage <= 0) return 0;
        try
        {
            var dealer = card.Owner?.Creature;
            var combat = dealer?.CombatState ?? target?.CombatState;
            var run = combat?.RunState;
            if (combat == null || run == null) return baseDamage;

            var victim = target
                         ?? MegaCrit.Sts2.Core.Context.LocalContext.GetMe(combat)?.Creature
                         ?? dealer;

            decimal value = CharacterBosses.Core.GameCompat.ModifyDamage(run, combat, victim, dealer, baseDamage,
                ValueProp.Move, card,
                ModifyDamageHookType.Cap, CardPreviewMode.None, out _);
            int result = Math.Max(0, FloorToInt(value));

            if (result != baseDamage && _capLogs < 20)
            {
                _capLogs++;
                Logger.Log($"[CharBoss] 意图伤害上限 {SafeEntryOf(card)}: {baseDamage} → {result}" +
                           $"（目标={SafeName(victim)}，例如无实体=1）");
            }
            return result;
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 意图伤害上限失败（{SafeEntryOf(card)}）: {e.Message}");
            return baseDamage;
        }
    }

    /// <summary>
    /// **奥斯提的牌**（戳击 / 重压 / 出击 / 击掌 …）打出那一刻的真实每段伤害。
    ///
    /// ★ v0.2.362（用户 2026-11-04：「出击…没有计算易伤或致死性（伤害被大幅低估了）」）：
    ///   这批牌走 `<c>AttackIntent.Raw</c>`，**完全不经过** <c>Hook.ModifyDamage</c>
    ///   ⇒ 目标身上的**易伤 ×1.5** 以前一点都没算（以前只跑 <see cref="CapDamage"/> 的 Cap 阶段，
    ///   那只是为了「无实体 = 1」）。
    ///
    /// 做法：把原版伤害管道**整条**跑一遍，但把 **dealer 换成奥斯提本人**
    /// （<c>card.Owner.Osty</c>；没有随从就退回她自己）——
    ///   · 目标侧的修正（易伤、摧残、无实体上限…）照常生效；
    ///   · 她自己的力量 / 虚弱 / 致死性**不会**误加进来（那些 power 都挂在**她**身上，
    ///     而它们的 `ModifyDamageMultiplicative` 判的是 `dealer == Owner`）；
    ///   · `cardSource` 传 **null** ⇒ 「一心化万」这类按牌判定的增伤不会重复加，
    ///     致死性也不会在这里被乘第二次（它由 `BossIntentBuilder` 按「本回合第一张攻击牌」手工乘一次）。
    /// </summary>
    public static int OstyFinalDamage(CardModel card, int baseDamage, Creature? target)
    {
        if (baseDamage <= 0) return 0;
        try
        {
            var dealer = card.Owner?.Osty ?? card.Owner?.Creature;
            var combat = dealer?.CombatState ?? target?.CombatState ?? card.Owner?.Creature?.CombatState;
            var run = combat?.RunState;
            if (dealer == null || combat == null || run == null) return baseDamage;

            var victim = target
                         ?? MegaCrit.Sts2.Core.Context.LocalContext.GetMe(combat)?.Creature
                         ?? dealer;

            decimal value = CharacterBosses.Core.GameCompat.ModifyDamage(run, combat, victim, dealer, baseDamage,
                ValueProp.Move, null,
                ModifyDamageHookType.All, CardPreviewMode.None, out _);
            int result = Math.Max(0, FloorToInt(value));

            if (result != baseDamage && _ostyLogs++ < 30)
            {
                Logger.Log($"[CharBoss] 奥斯提牌伤害管道 {SafeEntryOf(card)}: 牌面 {baseDamage} → {result}" +
                           $"（目标易伤={PowerOf(victim, "VULNERABLE_POWER")}，目标={SafeName(victim)}）");
            }
            return result;
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 奥斯提牌伤害管道失败（{SafeEntryOf(card)}）: {e.Message}");
            return baseDamage;
        }
    }

    /// <summary>上限日志条数（意图每帧重算，别刷屏）。</summary>
    private static int _capLogs;

    /// <summary>奥斯提牌伤害管道日志条数。</summary>
    private static int _ostyLogs;

    /// <summary>
    /// 已经打过的「牌名 + 从多少变成多少」。意图**每帧都会重算**，所以不能每次都打；
    /// 但也不能像以前那样给一个**全局 40 行**的硬上限 —— 一场战斗前面某个牌（ASTRAL_PULSE）
    /// 就能把额度烧光，后面真正要查的牌（王者之拳 / 万向斩）一行都不剩
    /// （2026-11-04 排查「预览和实伤对不上」时就卡在这里：日志里查不到那两张牌显示了多少）。
    /// 现在按「牌名 + 数值变化」去重：同一种变化只打一次，组合数很少，不会刷屏也不会被谁耗光。
    /// </summary>
    private static readonly HashSet<string> PipelineLogs = new(StringComparer.Ordinal);

    /// <summary>这条日志只在「算出来的和牌面不一样」时才问一句；同一种变化只打一次。</summary>
    private static bool LogDamagePipeline(CardModel? card, int from, int to)
    {
        try
        {
            string entry = card != null ? SafeEntryOf(card) : "(无卡源)";
            return PipelineLogs.Add(entry + ":" + from + "→" + to);
        }
        catch { return false; }
    }

    private static int StrengthOf(Creature dealer)
    {
        try { return dealer.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>(); }
        catch { return 0; }
    }

    /// <summary>按 entry 读某个 power 的层数（诊断日志用；读不到就 0）。</summary>
    private static int PowerOf(Creature? creature, string entry)
    {
        if (creature == null) return 0;
        try
        {
            var powers = creature.Powers;
            if (powers == null) return 0;
            foreach (var power in powers)
            {
                if (power == null) continue;
                string id;
                try { id = power.Id.Entry; } catch { continue; }
                if (id != entry) continue;
                try { return power.Amount; } catch { return 0; }
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    private static string SafeName(Creature? creature)
    {
        try { return creature?.Name ?? "?"; }
        catch { return "?"; }
    }

    private static string SafeEntryOf(CardModel card)
    {
        try { return card.Id.Entry; } catch { return "?"; }
    }

    private static decimal VarValue(CardModel card, string key, decimal fallback)
    {
        try
        {
            if (card.DynamicVars.TryGetValue(key, out var v) && v != null) return v.BaseValue;
        }
        catch { /* 忽略 */ }
        return fallback;
    }

    /// <summary>
    /// 「金斧」（`GoldAxe`）的真实伤害 —— **直接照反编译的公式算**，不信牌面上的缓存值。
    ///
    /// 反编译（`MegaCrit.Sts2.Core.Models.Cards.GoldAxe.CanonicalVars`）：
    /// <code>
    /// new CalculationBaseVar(0),
    /// new ExtraDamageVar(1),
    /// new CalculatedDamageVar(ValueProp.Move).WithMultiplier(
    ///     (card, _) =&gt; CombatManager.Instance.History.CardPlaysFinished.Count())
    /// </code>
    /// 也就是「（Base + Extra）× **本场战斗已打完的牌数**」= 每张牌 1 点，而且是**全场**的牌
    /// （玩家打的、她的、消耗牌堆里每回合复燃的「轰击」全都算）。
    ///
    /// 为什么要自己数（用户 2026-11-05：「金斧的预览还是偏低，是不是没考虑他的轰击？或者力量？」）：
    ///   意图是在**玩家回合开始**算的，那时玩家这回合的牌一张都还没打 ⇒ 牌面缓存值天然偏低；
    ///   而意图节点是**每帧**问一次数字的，所以这里必须现场读历史，
    ///   玩家打一张它就该涨一点，否则永远追不上实际值。
    /// </summary>
    /// <param name="pendingPlays">本回合**排在她这张牌前面、还没打出去**的牌数（由调用方算）。</param>
    public static int GoldAxeDamage(CardModel card, int pendingPlays)
    {
        int finished = 0;
        try
        {
            foreach (var _ in MegaCrit.Sts2.Core.Combat.CombatManager.Instance.History.CardPlaysFinished) finished++;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 金斧：读战斗历史失败（退回牌面值）: " + e.Message);
            return Math.Max(0, HitDamage(card, null));
        }

        // 每张牌 1 点（Base 0 + Extra 1）；万一哪天原版改了数值，这里也跟着走。
        int perCard = (int)Math.Round(VarValue(card, "ExtraDamage", 1m) + VarValue(card, "CalculationBase", 0m));
        if (perCard <= 0) perCard = 1;

        return Math.Max(0, finished + Math.Max(0, pendingPlays)) * perCard;
    }
}
