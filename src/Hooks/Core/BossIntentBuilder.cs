using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace CharacterBosses.Core;

/// <summary>
/// 把一张牌翻译成「卡牌下方要显示的意图」。一张牌可能有多个效果，所以返回列表。
///
/// 判定顺序：攻势（带真实伤害与命中次数）→ 守势 → 策略（施加负面）→ 强化（自我增益）。
/// 伤害走 <see cref="BossDamagePredictor"/>（预览通道 + Hook.ModifyDamage），
/// 增益/减益靠 <c>PowerVar&lt;T&gt;</c> 的泛型参数查 power 的 <see cref="PowerType"/>（并看数值正负）。
/// </summary>
public static class BossIntentBuilder
{
    private static readonly string[] DrawKeys = { "Cards" };
    private static readonly string[] EnergyKeys = { "Energy" };
    private static readonly string[] StarKeys = { "Stars", "Star" };
    /// <summary>
    /// 「黑洞」（`BlackHolePower`）在这张牌下会打多少：**只对会花/得辉星的牌**给数。
    /// 反编译：`BlackHolePower` 在「花费或获得辉星」时对 `HittableEnemies` 造成 `Amount` 点
    /// `Unpowered` 伤害（不吃力量/虚弱）⇒ 这里返回「层数」，画成 Raw 攻势意图。
    /// 判定「这张牌会碰辉星」= 有星费（`CanonicalStarCost`/`StarCost`）或带 `Stars`/`Star` 变量。
    /// </summary>
    internal static int BlackHoleDamageOf(CardModel card)
    {
        try
        {
            if (card == null) return 0;
            var creature = card.Owner?.Creature;
            if (creature == null) return 0;
            int stacks = creature.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.BlackHolePower>();
            if (stacks <= 0)
            {
                // 规划期他还没打出黑洞（第 1 回合它是第一张）⇒ 按剧本前瞻：排它后面的牌用它的层数
                try
                {
                    if (creature.Monster is CharacterBosses.Monsters.CharBossMonster bhBoss && bhBoss.Plan != null)
                    {
                        int idx = -1, bhIdx = -1, amt = 0;
                        for (int i = 0; i < bhBoss.Plan.Count; i++)
                        {
                            var pc = bhBoss.Plan[i].Card;
                            if (pc == null) continue;
                            if (BossDamagePredictor.EntryOf(pc) == "BLACK_HOLE")
                            {
                                bhIdx = i;
                                amt = Math.Max(0, (int)Math.Round(Var(pc, "BlackHolePower", 0m)));
                            }
                            if (idx < 0 && ReferenceEquals(pc, card)) idx = i;
                        }
                        if (bhIdx >= 0 && idx > bhIdx) stacks = amt;
                    }
                }
                catch { /* 前瞻失败就算了 */ }
            }
            if (stacks <= 0) return 0;

            // ⚠ 「下回合才获得辉星」的牌（汇流）不该在这张牌下面标黑洞 —— 星是**下回合**到账的，
            //    那一刻的伤害由「回合开始那一条」表示（用户 2026-10-28 口径）。
            if (BossDamagePredictor.EntryOf(card) == "CONVERGENCE") return 0;

            bool touchesStars = false;
            try { if (card.GetStarCostWithModifiers() > 0) touchesStars = true; } catch { /* 忽略 */ }
            if (!touchesStars)
            {
                foreach (var key in StarKeys)
                {
                    if (Var(card, key, 0m) != 0m) { touchesStars = true; break; }
                }
            }
            return touchesStars ? stacks : 0;
        }
        catch { return 0; }
    }

    private static readonly string[] HealKeys = { "Heal", "MaxHp", "HpLoss" };

    public static List<AbstractIntent> IntentsFor(CardModel card, Creature? target, int pendingDrop = 0,
                                                  int pendingRise = 0, int extraDamage = 0, int extraHits = 0,
                                                  int xHits = 0, int shivBonus = 0, bool shivBonusBakedIn = false,
                                                  int handAtPlay = -1, int serpentDamage = 0, int playCount = 1,
                                                  int discardsThisTurn = -1, int bodySlamBlock = -1,
                                                  int strengthBonus = 0, int playsBeforeThisTurn = 0,
                                                  int smokePerHit = 0, int smokeHits = 0,
                                                  int letterOpenerHit = 0, int pendingStatusCards = 0,
                                                  bool aoeFromCaller = false, bool minionSummon = false,
                                                  int shivOverride = 0, int etherealBefore = 0)
    {
        var intents = new List<AbstractIntent>();

        var entry = SafeEntry(card);

        // ★ 2026-11-05（用户）：「当储君的仆从牌满足召唤仆从的条件时，其意图应该为**召唤**而非攻势/强化/守势。
        //   这个需要根据场上的仆从数量实时计算。」——仆从只会在**她的回合**里被召唤，玩家的回合里数量不变，
        //   所以按当前仆从数判断就等价于实时。
        try
        {
            if (entry == "MINION_STRIKE" || entry == "MINION_DIVE_BOMB" || entry == "MINION_SACRIFICE")
            {
                // 规划期已经把「本回合前面那几张仆从牌会占掉几个召唤名额」算好了（`minionSummon`）；
                // 只有真的没有规划上下文时（比如每帧重算）才退回「按场上现有仆从数」判断。
                bool summon = minionSummon;
                if (!summon)
                {
                    // ★ 2026-11-05（用户：还是不会实时计算）：每帧重算那条路没有规划上下文，
                    //   以前只看「场上现有仆从数」⇒ 同一回合里的两张仆从牌都显示召唤。
                    //   现在自己把**本回合计划**从头走一遍：起始 = 场上现有仆从数，
                    //   每遇到一张排在它前面、会召唤的仆从牌就 +1，于是第二张就正确显示「攻击」。
                    var minionBoss = card.Owner?.Creature?.Monster as Monsters.CharBossMonster;
                    if (minionBoss?.Creature?.GetPower<Powers.BossCallToArmsPower>() != null)
                    {
                        int planned = Monsters.BossMinionMonster.CountAlive(minionBoss);
                        try
                        {
                            var plan = minionBoss.Plan;
                            if (plan != null)
                            {
                                foreach (var play in plan)
                                {
                                    var planned2 = play?.Card;
                                    if (planned2 == null) continue;
                                    if (string.Equals(planned2.Id.Entry, entry, StringComparison.Ordinal)
                                        && ReferenceEquals(planned2, card)) break;   // 走到它自己就停
                                    if (planned2.Id.Entry != "MINION_STRIKE"
                                        && planned2.Id.Entry != "MINION_DIVE_BOMB"
                                        && planned2.Id.Entry != "MINION_SACRIFICE") continue;
                                    if (planned >= Powers.BossCallToArmsPower.MinionCap) break;
                                    planned++;
                                }
                            }
                        }
                        catch { /* 忽略 */ }
                        summon = planned < Powers.BossCallToArmsPower.MinionCap;
                    }
                }
                if (summon) return new List<AbstractIntent> { new SummonIntent() };
            }
        }
        catch { /* 判断失败就按原版分类 */ }

        // ★ v0.2.357（用户 2026-11-04：「疯狂科学错误的显示成了攻击意图，应该是守势加强化」）：
        //   「疯狂科学」（`MadScience`）是一张**三套模板合一**的事件牌：它的卡面文本是
        //   `{CardType:choose(Attack|Skill|Power):造成{Damage}点伤害…|获得{Block}点格挡。|…}`
        //   —— 也就是**不管这一场它变成哪种类型，`Damage` 变量一直都在**。
        //   而 `BossDamagePredictor.HitDamage` 会扫所有名字里带 `Damage` 的变量取最大值
        //   ⇒ 它被算成 12 点伤害、画出一个攻击意图（日志实证：
        //   `意图 MAD_SCIENCE: [LiveSingleAttackIntent+DefendIntent+SacrificeIntent+BuffIntent]`，
        //   而它真正的类型是 `Skill`、打出时只给 8 点格挡）。
        //   她这张牌在我们剧本里的 rider 固定是 Chaos（见 `EventMachineScript`），
        //   用户要求意图就显示「**守势 + 强化**」两条 ⇒ 这里按 entry 特判。
        bool madScience = string.Equals(entry, "MAD_SCIENCE", StringComparison.Ordinal);

        // 奥斯提的攻击**不吃她的力量/虚弱/易伤** —— 用户实测：戳击/重压这类用的是 OstyDamage 变量，
        // 之前只把 UNLEASH/BONE_SHARDS 算成「奥斯提的牌」，于是戳击/重压的预览被她的**负力量**和
        // 虚弱一起压低了。所以这里把「数据上是 OstyDamageVar 的牌」也算进来。
        bool fromOsty = OstyAttackCards.Contains(entry) || BossDamagePredictor.HasVar(card, "OstyDamage");

        int damage;
        if (fromOsty && BossDamagePredictor.HasVar(card, "ExtraDamage"))
        {
            // 「出击」：实际伤害 = CalculationBase + ExtraDamage × 奥斯提当前生命（见 OstyScaledDamage）
            damage = Math.Max(0, BossDamagePredictor.OstyScaledDamage(card));
        }
        else if (fromOsty)
        {
            // 戳击/重压这类 OstyDamageVar 牌：基础值 + 奥斯提的加伤（钙化 CalcifyPower 挂在**她**身上，
            // 原版 ModifyDamageAdditive 直接返回它的 Amount）。**不加**她的力量 / 虚弱。
            int baseDamage = Math.Max(0, BossDamagePredictor.HitDamage(card, target) - pendingDrop);
            damage = baseDamage > 0 ? baseDamage + Math.Max(0, CalcifyAdd(card)) : 0;
        }
        else
        {
            // ⚠ 先算「这张牌本来就有的伤害」，**只有本来就有伤害**才加 <paramref name="pendingRise"/>。
            // 之前先加再判 >0，导致技能/能力牌也被算出 1 点伤害、多出一个攻击意图
            // （用户实测：涂毒、偏折下面冒出小刀/剑图标）。
            //
            // 「精确切击」例外：它的伤害 = 13 − 2 × 打出那一刻手牌里的**其他**牌数（反编译，见
            // BossDamagePredictor.PreciseCutDamage）。玩家回合里手牌是满的 ⇒ 直接读会算得很低，
            // 所以由调用方（BladesGrowth 按计划顺序走一遍）把「打出时的手牌数」传进来（用户 2026-10-26 要求）。
            int baseDamage = (entry == "PRECISE_CUT" && handAtPlay >= 0)
                ? Math.Max(0, BossDamagePredictor.PreciseCutDamage(card, handAtPlay) - pendingDrop)
                // 「铭记死亡」：伤害 = 9 + 4 × **本回合已丢弃的张数**（反编译见 BossDamagePredictor.
                // MementoMoriDamage）。规划期历史里一张弃牌都还没有 ⇒ 不传就会显示 9（实际 17）。
                : (entry == "MEMENTO_MORI" && discardsThisTurn >= 0)
                    ? Math.Max(0, BossDamagePredictor.MementoMoriDamage(card, discardsThisTurn) - pendingDrop)
                    : Math.Max(0, BossDamagePredictor.HitDamage(card, target) - pendingDrop);
            damage = baseDamage > 0 ? baseDamage + Math.Max(0, pendingRise) : 0;
        }

        // ── 「全身撞击」：伤害 = **出手那一刻**的格挡 ──
        // 反编译：`BodySlam.CanonicalVars` 里是
        //   `CalculatedDamageVar(...).WithMultiplier((card, _) => card.Owner.Creature.Block)`，
        // 也就是预览直接读「现在身上的格挡」。可意图是**玩家回合里**算的：那时他本回合该清的格挡
        // 还没清、本回合的血墙/坚毅/防御也都还没打 ⇒ 显示的是旧值（用户 2026-10-28 实测：
        // 「全身撞击的伤害没有考虑他在本回合会获得的格挡值」）。
        // 所以由调用方（`BossScriptPlanner.Plan` 按剧本顺序走一遍）把「出手时会有多少格挡」算好传进来，
        // 这里直接用它当牌面伤害，后面照常过力量/虚弱/易伤管道。
        if (bodySlamBlock >= 0 && entry == "BODY_SLAM")
        {
            damage = Math.Max(0, bodySlamBlock);
            // 覆盖牌面之后**不能丢掉待补的正向加成**（力量/手里剑那套 `pendingRise`，
            // 例如本回合前面那张血墙刚给他 +1 力量）—— 否则格挡对了、力量又少了。
            if (damage > 0) damage += Math.Max(0, pendingRise);
        }

        int hits = BossDamagePredictor.Hits(card, target);

        // ★ 2026-11-05（用户设计：三层「状态机」）：「散射炮」（`FLAK_CANNON`）的段数 =
        //   **她自己**牌堆里「状态牌」的数量（不含消耗牌堆）—— 反编译：
        //   `owner.PlayerCombatState.AllCards.Where(c => c.Type == Status && c.Pile.Type != Exhaust)`。
        //   意图在她出手前算，所以这里现数一遍覆盖掉牌面算出来的段数。
        if (entry == "FLAK_CANNON")
        {
            try
            {
                var cannonBoss = card.Owner?.Creature?.Monster as Monsters.CharBossMonster;
                int statuses = CountOwnStatusCards(cannonBoss) + Math.Max(0, pendingStatusCards);
                if (statuses > 0)
                {
                    if (hits != statuses)
                    {
                        Logger.Log($"[CharBoss] 散射炮：她牌堆里 {statuses} 张状态牌 ⇒ 意图段数 {hits} → {statuses}");
                    }
                    hits = statuses;
                }
            }
            catch { /* 数不出来就按原版 */ }
        }

        // ★ 2026-11-10（用户实测：第五回合亡魂牵引预览 7×**3**、真实 6×**4**）：
        //   「亡魂牵引」（`PULL_FROM_BELOW`）的段数 = 本场战斗里**她打出的虚无牌数**
        //   （反编译 `CalculatedHits`：`e.CardPlay.Player == card.Owner && e.WasEthereal`）——
        //   数的是战斗历史。可这条意图是在**玩家回合**里算的：那时她本回合那张「致死性」
        //   （虚无牌）还没打出去，历史里没有它 ⇒ 永远少一段。
        //   ⇒ 把「本回合计划里排在它前面的虚无牌」补上（`PlannedEtherealBefore`）。
        //   注意**不会双算**：她真打完那张牌之后 `CharBossMonster.PlayOne` 会把它从 `Plan` 里摘掉，
        //   而历史里已经记上它了 —— 两个来源在时间上互斥。
        if (entry == "PULL_FROM_BELOW")
        {
            try
            {
                var pullBoss = card.Owner?.Creature?.Monster as Monsters.CharBossMonster;
                int fromPlan = PlannedEtherealBefore(pullBoss, card);
                // ⚠ **规划期 `boss.Plan` 还是空的**（`BossScriptPlanner.Plan` 先算出 intents、最后才写 Plan），
                //   所以规划期必须用调用方按剧本顺序数好的 `etherealBefore`；
                //   出牌期的「每帧重算」则没有调用方上下文，靠 `Plan` 里的剩余顺序。
                //   两条路取**较大值**而不是相加 —— 同一个来源被两边各数一次时不会翻倍。
                int planned = Math.Max(fromPlan, Math.Max(0, etherealBefore));
                if (planned > 0)
                {
                    Logger.Log($"[CharBoss] 亡魂牵引：本回合排它前面的虚无牌 {planned} 张" +
                               $"（计划内 {fromPlan} / 调用方 {etherealBefore}）⇒ 意图段数 {hits} → {hits + planned}");
                    hits += planned;
                }
            }
            catch { /* 数不出来就按原版 */ }
        }

        // 「疯狂科学」：把那个**假的**攻击伤害清零（它真正的效果是格挡 + Chaos rider）。
        // 清零必须放在下面 `if (damage > 0)` 之前，否则还是会画出攻势意图。
        if (madScience)
        {
            if (damage > 0)
                Logger.Log($"[CharBoss] 疯狂科学：牌面 Damage 变量（{damage}）不是本场效果（它是 {card.Type} 牌）⇒ 不画攻击意图");
            damage = 0;
            hits = 0;
        }

        // ★ v0.2.341（用户 2026-11-03：「猛晃不会正确显示它被打出的段数」）：
        //   「猛晃」（Rattle）的段数 = 1 + 本回合奥斯提**已经攻击过**的次数，而反编译的
        //   `CalculatedHits` 数的是战斗历史 —— 意图是在她出手前算的（历史空）⇒ 永远 1 段。
        //   规划期由 `BossScriptPlanner` 用本地计数器通过 `extraHits` 传进来；
        //   出牌期的重算（`BladesGrowth.RecomputeAll` / `RefreshPlannedIntents`）没有那段代码，
        //   所以这里按 `Plan` 顺序自己数一遍（规划期 `Plan` 是空的 ⇒ 不会和 extraHits 双算）。
        if (entry == "RATTLE")
        {
            try
            {
                var rattleBoss = card.Owner?.Creature?.Monster as Monsters.CharBossMonster;
                int before = rattleBoss?.OstyAttacksBefore(card) ?? 0;
                if (before > 0)
                {
                    extraHits += before;
                    Logger.Log($"[CharBoss] 猛晃：本回合奥斯提已经攻击过 {before} 次 ⇒ 意图段数 +{before}");
                }
            }
            catch { /* 忽略 */ }
        }

        // ★ v0.2.388（用户 2026-11-05：「金斧的预览还是偏低，是不是没考虑他的轰击？或者力量？」）：
        //   「金斧」的伤害 = （Base 0 + Extra 1）× **她本场战斗已打完的牌数**
        //   （反编译见 <see cref="BossDamagePredictor.GoldAxeDamage"/>）。
        //   ★ v0.2.516 改口径（用户 2026-11-09：「bug：轰击储的金斧把玩家出牌也算上了」）：
        //   原版倍率数的是 `CardPlaysFinished.Count()`（**全场**，玩家的牌也算），
        //   现在只数**她自己**打完的牌（消耗堆里每回合复燃的「轰击」仍然算她的牌）。
        //   意图是在**玩家回合开始**算的，那时她本回合的牌一张都还没打 ⇒ 牌面缓存值天然偏低。
        //   ⇒ 这条意图改成**每帧现算**（`LiveDelegateSingleAttackIntent`），基础值直接数战斗历史，
        //   再把本回合排在她前面、还没打出去的牌补上（规划期由 `BossScriptPlanner` 传
        //   `playsBeforeThisTurn`，出牌期按 `Plan` 剩余顺序数）。力量/虚弱/易伤仍由
        //   `FinalDamage` 走原版管道算，所以这里喂的是**牌面基础值**，不喂最终值。
        bool isGoldAxe = entry == "GOLD_AXE";
        if (isGoldAxe)
        {
            // 这三个数只影响「她本回合还差几张没打」，和上面的 extraDamage 无关
            Logger.Log($"[CharBoss] 金斧：按战斗历史现算（本回合排它前面还没打的牌 {Math.Max(0, playsBeforeThisTurn)} 张）");
        }

        // ── 「这张牌会被打几次」（复制药水 `DuplicationPower` / 爆发 `BurstPower`）──
        // 用户 2026-10-26：「被复制药水或爆发影响而回打出 2 次的牌应标为 4×2」。
        // 运行时确实会打两遍（`CharBossMonster.PlayOne` 里手动重放），所以最终段数要乘上它；
        // 乘法放在下面**算出全部段数之后**再做（`extraHits` 是牌自己的重放，也要一起翻倍）。
        int plays = Math.Max(1, playCount);

        // ───── F「意图按人清零」（多人）─────
        // 这张牌的单体/随机攻击如果打的是**别人**，那对本地玩家来说它这一手就是 0 伤害
        // → 攻势意图显示 0（用户口径：不会打到我的意图对我显示 0）。
        // **群攻（AllEnemies）永远不清零** —— 它打到所有人，每个人都该看到真实伤害
        // （用户 2026-10-22：「boss 的群攻牌所有玩家都应该能看到正确的伤害预览」）。
        // 单人、或打的就是本地玩家、或目标不是玩家 → 一律原样（零回归）。
        // ⚠ v0.2.156：判据从「计划里存的那个 target」换成「**这只 Boss 此刻的锁定对象**」
        //（用户 2026-10-29 截图：1 号位**就是**被储君锁定的那位、也真的挨了这张 10 点伤害，
        // 可意图上显示 0）。原因：计划里那份 target 是规划期的，中途可能已经改派
        //（`ReassignDeadLocks` / 开局分配晚于规划），而**出牌**在 v0.2.155 起是按实时锁定的 ⇒
        // 显示的判据必须跟着实时锁定走，否则会出现「显示 0、实际挨打」。
        bool aimedElsewhere = !IsAoE(card) && AimedAwayFromLocal(card, target);
        // 「还没到账的加成」（熔炉铸造 / 剑圣重放）只对本来就有伤害的牌生效，
        // 否则技能牌会被算出一条幻影攻击（踩过）。
        // 这张牌「还没跑力量/虚弱/易伤」的牌面伤害 —— X 费那张（根除）要用它去走原版伤害管道。
        int baseForHooks = damage;

        if (damage > 0)
        {
            damage += Math.Max(0, extraDamage);
            // ★ v0.2.362（用户 2026-11-04：「出击的伤害预览多了 2」）：
            //   「**她自己的**力量」这部分必须单独传（`strengthBonus`），**奥斯提的牌一律不吃**
            //   （反编译：奥斯提的攻击用的是 OstyDamage / 奥斯提生命那套公式，她的 StrengthPower 不参与）。
            //   以前把它和「奥斯提待加生命」等其他补正一起塞在 `extraDamage` 里 ⇒ 出击多算了她的力量
            //   （日志实证：`意图数值 UNLEASH: 显示 40（预补 8…）`，其中 8 = 奥斯提 +6 + 她的力量 +2，
            //   而真实公式是 6 + 1×26 = 32，正确预览应是 38）。
            if (!fromOsty) damage += Math.Max(0, strengthBonus);
            hits += Math.Max(0, extraHits);
            baseForHooks = damage;
            // ⚠ **不要在这里再手动乘虚弱**（用户 2026-10-26 截图实测：中和意图 6，玩家给她上虚弱后变 3，
            // 精确切击 23 → 12 —— 正好是 ×0.75 乘了两遍）。
            // 原因：原版意图节点渲染时会自己跑一遍
            // <c>Hook.ModifyDamage(..., target = 本机玩家, dealer = owner)</c>，
            // 而 `WeakPower.ModifyDamageMultiplicative` 的条件是 `dealer == Owner`
            // —— 这里传的 dealer 就是她本人 ⇒ **虚弱本来就会被算进去**。
            // （早先那版注释说"节点没算虚弱"，实测已经不对了；力量更是节点算的。）
            // 走 Raw 意图的分支（奥斯提 / X 费 / 群蛇 / 速行者）本来就不吃节点修正，
            // 那些数字是 `ValueProp.Unpowered` 之类的原样值，也不该乘虚弱。
        }
        LogSpecialDamageOnce(card, damage, extraDamage, hits);
        if (entry == "SUPERMASSIVE") LogSupermassive(card, damage, extraDamage);

        // ★ v0.2.341（用户 2026-11-03：「第四回合开了致死性后每回合的第一张攻击牌的伤害预览
        //   仍然没有正确增加」）：
        //   「致死性」（`LethalityPower`）= 每回合**第一张攻击牌**伤害 +Amount%。
        //   它不是「按牌」的加成，而是 power 里的 `ModifyDamageMultiplicative`，靠**战斗历史**判断
        //   「这是不是本回合第一张」—— 而意图是在她出手前算的，历史里一张牌都没有。
        //   两条渲染路径的处境还不一样：
        //     · 普通攻击牌走 `LiveSingle/MultiAttackIntent` → 渲染时调 `BossDamagePredictor.FinalDamage`
        //       （`Hook.ModifyDamage` 带 cardSource）⇒ 那 50% 由 power 自己加，我们**不能**再乘；
        //     · **奥斯提**那几张（击掌 / 紧追不放 / 猛晃 / 重压…）走 `AttackIntent.Raw`，
        //       渲染时**不过** `Hook.ModifyDamage` ⇒ 不自己乘就永远看不到这 50%（用户这次报的正是它）。
        //   所以只在 `fromOsty` 这一支手动乘一次。
        //
        // ★ 2026-11-10（用户：「第五回合的亡魂牵引没有考虑致死性的增伤」）：
        //   「亡魂牵引」（`PULL_FROM_BELOW`）是**普通攻击牌**，但它的伤害来自
        //   `CalculatedHits`（本场打出过的虚无牌数），而意图是在**玩家回合**里算的 ——
        //   那一刻她本回合的「致死性」还没打出去、power 不在身上 ⇒
        //   `Hook.ModifyDamage` 那一路乘不出那 50%。
        //   所以：**她身上还没有这层 power**（= 只可能是「本回合计划里排在她前面」的那种情况）
        //   且这张是她本回合第一张攻击牌时，也要把这份加成算进预览；
        //   等 power 真上身之后（她回合里的每帧重算）就走上面那条原版管道，绝不双份。
        //
        // ★★ 2026-11-10 第二次修正（用户实测：预览 7×3、真实 6×4）：
        //   **顺序**必须先算力量、**再**乘致死性：真实结算 `(5 + 力量 -1) × 1.5 = 6`，
        //   而我上一版是「先把牌面 ×1.5 再走管道减力量」⇒ 5×1.5=8 → 8−1=7（多了 1 点）。
        //   所以**非奥斯提**这条路不再改 `damage`，而是记下 `pendingLethalityPct`，
        //   到建意图时用「先 `FinalDamage`（含力量/虚弱/易伤）再乘倍率并向下取整」的写法 ——
        //   与真实结算的取整一致（StS2 是向下取整）。
        bool lethalityManual = fromOsty;
        if (!lethalityManual && damage > 0)
        {
            try
            {
                int onHer = card.Owner?.Creature?
                    .GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.LethalityPower>() ?? 0;
                lethalityManual = onHer <= 0;      // 还没上身 ⇒ 需要我们补
            }
            catch { lethalityManual = false; }
        }
        int pendingLethalityPct = 0;
        if (lethalityManual && damage > 0)
        {
            try
            {
                var lethalBoss = card.Owner?.Creature?.Monster as Monsters.CharBossMonster;
                if (lethalBoss != null && lethalBoss.IsFirstPlannedAttackCard(card))
                {
                    int pct = LethalityPercentFor(lethalBoss, card);
                    if (pct > 0)
                    {
                        if (fromOsty)
                        {
                            // 奥斯提那几张走 Raw 意图、全程不过伤害管道 ⇒ 只能当场乘掉
                            int before = damage;
                            damage = Math.Max(0, (int)Math.Round(damage * (100 + pct) / 100.0));
                            Logger.Log($"[CharBoss] 致死性：{entry} 是本回合第一张攻击牌 ⇒ 意图 {before} → {damage}（+{pct}%）");
                        }
                        else
                        {
                            // 普通攻击牌：留到建意图时**先走管道再乘**（见下面 pendingLethalityPct 的用法）
                            pendingLethalityPct = pct;
                            Logger.Log($"[CharBoss] 致死性：{entry} 是本回合第一张攻击牌 ⇒ 预览将在「力量之后」×" +
                                       $"{(100 + pct) / 100.0:0.##}（+{pct}%，与真实结算同序）");
                        }
                    }
                }
            }
            catch { /* 忽略：算不出就按不加成 */ }
        }
        if (damage > 0 && aimedElsewhere)
        {
            intents.Add(new RawAttackIntent(0));
            Logger.Log($"[CharBoss] 意图按人清零：{entry} 打的是别人 → 对本地玩家显示 0");
        }
        else if (damage > 0)
        {
            if (xHits > 0)
            {
                // X 费牌（根除）：段数 = X（＝本回合花掉的能量），必须用 Raw 多段意图才画得出「10×4」；
                // 原版 MultiAttackIntent 的可见文字内部走 GetSingleDamage，在这个手牌栏里只有一段。
                //
                // ⚠ 以前这里是 `damage + CurrentStrengthOf(card)`：**规划期算死 + 只认原版力量**。
                // 于是玩家在她回合外减她力量（尤其崩坠「哀嚎」那种「本回合失去 N 点力量」的临时减力量，
                // `StrengthPower` 层数根本不变），意图纹丝不动（用户 2026-10-23 实测：减了 11 点还显示 10×4）。
                // 现在改成 <see cref="LiveMultiAttackIntent"/>：**每次渲染都重算**，
                // 并且走原版伤害管道 <c>Hook.ModifyDamage</c> ⇒ 任何 mod 的任何增/减伤害都算进来。
                int times = Math.Max(1, 1 + xHits) * plays;   // 复制药水/爆发 ⇒ 整张牌打两遍，X 也随之翻倍
                int planBase = baseForHooks;
                if (times <= 1)
                {
                    // X 只花了 1 点（或只有 1 段）⇒ 单段攻击，别画成「10×1」
                    intents.Add(new LiveSingleAttackIntent(card, planBase));
                }
                else
                {
                    intents.Add(new LiveMultiAttackIntent((targets, owner) =>
                    {
                        var victim = FirstPlayerTarget(targets) ?? owner;
                        int perHit = BossDamagePredictor.FinalDamage(card, planBase, victim);
                        return (Math.Max(0, perHit), times);
                    }));
                }
            }
            else if (fromOsty)
            {
                // 奥斯提的牌走 Raw 意图（不吃意图节点的 Hook.ModifyDamage）⇒ **上限和易伤都要自己算**。
                // ★ v0.2.362（用户 2026-11-04：「出击…没有计算易伤…伤害被大幅低估了」）：
                //   以前这里只跑 `CapDamage`（只算「无实体」那种上限），**目标身上的易伤 ×1.5 完全没算**
                //   ⇒ 玩家挂着易伤时出击的真实伤害远高于预览。
                //   现在改用 `OstyFinalDamage`：把原版伤害管道整条跑一遍，但 **dealer 换成奥斯提本人**
                //   ⇒ 目标侧的易伤/摧残等等照常生效，而她自己的力量/虚弱/致死性不会误加进来
                //   （致死性由上面那段按「本回合第一张攻击牌」手工乘，`cardSource` 传 null 所以不会双份）。
                int capped = BossDamagePredictor.OstyFinalDamage(card, damage, target);
                // ⚠ 段数 ≤ 1 时必须是**单段**意图：戳击是 1 段，以前无条件建多段 ⇒ 显示成「6×1」
                //（用户 2026-10-28 实测）。
                intents.Add(AttackIntent.Raw(capped, hits * plays));
            }
            else
            {
                int totalHits = hits * plays;      // 复制药水/爆发：整张牌打两遍 ⇒ 段数翻倍（4 → 4×2）
                if (isGoldAxe)
                {
                    // 每帧现算：基础值 = 战斗历史里已打完的牌数 + 她本回合还差的那几张（见上面 v0.2.388 说明）
                    var goldBoss = card.Owner?.Creature?.Monster as Monsters.CharBossMonster;
                    int fallbackPlays = Math.Max(0, playsBeforeThisTurn);
                    PlannedCardsBefore(goldBoss, card, out bool goldInPlan);
                    intents.Add(new LiveDelegateSingleAttackIntent((liveTargets, liveOwner) =>
                    {
                        var victim = FirstPlayerTarget(liveTargets) ?? liveOwner;
                        int pending = goldInPlan ? PlannedCardsBefore(goldBoss, card) : fallbackPlays;
                        int liveBase = BossDamagePredictor.GoldAxeDamage(card, pending);
                        return BossDamagePredictor.FinalDamage(card, liveBase, victim);
                    }));
                }
                // ⚠ 下面两条都走「渲染时用原版伤害管道算**一次**」的 Live 意图（2026-10-27 根治）：
                //   以前喂的是牌面值、让意图节点自己跑 `Hook.ModifyDamage`，于是「我们这边算过一次」
                //   与「节点再算一次」两条路径并存时，预览就可能和实伤不一致
                //   （彗星吃虚弱后预览 33 → 16）。现在数值只来自 `FinalDamage` 的一次结果，节点不再二次加工。
                // ★ 2026-11-04（用户要求）：「回响斩击」按**斩杀链**显示段数 ——
                //   原版是「对所有敌人造成 10 点伤害；每杀死一名敌人就把这一遍再打一次」，
                //   所以段数 = 1 + 这一手会杀死的玩家方单位数（**含替主人承伤而死的奥斯提**）。
                //   段数会随玩家血量/格挡/奥斯提存亡变化 ⇒ 必须每帧重算。
                else if (entry == "ECHOING_SLASH")
                {
                    int echoBase = damage;
                    intents.Add(new LiveMultiAttackIntent((liveTargets, owner) =>
                    {
                        var victim = FirstPlayerTarget(liveTargets) ?? owner;
                        int perHit = BossDamagePredictor.FinalDamage(card, echoBase, victim);
                        int times = BossDamagePredictor.EchoingSlashHits(card, echoBase, victim) * plays;
                        return (Math.Max(0, perHit), Math.Max(1, times));
                    }));
                }
                // ★ 2026-11-10：「致死性」的 +N% 必须**排在力量和易伤之后**（真实结算
                //   `(5 + 力量 -1) × 1.5 = 6`，而先把牌面乘 1.5 再减力量会得到 7）。
                //   所以她身上还没有这层 power（= 本回合计划里排她前面）时，这里改成
                //   「先 `FinalDamage` 跑完整条原版管道，再乘倍率并向下取整」。
                else if (pendingLethalityPct > 0)
                {
                    int lethalBase = damage;
                    int lethalPct = pendingLethalityPct;
                    if (totalHits > 1)
                    {
                        intents.Add(new LiveMultiAttackIntent((targets, owner) =>
                        {
                            var victim = FirstPlayerTarget(targets) ?? owner;
                            int perHit = BossDamagePredictor.FinalDamage(card, lethalBase, victim);
                            return (Math.Max(0, ApplyPct(perHit, lethalPct)), totalHits);
                        }));
                    }
                    else
                    {
                        intents.Add(new LiveDelegateSingleAttackIntent((targets, owner) =>
                        {
                            var victim = FirstPlayerTarget(targets) ?? owner;
                            return ApplyPct(BossDamagePredictor.FinalDamage(card, lethalBase, victim), lethalPct);
                        }));
                    }
                }
                else intents.Add(totalHits > 1
                    ? new LiveMultiAttackIntent((targets, owner) =>
                    {
                        var victim = FirstPlayerTarget(targets) ?? owner;
                        int perHit = BossDamagePredictor.FinalDamage(card, damage, victim);
                        return (Math.Max(0, perHit), totalHits);
                    })
                    : new LiveSingleAttackIntent(card, damage));
            }
        }

        int block = (int)Math.Round(Var(card, "Block", 0) + Var(card, "CalculatedBlock", 0));
        if (block > 0) intents.Add(new DefendIntent());

        // ── 群蛇形态（SerpentFormPower）──
        // 她已经带着（或本回合这张之后会打出来）群蛇形态 ⇒ 每打出一张牌，对**随机一名敌人**造成 4 点伤害
        // （`SerpentFormPower.AfterCardPlayed`：`Card.Owner == Owner.Player` → 随机取 `HittableEnemies` →
        //  `CreatureCmd.Damage(..., ValueProp.Unpowered, Owner)`）。
        // 用户 2026-10-26 要求：**每张牌下面都标一个「打 4」的攻势意图**（群蛇形态自己那张不标），
        // 并且「这个伤害不受力量虚弱等影响」⇒ 用 Raw 意图（不吃 Hook.ModifyDamage）显示原样数字。
        // ⚠ 这里**只画图**，不额外造成任何伤害：真实伤害仍由原版 power 结算（不会重复）。
        // ★ 2026-11-05（用户实测：回合开始时显示一下又没了）：出牌期每帧重算走的是**没有** planner 上下文的
        //   这条路径，所以这里自己再算一遍 —— 烟囱（本牌生成几张状态牌）与开信刀（本回合第 3/6/9 张技能牌）。
        // ★ v0.2.412（用户：「全息影像不应该标注打 5」，日志实证这 5 点会在几次重算之间
        //   从 HOLOGRAM 跳到 BOOST_AWAY、再跳到 DEFEND_DEFECT）：
        //   调用方（规划期 / 每帧重算）现在会在 `PlannedPlay` 里把这三个数**定死**并原样传来，
        //   这时**绝不能再**退回「从剩余计划里重新数第几张技能牌」——牌一打出去序号就整体前移，
        //   于是 5 会一直往后漂。只有真正没有计划上下文的调用（贪心 AI / 临时牌）才走这条兜底。
        if (!aoeFromCaller && smokeHits <= 0 && letterOpenerHit <= 0)
        {
            try
            {
                var syncBoss = card.Owner?.Creature?.Monster as Monsters.CharBossMonster;
                int times = Core.StatusBossMath.StatusCardsGenerated(card);
                if (times > 0)
                {
                    smokePerHit = Core.StatusBossMath.SmokestackDamage(syncBoss);
                    smokeHits = times;
                }
                int lo = Core.StatusBossMath.LetterOpenerDamage(syncBoss);
                if (lo > 0 && Core.StatusBossMath.IsSkill(card))
                {
                    int before = CountSkillsBeforeInPlan(syncBoss, card, playsBeforeThisTurn);
                    if ((before + 1) % Core.StatusBossMath.LetterOpenerSkillStep == 0) letterOpenerHit = lo;
                }
            }
            catch { /* 忽略 */ }
        }
        // ★ 2026-11-05（用户）：**烟囱 / 开信刀的伤害要挂在触发它的那张牌底下**
        //   （像「激发球的牌」把球的伤害挂在牌下面那样），不再放进左侧独立意图列。
        //
        // ★ 2026-11-05（用户：「第一回合的强撑没有标 5*2」）：烟囱是**每生成一张状态牌打一次**
        //   （反编译 `SmokestackPower.AfterCardGeneratedForCombat` 就是这个语义）⇒ 生成几张就打几段。
        //   以前把「5 × 2 张」压成一个 10 点的单段意图，和实际挨的两下对不上；现在按段数画成「5×2」。
        if (smokePerHit > 0 && smokeHits > 0)
        {
            intents.Add(smokeHits > 1
                ? new RawMultiAttackIntent(smokePerHit, smokeHits)
                : new RawAttackIntent(smokePerHit));
            Logger.Log($"[CharBoss] 牌底下挂全体伤害：{entry} → {smokePerHit}×{smokeHits} 点" +
                       $"（烟囱：本牌生成 {smokeHits} 张状态牌）");
        }
        if (letterOpenerHit > 0)
        {
            intents.Add(new RawAttackIntent(letterOpenerHit));
            Logger.Log($"[CharBoss] 牌底下挂全体伤害：{entry} → {letterOpenerHit} 点（开信刀）");
        }
        if (serpentDamage > 0)
        {
            // ⚠ 多人：群蛇形态虽然写的是「随机一名敌人」，但在「跟踪锁定」下**随机攻击也只会打被锁定的那位**
            //   （`LOCK_ON_POWER.description`：「{Boss}的单体攻击、随机攻击和充能球只会攻击你」）。
            //   以前这里只看**这张牌自己的目标**（群蛇形态是 Power，target 为 null）⇒ 判定成「打得到本地玩家」，
            //   于是**每个玩家的手牌栏都显示自己会挨这一下**，看起来像群攻（用户 2026-10-27 反馈）。
            //   现在改成按「她锁的是不是本地玩家」判断：非锁定玩家显示 0，和单体攻击同一套规则。
            bool serpentElsewhere = serpentDamage > 0 && RandomAimIsElsewhere(card);
            int serpent = serpentElsewhere ? 0 : serpentDamage;
            // 被打两遍的牌会让群蛇形态**也触发两次** ⇒ 画成「4×2」
            intents.Add(plays > 1 ? new RawMultiAttackIntent(serpent, plays) : new RawAttackIntent(serpent));
            if (serpentElsewhere)
            {
                Logger.Log($"[CharBoss] 群蛇形态：{entry} 的「打 {serpentDamage}」是随机攻击，她锁的不是本地玩家 → 对本地显示 0");
            }
        }

        // 卡戎之灰（CharonsAshes）：**每消耗一张牌 → 对所有敌人造成 3 点伤害**。
        // 烙印 / 坚毅 打出时会「消耗 1 张牌」⇒ 在它们下面补一条「群攻 3」的攻势意图
        //（用户 2026-10-23 要求）。伤害是 `ValueProp.Unpowered`（不吃力量）⇒ 用 Raw 意图显示原样数字。
        int ashesDamage = CharonsAshesDamageOf(card);
        if (ashesDamage > 0) intents.Add(new RawAttackIntent(ashesDamage));

        // ── 「黑洞」（BlackHolePower）──
        // 用户 2026-10-28：跟群蛇形态一样，**凡是会花/得辉星的牌**下面都画一条黑洞的群伤意图。
        // 反编译：`BlackHolePower` 每次「花费或获得辉星」都对 `HittableEnemies` 造成 `Amount` 点
        // `Unpowered` 伤害 ⇒ 不吃力量/虚弱，用 Raw 意图显示原样数字。
        int blackHole = BlackHoleDamageOf(card);
        if (blackHole > 0) intents.Add(new RawAttackIntent(blackHole));

        // ── 速行者（`SpeedsterPower`）：每抽 1 张牌 → 对所有敌人造成 `Amount` 点伤害 ──
        // 用户 2026-10-26（一层「奇巧猎」）：「给所有抽牌卡加上抽牌数 × 2 的群伤（速行者的伤害），
        // 不要和原版的重复计算」。
        // ⚠ **只画意图，绝不自己造成伤害**：真实伤害由原版 `SpeedsterPower.AfterCardDrawn` 结算
        // （反编译：`!fromHandDraw && card.Owner == Owner && 当前阵营是她这一侧` →
        //  `CreatureCmd.Damage(HittableEnemies, Amount, Unpowered, Owner)`）。她这回合真的会抽牌
        //  （见 `BossDefinition.KeepsScriptedHandByAct`：抽牌照常、只把抽到的牌放回牌堆顶）。
        // 数值是 `Unpowered`（不吃力量/虚弱）⇒ 用 Raw 意图原样显示。
        int drawCount = DrawCountOf(card);
        // ★ v0.2.504（用户 2026-11-08：「逃脱计划、投掷匕首下面没有标速行者的二点伤害」）：
        //   这两张的抽牌写在 OnPlay 里、牌面没有 Cards 变量 ⇒ `DrawCountOf` 读不到。
        //   它们确实各抽 1 张（真实伤害由原版 SpeedsterPower 结算），这里补齐显示。
        if (drawCount == 0 && (entry == "ESCAPE_PLAN" || entry == "DAGGER_THROW")) drawCount = 1;
        if (drawCount > 0)
        {
            int perDraw = SpeedsterAmountOf(card);
            if (perDraw > 0)
            {
                intents.Add(drawCount > 1
                    ? new RawMultiAttackIntent(perDraw, drawCount)
                    : new RawAttackIntent(perDraw));
                Logger.Log($"[CharBoss] 速行者：{entry} 会抽 {drawCount} 张 → 补 {perDraw}×{drawCount} 的群伤意图" +
                           "（真实伤害由原版 SpeedsterPower 结算，这里只是显示）");
            }
        }

        // 「生成刀」的那张牌：它自己不打伤害，但生成的刀当回合就会打出去
        // → 在它下面补一条「小刀 ×N」的攻势意图（用户要求：先制打击 = 「3」+「4*2」）。
        // 小刀是攻击牌，所以也吃 <paramref name="pendingRise"/> 那点成长（力量还没到手的部分）。
        // ⚠ 只有 **N > 1** 才用多段意图：N == 1 时是一个单段攻击，应显示「4」而不是「4×1」
        //（用户实测：斗篷与匕首降级后只给 1 把刀，意图却成了 4×1）。
        // ★ v0.2.521：钢铁风暴（StormOfSteel）的小刀数量是**丢掉几张就生成几把**，牌面上没有变量
        //   ⇒ 由调用方（剧本规划器）按本回合被丢弃的张数传进来。
        int shivs = shivOverride > 0 ? shivOverride : ShivsFrom(card);
        // 小刀默认只打「锁定她的那位玩家」→ 打别人时对本地玩家显示 0；
        // 但**打出「刀扇」之后小刀改成全体攻击**（`FanOfKnivesPower`）⇒ 那时人人都该看到真实伤害。
        bool shivAimedElsewhere = aimedElsewhere && !ShivsHitAll(card);
        if (shivs > 0 && shivAimedElsewhere)
        {
            // 小刀也是朝「锁定她的那位玩家」扔的 → 打的是别人时同样显示 0
            intents.Add(new RawAttackIntent(0));
        }
        else if (shivs > 0)
        {
            // ★ v0.2.366（用户 2026-11-04 截图：玩家身上明明有易伤，刀刃之舞还是显示 4×3）：
            //   真正的根因不是「算错了」，而是**算的时间点**：
            //     · 冲刺这种普通攻击走 `LiveSingleAttackIntent` ⇒ **每帧渲染时重算** ⇒ 玩家后来上的易伤算得进去（显示 15 = 10×1.5）；
            //     · 小刀这条用的 `AttackIntent.Raw` / `RawMultiAttackIntent` 是**常数**，数值在
            //       **规划那一刻**就定死了（日志实证 `小刀预览: 牌面 4 → 4（目标易伤=0）`，那时玩家还没易伤）
            //       ⇒ 之后玩家再上易伤，这条永远是 4。
            //   现在改成 `LiveMultiAttackIntent`：**渲染时**才调 `ShivDamagePerHit`，
            //   于是易伤/力量/虚弱/幻影之刃全都跟普通攻击牌同一时刻取到。
            var shivBoss = card.Owner?.Creature?.Monster as Monsters.CharBossMonster;
            int rise = Math.Max(0, pendingRise);
            // ★ v0.2.375（用户 2026-11-04：「这回合这两把小刀的实际伤害是 18 和 9」）：
            //   18 / 9 = (6+3+9) / (6+3) —— 小刀基础伤害是 **6**，不是 4！
            //   因为「**隐秘匕首+**」升级时会对它生成的每把小刀执行 `CardCmd.Upgrade(current)`
            //   （反编译 `HiddenDaggers.OnPlay`：`if (hiddenDaggers.IsUpgraded) CardCmd.Upgrade(current);`），
            //   而 `Shiv` 是 4 点、升级 +2 ⇒ 6 点。以前这里把 `ShivBaseDamage = 4` 写死 ⇒ 升级版少算 2 点/刀。
            int shivBase = ShivBaseDamage + (GeneratesUpgradedShivs(card) ? 2 : 0);
            // 幻影之刃（shivBonus > 0）：每回合**第一把**小刀 +N（见 PhantomShivs）；
            // 拆成「第一把」+「其余 N-1 把」两条，不能整排画成「(4+9)×N」（用户 2026-10-28 的口径）。
            // ⚠ 剩下只有 1 把时也必须是单段（否则「隐秘匕首」2 把刀会显示成 16、7×1）。
            //
            // ★ v0.2.374（用户 2026-11-04 截图：刀贼第 4 回合「隐秘匕首+」显示 **`7` 和 `0`**）：
            //   之前用 `shivBonusBakedIn ? -shivBonus : 0` 去「扣回」原版多给的那 9 点，
            //   可 `shivBonusBakedIn`（= `phantomOnHer`）在**幻影之刃还只在手里**时就已经是 true 了，
            //   而那时 `ShivDamagePerHit` 根本没拿到 +9（power 还没上身）⇒ 7 − 9 → 夹成 **0**。
            //   现在改成「先归一、再加一次」：
            //     · `phantomReal` = **真正挂在她身上**的幻影层数（`PhantomShivs.AmountOf`）；
            //     · 把它从 `ShivDamagePerHit` 里减回去，得到**不含幻影**的干净每刀伤害；
            //     · 第一把 +`shivBonus`，其余原样（幻影只加一次，绝不会出现负数）。
            //   `Patches/PhantomBladesFirstShivPatch` 本来也只让第一把吃这 N 点，
            //   所以这里不再需要任何「扣回」逻辑。
            //
            // ★★ v0.2.533（玩家 2026-11-09：「刀扇那回合触发袖里乾坤 ⇒ 刀扇显示 17×4，
            //   把幻影之刃算到每一刀上了；第 7 回合刀刃交响曲显示 20×2（7 力量 + 9 幻影），
            //   应该是 20 和 11」）：
            //   **幻影之刃已经挂在她身上**时（`phantomReal > 0`），`ShivDamagePerHit` 走的是
            //   原版 `PhantomBladesPower.ModifyDamageAdditive` ⇒ **每一刀都 +N**；
            //   而 `shivBonus`（= `BladesGrowth` 算出来的 `phantomHere`）只在「幻影之刃还只在手里」时才 > 0
            //   ⇒ 上一版一旦 power 已经上身就掉进下面那个「不分段」的分支 ⇒ 整排都带 +9。
            //   现在统一：`首刀加成 = max(shivBonus, 真正挂着的层数)`，只要它 > 0 就一律
            //   「第一刀 +N、其余用干净伤害」，不管 power 是已经在身上还是还在手里。
            int phantomNow = 0;
            try { phantomNow = Math.Max(0, Core.PhantomShivs.AmountOf(shivBoss)); }
            catch { phantomNow = 0; }
            int firstShivBonus = Math.Max(shivBonus, phantomNow);

            if (firstShivBonus > 0)
            {
                int phantomBonus = firstShivBonus;
                // 第一把：**单段**（段数 1 不能画成「×1」）
                intents.Add(new LiveDelegateSingleAttackIntent((targets, _) =>
                {
                    // 受害者只认真人玩家（见 ResolvePreviewVictim 的注释）
                    Creature? victim = ResolvePreviewVictim(shivBoss, targets, card);
                    int per = CleanShivDamage(shivBoss, rise, victim, shivBase);
                    return Math.Max(0, per + phantomBonus);
                }));
                if (shivs > 1)
                {
                    int rest = shivs - 1;
                    // ★ v0.2.373：剩下只有 **1 把**刀时必须画成**单段**（`7`），不能画成「7×1」。
                    if (rest > 1)
                    {
                        intents.Add(new LiveMultiAttackIntent((targets, _) =>
                        {
                            Creature? victim = ResolvePreviewVictim(shivBoss, targets, card);
                            return (CleanShivDamage(shivBoss, rise, victim, shivBase), rest);
                        }));
                    }
                    else
                    {
                        intents.Add(new LiveDelegateSingleAttackIntent((targets, _) =>
                        {
                            Creature? victim = ResolvePreviewVictim(shivBoss, targets, card);
                            return CleanShivDamage(shivBoss, rise, victim, shivBase);
                        }));
                    }
                }
            }
            else if (shivs > 1)
            {
                int count = shivs;
                intents.Add(new LiveMultiAttackIntent((targets, _) =>
                {
                    // 受害者只认真人玩家（见 ResolvePreviewVictim 的注释）
                    Creature? victim = ResolvePreviewVictim(shivBoss, targets, card);
                    // ★ v0.2.525（用户 2026-11-09：「隐秘匕首和钢铁风暴还是一个都没有修好，预览仍是 4×2」）：
                    //   这里**漏传了 `shivBase`**（默认值 = 未升级的 4）⇒ 升级版的生成者
                    //   （隐秘匕首+ / 钢铁风暴+ 会给升级小刀，6 点）照样画成 4。
                    //   `shivBase` 在函数上面已经算好了（`ShivBaseDamage + (GeneratesUpgradedShivs ? 2 : 0)`），
                    //   只有「幻影之刃」那一支用到了它 —— 这条与下面那条是同一处遗漏。
                    return (ShivDamagePerHit(shivBoss, rise, victim, shivBase), count);
                }));
            }
            else
            {
                intents.Add(new LiveDelegateSingleAttackIntent((targets, _) =>
                {
                    // 受害者只认真人玩家（见 ResolvePreviewVictim 的注释）
                    Creature? victim = ResolvePreviewVictim(shivBoss, targets, card);
                    return ShivDamagePerHit(shivBoss, rise, victim, shivBase);   // ★ v0.2.525：同上，别漏 shivBase
                }));
            }
        }

        // 多人换牌 A3（斗篷与匕首 → 刀刃交响曲）：每只 Boss（**包括她自己**）各拿 n 把刀，
        // 当场朝「自己跟踪锁定的那位玩家」扔出去（用户 2026-10-22 订正：刀猎自己也要拿到刀，
        // 原版这张牌给的就是「每个队友（含自己）」）。
        // 意图必须提前显示 ⇒ 这里按**各自的**力量算，用 Raw 意图（不吃施法者也就是这个意图节点的加成）。
        if (entry == "BLADE_SYMPHONY")
        {
            int each = (int)Math.Round(Var(card, "Cards", 0));
            if (each > 0)
            {
                foreach (var mate in VolleyBossesOf(card))
                {
                    int mateRise = Math.Max(0, PendingRise(mate.Creature));
                    // 扔刀的目标是**各自的锁定者** —— 如果那不是本地玩家，就对本地玩家显示 0。
                    // ★ v0.2.533：但**刀扇**激活时小刀是群攻（`FanOfKnivesPower`），打到每个人 ⇒ 不能清零
                    //   （与单卡那条 `shivAimedElsewhere = aimedElsewhere && !ShivsHitAll(card)` 同一规矩）。
                    bool mateAimsElsewhere = !ShivsHitAll(card)
                                             && AimsElsewhere(Core.BossLockOn.TargetFor(mate)?.Creature);
                    // 每把刀的伤害同样过一遍伤害管道（含她自己那份「一心化万」的 0 费攻击增伤、
                    // 力量与待到手的成长），与打出时一致 —— 见 ShivDamagePerHit 的注释。
                    // ★ v0.2.366：这里也必须用**渲染时重算**的动态意图（`Raw*` 是算死的常数 ⇒
                    //   玩家事后上的易伤/虚弱不会反映出来）。
                    if (mateAimsElsewhere)
                    {
                        intents.Add(new RawAttackIntent(0));
                        Logger.Log($"[CharBoss] 刀刃交响曲意图：{mate.Id.Entry} 的小刀打的是别人 ⇒ 对本地玩家显示 0");
                        continue;
                    }
                    var mated = mate;
                    int matesRise = mateRise;
                    int mateShivBase = ShivBaseDamage + (GeneratesUpgradedShivs(card) ? 2 : 0);
                    // ★★ v0.2.533：与单卡那条同一个规矩 —— **只有每回合第一把刀**吃幻影之刃的 +N。
                    //   以前这里对每一把都走 `ShivDamagePerHit`（power 一上身就是「每把都 +9」），
                    //   于是「斗篷与匕首+/刀刃交响曲」画成 `20×2`，正确应是 `20` 和 `11`。
                    int matePhantom = 0;
                    try { matePhantom = Math.Max(0, Core.PhantomShivs.AmountOf(mated)); }
                    catch { matePhantom = 0; }
                    if (matePhantom > 0)
                    {
                        int phantomHere = matePhantom;
                        intents.Add(new LiveDelegateSingleAttackIntent((targets, _) =>
                        {
                            Creature? victim = ResolvePreviewVictim(mated, targets, card);
                            return Math.Max(0, CleanShivDamage(mated, matesRise, victim, mateShivBase) + phantomHere);
                        }));
                        if (each > 1)
                        {
                            int restCount = each - 1;
                            if (restCount > 1)
                            {
                                intents.Add(new LiveMultiAttackIntent((targets, _) =>
                                {
                                    Creature? victim = ResolvePreviewVictim(mated, targets, card);
                                    return (CleanShivDamage(mated, matesRise, victim, mateShivBase), restCount);
                                }));
                            }
                            else
                            {
                                intents.Add(new LiveDelegateSingleAttackIntent((targets, _) =>
                                {
                                    Creature? victim = ResolvePreviewVictim(mated, targets, card);
                                    return CleanShivDamage(mated, matesRise, victim, mateShivBase);
                                }));
                            }
                        }
                    }
                    else if (each > 1)
                    {
                        intents.Add(new LiveMultiAttackIntent((targets, _) =>
                        {
                            // 受害者只认真人玩家（见 ResolvePreviewVictim 的注释）
                            Creature? victim = ResolvePreviewVictim(mated, targets, card);
                            return (ShivDamagePerHit(mated, matesRise, victim, mateShivBase), each);
                        }));
                    }
                    else
                    {
                        intents.Add(new LiveDelegateSingleAttackIntent((targets, _) =>
                        {
                            // 受害者只认真人玩家（见 ResolvePreviewVictim 的注释）
                            Creature? victim = ResolvePreviewVictim(mated, targets, card);
                            return ShivDamagePerHit(mated, matesRise, victim, mateShivBase);
                        }));
                    }
                    Logger.Log($"[CharBoss] 刀刃交响曲意图：{mate.Id.Entry} 打 {each} 把小刀（伤害每帧重算）");
                }
            }
        }

        var (hasDebuff, hasBuff, hasSacrifice) = ClassifyPowers(card);
        // 「疯狂科学」只要「守势 + 强化」两条：牺牲意图也去掉（它是三套模板的混合体，
        // 数据上会带上「失去力量」这类减益变量，画出来就是用户看到的那个多余的下箭头）。
        if (madScience) hasSacrifice = false;
        if (hasDebuff) intents.Add(new DebuffIntent(false));
        if (hasSacrifice) intents.Add(new SacrificeIntent());   // 付出代价换强力效果（蓝色向下箭头）

        // 抽牌 / 回能量 / 回星 / 治疗 这类「对自己有利」的效果算强化
        // （小刀牌的 <c>Cards</c> 变量是"塞几把刀"，不是抽牌 → 排除，否则会多一个抽牌意图）
        // 「疯狂科学」恒定要一条强化（Chaos rider = 往手里塞一张牌、还能免费打出）。
        bool selfBenefit = madScience || (!NoBuffIntentCards.Contains(entry) &&
                           ((Var(card, "Cards", 0) > 0 && ShivsFrom(card) == 0) || Var(card, "Energy", 0) > 0
                           || Var(card, "Stars", 0) > 0 || Var(card, "Heal", 0) > 0
                           || Var(card, "MaxHp", 0) > 0 || hasBuff
                           || BuffVarCards.Contains(entry)));
        if (selfBenefit) intents.Add(new BuffIntent());

        if (intents.Count == 0)
        {
            // 什么都没认出来：技能牌默认偏「对自己有利」（如「变化一张手牌」），攻击牌给个攻势兜底
            intents.Add(card.Type switch
            {
                CardType.Attack => new SingleAttackIntent(Math.Max(1, damage)),
                CardType.Power => new BuffIntent(),
                _ => new BuffIntent(),
            });
        }

        return intents;
    }

    /// <summary>
    /// 这张牌打出时「**致死性**」会给它多少百分比加成。
    ///
    /// ★ v0.2.360（用户 2026-11-04：「第四回合养手骨的击掌伤害预览没有考虑她刚打出的致死性」）：
    ///   第 4 回合的剧本是 `致死性(1) → 击掌(2) → 增生(0)` —— 致死性**是同一回合先打的**，
    ///   而意图是在**规划期/重算期**算的：那时 `LethalityPower` 还没上身，
    ///   原来只读 `GetPowerAmount&lt;LethalityPower&gt;()` ⇒ 恒为 0 ⇒ 击掌那 50% 永远不显示。
    ///   现在：身上没有这层 power 时，去**本回合计划里**找那张「致死性」，读它的卡面数值（升级版会自动算）。
    ///   ⚠ 只给 `fromOsty`（走 Raw 意图、不过 `Hook.ModifyDamage`）那条路用 —— 普通攻击牌的加成由
    ///     power 自己在渲染期乘，这里再乘就会双份（见上面那段长注释）。
    /// </summary>
    internal static int LethalityPercentFor(Monsters.CharBossMonster? boss, CardModel card)
    {
        try
        {
            int pct = card.Owner?.Creature?
                .GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.LethalityPower>() ?? 0;
            if (pct > 0) return pct;      // 已经在身上：直接用它
        }
        catch { /* 读不到就往下走 */ }

        // 还没上身：本回合计划里有没有「致死性」还没打（她这回合会先打它）
        try
        {
            if (boss?.Plan == null) return 0;
            var pcs = boss.Shadow?.PlayerCombatState;
            foreach (var play in boss.Plan)
            {
                var planned = play?.Card;
                if (planned == null) continue;
                if (!string.Equals(BossDamagePredictor.EntryOf(planned), "LETHALITY", StringComparison.Ordinal)) continue;
                if (pcs != null && !pcs.Hand.Cards.Contains(planned)) continue;   // 已经打掉了就别重复
                int from = BossDamagePredictor.VarPreviewInt(planned, "Lethality", 0);
                if (from <= 0) from = BossDamagePredictor.VarPreviewInt(planned, "LethalityPower", 0);
                if (from > 0)
                {
                    Logger.Log($"[CharBoss] 致死性：本回合还没打出去（在计划里）⇒ 第一张攻击牌按 +{from}% 预览");
                    return from;
                }
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    /// <summary>
    /// 「到 Boss 真正出手之前还会掉多少力量/集中」——用来把意图数字提前修正。
    ///
    /// 偏差认知（<c>BiasedCognitionPower</c>）的原版实现：
    /// <code>
    /// // AfterSideTurnStart(side, participants, combatState)
    /// if (participants.Contains(Owner)) await PowerCmd.Apply&lt;FocusPower&gt;(ctx, Owner, -Amount, Owner, null);
    /// </code>
    /// 它在**Boss 自己回合开始**（敌方回合）掉 <c>Amount</c> 点集中，而我们的计划和意图是在
    /// **玩家回合开始**就算死的 —— 那时这笔还没扣，所以所有受力量加成的意图都偏高一点。
    /// 这里把它取出来，规划时先从基础值里减掉（意图节点之后会再补一次力量，正好抵消）。
    /// </summary>
    public static int PendingDrop(Creature? boss)
    {
        if (boss == null) return 0;
        try { return Math.Max(0, boss.GetPower<BiasedCognitionPower>()?.Amount ?? 0); }
        catch { return 0; }
    }

    /// <summary>
    /// 「到 Boss 真正出手之前还会**涨**多少力量」—— <see cref="PendingDrop"/> 的反方向。
    ///
    /// 烘焙手套（<c>ToastyMittens.AfterPlayerTurnStart</c>：每回合开始消耗 1 张手牌、给她 +1 力量，
    /// 反编译确认 `PowerCmd.Apply&lt;StrengthPower&gt;(player.Creature, DynamicVars.Strength, …)`）
    /// 是在**她自己的回合开始**才加力量的，而意图在**玩家回合开始**就算死了 ⇒ 预览比实际低 1
    /// （用户实测：「手套每回合会给她力量，所以伤害预览偏低」）。
    ///
    /// 这里把这 1 点先加进意图的基础值：意图节点之后还会再补一次**当前**力量，
    /// 于是显示的数字 = 基础+1 + 当前力量 = 她出手时的真实伤害。
    /// 等她回合真的加完力量后，<c>RefreshPlannedIntents</c> 会以 0 重新算一遍，不会重复计入。
    /// </summary>
    public static int PendingRise(Creature? boss)
    {
        if (boss == null) return 0;
        int total = 0;
        try
        {
            var relics = boss.Player?.Relics;
            if (relics == null) return 0;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                if (relic.Id.Entry == "TOASTY_MITTENS")
                {
                    try { total += Math.Max(0, (int)relic.DynamicVars["Strength"].BaseValue); }
                    catch { total += 1; }
                    continue;
                }
                // ★ v0.2.510（用户 2026-11-08：「第三回合飞镖的伤害预览没有计算口红会给她的力量」）：
                //   闪亮口红（`SparklingRouge`）反编译确认：
                //     AfterBlockCleared(creature) 里 `creature == Owner.Creature
                //       && Owner.PlayerCombatState.TurnNumber == 3` ⇒ +力量 / +敏捷（各 1）。
                //   也就是**她第 3 回合的格挡被清空那一刻**才到账，而规划是在玩家回合开始就做完的
                //   （那时她的格挡还挂着 ⇒ 还没到账）⇒ 所有吃力量的意图都少 1（飞镖这种多段牌最明显）。
                //
                //   判据：① 当前这一手是她第 3 回合（`TurnNumber == 3`）；
                //         ② 她的格挡**还没被清**（`Block > 0`）——格挡已经清空说明这一笔已经到账
                //            （或者这一回合本来没格挡、遗物根本不会触发）⇒ 不再补，避免重复计入。
                if (relic.Id.Entry == "SPARKLING_ROUGE")
                {
                    try
                    {
                        if (boss.Monster is CharacterBosses.Monsters.CharBossMonster charBoss
                            && charBoss.TurnNumber == 3
                            && boss.Block > 0)
                        {
                            int gain = 1;
                            try { gain = Math.Max(0, (int)relic.DynamicVars["Strength"].BaseValue); }
                            catch { gain = 1; }
                            total += gain;
                            Logger.Log($"[CharBoss] 闪亮口红：她第 3 回合的格挡还没清 ⇒ 预览先补 {gain} 点力量" +
                                       $"（当前格挡 {boss.Block}，力量 {SafeStrength(boss)}）");
                        }
                    }
                    catch { /* 读不到就按 0 */ }
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 估算「回合前涨力量」失败: " + e.Message);
        }
        return total;
    }

    private static int SafeStrength(Creature c)
    {
        try { return c.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>(); }
        catch { return 0; }
    }

    /// <summary>
    /// 「牺牲」牌：付出代价换强力效果。数据里能认的规则 + 一张写死的表（反编译逐个核对过）。
    ///
    /// 规则（能自动认的）：
    ///   · <c>HpLoss</c> &gt; 0 —— 扣自己的血（血墙 / 御血术）；
    ///   · 能力变量为**负**且卡牌目标是自己 —— 失去自己的力量/敏捷/集中（友谊 -2 力量）；
    ///   · 能力变量是**减益**且卡牌目标是自己 —— 给自己上负面（偏差认知、精神过载的灾厄）。
    ///
    /// 表（数据里看不出来的）：超能光束（全体伤害 + 失去 3 集中，目标不是自己）、
    /// 子弹时间 / 战斗专注（代价是「本回合不能再抽牌」，没有变量）、
    /// 暴涨（`OrbCmd.RemoveSlots`，代价是失去球位 —— 注意扩容是 **Add**Slots、变量名相同，不能按变量判断）、
    /// 幽魂形态（每回合掉敏捷写在 Power 内部，卡面只有一个「无实体」变量）。
    /// </summary>
    private static readonly HashSet<string> SacrificeCards = new(StringComparer.Ordinal)
    {
        "BIASED_COGNITION", "WRAITH_FORM", "HYPERBEAM", "BULLET_TIME", "BATTLE_TRANCE",
        "FRIENDSHIP", "NEUROSURGE", "BULK_UP",
        // 碎骨：牺牲奥斯提（它先攻击并给格挡，然后死去）
        "BONE_SHARDS",
    };

    /// <summary>数据里看不出「强化」的牌（例如「能量汲取」升级弃牌堆里的牌）。</summary>
    private static readonly HashSet<string> ForceBuffCards = new(StringComparer.Ordinal)
    {
        "DRAIN_POWER",
        "BEAT_INTO_SHAPE",   // 锻打成型：铸造（数据里没有可识别的增益变量）→ 用户要求给强化意图
        // 主宰（Dominate，三层易伤战）：给敌人 1 层易伤（策略）+ **按敌人身上易伤层数给自己加力量**（强化）。
        // 力量那份是运行时算的（`DynamicVar("StrengthPerVulnerable")`，不是 PowerVar<StrengthPower>）
        // ⇒ 自动认不出强化，只会显示策略。用户 2026-10-25：**两个都要显示**。
        "DOMINATE",
    };

    /// <summary>
    /// **不该给「强化」意图**的牌：它们的数据看起来像"给自己好处"，但用户不想要那个蓝色上箭头。
    /// 「如此甚好」（MAKE_IT_SO）就是 —— 它把打出的牌返手（用户实测：不该有强化意图）。
    /// </summary>
    private static readonly HashSet<string> NoBuffIntentCards = new(StringComparer.Ordinal)
    {
        "MAKE_IT_SO",
        // ★ 2026-11-10（用户：「第四回合女妖之嚎的意图应该是攻势+策略，而不是攻势+强化；
        //   第七回合的就只有攻势（没有策略也没有强化）」）：
        //   女妖之嚎（`BansheesCry`）只造成伤害、卡面上没有任何「给自己上能力」的变量，
        //   但 `ClassifyPowers` 把它身上的某个变量认成了增益 ⇒ 多出一条「强化」。
        //   这里点名禁掉；第 4 回合那条「策略」是剧本 `ScriptCard.ForceDebuffIntent` 单独画的。
        "BANSHEES_CRY",
    };

    /// <summary>
    /// 「给自己上能力，但牌面上不是 <c>PowerVar&lt;T&gt;</c>」的牌 —— <c>ClassifyPowers</c> 只认泛型
    /// <c>PowerVar&lt;&gt;</c>，这些牌自动识别不到，需要在这里点名补一个「强化」意图。
    ///
    /// · **残影（`BLUR`）**：反编译 `Blur.OnPlay` =
    ///   `CreatureCmd.GainBlock(Block)` + `PowerCmd.Apply&lt;BlurPower&gt;(…, DynamicVars["Blur"].BaseValue, …)`，
    ///   变量名是普通的 `Blur`（不是 `PowerVar&lt;BlurPower&gt;`）⇒ 自动认不出「强化」，
    ///   只显示守势（用户 2026-10-26 要求「为残影的意图添加强化，保留守势」）。
    /// </summary>
    private static readonly HashSet<string> BuffVarCards = new(StringComparer.Ordinal) { "BLUR" };

    /// <summary>数据里看不出「给玩家上负面」的牌（无处可逃/荒疫打击的灾厄是普通 DynamicVar 而不是 PowerVar，认不出来）。
    /// 注意「能量汲取」已从这张表里移除 —— 用户要求它不再显示策略意图。</summary>
    private static readonly HashSet<string> ForceDebuffCards = new(StringComparer.Ordinal)
    {
        "NO_ESCAPE", "BLIGHT_STRIKE",
        // 尖啸（PiercingWail）：变量名是普通的 StrengthLoss（不是 PowerVar），自动认不出来，
        // 会掉到"技能牌兜底 = 强化"上（用户实测：被标成蓝色上箭头，应该是红色下箭头）
        "PIERCING_WAIL",
        // 黑暗镣铐（DarkShackles）：反编译是 `new DynamicVar("StrengthLoss", 9)` + `PowerCmd.Apply<DarkShacklesPower>`
        // （普通 DynamicVar，不是 PowerVar）⇒ 同样自动认不出来，会显示成「强化」。
        // 用户 2026-10-24：它是给**敌人**扣力量的，应为策略（红色下箭头）。
        "DARK_SHACKLES",
        // 震荡波（Shockwave）：`PowerVar<WeakPower>` + `PowerVar<FrailPower>` 都是**正数**变量，
        // 自动判定会当成「强化」（用户 2026-11-05 实测：被标成蓝色上箭头）。它是给敌人上虚弱+易伤
        // ⇒ 应为策略（红色下箭头）。
        "SHOCKWAVE",
    };

    /// <summary>看这张牌施加的 power 是增益还是减益（数值为负的也算减益，如「下砸」减敌人力量）。</summary>
    private static (bool hasDebuff, bool hasBuff, bool hasSacrifice) ClassifyPowers(CardModel card)
    {
        bool debuff = false, buff = false, sacrifice = false;
        bool selfTargeted = TargetsSelf(card);

        try
        {
            if (SacrificeCards.Contains(card.Id.Entry)) sacrifice = true;
            if (ForceBuffCards.Contains(card.Id.Entry)) buff = true;
            if (ForceDebuffCards.Contains(card.Id.Entry)) debuff = true;
            if (Var(card, "HpLoss", 0) > 0m) sacrifice = true;

            foreach (var v in card.DynamicVars.Values)
            {
                var type = v.GetType();
                if (!type.IsGenericType) continue;
                if (type.GetGenericTypeDefinition() != typeof(PowerVar<>)) continue;

                var powerType0 = type.GetGenericArguments()[0];
                var power = ModelDb.GetById<PowerModel>(ModelDb.GetId(powerType0));
                if (power == null) continue;

                var amount = v.BaseValue;
                var kind = power.GetTypeForAmount(amount);
                bool harmful = kind == PowerType.Debuff || amount < 0m;
                bool beneficial = kind == PowerType.Buff && amount > 0m;

                // 「对自己不利」= 数据上像减益、且这张牌是往自己身上放 → 牺牲；否则才是给玩家的减益
                if (harmful && selfTargeted) sacrifice = true;
                else if (harmful) debuff = true;
                else if (beneficial) buff = true;
            }
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] power 分类失败（{card.Id.Entry}）: {e.Message}");
        }
        return (debuff, buff, sacrifice);
    }

    /// <summary>由奥斯提出手的牌：意图数字不吃 Boss 的力量/易伤。</summary>
    private static readonly HashSet<string> OstyAttackCards = new(StringComparer.Ordinal)
    {
        "UNLEASH", "BONE_SHARDS",
    };

    /// <summary>钙化的加伤（<c>CalcifyPower</c> 挂在她身上，原版是按 Additive 加到奥斯提的每次攻击上）。</summary>
    private static int CalcifyAdd(CardModel card)
    {
        try
        {
            return card.Owner?.Creature?
                .GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.CalcifyPower>() ?? 0;
        }
        catch { return 0; }
    }

    private static int _superMassiveLogs;

    /// <summary>
    /// 诊断：超质量体的意图数字到底从哪来（用户 2026-10-24：玩家印牌也会涨，还出现两个不一样的红剑）。
    ///
    /// 原版倍率（反编译 <c>Supermassive</c>）：
    /// <c>History.Entries.OfType&lt;CardGeneratedEntry&gt;().Count(c =&gt; c.Creator == card.Owner)</c>。
    /// 所以这里把「她自己印过几张 / 别人生成几张 / 我们的预补几次」摊开打出来，
    /// 一眼就能看出那多出来的伤害是历史计数还是预补带来的。
    /// </summary>
    private static void LogSupermassive(CardModel card, int damage, int extraDamage)
    {
        if (_superMassiveLogs++ >= 12) return;
        try
        {
            var owner = card.Owner;
            int mine = 0, others = 0;
            var history = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.History;
            if (history != null && owner != null)
            {
                foreach (var entry in history.Entries)
                {
                    if (entry is not MegaCrit.Sts2.Core.Combat.History.Entries.CardGeneratedEntry gen) continue;
                    if (ReferenceEquals(gen.Creator, owner)) mine++;
                    else others++;
                }
            }
            Logger.Log($"[CharBoss] 超质量体诊断 {card.Id.Entry}: 意图每段={damage}（含预补 {extraDamage}）" +
                       $" 历史里她自己印={mine} 别人生成={others} 牌主={owner?.Creature?.Name ?? "null"}" +
                       $"{(owner?.Creature?.Monster is Monsters.CharBossMonster ? "（是 Boss 自己）" : "（⚠ 不是 Boss 自己）")}");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 超质量体诊断失败: " + e.Message);
        }
    }

    /// <summary>
    /// 意图预览里「这一下会打在谁身上」—— **只能返回真人玩家**，拿不到就返回 null
    /// （null 会让 <c>BossDamagePredictor</c> 自己兜底，最后才退到 dealer）。
    ///
    /// ★ v0.2.372（用户 2026-11-04 录屏：**玩家回合里**刀刃之舞也变成 4×3）：
    ///   日志实证同一秒里 `小刀预览` 在 4 / 6 之间来回跳，而两条都打「目标=静默猎手」——
    ///   因为**她自己也是静默猎手**。v0.2.370 我加的 `?? target` 就是元凶：
    ///   刀刃之舞是**无目标技能牌**，`BossPlanner.ChooseTarget` 给它的 `target` 就是**她自己**
    ///   ⇒ 受害者变成她本人（身上没有易伤）⇒ 4。
    ///   现在改成三级兜底，每一步都校验「不是怪物」（真人玩家 `Monster == null`；
    ///   我们的 Boss 生物挂着影子 Player、`Monster != null`）：
    ///     ① 意图节点给的目标列表里的真人玩家；
    ///     ② 本机玩家（`LocalContext.GetMe`）；
    ///     ③ 她跟踪锁定的那位。
    ///   **不再使用**规划期那个 `target`。
    /// </summary>
    private static Creature? ResolvePreviewVictim(Monsters.CharBossMonster? boss,
                                                   IEnumerable<Creature>? targets,
                                                   CardModel? card)
    {
        var fromTargets = FirstPlayerTarget(targets);
        if (fromTargets != null) return fromTargets;

        try
        {
            var combat = boss?.CombatState ?? card?.Owner?.Creature?.CombatState;
            if (combat != null)
            {
                var me = MegaCrit.Sts2.Core.Context.LocalContext.GetMe(combat)?.Creature;
                if (me != null && me.IsAlive && me.Monster == null) return me;
            }
        }
        catch { /* 忽略 */ }

        try
        {
            if (boss != null)
            {
                var locked = Core.BossLockOn.PeekLock(boss)?.Creature;
                if (locked != null && locked.IsAlive && locked.Monster == null) return locked;
            }
        }
        catch { /* 忽略 */ }

        return null;
    }

    /// <summary>
    /// 意图的目标里挑「真正会挨打的那位玩家」—— 动态意图重算时当作 <c>Hook.ModifyDamage</c> 的 target
    /// （易伤、格挡之外的减伤都挂在被打的人身上）。挑不到就返回 null，让调用方退回她本人。
    /// </summary>
    private static Creature? FirstPlayerTarget(IEnumerable<Creature>? targets)
    {
        if (targets == null) return null;
        try
        {
            foreach (var creature in targets)
            {
                if (creature == null || !creature.IsAlive) continue;
                // ★ v0.2.371（用户 2026-11-04 录屏：刀刃之舞在**她的回合**显示 4×3，同一帧冲刺却是 19）
                //   **致命细节**：我们这只 Boss 的生物挂在**影子 Player** 身上 ⇒ `creature.Player != null`
                //   也成立！所以只判 `Player == null` 会把她自己当成「玩家」（她的回合里意图节点给的
                //   targets 往往就包含她自己）⇒ victim = 她本人 ⇒ 她身上没有易伤/虚弱 ⇒ 数字偏低。
                //   原版 `LiveSingleAttackIntent` 的判据是 `t.Monster != null` 就跳过（真人玩家没有 Monster）
                //   ⇒ 这里照抄：**有 Monster 的一律不是玩家**。
                if (creature.Monster != null) continue;
                if (creature.Player == null) continue;     // 只认玩家侧
                return creature;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>
    ///     /// <summary>
    /// 会往自己手里塞「小刀」的牌（静默猎手，反编译逐个核对过变量名）：
    ///   · <c>BladeDance</c> / <c>CloakAndDagger</c> 用 <c>Cards</c> 变量当刀数；
    ///   · <c>HiddenDaggers</c> / <c>FanOfKnives</c> / <c>LeadingStrike</c> 用 <c>Shivs</c> 变量。
    /// 白名单是必须的：<c>Cards</c> 这个键在别的牌上是「抽几张」（如 BladeSymphony），不能一概而论。
    /// </summary>
    public static readonly HashSet<string> ShivGeneratorCards = new(StringComparer.Ordinal)
    {
        "BLADE_DANCE", "CLOAK_AND_DAGGER", "HIDDEN_DAGGERS", "FAN_OF_KNIVES", "LEADING_STRIKE",
    };

    /// <summary>小刀的伤害基础值（<c>Shiv</c> 的 <c>DamageVar(4)</c>；升级 +2，但我们造的都是未升级刀）。</summary>
    public const int ShivBaseDamage = 4;

    /// <summary>每只 Boss 一份「小刀」卡（只用于预览时过伤害管道，缓存避免每次渲染都新建）。</summary>
    private static readonly Dictionary<Monsters.CharBossMonster, MegaCrit.Sts2.Core.Models.CardModel> ShivCardCache = new();

    private static MegaCrit.Sts2.Core.Models.CardModel? ShivCardFor(Monsters.CharBossMonster? boss)
    {
        try
        {
            if (boss == null) return null;
            if (ShivCardCache.TryGetValue(boss, out var cached) && cached != null) return cached;

            var shadow = boss.Shadow;
            var combat = boss.CombatState;
            if (shadow == null || combat == null) return null;

            var card = combat.CreateCard<MegaCrit.Sts2.Core.Models.Cards.Shiv>(shadow);
            if (card != null) ShivCardCache[boss] = card;
            return card;
        }
        catch { return null; }
    }

    /// <summary>
    /// **每把小刀**的伤害（已含力量/虚弱/易伤，以及「一心化万」这类**按牌判定**的增伤）。
    ///
    /// 为什么要专门拿一张「小刀」卡过管道（用户 2026-10-29）：
    /// 「涂毒猎的刀刃之舞没有考虑一心化万的增伤（它本身 1 费，但它印的小刀是 0 费）」——
    /// `OneForAllPower.ModifyDamageAdditive` 判的是 `cardSource`（即**被强化的那张牌**）的费用，
    /// 而小刀是 0 费攻击牌 ⇒ 每把 +N。以前这里用的是「4 + 待到手力量」再交给意图节点，
    /// 节点那一路 `cardSource` 是 null（原版自己的写法）⇒ 这条增伤永远算不到。
    /// </summary>
    private static int ShivDamagePerHit(Monsters.CharBossMonster? boss, int pendingRise, Creature? target,
                                        int shivBase = ShivBaseDamage)
    {
        int baseDamage = Math.Max(0, shivBase + Math.Max(0, pendingRise));
        try
        {
            // ★ v0.2.365（用户 2026-11-04：「玩家有易伤，刀刃之舞应该显示 6×3 而非 4×3」）：
            //   这条意图是 **Raw**（不吃意图节点自己的 Hook.ModifyDamage），所以力量/虚弱/**易伤**
            //   必须在这里一次算全。以前只有「拿得到真的 Shiv 卡实例」时才走
            //   `FinalDamage(shiv, …)`，拿不到就**直接返回牌面值 4** ⇒ 玩家挂着易伤也只显示 4×3。
            //   现在改成 `FinalDamageFor(dealer = 她, cardSource = 那把 Shiv 或 null, …)`：
            //   dealer 显式给，所以即使造不出 Shiv 卡实例，目标易伤 / 她的力量（虚弱）**照样算进去**。
            var shiv = ShivCardFor(boss);
            int result = BossDamagePredictor.FinalDamageFor(boss?.Creature, shiv, baseDamage, target);
            if (_shivLogs++ < 40)
            {
                int vuln = 0;
                try { vuln = target?.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.VulnerablePower>() ?? 0; }
                catch { vuln = 0; }
                Logger.Log($"[CharBoss] 小刀预览: 牌面 {baseDamage} → {result}" +
                           $"（Shiv卡实例={(shiv != null ? "有" : "无")}，目标易伤={vuln}，目标={target?.Name ?? "?"}）");
            }
            return Math.Max(0, result);
        }
        catch { return baseDamage; }
    }

    /// <summary>
    /// 这张**生成小刀的牌**印出来的小刀是**升级版**吗（升级版小刀 6 点，普通 4 点）。
    ///
    /// ★ v0.2.375（用户 2026-11-04：刀贼第 4 回合两把小刀实际打 **18 / 9**）：
    ///   反编译 `HiddenDaggers.OnPlay`：
    ///   <code>
    ///   await Shiv.CreateInHand(Owner, DynamicVars["Shivs"].IntValue, CombatState);
    ///   if (IsUpgraded) foreach (var current in cards) CardCmd.Upgrade(current);   // ← 升级版给升级刀
    ///   </code>
    ///   而 `Shiv.CanonicalVars` = `new DamageVar(4m)`、`OnUpgrade` = `UpgradeValueBy(2m)` ⇒ **6**。
    ///   其它生成刀的牌（刀刃之舞 / 斗篷与匕首 / 先制打击 / 刀扇）升级时改的是**张数或别的数值**，
    ///   小刀本身仍是 4 点 ⇒ 只有「隐秘匕首」按升级状态取 6。
    ///   ⚠ 以后原版若再出现「升级后给升级刀」的牌，在这里加一条即可。
    /// </summary>
    internal static bool GeneratesUpgradedShivs(CardModel? card)
    {
        try
        {
            if (card == null) return false;
            // ★ v0.2.522（用户 2026-11-09：「隐秘匕首+ 和钢铁风暴+ 给的小刀是升级的，基础伤害为 6」）：
            //   这两张牌的**升级版**生成的是升级小刀（Shiv 4 → 6），预览必须跟着 +2。
            string entry = SafeEntry(card);
            bool upgradedGenerator = string.Equals(entry, "HIDDEN_DAGGERS", StringComparison.Ordinal)
                                     || string.Equals(entry, "STORM_OF_STEEL", StringComparison.Ordinal);
            return upgradedGenerator && card.IsUpgraded;
        }
        catch { return false; }
    }

    /// <summary>
    /// **不含幻影之刃加成**的每把小刀伤害。
    ///
    /// 幻影之刃（`PhantomBladesPower.ModifyDamageAdditive`）会给小刀加成，
    /// 而它在「真的挂到她身上」之后，原版伤害管道就已经把它算进 `ShivDamagePerHit` 的结果里了。
    /// 这里把**真正挂着的层数**减回去，得到干净的每刀伤害，
    /// 再由调用方按「每回合第一把 +N、其余不加」的口径重新加一次 ——
    /// 这样无论那一刻 power 在不在身上，都**只会加一次**，也不会出现负数（v0.2.374 的修复）。
    /// </summary>
    private static int CleanShivDamage(Monsters.CharBossMonster? boss, int rise, Creature? victim,
                                        int shivBase = ShivBaseDamage)
    {
        int per = ShivDamagePerHit(boss, rise, victim, shivBase);
        int phantomReal = 0;
        try { phantomReal = Math.Max(0, Core.PhantomShivs.AmountOf(boss)); }
        catch { phantomReal = 0; }
        return Math.Max(0, per - phantomReal);
    }

    /// <summary>小刀预览的日志条数（意图每帧重算，别刷屏）。</summary>
    private static int _shivLogs;

    /// <summary>
    /// 这张牌是不是**群攻**（对所有敌人）—— 群攻打到每个人，所以「意图按人清零」不能作用在它身上。
    /// </summary>
    private static bool IsAoE(CardModel card)
    {
        try { return card.TargetType == TargetType.AllEnemies; }
        catch { return false; }
    }

    /// <summary>这位 Boss 打出的**小刀**现在是不是群攻（她身上有「刀扇」的 FanOfKnivesPower 时是）。</summary>
    private static bool ShivsHitAll(CardModel card)
    {
        try
        {
            var owner = card?.Owner?.Creature;
            return owner != null &&
                   owner.GetPower<MegaCrit.Sts2.Core.Models.Powers.FanOfKnivesPower>() != null;
        }
        catch { return false; }
    }

    /// <summary>
    /// 「**随机目标**的攻击（群蛇形态、闪电球这类）会不会落在本地玩家头上」。
    ///
    /// 依据：多人「跟踪锁定」的文案就是「{Boss}的单体攻击、**随机攻击**和充能球只会攻击你」，
    /// 而且 `HittableEnemies` 在 Boss 出手时被收窄到被锁定的那位 ⇒ 随机攻击实际只打他。
    /// 所以：多人且本地玩家**不是**这只 Boss 锁定的那位 → true（该对本地显示 0）。
    /// 单人恒 false（原样，零回归）。
    /// </summary>
    internal static bool RandomAimIsElsewhere(CardModel? card)
    {
        try { return RandomAimIsElsewhere(card?.Owner?.Creature?.Monster as Monsters.CharBossMonster); }
        catch { return false; }
    }

    /// <summary>
    /// 同上，但直接按**Boss**判定 —— 给「不属于任何一张牌」的无来源单体伤害用
    /// （典型：闪电 / 玻璃充能球的**被动**，见 <c>BossHandPanel.OrbPassiveDamage</c>）。
    ///
    /// 用户 2026-10-29：「故障机器人的闪电充能球被动应该只在被它锁定的人显示正确的伤害，
    /// 但它现在对所有人显示伤害（这和冰雹风暴、炸弹不同，这个无来源伤害是单体的）」——
    /// 冰雹风暴 / 炸弹打的是全体，所以每个客户端都该显示真实数字；球被动是单体随机 ⇒ 只有被锁的人会挨。
    /// </summary>
    internal static bool RandomAimIsElsewhere(Monsters.CharBossMonster? boss)
    {
        try
        {
            var combat = boss?.CombatState;
            if (combat == null) return false;
            if (Core.BossLockOn.RealPlayerCount(combat) < 2) return false;   // 单人：原样

            var locked = Core.BossLockOn.PeekLock(boss);
            if (locked?.Creature == null) return false;   // 还没分配锁 ⇒ 不判（宁可显示真实数字）

            var local = MegaCrit.Sts2.Core.Context.LocalContext.GetMe(combat);
            if (local?.Creature == null) return false;
            return !ReferenceEquals(locked, local);
        }
        catch { return false; }
    }
    /// <summary>
    /// 「这张牌对**本地玩家**来说是不是 0 伤害」。优先级：
    ///   1. 这只 Boss **此刻锁着谁**（`BossLockOn.PeekLock`，只读、不产生副作用）——
    ///      出牌也按它走（见 `CharBossMonster.PlayOne`），所以显示必须跟它一致；
    ///   2. 没有锁（比如开局玩家还没到齐、或这只 Boss 还没分配）→ 退回旧判据
    ///      <see cref="AimsElsewhere"/>（看计划里的 target），至少不会把真实伤害误判成 0。
    /// </summary>
    private static bool AimedAwayFromLocal(CardModel? card, Creature? target)
    {
        try
        {
            var boss = card?.Owner?.Creature?.Monster as Monsters.CharBossMonster;
            var combat = boss?.CombatState;
            if (boss != null && combat != null && Core.BossLockOn.RealPlayerCount(combat) >= 2)
            {
                var locked = Core.BossLockOn.PeekLock(boss);
                var local = MegaCrit.Sts2.Core.Context.LocalContext.GetMe(combat);
                if (locked?.Creature != null && local?.Creature != null)
                    return !ReferenceEquals(locked, local);
            }
        }
        catch { /* 忽略：退回旧判据 */ }
        return AimsElsewhere(target);
    }

    /// <summary>
    /// F「意图按人清零」的判定：这条意图的目标是不是**别的玩家**（对本地玩家来说打不到自己）。
    ///
    /// 为什么只能在渲染侧做：原版 <c>MoveState.Intents</c> 是**每只生物一份、不分观看者**，
    /// 所以「对某人显示 0」这件事没法写进原版字段，只能在我们自己的手牌栏意图里清零。
    ///   · 单人（真人玩家 &lt; 2）→ 恒 false（原样，零回归）；
    ///   · 目标是本地玩家 / 目标不是玩家生物（随从等）→ false；
    ///   · 目标是**另一名**真人玩家 → true。
    /// </summary>
    private static bool AimsElsewhere(Creature? target)
    {
        try
        {
            if (target == null) return false;
            var combat = target.CombatState;
            if (combat == null) return false;
            if (target.Monster != null) return false;             // 目标不是玩家（随从/奥斯提等）
            if (Core.BossLockOn.RealPlayerCount(combat) < 2) return false;   // 单人：原样

            var local = MegaCrit.Sts2.Core.Context.LocalContext.GetMe(combat)?.Creature;
            if (local == null) return false;
            return !ReferenceEquals(target, local);
        }
        catch { return false; }
    }

    /// <summary>
    /// 施法者的「队友 Boss」—— 同一阵营里**其它** CharBossMonster（不含施法者自己，也不含真人玩家）。
    ///
    /// 用于多人换牌 A3（刀刃交响曲）：原版 <c>GetTeammatesOf</c> 返回的是「同阵营全体」，
    /// 里面既有其它 Boss 也有真人玩家；我们只要 Boss 那几只（效果绝不落到玩家身上）。
    /// 单人局（场上只有一只 Boss）恒为空列表 ⇒ 这条意图不会出现，零回归。
    /// </summary>
    /// <summary>
    /// 「刀刃交响曲」会出刀的那些 Boss：**先她自己，再各队友**（原版这张牌给的是「每个队友（含自己）」）。
    /// 意图与真正的出刀都走这一个列表，免得两边不一致。
    /// </summary>
    public static List<Monsters.CharBossMonster> VolleyBossesOf(CardModel card)
    {
        var list = new List<Monsters.CharBossMonster>();
        try
        {
            var self = card?.Owner?.Creature?.Monster as Monsters.CharBossMonster;
            if (self != null) list.Add(self);
            foreach (var mate in TeammateBossesOf(card))
            {
                if (!list.Contains(mate)) list.Add(mate);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 取刀刃交响曲出刀者失败: " + e.Message);
        }
        return list;
    }

    public static List<Monsters.CharBossMonster> TeammateBossesOf(CardModel card)
    {
        var result = new List<Monsters.CharBossMonster>();
        try
        {
            var owner = card?.Owner?.Creature;
            if (owner?.CombatState == null) return result;

            foreach (var c in owner.CombatState.GetTeammatesOf(owner))
            {
                if (c == null || !c.IsAlive) continue;
                if (ReferenceEquals(c, owner)) continue;
                // ⚠ **不能**再要求 c.IsPlayer：影子 Player 不在 CombatState.PlayerCreatures 里
                //    （实测依据见 LegionOfBonePlayPatch 的注释），按 Monster 类型判定才准。
                if (c.Monster is Monsters.CharBossMonster other && !result.Contains(other)) result.Add(other);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 取队友 Boss 失败: " + e.Message);
        }
        return result;
    }

    /// <summary>这张牌会塞几张小刀（0 = 不是小刀牌）。</summary>
    public static int ShivsFrom(CardModel card)
    {
        try
        {
            if (!ShivGeneratorCards.Contains(card.Id.Entry)) return 0;
            int n = (int)Math.Round(Var(card, "Shivs", -1));
            if (n < 0) n = (int)Math.Round(Var(card, "Cards", 0));
            return Math.Max(0, n);
        }
        catch { return 0; }
    }

    /// <summary>
    /// 会**抽牌**的牌（静默猎手，反编译逐张核对过变量名）：抽几张就是牌面的 `Cards` 变量。
    /// 白名单是必须的 —— `Cards` 在「隐秘匕首」上是**弃几张**、在「刀刃交响曲」上是"给几个队友"，
    /// 一概当成抽牌会凭空多出一条群伤意图。
    /// </summary>
    private static readonly HashSet<string> DrawCards = new(StringComparer.Ordinal)
    {
        "PREPARED",      // 早有准备：抽 Cards（1，升级 2）
        "ACROBATICS",    // 杂技：抽 Cards（3，升级 4）
        "BACKFLIP",      // 后空翻：抽 2
        "REFLEX",        // 本能反应：抽 Cards（2，升级 3）
    };

    /// <summary>这张牌会抽几张（不在白名单里 = 0）。给「速行者」的群伤意图用。</summary>
    public static int DrawCountOf(CardModel card)
    {
        try
        {
            if (card == null) return 0;
            if (!DrawCards.Contains(card.Id.Entry)) return 0;
            return Math.Max(0, (int)Math.Round(Var(card, "Cards", 0)));
        }
        catch { return 0; }
    }

    /// <summary>
    /// 她身上「速行者」（<c>SpeedsterPower</c>）每抽一张牌的群伤（没有这层能力就是 0）。
    ///
    /// 反编译 `Speedster`：`PowerVar&lt;SpeedsterPower&gt;(2)`，**升级只加「固有」**（`AddKeyword(Innate)`）
    /// ⇒ 每抽一张恒为 2 点，和用户写的「抽牌数 × 2」一致。
    /// </summary>
    private static int SpeedsterAmountOf(CardModel card)
    {
        try
        {
            return Math.Max(0, card?.Owner?.Creature?
                .GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.SpeedsterPower>() ?? 0);
        }
        catch { return 0; }
    }

    /// <summary>
    /// 「这张牌会消耗 1 张牌，而她又带着卡戎之灰」时，返回那 3 点群攻伤害；否则 0。
    ///
    /// 反编译依据：
    ///   · 烙印 <c>Brand</c> / 坚毅 <c>TrueGrit</c> 的 `OnPlay` 里都有
    ///     `CardSelectCmd.FromHand(CardSelectorPrefs.ExhaustSelectionPrompt, 1)` → 消耗 1 张手牌；
    ///   · 卡戎之灰 <c>CharonsAshes.AfterCardExhausted</c>：
    ///     `CreatureCmd.Damage(ctx, Owner.Creature.CombatState.HittableEnemies, DynamicVars.Damage.BaseValue, Unpowered, Owner.Creature)`
    ///     —— 对所有敌人（= 玩家侧）3 点、不吃力量、可被格挡。
    /// 数值直接读遗物的 `Damage` 变量（基础 3），读不到退回 3。
    /// </summary>
    private static int CharonsAshesDamageOf(CardModel card)
    {
        try
        {
            string entry = SafeEntry(card);
            if (entry != "BRAND" && entry != "TRUE_GRIT") return 0;   // 目前只有这两张是「消耗 1 张牌」

            var relics = card.Owner?.Relics;
            for (int i = 0; i < (relics?.Count ?? 0); i++)
            {
                var relic = relics![i];
                if (relic == null) continue;
                string relicEntry;
                try { relicEntry = relic.Id.Entry; } catch { continue; }
                if (relicEntry != "CHARONS_ASHES") continue;

                try
                {
                    if (relic.DynamicVars.TryGetValue("Damage", out var v) && v != null)
                        return Math.Max(0, (int)Math.Round((double)v.BaseValue));
                }
                catch { /* 用默认 3 */ }
                return 3;
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    /// <summary>
    /// 她的本回合计划里，**排在这张牌前面、还没打出来**的牌有几张。
    /// 出牌期 <see cref="Monsters.CharBossMonster.Plan"/> 是实时收缩的（打一张就 Remove 一张），
    /// 所以这个数天然就是「还差几张」。读不到就返回 0。
    /// </summary>
    /// <summary>本回合排在 <paramref name="card"/> 之前**已经打出/已计划**的技能牌张数。</summary>
    private static int CountSkillsBeforeInPlan(Monsters.CharBossMonster? boss, CardModel card, int fallbackBefore)
    {
        try
        {
            var plan = boss?.Plan;
            if (plan != null && plan.Count > 0)
            {
                int n = 0;
                foreach (var play in plan)
                {
                    if (play == null) continue;
                    if (ReferenceEquals(play.Card, card)) return n;
                    if (Core.StatusBossMath.IsSkill(play.Card)) n++;
                }
            }
        }
        catch { /* 忽略 */ }
        return Math.Max(0, fallbackBefore);
    }
    /// <summary>
    /// 把「致死性」的 +<paramref name="pct"/>% 乘到**已经跑完原版伤害管道**的单段伤害上。
    ///
    /// 顺序必须是「力量/虚弱/易伤 → 致死性」，与真实结算同序（`(5 + 力量 -1) × 1.5 = 6`）；
    /// 取整用**向下取整**（原版伤害一律 floor，见 <see cref="BossDamagePredictor.FloorToInt"/>），
    /// 用 `Math.Round` 会在 `.5` 处多给 1 点（用户实测 5×1.5=8 而非 7 的同类问题）。
    /// </summary>
    private static int ApplyPct(int damage, int pct)
    {
        if (damage <= 0 || pct == 0) return Math.Max(0, damage);
        return Math.Max(0, (int)Math.Floor(damage * (100.0 + pct) / 100.0));
    }

    private static int PlannedCardsBefore(Monsters.CharBossMonster? boss, CardModel card)
        => PlannedCardsBefore(boss, card, out _);

    /// <summary>
    /// 「亡魂牵引」的段数补正：**本回合排在它前面、且带「虚无」关键字的牌**有几张。
    ///
    /// 反编译 `PullFromBelow`：`CalculatedHits` 数的是战斗历史里
    /// `e.CardPlay.Player == 卡主 &amp;&amp; e.WasEthereal` 的条目数。而意图是在**玩家回合**里算的 ——
    /// 那一刻她本回合一张牌都还没打，历史里没有「致死性」那张虚无牌 ⇒ 预览少一段。
    /// （用户 2026-11-10：「没有把致死性这张虚无牌算上」，实测预览 3 段、真实 4 段。）
    ///
    /// ★ 为什么放在**这里**而不是由规划器传参：这个数会被**三条路**各算一次
    ///   （规划期 / `BladesGrowth.RecomputeAll` / 有球 Boss 的 `RefreshPlannedIntents`），
    ///   传参只要漏一条，面板显示就不对（v0.2.537 就是这样丢的）。按 `boss.Plan` 自己数，
    ///   三条路都自动一致。
    /// </summary>
    private static int PlannedEtherealBefore(Monsters.CharBossMonster? boss, CardModel card)
    {
        try
        {
            var plan = boss?.Plan;
            if (plan == null || card == null) return 0;
            int n = 0;
            foreach (var play in plan)
            {
                if (play?.Card == null) continue;
                if (ReferenceEquals(play.Card, card)) return n;
                bool ethereal = false;
                try
                {
                    ethereal = play.Card.Keywords != null
                               && play.Card.Keywords.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Ethereal);
                }
                catch { ethereal = false; }
                if (ethereal) n++;
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    /// <summary>同上，另外告诉调用方「这张牌还在计划里吗」（不在 ⇒ 调用方用自己的兜底值）。</summary>
    private static int PlannedCardsBefore(Monsters.CharBossMonster? boss, CardModel card, out bool inPlan)
    {
        inPlan = false;
        try
        {
            var plan = boss?.Plan;
            if (plan == null || card == null) return 0;
            int n = 0;
            foreach (var play in plan)
            {
                if (play == null) continue;
                if (ReferenceEquals(play.Card, card)) { inPlan = true; return n; }
                n++;
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    /// <summary>
    /// 诊断用：把「死亡行军 / 灵魂风暴 / 出击」**显示出来的**伤害记一行日志 —— **只在数值真的变了时记**。
    /// 用户反馈「意图预测不准 / 伤害来回闪」，有了这条一眼就能看出：显示值在哪一步跳、跳了多少、
    /// 和实际打出的伤害差多少（配合 `done <牌>` 与下一张 `PLAY` 里的 hp）。
    /// 同时把**输入**也摊开：死亡行军数本回合抽牌条目、灵魂风暴数消耗牌堆里的灵魂、出击读奥斯提生命 ——
    /// 这样下次对不上时不用再猜是公式错还是输入错。
    /// </summary>
    private static void LogSpecialDamageOnce(CardModel card, int damage, int extraDamage, int hits)
    {
        try
        {
            var entry = SafeEntry(card);
            if (entry != "DEATH_MARCH" && entry != "SOUL_STORM" && entry != "UNLEASH") return;
            if (SpecialDamageLogged.TryGetValue(entry, out int last) && last == damage) return;
            SpecialDamageLogged[entry] = damage;

            string input = entry switch
            {
                "DEATH_MARCH" => $"本回合抽牌条目={DrawnThisTurnCount(card)}",
                "SOUL_STORM" => $"消耗牌堆灵魂={SoulsInExhaust(card)}",
                "UNLEASH" => $"奥斯提生命={OstyHp(card)}",
                _ => string.Empty,
            };
            Logger.Log($"[CharBoss] 意图数值 {entry}: 显示 {damage}（预补 {extraDamage}，段数 {hits}，{input}）");
        }
        catch { /* 忽略 */ }
    }

    /// <summary>死亡行军数的那一类条目：本回合、她当 Actor、不是「回合开始补手牌」。</summary>
    private static int DrawnThisTurnCount(CardModel card)
    {
        try
        {
            var combat = card.CombatState;
            var owner = card.Owner?.Creature;
            if (combat == null || owner == null) return -1;
            int n = 0;
            foreach (var e in MegaCrit.Sts2.Core.Combat.CombatManager.Instance.History.Entries)
            {
                if (e is MegaCrit.Sts2.Core.Combat.History.Entries.CardDrawnEntry d
                    && d.HappenedThisTurn(combat) && d.Actor == owner && !d.FromHandDraw) n++;
            }
            return n;
        }
        catch { return -1; }
    }

    /// <summary>灵魂风暴数的那一类：她消耗牌堆里的「灵魂」张数。</summary>
    private static int SoulsInExhaust(CardModel card)
    {
        try
        {
            var pile = card.Owner?.PlayerCombatState?.ExhaustPile?.Cards;
            if (pile == null) return -1;
            int n = 0;
            foreach (var c in pile) if (c is MegaCrit.Sts2.Core.Models.Cards.Soul) n++;
            return n;
        }
        catch { return -1; }
    }

    private static int OstyHp(CardModel card)
    {
        try
        {
            var osty = card.Owner?.Osty;
            return osty != null && osty.IsAlive ? osty.CurrentHp : 0;
        }
        catch { return -1; }
    }

    /// <summary>上次记过日志的「显示伤害」（按牌名），用来去重，避免每次重算都刷一行。</summary>
    private static readonly System.Collections.Generic.Dictionary<string, int> SpecialDamageLogged = new();

    /// <summary>她自己（影子玩家）牌堆里的**状态牌**数量（不含消耗牌堆）——「散射炮」的段数。</summary>
    private static int CountOwnStatusCards(Monsters.CharBossMonster? boss)
    {
        try
        {
            var pcs = boss?.Shadow?.PlayerCombatState;
            if (pcs == null) return 0;
            int n = 0;
            foreach (var c in pcs.AllCards)
            {
                try
                {
                    if (c == null) continue;
                    if (c.Type != MegaCrit.Sts2.Core.Entities.Cards.CardType.Status) continue;
                    if (c.Pile != null && c.Pile.Type == MegaCrit.Sts2.Core.Entities.Cards.PileType.Exhaust) continue;
                    n++;
                }
                catch { /* 忽略 */ }
            }
            return n;
        }
        catch { return 0; }
    }
    private static string SafeEntry(CardModel card)
    {
        try { return card.Id.Entry; } catch { return string.Empty; }
    }

    private static bool TargetsSelf(CardModel card)
    {
        try { return card.TargetType == TargetType.Self; }
        catch { return false; }
    }

    private static decimal Var(CardModel card, string key, decimal fallback)
    {
        try
        {
            if (card.DynamicVars.TryGetValue(key, out var v) && v != null) return v.BaseValue;
        }
        catch { /* 忽略 */ }
        return fallback;
    }

    /// <summary>把一张牌实际带的动态变量名打出来（设计卡组时核对数值用）。</summary>
    public static string DescribeVars(CardModel card)
    {
        try
        {
            var parts = new List<string>();
            foreach (var key in card.DynamicVars.Keys)
            {
                if (card.DynamicVars.TryGetValue(key, out var v) && v != null)
                {
                    parts.Add($"{key}={v.BaseValue}");
                }
            }
            return $"{card.Id.Entry}[{string.Join(",", parts)}]";
        }
        catch
        {
            return card.Id.Entry;
        }
    }
}
