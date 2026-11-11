using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 按**剧本**出牌（<see cref="BossPlanner"/> 的剧本分支，拆出来纯粹是为了单文件 200 行以内）。
///
/// 和贪心 AI 的区别：手牌是 <see cref="BossScriptRunner.BuildHand"/> 按剧本造出来的，
/// 这里只按剧本顺序逐个尝试 —— 能量不够就跳过（这正是设计里那种「这张牌没费打不了」）。
/// 意图因此完全固定、可复现。
/// </summary>
public static class BossScriptPlanner
{
    public static List<PlannedPlay> Plan(CharBossMonster boss, PlayerCombatState pcs,
                                         BossTurn turn, int turnNumber, int spentThisTurn = 0)
    {
        // ★ 2026-11-05（状态机）：本回合到目前为止「她生成的状态牌张数」与「已打出的技能牌数」，
        //   用来把烟囱 / 开信刀的伤害挂到**触发它的那张牌**底下，并让散射炮算上本回合将生成的状态牌。
        int statusesThisTurn = 0;
        int skillsThisTurn = 0;
        int smStack = Core.StatusBossMath.SmokestackDamage(boss);
        // ★ v0.2.411（用户实测截图：第 1 回合「强撑」没有 5×2）：
        //   意图是**规划期**算的，而第 1 回合「烟囱」是她这一回合的第一张牌 ——
        //   那一刻 `SmokestackPower` 还没上身，`SmokestackDamage` 读到 0 ⇒ 排在它后面的「强撑」
        //   （2 张伤口）一个 5×2 都挂不上。所以按「本回合**已经计划**打出的牌」实时跟踪：
        //   计划到「烟囱」那一刻起，后面的牌才开始按它的卡面数值算伤害（与致死性/黑洞同一套前瞻口径）。
        // ★ 2026-11-05（用户）：「也要提前计算本回合会召唤的仆从」——
        //   仆从牌是先召唤、召唤满了之后才按原版结算伤害/格挡。
        //   所以按剧本顺序累加：起始 = 场上现有仆从数；每遇到一张「会召唤」的仆从牌就 +1。
        int plannedMinions = 0;
        try { plannedMinions = Monsters.BossMinionMonster.CountAlive(boss); } catch { plannedMinions = 0; }
        int smStackThisTurn = smStack;
        int loDamage = Core.StatusBossMath.LetterOpenerDamage(boss);
        var result = new List<PlannedPlay>();
        // ★★ v0.2.532（玩家 2026-11-09：「灵魂骨第一回合会打切断，可她那会儿还没打出自动化、费用根本不够」）：
        //   `CardCmd.AutoPlay` **不扣费** ⇒ 她打完牌之后 `pcs.Energy` 一直是「本回合的总能量」原值。
        //   而「回合中又拿到资源 → 按剧本补打」会在**回合中途**再跑一次本函数：
        //   已经打出去的牌因为「不在手牌」被跳过、**不记这笔记账** ⇒ 它们的费用等于被退还
        //   ⇒ 剩下的 切断(2)/防御(1) 在新预算里都「打得起了」，于是第一回合凭空多打两张。
        //   实测日志（`mod_log.txt` 125461–125463 行）：
        //     [14:45:21] 剧本第 1 回合: 打 [SEVERANCE,DEFEND_NECROBINDER] 跳过 [Delay(不在手牌),Automation(不在手牌),Dirge(不在手牌)] 余费=0
        //     [14:45:21] 回合中又拿到资源 → 按剧本补打： [SEVERANCE,DEFEND_NECROBINDER]（余费 3）   ← 3 是**没扣过**的总能量
        //   修法：补打时把「本回合已经花掉的能量」从预算里扣掉（`spentThisTurn`，由
        //   `CharBossMonster.ScriptEnergySpent` 逐张累加）。真正中途到账的能量照样算得进来。
        int energy = Math.Max(0, pcs.Energy - Math.Max(0, spentThisTurn));
        int energyAtStart = energy;      // 螺旋钻击的段数 = 本回合已花费的能量之和（见下面的 HELIX_DRILL）
        // ⚠ 规划发生在**玩家回合**，而「下回合辉星」（StarNextTurnPower）要到他回合开始才到账
        // ⇒ 规划期的星预算必须把这笔算进来，否则第 8 回合的「七星（7 星）」会被判成星不够而不打
        //（用户 2026-10-28 实测：第 8 回合意图里直接没了七星）。
        //
        // ★ 2026-11-10（用户：「第一回合选隐秘藏品的情况下，第二回合的辐射没有计算隐秘藏品在
        //   回合开始时给的星」）：这笔「下回合 +N 星」是**她回合开始时到账**的 ⇒ 对本回合而言它就是
        //   「本回合获得过的辉星」，辐射（`Radiate`）的段数必须把它算上。
        //   所以它同时进 `stars`（预算）与 `starsGainedThisTurn`（辐射的段数口径）。
        int starNextTurn = 0;
        try { starNextTurn = Math.Max(0, boss.Creature?.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.StarNextTurnPower>() ?? 0); }
        catch { starNextTurn = 0; }
        int stars = pcs.Stars + starNextTurn;
        // ★ 2026-11-10（三层「抉择储」）：**本回合她获得过多少辉星**（辐射的段数靠它）。
        int starsGainedThisTurn = starNextTurn;
        var hand = pcs.Hand.Cards.ToList();
        var used = new HashSet<CardModel>();
        var skipped = new List<string>();

        BossCardSelector.ClearPreferDiscard(boss, turnNumber);   // 只清这一回合的点名单（其它回合各存一份）
        boss.DisplayPlays.Clear();               // 展示牌的「会打出去」名单也每回合重填
        // 「致死性」的意图补正要认「本回合第一张攻击牌」，规划期由这里重填（见下面写入处）
        boss.PlannedFirstAttackCard = null;

        // 「子程序」：每打出一张能力牌 +1 能量。规划时必须算进来，
        // 否则会低估本回合可用能量、把最后一张牌误判成「费不够」（用户实测过）。
        bool hasSubroutine = HasPower<MegaCrit.Sts2.Core.Models.Powers.SubroutinePower>(boss);

        // 「自动化」（Automation）：每抽 10 张牌 +Amount 能量。而她这一回合**确定**会抽的牌是已知的：
        //   ① 回合开始「补手牌」——灵魂绑定替她记 `手牌数 − 涅奥 2` 张（<c>SoulBoundTurnStart</c>）；
        //   ② 剧本里每张「灵魂」抽 `Cards` 张（被拦掉但记账，见 `ShadowBossNoDrawPatch`）。
        // 规划期必须把这两笔会到账的能量算进来 —— 否则计划会因为「当时没费」把牌判成打不出
        // （手牌栏画成暗的），而她真正出牌时能量已经到账并打出去了
        // ⇒ 用户 2026-10-23 实测的「有费不出牌 / 打出去的牌却是暗的」。
        bool soulBound = HasSoulBound(boss);
        int autoAmount = 0, autoCounter = 0;
        try
        {
            var automation = boss.Creature?
                .GetPower<MegaCrit.Sts2.Core.Models.Powers.AutomationPower>();
            if (automation != null)
            {
                autoAmount = Math.Max(0, automation.Amount);
                autoCounter = Math.Max(0, automation.DisplayAmount);   // = 还差几张牌
            }
        }
        catch { /* 没这能力就按 0 */ }

        void NoteAutomationDraws(int draws)
        {
            if (autoAmount <= 0 || draws <= 0) return;
            for (int i = 0; i < draws; i++)
            {
                autoCounter--;
                if (autoCounter > 0) continue;
                energy += autoAmount;      // 原版：减到 0 就给能量并把计数重置为 10
                autoCounter = 10;
                Logger.Log($"[CharBoss] 规划：自动化到账 +{autoAmount} 能量（抽牌计数归零）");
            }
        }

        if (soulBound)
        {
            int turnStartDraws = 0;
            try
            {
                turnStartDraws = boss.Def.HandSizeForAct(boss.ActIndex)
                                 - (boss.Def.CarriesNeowBlessing ? Relics.NeowBlessing.DrawPenalty : 0);
            }
            catch { /* 用 0 */ }
            NoteAutomationDraws(turnStartDraws);   // ① 回合开始那几张在她出牌之前就到账
        }

        // 本回合奥斯提是否已经攻击过 —— 决定「重压」是不是 0 费（见 CostsNothingWithOsty）。
        // 玩家回合开始时她自己的奥斯提通常还没动，先按 false 起步，剧本里打了戳击/出击再置 true。
        bool ostyAttacked = false;
        // 本回合**奥斯提已经攻击过几次** —— 「猛晃」（Rattle）的段数 = 1 + 这几次
        // （反编译 `Rattle.CalculatedHits`：`1 + History.CreatureAttackedEntry` 里 Acto==Osty 且本回合的条数）。
        // 规划期历史里一条都还没有 ⇒ 不自己记账，它的意图就永远显示 1 段（用户实测口径：要显示真实段数）。
        int ostyAttacksThisTurn = 0;

        // 「本回合剧本里**将要**生成的牌数」——超质量体（SUPERMASSIVE）的伤害是
        // 「基础 + 每生成过一张牌 +4」，规划时它排在君权自授/发现/光谱偏移之后，
        // 那些牌**还没真的印出来**，boss.PendingGenerations 里没有它们 → 意图偏低（用户实测）。
        // 所以在规划期把前面的剧本牌会印的牌数先加进去。
        int plannedGenerations = 0;

        // 「本回合剧本里前面那几张爪击带来的成长」——爪击（Claw）打出后会让**所有爪击**的伤害 +Increase，
        // 第二张起就得把前一张的成长先算进意图（卡实例上那份要等前面那张真的打出去才会涨）。
        int clawPending = 0;

        // 「本回合剧本里**将要**给奥斯提加的生命」——「出击」的伤害 = 基础 + 1 × 奥斯提当前生命，
        // 而规划发生在这些牌打出去之前，所以回合内先召唤/加血的牌带来的那部分 HP 还没进公式 → 预览偏低。
        // 注意：**只有出击**吃奥斯提生命，其它奥斯提牌（戳击/重压）不吃，所以只加在出击那条上。
        int plannedOstyHp = 0;
        // 「吞噬生命」可能这回合才打出来 → 边走剧本边带上当前的召唤值（见 BladesGrowth.PlanCompensation）
        int plannedDevour = BossDamagePredictor.DevourAmountOf(boss);

        // 球位模拟器：按剧本出牌顺序推进队列，判断哪几张牌会（因为球位已满）激发球
        var orbSim = new BossOrbSim(boss.Creature, pcs.OrbQueue);
        // 到 Boss 出手前还会掉的力量/集中（偏差认知），意图要先扣掉它
        int pendingDrop = BossIntentBuilder.PendingDrop(boss.Creature);
        int pendingRise = BossIntentBuilder.PendingRise(boss.Creature);   // 烘焙手套每回合 +1 力量 → 预览先补

        // 君王之剑的两笔"还没到账"的加成（用户要求意图要准）：
        //   ① 熔炉（FurnacePower）在**他的回合开始**铸造一次，而计划是玩家回合开始前做的 ⇒ 这笔还没进卡面；
        //   ② 追踪之刃（SeekingEdge）在他本回合里铸造 7 —— 若剑在它之后打出，也该算进去；
        //   ③ 剑圣（SwordSage）给他 SwordSagePower → 君王之剑 **重放 +N**（打出次数 = 1+N）。
        int pendingBladeDamage = PendingFurnaceForge(boss);
        int pendingBladeHits = 0;

        // 「全身撞击」的意图：要算上他**本回合**会拿到的格挡（用户 2026-10-28 实测）。
        // 格挡会在他**自己回合开始**清空，所以出手那一刻身上的格挡 =
        //   ① 回合开始到账的（绯红披风 power 每回合给 N 格挡、锚已在此前用掉…）；
        //   ② 本回合剧本里**排在他前面**那些牌给的（血墙 16 / 坚毅 7 / 防御 5 …）。
        int simBlock = TurnStartBlockOf(boss.Creature);

        // ★ v0.2.300（用户 2026-11-02：「第三回合…预备打击1费，打击1费，打完他们之后踩踏降低至1费也可以打出」）：
        //   「踩踏」（STOMP）的实现在**打出时**按「本回合已经打完的攻击牌数」减费
        //   （反编译：`ReduceCostBy(count(CardPlaysFinished 里本人本回合的攻击牌))` →
        //    `EnergyCost.AddThisTurn(-amount)`）。规划期发生在这些牌打出去**之前**，
        //   所以必须自己按剧本顺序记账，否则它永远被判成「3 费打不起」而跳过。
        int attacksPlannedThisTurn = 0;

        // ★ v0.2.302（用户 2026-11-02：①「第二回合的第二张打击（被惊逃打出的）没有算预备打击的临时力量」、
        //   ②「第三回合的第 4 张踩踏没有计算手里剑」）：
        //   本回合**还没到账**的临时力量。两种来源：
        //     · 预备打击（SetupStrike）：打完给自己挂 SetupStrikePower +3 力量（反编译确认加给她自己）；
        //     · 手里剑（Shuriken）：每打出 3 张攻击牌 +1 力量（第 3 张**打完**才到账 ⇒ 第 4 张吃得到）。
        //   意图节点只加「此刻真实存在的力量」，这部分必须由规划期预补，否则后面的意图偏低。
        int inTurnStrength = 0;

        // ★ v0.2.521（用户 2026-11-09：「铭记死亡的伤害预览没有考虑必备工具或杂技的弃牌」/
        //   「钢铁风暴没有显示它生成的刀会造成的伤害」）：
        //   · `discardsThisTurn`：本回合**到当前这张牌为止**已经被/将要被丢掉的张数（铭记死亡 +4 用）；
        //     「必备工具」在她回合开始先丢 1 张 ⇒ 起算值就是 1。
        //   · `stormDiscards`：本回合剧本里标了「被丢弃」的张数 —— 钢铁风暴把手牌全丢掉、
        //     每丢一张生成一把小刀 ⇒ 它的 `x*x` 段数就是这个数（用户给的 5 / 4 张）。
        int discardsThisTurn = HasToolsOfTrade(boss) ? 1 : 0;
        // ★ 2026-11-10（用户：「致死性也算虚无牌」）：「亡魂牵引」的段数要算上**本回合已经打出的虚无牌**，
        //   而规划期战斗历史里还没有它们 ⇒ 这里按剧本顺序累加（只给 PULL_FROM_BELOW 用）。
        int etherealPlanned = 0;
        int stormDiscards = 0;
        {
            bool multiNow = MultiplayerCardSwap.IsMultiplayer(boss);
            // ★ v0.2.525（用户 2026-11-09：「钢铁风暴的 x*x = **丢几张打几刀**」）：
            //   「钢铁风暴」丢的是**它出手那一刻手上剩下的**牌 ⇒ 段数 = 剧本里排在它**后面**、
            //   会被丢掉的张数。
            //   ⚠ 两个坑（旧写法直接数「本回合所有被丢弃的条目」）：
            //     ① 排在她前面的被丢弃条目（已经不在手上了）不该算；
            //     ② 「必备工具」在她**回合开始**就先丢掉 1 张，那张也不在手上了 ⇒ 要排除。
            //   实测（第 10 回合）：旧写法算出 6，而用户设计的口径是 **4**（防御 + 隐秘匕首 + 打击×2）。
            bool afterStorm = false;
            foreach (var eachEntry in turn.Cards)
            {
                if (!afterStorm)
                {
                    if (EntryOfScriptType(eachEntry.Card) == "STORM_OF_STEEL") afterStorm = true;
                    continue;
                }
                if (eachEntry.ToolsDiscard) continue;      // 回合开始就被必备工具丢掉了
                bool discardNow = eachEntry.DiscardInstead
                                  || (eachEntry.DiscardInsteadWhenMultiplayer && multiNow)
                                  || (eachEntry.DiscardInsteadSingleOnly && !multiNow);
                if (discardNow) stormDiscards++;
            }
        }

        foreach (var entry in turn.Cards)
        {
            // ⚠ 多人换牌：手里那张是**换上的牌**（休眠 / 骸骨军团 / 刀刃交响曲），
            //   所以「这张剧本牌在不在手牌」必须按**换牌后的类型**比对 ——
            //   以前直接拿原牌类型比，于是日志写着 `PullAggro(不在手牌)`，
            //   换上的那张永远不会被打出去（用户实测：三层根除骨不召唤奥斯提）。
            var card = hand.FirstOrDefault(c => !used.Contains(c) && Matches(c, entry, boss));
            if (card == null)
            {
                skipped.Add(entry.Card.Name + "(不在手牌)");
                continue;
            }

            if (!IsPlayable(card)) { skipped.Add(card.Id.Entry + "(不可打出)"); continue; }

            // 「抉择储」：这张牌只有在玩家「抉择，抉择」里挑了指定那张时才打得出
            //（第 1 回合的天际钻头 —— 3 点能量被前三张花光，只有玩家选星位序列才有费）。
            // 规划期选择还不知道 ⇒ 跳过（手牌里压暗、没有意图）；选择落地后由
            // `CharBossMonster.RunChoiceFollowUpsAsync` 按剧本补打。
            if (!ChoiceRegent.EntryAllowed(boss, turnNumber, entry))
            {
                skipped.Add(entry.Card.Name + "(等抉择结果)");
                continue;
            }

            // 「惊逃」那张（二层杂耍战）：**不花她的能量**，但**照样按普通出牌规划**
            //（走下面同一条路 ⇒ 意图、每打一张牌后的重算、力量到账后把预补撤回去 全都一致）。
            // 用户 2026-11-02：「你就不能让惊逃的伤害预览计算逻辑和直接打牌一样吗？…
            // 直接打出伤害预览会在力量等加成到账之后就将预览里补的伤害撤回去，惊逃直接叠加起来了」。
            bool stampedeOnly = entry.StampedePlay;
            // 「即使有费也不出、留到下回合再打」（用户设计：根除骨第 2/6 回合的根除）
            // ⚠ 同时标了 StampedePlay 的不算「保留在手」——它虽然也留在手里，但**本回合一定会被惊逃打出去**，
            //   必须继续往下走成「0 费计划牌」，否则既不进 Plan 也没有意图
            //（用户 2026-11-02：「第三回合惊逃打的踩踏又不显示攻击意图了」）。
            if (entry.HoldInstead && !stampedeOnly)
            {
                used.Add(card);
                skipped.Add(card.Id.Entry + "(保留在手)");
                continue;
            }

            // 只用来显示意图的牌（所向无敌：回合结束从摸牌堆顶免费打出）→ 不参与出牌
            if (entry.DisplayOnly)
            {
                used.Add(card);
                // 标了 AutoPlayed 的展示牌**本回合会被打出去** → ①画亮 ②把它下方那排意图也画出来
                //（用户实测：所向无敌下面缺「守势」）。没标的（剑圣：费不够）保持压暗、无意图。
                //
                // ★ 2026-11-10（用户，三层「抉择储」）：那 5 张候选技能牌要**压暗但画意图**
                //   （铸墙 = 守势+强化、隐秘藏品 = 强化、共鸣 = 策略+强化、震荡波 = 策略、
                //     星位序列 = 标出天际钻头会多出多少段）。由剧本点名 → 这里按标签造意图，
                //   并把这一条登记成 `DimWhenShown`（手牌栏据此压暗）。
                bool autoPlayed = entry.AutoPlayed;
                bool forcedHere = entry.ForceIntents != BossIntentTag.None || entry.ShowsDrillGain;
                if (autoPlayed || forcedHere)
                {
                    var displayTarget = BossPlanner.ChooseTarget(boss, card) ?? boss.Creature;
                    var displayIntents = autoPlayed
                        ? PreviewIntents(card, displayTarget, pendingDrop, pendingRise, inTurnStrength)
                        : ForcedIntents(entry.ForceIntents);

                    int drillPerHit = 0, drillGain = 0, drillBase = 0, drillTotal = 0;
                    if (entry.ShowsDrillGain)
                    {
                        try
                        {
                            var drill = FindChoiceDrill(boss, turnNumber);
                            if (drill != null)
                            {
                                drillPerHit = Math.Max(0, (int)Math.Round(ReadVar(drill, "Damage")));
                                (drillBase, drillTotal) = DrillHitsPreview(hand, card, energy);
                                drillGain = Math.Max(0, drillTotal - drillBase);
                                Logger.Log($"[CharBoss] 抉择：星位序列预告 —— 天际钻头 {drillPerHit}×{drillBase}" +
                                           $" ⇒ 选它之后 {drillPerHit}×{drillTotal}（多 {drillPerHit}×{drillGain}）" +
                                           $"（当前余费 {energy}）");
                            }
                        }
                        catch (Exception e) { Logger.Log("[CharBoss] 抉择：星位序列预告失败: " + e.Message); }
                    }

                    boss.DisplayPlays.Add(new PlannedPlay
                    {
                        Card = card,
                        Target = displayTarget,
                        Cost = 0,
                        StarCost = 0,
                        Intents = displayIntents,
                        DimWhenShown = !autoPlayed,
                        DrillPerHit = drillPerHit,
                        DrillGainHits = drillGain,
                        DrillBaseHits = drillBase,
                        DrillTotalHits = drillTotal,
                    });
                }
                skipped.Add(card.Id.Entry + (entry.AutoPlayed ? "(仅显示意图/回合结束自动打出)"
                                     : forcedHere ? "(仅显示意图，压暗)" : "(仅显示意图)"));
                continue;
            }

            // 剧本点名「这张要被上一张牌丢掉」的牌：不打出，只登记给自动选择器。
            // ⚠ 第一幕「奇巧猎」第 2 回合单人/多人不一样（单人打出余像并丢防御；多人把余像换成鬼祟+、
            //   改成丢掉它）⇒ 判定要带上「是不是多人局」。
            bool multiHere = MultiplayerCardSwap.IsMultiplayer(boss);
            bool discardHere = entry.DiscardInstead
                               || (entry.DiscardInsteadWhenMultiplayer && multiHere)
                               || (entry.DiscardInsteadSingleOnly && !multiHere);
            if (discardHere)
            {
                BossCardSelector.MarkPreferDiscard(boss, turnNumber, card);
                used.Add(card);
                discardsThisTurn++;      // 铭记死亡的 +4 靠它（本回合到它为止被丢掉的张数）

                // 被丢掉之后**自己会打出去**的牌（「奇巧」/Sly：磨蚀）：
                // 它不在 Plan 里，但本回合确实会打出去 ⇒ 要画亮、并把意图也画出来
                //（用户 2026-10-25：「磨蚀在意图中不亮」）。
                bool sly = false;
                try { sly = card.Keywords?.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Sly) == true; }
                catch { sly = false; }
                if (sly)
                {
                    var slyTarget = BossPlanner.ChooseTarget(boss, card) ?? boss.Creature;
                    var slyIntents = PreviewIntents(card, slyTarget, pendingDrop, pendingRise, inTurnStrength);
                    boss.DisplayPlays.Add(new PlannedPlay
                    {
                        Card = card,
                        Target = slyTarget,
                        Cost = 0,
                        StarCost = 0,
                        Intents = slyIntents,
                    });

                    // ★ v0.2.520（用户 2026-11-09：「注意肾上腺素和战术大师都会给费」）：
                    //   奇巧牌被丢弃时**会免费打出**，它给的能量/辉星也得记账 ——
                    //   否则后面的牌会被误判成「费不够」而跳过（华丽猎第 2 回合的夜魇+ / 第 8 回合的谋杀）。
                    try
                    {
                        int gained = Math.Max(0, (int)Math.Round(ReadVar(card, "Energy")));
                        if (gained > 0)
                        {
                            energy += gained;
                            Logger.Log($"[CharBoss] 规划：{card.Id.Entry}（奇巧，被丢弃后自动打出）+{gained} 能量");
                        }
                        stars += Math.Max(0, (int)Math.Round(ReadVar(card, "Stars")));
                        stars += Math.Max(0, (int)Math.Round(ReadVar(card, "Star")));
                    }
                    catch { /* 忽略 */ }
                }
                skipped.Add(card.Id.Entry + (sly ? "(被丢弃 → 奇巧自动打出)" : "(被丢弃)"));
                continue;
            }

            // 剧本点名「这张会被烘焙手套烧掉」的牌：不打出，登记成优先消耗目标
            //（不然自动选择器会按价值乱烧，烧掉本来要打的牌）
            if (entry.Burned)
            {
                BossCardSelector.MarkPreferDiscard(boss, turnNumber, card);
                used.Add(card);

                // ★ v0.2.260（用户 2026-10-31：「被消耗打出的彼岸咆哮还是没有攻击意图」）：
                //   被烧掉、**回合结束会在消耗牌堆里自动打出**的那张牌（剧本的 `FreePlayExhaustEntry`），
                //   也要在它自己那张牌下面显示意图（群伤攻击）——否则面板上那张牌光秃秃的，
                //   第三方问「他这一手算不算攻击」也得不到真。
                if (IsAutoPlayFromExhaust(boss, turnNumber, card))
                {
                    var burnTarget = BossPlanner.ChooseTarget(boss, card) ?? boss.Creature;
                    var burnIntents = PreviewIntents(card, burnTarget, pendingDrop, pendingRise, inTurnStrength);
                    boss.DisplayPlays.Add(new PlannedPlay
                    {
                        Card = card,
                        Target = burnTarget,
                        Cost = 0,
                        StarCost = 0,
                        Intents = burnIntents,
                    });
                    skipped.Add(card.Id.Entry + "(被烧掉 → 回合结束自动打出，已画意图)");
                }
                else
                {
                    skipped.Add(card.Id.Entry + "(被烧掉)");
                }
                continue;
            }

            int cost = entry.CostOverride >= 0 ? entry.CostOverride : BossPlanner.CostOf(card);
            if (stampedeOnly) cost = 0;    // 惊逃打出的那张不花能量
            // 「踩踏」：本回合已经计划打出的攻击牌每张让它 -1 费（见上面 attacksPlannedThisTurn 的注释）
            if (string.Equals(SafeEntry(card), "STOMP", StringComparison.Ordinal) && attacksPlannedThisTurn > 0)
            {
                int before = cost;
                cost = Math.Max(0, cost - attacksPlannedThisTurn);
                if (cost != before) skipped.Add($"{card.Id.Entry}(踩踏减费 {before}->{cost})");
            }
            // 重压（Flatten）：本回合奥斯提已经攻击过（戳击/出击在前面打过了）→ 它变 0 费。
            if (CostsNothingWithOsty(card) && ostyAttacked) cost = 0;
            // **X 费牌**（挽歌 Dirge / 根除 Eradicate）：原版 `CardEnergyCost.GetAmountToSpend()`
            // 对 X 费返回**当前** `pcs.Energy`（规划期一分钱都还没扣 ⇒ 永远是满的 3），
            // 而它真正花掉的是「打出那一刻的剩余能量」（`CardCmd.AutoPlay` 里
            // `CapturedXValue = pcs.Energy`）。所以这里必须按**本地剩余 energy** 记账，
            // 否则排在它前面的牌一扣费，它就被判成「费不够 3>1」而永远打不出来。
            // 用户 2026-10-23 的用例：第 1 回合 拖延+(2) + 自动化+(0) ⇒ 挽歌**以 1 费打出**（X=1）。
            if (IsXCost(card) || entry.SpendsAllEnergy) cost = Math.Max(0, energy);
            // **X 星费牌**（星尘 `Stardust`：`HasStarCostX`，把身上**所有**辉星花光，段数 = 花掉的星数）
            // ⇒ 规划期把星费按「当前剩余星」记账并清零，否则后面吃星的牌会被误判成还够。
            bool starX = IsStarXCost(card);
            int starCost = starX ? Math.Max(0, stars) : BossPlanner.StarCostOf(card);
            if (cost > energy || starCost > stars)
            {
                // ⚠ 这条消息**也覆盖星费不足**（原来只写 `cost>energy`，看不出到底是费还是星不够 ——
                //   2026-11-05 排查「群星斗篷 0 费却被跳过、日志写 费不够 0>2」时就被它误导过）。
                skipped.Add($"{card.Id.Entry}(费/星不够 费 {cost}>{energy} / 星 {starCost}>{stars})");
                continue;
            }

            var target = BossPlanner.ChooseTarget(boss, card);
            if (target == null) { skipped.Add(card.Id.Entry + "(没目标)"); continue; }

            used.Add(card);
            // ★ v0.2.339（用户 2026-11-03：「伤害预览没有计算致死性的加成」）：
            //   「致死性」（`LethalityPower`）只让**每回合第一张攻击牌**增伤 50%，而它判定
            //   「是不是第一张」靠的是战斗历史（`History.CardPlaysStarted`）—— 规划期历史里一张牌都没打，
            //   于是它对**每一张**攻击牌都返回 ×1.5；我们的补丁
            //   `Patches/LethalityFirstAttackOnlyPatch` 负责把非第一张的压回 ×1，
            //   可它当时只能查 `boss.Plan`，而规划期 `Plan` 还**没填好** ⇒ 全被压回 ×1（= 预览没有加成）。
            //   这里把「计划里的第一张攻击牌」记到 Boss 身上，让补丁在规划期也认得出它。
            try
            {
                if (!stampedeOnly && boss.PlannedFirstAttackCard == null
                    && card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack)
                {
                    boss.PlannedFirstAttackCard = card;
                }
            }
            catch { /* 读不到类型就算了 */ }

            // X 费牌（根除）：真的打出去时 X = 花光本回合剩余能量，打 X 次（也就是伤害段数）。
            int xHits = XHitBonus(card, cost);
            // ★ 2026-11-10（用户：「星尘和辐射没有正确显示段数。记得计算他在本回合会获得的辉星」）：
            //   · 星尘 `Stardust`：`WithHitCount(ResolveStarXValue())` = **把身上所有辉星花光、一段一星**；
            //   · 辐射 `Radiate`：段数 = **本回合获得过的辉星数**（战斗历史里的正数 `StarsModifiedEntry`）。
            //   两者的段数在规划期都算不出来（历史空 / 还没花星）⇒ 由这里按剧本顺序把数递进去。
            string entryNow = SafeEntry(card);
            int starHits = -1;
            if (starX) starHits = Math.Max(0, starCost);
            else if (entryNow == "RADIATE") starHits = Math.Max(0, starsGainedThisTurn);
            var intents = BossIntentBuilder.IntentsFor(card, target, pendingDrop, pendingRise,
                // ⚠ v0.2.362：**她自己的力量**单独走 `strengthBonus`（奥斯提的牌不吃）
                extraDamage: (IsBlade(card) ? pendingBladeDamage : 0)
                             + (SafeEntry(card) == "SUPERMASSIVE"
                                    // 超质量体：每「她生成过一张牌」+牌面的 ExtraDamage（反编译：ExtraDamageVar(3)、升级 4；
                                    // 以前这里写死 4，用户实测数值不对）
                                    ? (Math.Max(0, boss.PendingGenerations) + plannedGenerations)
                                      * Math.Max(1, (int)Math.Round(ReadVar(card, "ExtraDamage")))
                                    : 0)
                             // 出击：把本回合前面那些牌会给奥斯提加的生命也算进「1 × 生命」那一段
                             + (XtraOstyHpBonus(card) * plannedOstyHp)
                             // 爪击：前面几张爪击已经让所有爪击 +N 了，这张也得先算上
                             + (SafeEntry(card) == "CLAW" ? clawPending : 0),
                // ★ v0.2.339：猛晃（Rattle）的段数 = 1 + 本回合奥斯提**已经攻击过**的次数。
                //   反编译 `CalculatedHits` 数的是战斗历史，规划期历史是空的 ⇒ 永远 1 段，
                //   而剧本里排在它前面的紧追不放/重压都已经让奥斯提打过了（用户实测口径）。
                extraHits: (IsBlade(card) ? pendingBladeHits : 0) + xHits
                           + HelixExtraHits(card, energyAtStart - energy)
                           + (SafeEntry(card) == "RATTLE" ? ostyAttacksThisTurn : 0),
                // ⚠ 「亡魂牵引」的段数（本回合已打出的虚无牌）走 `etherealBefore`（见函数末尾那个参数）：
                //   规划期 `boss.Plan` 还是空的 ⇒ 只能由这里按剧本顺序数；出牌期的「每帧重算」
                //   则由 `BossIntentBuilder.PlannedEtherealBefore` 按 `Plan` 自己数。两边取较大值，不会双算。
                xHits: xHits,
                // 全身撞击：用「出手时会有多少格挡」覆盖牌面（见上面 simBlock 注释）
                bodySlamBlock: SafeEntry(card) == "BODY_SLAM" ? simBlock : -1,
                // 她自己的力量（奥斯提的牌不吃，见 IntentsFor 里的说明）
                strengthBonus: inTurnStrength,
                // ★ 2026-11-05：烟囱 / 开信刀的伤害挂在**触发它的这张牌**底下（不再进左侧独立列）；
                //   烟囱按「本牌生成几张状态牌」打几段（强撑 = 2 张伤口 ⇒ 5×2），开信刀永远是单段。
                //   散射炮把「本回合排在它前面、会生成状态牌的牌」也算进段数。
                smokePerHit: smStackThisTurn,
                smokeHits: Core.StatusBossMath.StatusCardsGenerated(card),
                letterOpenerHit: Core.StatusBossMath.IsSkillForLetterOpener(card, skillsThisTurn) ? loDamage : 0,
                pendingStatusCards: statusesThisTurn,
                aoeFromCaller: true,
                // ★ v0.2.388：金斧的伤害 = 本场战斗打出的牌数，而意图是出手前算的
                //   ⇒ 本回合排在它前面、还没打出来的牌要补上（规划期 `Plan` 还是空的，只能这样传）。
                playsBeforeThisTurn: result.Count,
                // 铭记死亡：本回合到它为止已经/将要丢掉的牌数（原版「本回合每丢一张 +4」）
                discardsThisTurn: discardsThisTurn,
                // 钢铁风暴：每丢一张生成一把小刀 ⇒ 段数 = 本回合剧本里被丢弃的张数
                shivOverride: SafeEntry(card) == "STORM_OF_STEEL" ? stormDiscards : 0,
                // ★ 2026-11-10：星尘（花光所有星）/ 辐射（本回合获得的星）的段数（-1 = 不覆盖）
                starHits: starHits,
                // 仆从储：这一张是不是「召唤」（按本回合前面已计划的仆从数量算）
                minionSummon: IsMinionSummonHere(card, boss, ref plannedMinions),
                // ★ 2026-11-10：亡魂牵引的段数 = 本场打出的虚无牌数，而本回合排它前面的那几张
                //   （例如致死性 `Lethality` 本身就是虚无牌）在规划期战斗历史里还没有 ⇒ 由这里按剧本顺序数给它。
                etherealBefore: etherealPlanned);
            // ★ 2026-11-05：累加「本回合已生成的状态牌 / 已打出的技能牌」，供后面几张牌的意图用
            statusesThisTurn += Core.StatusBossMath.StatusCardsGenerated(card);
            if (Core.StatusBossMath.IsSkill(card)) skillsThisTurn++;
            // ★ v0.2.411：她这一回合打到「烟囱」了 ⇒ 后面的牌开始按烟囱的卡面数值挂全体伤害
            if (smStackThisTurn <= 0 && Core.StatusBossMath.IsSmokestackCard(card))
            {
                smStackThisTurn = Core.StatusBossMath.CardSmokestackDamage(card);
                if (smStackThisTurn > 0)
                {
                    Logger.Log($"[CharBoss] 烟囱：本回合第 {result.Count + 1} 张打出 ⇒ 后面的牌按 {smStackThisTurn} 点/张挂全体伤害");
                }
            }

            // X 费牌（暴风雨）生成的球数 = 出手那一刻的剩余能量（用户 2026-11-05 要求提前算）
            // X 费牌（暴风雨）生成的球数 = **出手那一刻的剩余能量** = 这张牌花掉的 cost。
            // ⚠ 不能用 `xHits`：那是「伤害段数」用的 x−1（根除那套），会把球数少算 1。
            int xEnergyForOrbs = 0;
            try { if (card.EnergyCost.CostsX) xEnergyForOrbs = Math.Max(1, cost); } catch { xEnergyForOrbs = 0; }
            var evoke = orbSim.Predict(card, xEnergyForOrbs);
            BossOrbSim.AppendIntents(intents, evoke, pendingDrop, BossIntentBuilder.RandomAimIsElsewhere(card));
            // ★ 2026-11-09（二层「虚无骨」）：剧本点名要画**策略（减益）意图**的那一张（女妖之嚎第 4 回合）。
            if (entry.ForceDebuffIntent)
            {
                intents.Add(new MegaCrit.Sts2.Core.MonsterMoves.Intents.DebuffIntent(false));
                Logger.Log($"[CharBoss] 剧本点名：{SafeEntry(card)} 额外画一条策略（减益）意图");
            }
            // 诊断（用户 2026-11-09「钢铁风暴的意图还是只有一个强化」/「隐秘匕首+ 还是 4×2」）：
            // 把「剧本会丢几张 / 这张是不是升级版 / 最终意图条数」打出来，一眼能看出是哪一环没接上。
            if (SafeEntry(card) == "STORM_OF_STEEL" || SafeEntry(card) == "HIDDEN_DAGGERS")
            {
                Logger.Log($"[CharBoss] 小刀段数诊断：{SafeEntry(card)}（升级={card.IsUpgraded}）" +
                           $" 剧本丢弃={stormDiscards} 张 ⇒ 意图 {intents.Count} 条" +
                           $"（升级刀基础={BossIntentBuilder.ShivBaseDamage + (BossIntentBuilder.GeneratesUpgradedShivs(card) ? 2 : 0)}）");
            }
            Logger.Log($"[CharBoss] 意图 {SafeEntry(card)}: [{string.Join("+", intents.ConvertAll(i => i.GetType().Name))}]" +
                       $"（预补力量 +{inTurnStrength}、力量遗留 -{pendingDrop}、回合前 +{pendingRise}、费用 {cost}）");
            result.Add(new PlannedPlay
            {
                Card = card,
                Target = target,
                Cost = cost,
                StarCost = starCost,
                Intents = intents,
                EvokeDamage = evoke.TotalDamage,
                GeneratedShivs = BossIntentBuilder.ShivsFrom(card)
                                 + (SafeEntry(card) == "STORM_OF_STEEL" ? stormDiscards : 0),
                StampedeOnly = stampedeOnly,
                // ★ v0.2.412：这三个数存下来，供「每帧重算」原样再用（否则序号会随出牌漂移）
                SmokePerHit = smStackThisTurn,
                SmokeHits = Core.StatusBossMath.StatusCardsGenerated(card),
                LetterOpenerHit = Core.StatusBossMath.IsSkillForLetterOpener(card, skillsThisTurn) ? loDamage : 0,
                PendingStatusCards = statusesThisTurn,
                // ★ 2026-11-10：星尘/辐射的段数（辉星口径）在这里定死，重算路径原样再用
                StarHits = starHits,
            });
            // ★ v0.2.300：「踩踏」的减费看的是「本回合已经打完的攻击牌数」⇒ 每计划一张攻击牌就 +1
            //   （必须在扣费之后记，位置无所谓；放在这里与 energy 记账相邻，方便以后核对）。
            try
            {
                bool isAttack = card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack;
                if (isAttack && !stampedeOnly) attacksPlannedThisTurn++;

                // ★ 2026-11-10：这张牌**是虚无牌**（带「虚无」关键字，例如致死性 `Lethality`）⇒
                //   记一笔，供排在它后面的「亡魂牵引」算段数（见上面 etherealPlanned 的注释）。
                try
                {
                    if (card.Keywords != null
                        && card.Keywords.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Ethereal))
                    {
                        etherealPlanned++;
                        Logger.Log($"[CharBoss] 规划：{SafeEntry(card)} 是虚无牌 ⇒ 本回合虚无牌累计 {etherealPlanned} 张" +
                                   "（亡魂牵引的段数要算上它）");
                    }
                }
                catch { /* 忽略 */ }

                // 「这张牌打完会给她自己多少力量」⇒ 排在它后面的攻击意图要预补。
                // ★ v0.2.340（用户 2026-11-03：「伤害意图没有计算**疯狂进食**的临时力量」）：
                //   以前只点名处理「预备打击」，其它加力量的牌（疯狂进食 = 本回合 +N 力量、
                //   烙印、献祭之类）全靠 `inTurnStrength` 的少数硬编码 ⇒ 漏一张就偏低。
                //   现在改成**通用**判据 `BossDamagePredictor.StrengthGainOf`（读牌面的 Strength/StrengthPower 变量），
                //   和出牌后「把预补撤回来」用的是同一把尺子（`CharBossMonster.PlayOne` 里那段）。
                {
                    int add = Math.Max(0, BossDamagePredictor.StrengthGainOf(card));
                    if (add > 0)
                    {
                        inTurnStrength += add;
                        Logger.Log($"[CharBoss] 意图预补：{SafeEntry(card)} 给她 {add} 点力量 ⇒ " +
                                   $"本回合后续攻击意图 +{inTurnStrength}");
                    }
                }

                // ★ v0.2.338 / v0.2.359：三层「事件机」的「一心化万」——**每有一张牌从弃牌堆回到手牌**
                //   ⇒ **所有单位** +1 力量（她自己也 +）⇒ 排在它后面的攻击意图要预补这份力量。
                //   来源两类（统一走 `DiscardReturnStrengthOf`，和出牌期/重算期同一把尺子）：
                //     · 万物一心：返手张数由剧本点名（`ScriptCard.ReturnsToHand`）；
                //     · 全息影像：从弃牌堆捞 1 张（搬运已拦，力量照给，见 `HologramNoReturnPatch`）。
                //   早先这里只硬编码了万物一心，于是第 7 回合「疯狂科学 → 全息影像 → 冰之长枪 …」
                //   那两张的攻击预览一直少 1 点（用户 2026-11-04 报的「一心化万的力量还是没被提前计算」）。
                {
                    int returnStrength = BossDamagePredictor.DiscardReturnStrengthOf(card, boss);
                    if (returnStrength > 0)
                    {
                        inTurnStrength += returnStrength;
                        Logger.Log($"[CharBoss] 意图预补：{SafeEntry(card)} 触发一心化万返手 {returnStrength} 张 ⇒ " +
                                   $"本回合后续攻击意图 +{inTurnStrength}（所有单位各 +{returnStrength} 力量）");
                    }
                }

                // 手里剑：每打出 3 张攻击牌 +1 力量（第 3 张**打完**才到账 ⇒ 第 4 张吃得到）
                if (isAttack && HasRelicOf(boss, "SHURIKEN"))
                {
                    int every = 3, each = 1;
                    try
                    {
                        var relic = FindRelic(boss, "SHURIKEN");
                        if (relic != null)
                        {
                            every = Math.Max(1, (int)Math.Round(ReadVarOr(relic.DynamicVars, "Cards", 3)));
                            each = Math.Max(0, (int)Math.Round(ReadVarOr(relic.DynamicVars, "Strength", 1)));
                        }
                    }
                    catch { /* 用默认 3 / 1 */ }
                    if (attacksPlannedThisTurn % every == 0)
                    {
                        inTurnStrength += each;
                        Logger.Log($"[CharBoss] 意图预补：手里剑（第 {attacksPlannedThisTurn} 张攻击）⇒ " +
                                   $"本回合后续攻击意图 +{inTurnStrength}");
                    }
                }
            }
            catch { /* 判断不了就不记 */ }

            energy -= cost;
            // 「附魔播种」的牌打出时 +1 费 —— 规划阶段必须先把这 1 点算进来，
            // 否则后面那张牌（根除骨的「根除」）会被误判成"费不够"而不打（用户实测）。
            if (entry.EnchantSown) energy += 1;
            // 象牙麻将牌（IvoryTile）：打出的牌**耗能 ≥ 3** 时回 1 费。
            // 根除是 X 费牌（X = 花光本回合剩余能量），打出这一下必然 ≥3 → 回 1 费，
            // 后面那张「违逆」就打得出。游戏里确实回了 1 费，但规划期没记账 → 违逆被跳过（用户实测）。
            if (cost >= 3 && HasIvoryTile(boss)) energy += 1;
            // 奥斯提出过手 → 记下来，后面那张「重压」就是 0 费了
            if (IsOstyAttack(card)) { ostyAttacked = true; ostyAttacksThisTurn++; }
            // 这张牌本回合会印几张牌（给后面的超质量体算加伤）
            plannedGenerations += GeneratedBy(card);
            // 这张爪击打完会让后面的爪击 +Increase（和第二张起用同一把尺子）
            if (SafeEntry(card) == "CLAW") clawPending += (int)Math.Round(ReadVar(card, "Increase"));
            // 这张牌会先给奥斯提加多少生命（给后面的「出击」算加伤）
            // 这张牌会给奥斯提加生命（召唤牌 / 灵魂×吞噬生命）→ 给后面那张出击算加伤
            // （统一走 BossDamagePredictor.OstyHpGainOf，和 BladesGrowth 里的预补用同一把尺子）
            if (SafeEntry(card) == "DEVOUR_LIFE")
                plannedDevour = Math.Max(plannedDevour, BossDamagePredictor.DevourLifeCardAmount(card));
            plannedOstyHp += BossDamagePredictor.OstyHpGainOf(card, plannedDevour);
            // 这张牌打完会给多少格挡 → 记给后面那张「全身撞击」（它排在这张之后才算得到这笔）
            simBlock += BlockGainOf(card);
            stars -= starCost;

            // 剧本里的牌自己会给资源（崇拜 = +2 辉星、某些牌 + 能量）→ 必须记账，
            // 否则后面的牌（例如陨星要 2 辉星）会被误判成不够费/不够星（用户实测：第 3、8 回合不出陨星）。
            // ★ 2026-11-10：**本回合获得的辉星**单独记一份 —— 辐射（`Radiate`）的段数就是它
            //   （用户：「记得计算他在本回合会获得的辉星」）。汇流那种「下回合 +星」不算。
            int gainedStars = Math.Max(0, (int)Math.Round(ReadVar(card, "Stars")))
                              + Math.Max(0, (int)Math.Round(ReadVar(card, "Star")));
            if (DeferredStarCard(card)) gainedStars = 0;
            stars += gainedStars;
            starsGainedThisTurn += gainedStars;
            // ⚠ 有些牌的 `Energy` 变量**不是**「现在回能量」：
            //   · 拖延 Delay → 下回合 +N（由 PendingNextTurnEnergyCredit 在下一回合到账）；
            //   · 自动化 Automation → 每抽 10 张才 +N；
            //   · 汇流 Convergence / 淬炼刀刃 RefineBlade → **下回合** +N。
            // 规划期把它们当成立刻回能 ⇒ 多算 1～2 点 ⇒ 后面的牌被误判成打得起，
            // 用户实测就表现为「第 9 / 10 回合那张本该没费的隐秘藏品被画成还能打」。
            if (!DeferredEnergyCard(card))
                energy += Math.Max(0, (int)Math.Round(ReadVar(card, "Energy")));
            // X 费牌（天际钻头）把这一回合剩下的能量**全部花光**：剧本点名的最可靠，
            // 不依赖 `EnergyCost.CostsX` 在规划期猜对（猜错就会把「君王之剑」画成还能打）。
            if (entry.SpendsAllEnergy) energy = 0;

            // 这张牌打出后会让她额外抽几张（「灵魂」）→ 也给「自动化」记账：抽满 10 张就 +能量，
            // 而这份能量在出牌阶段是真会到账的（被拦掉的抽牌照样记自动化计数）
            NoteAutomationDraws(InTurnDrawsOf(card, soulBound));

            // 小刀牌塞的小刀是 0 费，永远打得出去 → 规划时只记数量，真打出去在出牌阶段做
            // （那些 Shiv 卡是这张牌结算时才创建的，规划时手里还不存在）。

            // 能力牌 → 子程序回能（下一张牌用得上）
            if (hasSubroutine && card.Type == CardType.Power) energy += 1;

            // 记账：这张牌给后续「君王之剑」带来的加成（铸造／重放）
            try
            {
                // 「铸造」：追踪之刃 7、**战火铸就 7**（二层「征服者储君」）……
                // 凡是带 `Forge` 变量的**牌**（能力牌除外 —— 熔炉那种是回合开始铸造，
                // 已经由 PendingFurnaceForge 算过）都会让排在它后面那张君王之剑更疼。
                int forge = Math.Max(0, (int)Math.Round(ReadVar(card, "Forge")));
                if (forge > 0 && card.Type != CardType.Power) pendingBladeDamage += forge;
                if (SafeEntry(card) == "SWORD_SAGE")
                    pendingBladeHits += Math.Max(0, (int)Math.Round(ReadVar(card, "SwordSagePower")));
            }
            catch { /* 忽略 */ }
        }

        // 剧本自检（用户 2026-10-29：涂毒猎第 2 回合「生存者」丢了防御、连续反弹没打出去）：
        // 手牌里还剩着的**奇巧牌**（`CardKeyword.Sly`：被丢弃时会免费自动打出）如果既没排进计划、
        // 也没被 `discardInstead` 点名，那它这一回合就白躺了 —— 十有八九是剧本漏写了标记。
        // 只打日志、不改行为。
        try
        {
            var leftovers = new List<string>();
        foreach (var card in hand)
            {
                if (used.Contains(card)) continue;
                bool sly;
                try { sly = card.Keywords != null && card.Keywords.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Sly); }
                catch { sly = false; }
                if (!sly) continue;

                // 「费不够 / 不可打出」的牌本来就打不出去，不算剧本漏写（避免刷无意义的警告）
                string entry = SafeEntry(card);
                bool justUnaffordable = skipped.Exists(s =>
                    s.StartsWith(entry + "(", StringComparison.Ordinal) &&
                    (s.Contains("费不够") || s.Contains("不可打出") || s.Contains("不在手牌")));
                if (justUnaffordable) continue;

                leftovers.Add(entry);
            }
            if (leftovers.Count > 0)
            {
                Logger.Log($"[CharBoss] ⚠ 剧本自检（第 {turnNumber} 回合）：奇巧牌 " +
                           $"[{string.Join(",", leftovers)}] 既没排进计划、也没被点名丢弃" +
                           "（它本该靠 `discardInstead: true` 被丢掉后免费打出 ⇒ 检查剧本）");
            }
        }
        catch { /* 忽略 */ }

        Logger.Log($"[CharBoss] 剧本第 {turnNumber} 回合: 打 [{string.Join(",", result.Select(p => p.Card.Id.Entry))}]" +
                   (skipped.Count > 0 ? $" 跳过 [{string.Join(",", skipped)}]" : string.Empty) +
                   $" 余费={energy}");
        return result;
    }

    /// <summary>
    /// 「螺旋钻击」（<c>HelixDrill</c>）的**段数补正** —— 用户 2026-10-24：
    /// 「螺旋钻机没有正确打出，并且没有被视为多段」。
    ///
    /// 反编译（`HelixDrill` 的 `CalculatedVars`）：
    /// <code>
    /// new CalculatedVar("CalculatedHits").WithMultiplier((card, _) =&gt; History.OfType&lt;EnergySpentEntry&gt;()
    ///     .Where(e =&gt; e.HappenedThisTurn(card.CombatState) &amp;&amp; e.Actor.Player == card.Owner)
    ///     .Sum(e =&gt; e.Amount));
    /// ```
    /// 也就是**段数 = 本回合已经花掉的能量之和**（不含它自己，它是 0 费）。
    ///   · 出牌期：`PlayOne` 现在会往历史里补 `EnergySpentEntry`（见那里注释），所以预览自己就对；
    ///   · **规划期**（玩家回合、她还没出手）：历史里一笔都没有 ⇒ 这里按「剧本里前面几张已经花掉多少费」预补。
    /// `hits` 的基础值是 `max(1, CalculatedHits)`，所以补 `花掉的能量 - 1`。
    /// </summary>
    internal static int HelixExtraHits(CardModel card, int energySpentSoFar)
    {
        if (SafeEntry(card) != "HELIX_DRILL") return 0;
        return Math.Max(0, energySpentSoFar - 1);
    }

    // ───────── 三层「抉择储」：展示牌的意图与「天际钻头预告」 ─────────

    /// <summary>
    /// 按剧本点名的标签造意图（<see cref="ScriptCard.ForceIntents"/>）。
    /// 顺序固定 **守势 → 策略 → 强化** —— 用户给的口径正是这个顺序
    /// （铸墙 = 守势+强化、共鸣 = 策略+强化、震荡波 = 策略、隐秘藏品 = 强化）。
    /// </summary>
    private static List<MegaCrit.Sts2.Core.MonsterMoves.Intents.AbstractIntent> ForcedIntents(BossIntentTag tags)
    {
        var list = new List<MegaCrit.Sts2.Core.MonsterMoves.Intents.AbstractIntent>();
        if (tags == BossIntentTag.None) return list;
        if ((tags & BossIntentTag.Defend) != 0)
            list.Add(new MegaCrit.Sts2.Core.MonsterMoves.Intents.DefendIntent());
        if ((tags & BossIntentTag.Debuff) != 0)
            list.Add(new MegaCrit.Sts2.Core.MonsterMoves.Intents.DebuffIntent(false));
        if ((tags & BossIntentTag.Buff) != 0)
            list.Add(new MegaCrit.Sts2.Core.MonsterMoves.Intents.BuffIntent());
        return list;
    }

    /// <summary>
    /// 本回合剧本里那张「天际钻头」（X 费牌）；没有就 null。
    ///
    /// 认法有两条，够用即可：
    ///   · 剧本点名「只有玩家选了某张才打得出」的那张（第 1 回合）；
    ///   · **X 费牌**（第 5 回合的钻头是常驻的、没有 `OnlyIfChosenCard`，但它是 `HasEnergyCostX`）。
    /// </summary>
    private static CardModel? FindChoiceDrill(Monsters.CharBossMonster? boss, int turnNumber)
    {
        try
        {
            var scriptTurn = boss?.Def?.ScriptForAct(boss.ActIndex)?.TurnAt(turnNumber);
            var pcs = boss?.Shadow?.PlayerCombatState;
            if (scriptTurn?.Cards == null || pcs == null) return null;
            foreach (var entry in scriptTurn.Cards)
            {
                if (entry?.Card == null) continue;
                var card = FirstHandCardOf(pcs, entry);
                if (card == null) continue;
                bool xCost = false;
                try { xCost = card.EnergyCost.CostsX; } catch { xCost = false; }
                if (entry.OnlyIfChosenCard != null || xCost) return card;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>她手里「是这条剧本牌」的第一张实例（按类型 / 模型 id 比，升级版也算）。</summary>
    private static CardModel? FirstHandCardOf(MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState pcs, ScriptCard entry)
    {
        try
        {
            string? wanted = null;
            try { wanted = ModelDb.GetId(entry.Card)?.Entry; } catch { wanted = null; }
            foreach (var card in pcs.Hand.Cards)
            {
                if (card == null) continue;
                try
                {
                    if (card.GetType() == entry.Card) return card;
                    if (wanted == null) continue;
                    string? actual = ModelDb.GetId(card.GetType())?.Entry;
                    if (string.Equals(actual, wanted, StringComparison.Ordinal)) return card;
                }
                catch { /* 单张读不到就跳过 */ }
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>
    /// 天际钻头「原本」与「选了星位序列之后」各打几段（用户 2026-11-10 的口径：
    /// 星位序列被「抉择，抉择」打出 3 次、每次 +2 费 ⇒ 钻头的 X 多 6 点，而 X≥4 时 X 会翻倍）。
    /// 返回 (原本段数, 选它之后段数)；「多出来的」由调用方相减。
    /// </summary>
    private static (int Base, int Total) DrillHitsPreview(List<CardModel> hand, CardModel alignmentCard, int energyLeft)
    {
        // 星位序列每次给多少费（未升级 2、升级 3）
        int perPlay = 2;
        try { perPlay = Math.Max(0, (int)Math.Round(ReadVar(alignmentCard, "Energy"))); }
        catch { perPlay = 2; }

        // 「抉择，抉择」的 Repeat（未升级 3、升级 5 ⇒ 升级后是抽牌数变 5 而不是重复次数）
        int repeat = 3;
        try
        {
            foreach (var card in hand)
            {
                if (card == null) continue;
                if (SafeEntry(card) != "DECISIONS_DECISIONS") continue;
                repeat = Math.Max(1, (int)Math.Round(ReadVar(card, "Repeat")));
                break;
            }
        }
        catch { repeat = 3; }

        int baseX = Math.Max(0, energyLeft);
        int totalX = baseX + Math.Max(0, perPlay) * Math.Max(1, repeat);
        return (DrillHitsForX(baseX), DrillHitsForX(totalX));
    }

    /// <summary>天际钻头的段数 = X（X≥4 时照原版翻倍；X≤0 一段都打不出）。</summary>
    private static int DrillHitsForX(int x)
    {
        if (x <= 0) return 0;
        return x >= 4 ? x * 2 : x;
    }

    /// <summary>这张手牌是不是剧本点名的那张（**多人换牌后的类型**也算命中）。</summary>
    private static bool Matches(CardModel card, ScriptCard entry, Monsters.CharBossMonster? boss = null)    {
        try
        {
            var actual = card.GetType();
            var expected = entry.Card;
            if (ModelDb.GetId(actual) == ModelDb.GetId(expected)) return true;

            // 多人换牌表里的「换上那张」：不打印日志（每次比牌都打一条会刷屏）
            var swapped = boss != null ? MultiplayerCardSwap.ResolveQuiet(expected, boss) : null;
            return swapped != null && swapped != expected && ModelDb.GetId(actual) == ModelDb.GetId(swapped);
        }
        catch { return false; }
    }

    /// <summary>剧本里的牌只排除「根本打不出去」的（状态/诅咒/不可打出），不做 AI 偏好过滤。</summary>
    /// <summary>
    /// 这张剧本牌是不是「被烧掉之后**回合结束会在消耗牌堆里自动打出**」的那张
    /// （剧本 <see cref="BossTurn.FreePlayExhaustEntry"/> 指定的，例如彼岸咆哮）。
    /// </summary>
    private static bool IsAutoPlayFromExhaust(Monsters.CharBossMonster boss, int turnNumber, CardModel card)
    {
        try
        {
            string? entry = boss.Def?.ScriptForAct(boss.ActIndex)?.TurnAt(turnNumber)?.FreePlayExhaustEntry;
            if (string.IsNullOrEmpty(entry) || card == null) return false;
            string cardEntry;
            try { cardEntry = card.Id.Entry ?? ""; } catch { return false; }
            return cardEntry == entry;
        }
        catch { return false; }
    }
    private static bool IsPlayable(CardModel card)
    {
        // 「执迷」（Enthralled）是**可以打出的诅咒**（2 费、带「永恒」：在手牌里就必须先打它），
        // 用户 2026-10-27 要求三层「巨石战」真的把它打出去 ⇒ 单独放行。
        try { if (card.Id.Entry == "ENTHRALLED") return true; } catch { /* 忽略 */ }
        if (card.Type is CardType.Status or CardType.Curse or CardType.Quest) return false;
        try { return !card.Keywords.Contains(CardKeyword.Unplayable); }
        catch { return true; }
    }

    /// <summary>她身上有没有「灵魂绑定」（只有二层灵魂骨有）。</summary>
    private static bool HasSoulBound(CharBossMonster boss)
    {
        try { return boss.Creature?.GetPower<Powers.BossSoulBoundPower>() != null; }
        catch { return false; }
    }

    /// <summary>
    /// 这张剧本牌打出后会让她「额外抽几张」（目前只有「灵魂」：`Cards` 变量）。
    /// 「自动化」靠它算这回合还会到账多少能量（见 <c>NoteAutomationDraws</c>）。
    /// </summary>
    private static int InTurnDrawsOf(CardModel card, bool soulBound)
    {
        try
        {
            if (!soulBound) return 0;
            if (SafeEntry(card) != "SOUL") return 0;
            return Math.Max(0, (int)Math.Round(ReadVar(card, "Cards")));
        }
        catch { return 0; }
    }

    /// <summary>
    /// 这张牌是不是 **X 费牌**（X = 打出时花光本回合剩余能量，例如「挽歌」「根除」）。
    /// 判定只能问 `EnergyCost.CostsX` —— 卡牌上的 `HasEnergyCostX` 是 protected，mod 里访问不到。
    /// </summary>
    internal static bool IsXCost(CardModel card)
    {
        try { return card.EnergyCost.CostsX; }
        catch { return false; }
    }

    /// <summary>
    /// 这张牌的 `Energy` 变量**不是**「现在回能量」，规划期不能当成立刻到手：
    ///   · 拖延 <c>Delay</c>：`PowerCmd.Apply&lt;EnergyNextTurnPower&gt;(…, DynamicVars.Energy.IntValue, …)` → **下回合**；
    ///   · 充电 <c>ChargeBattery</c>：同上（反编译 `PowerCmd.Apply&lt;EnergyNextTurnPower&gt;`）→ **下回合**；
    ///     物理机（二层故障机器人）第 4/8 回合都有它，不排除的话这两回合会被判成「多 1 费、打击也打得出去」
    ///     （用户剧本里写的是「打击没费」）；
    ///   · 自动化 <c>Automation</c>：`EnergyVar` 是「每抽 10 张给多少」，不是立刻给；
    ///   · **友谊 <c>Friendship</c>**：反编译是
    ///     `PowerCmd.Apply&lt;FriendshipPower&gt;(…, DynamicVars.Energy.BaseValue, …)`，而
    ///     `FriendshipPower` 只实现 `ModifyMaxEnergy => amount + Amount` —— 它涨的是**每回合的能量上限**，
    ///     不是当场回 1 费。（用户 2026-10-29：灾厄骨第 6 回合用 2 费打出了
    ///     友谊 + 出击 + **护卫** 三张 1 费牌；剧本写的是「护卫没费」，那点凭空多出来的费就是它。）
    /// （鬼火 <c>Wisp</c> 那种 `PlayerCmd.GainEnergy(DynamicVars.Energy.IntValue, owner)` 才是立刻回能，不在此列。）
    ///
    /// 同一份判据同时被**规划期**和**出牌期**用（`CharBossMonster` 的「回能 +N」），
    /// 所以这里加一条，两边就一起对齐了。
    /// </summary>
    internal static bool DeferredEnergyCard(CardModel card)
    {
        string entry = SafeEntry(card);
        return entry == "DELAY" || entry == "CHARGE_BATTERY" || entry == "AUTOMATION"
               || entry == "FRIENDSHIP"
               // ★ 2026-11-10（三层「抉择储」，用户实测第 9 / 10 回合）：
               //   · 汇流 `Convergence`：本回合保留手牌，**下个回合**获得 1 能量；
               //   · 淬炼刀刃 `RefineBlade`：铸造之后**下个回合**获得 1 能量。
               //   不排除的话规划期会当成立刻回 1 费 ⇒ 该回合最后那张「隐秘藏品」被画成还能打。
               || entry == "CONVERGENCE" || entry == "REFINE_BLADE";
    }

    /// <summary>这张牌的 `Stars` 变量是「**下回合**获得」而不是现在就到手（汇流）。</summary>
    internal static bool DeferredStarCard(CardModel card)
    {
        try { return SafeEntry(card) == "CONVERGENCE"; }
        catch { return false; }
    }

    /// <summary>这张牌是 **X 星费牌**（把身上所有辉星花光，段数 = 花掉的星数；星尘 `Stardust`）。</summary>
    internal static bool IsStarXCost(CardModel card)
    {
        try { return card.HasStarCostX; }
        catch { return false; }
    }

    private static bool HasPower<TPower>(CharBossMonster boss) where TPower : PowerModel
    {
        try { return boss.Creature?.GetPower<TPower>() != null; }
        catch { return false; }
    }

    /// <summary>读一张牌的某个动态变量（BaseValue）。</summary>
    private static decimal ReadVar(MegaCrit.Sts2.Core.Models.CardModel card, string key)
    {
        try
        {
            if (card.DynamicVars.TryGetValue(key, out var v) && v != null) return v.BaseValue;
        }
        catch { /* 忽略 */ }
        return 0m;
    }

    /// <summary>读卡的某个 DynamicVar（读不到用 <paramref name="fallback"/>）。</summary>
    private static double ReadVarOr(CardModel? card, string key, double fallback)
    {
        try
        {
            if (card == null) return fallback;
            return card.DynamicVars.TryGetValue(key, out var v) && v != null
                ? (double)v.BaseValue
                : fallback;
        }
        catch { return fallback; }
    }

    /// <summary>读一组 DynamicVars 里的某个键（读不到用 <paramref name="fallback"/>）。</summary>
    private static double ReadVarOr(MegaCrit.Sts2.Core.Localization.DynamicVars.DynamicVarSet? vars, string key, double fallback)
    {
        try
        {
            if (vars == null) return fallback;
            return vars.TryGetValue(key, out var v) && v != null ? (double)v.BaseValue : fallback;
        }
        catch { return fallback; }
    }

    /// <summary>她身上有没有某件遗物（按 Id.Entry 判）。</summary>
    private static bool HasRelicOf(Monsters.CharBossMonster? boss, string entry) => FindRelic(boss, entry) != null;

    /// <summary>找她身上的某件遗物（找不到返回 null）。</summary>
    private static MegaCrit.Sts2.Core.Models.RelicModel? FindRelic(Monsters.CharBossMonster? boss, string entry)
    {
        try
        {
            var relics = boss?.Shadow?.Relics;
            if (relics == null) return null;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                string e;
                try { e = relic.Id.Entry; } catch { continue; }
                if (string.Equals(e, entry, StringComparison.Ordinal)) return relic;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }
    /// <summary>
    /// 「**即将被打出的牌**」的意图预览 —— 惊逃 / 奇巧 / 彼岸咆哮这三种「特殊打出方式」统一走这里。
    ///
    /// 用户 2026-11-02：
    /// > 「你让惊逃、奇巧、彼岸咆哮这些特殊打出方式的伤害和普通出牌的伤害预览计算方式对齐
    /// >  （你仍可以把他们分成不同的类）。比如这里，按照打出卡牌的预览计算方式是对的，
    /// >  而惊逃的计算方式就是错的（进阶 9~10 升级预备打击，预备打击+ 会提供 4 点临时力量而非 3 点）。」
    ///
    /// 所以：**参数口径与「她自己出牌」那条完全一致**（`pendingDrop` / `pendingRise` /
    /// `extraDamage: inTurnStrength` / `extraHits: 0`），只有「本条路径特有的东西」（X 费段数、
    /// 全身撞击的格挡、爪击成长…）才留各自的分支里。
    /// ⚠ 以前三条路径各写一份参数，`inTurnStrength` 的取法一旦不同就会出现
    /// 「同一张打击，计划里 10、惊逃里 9」这种对不上的情况。
    /// </summary>
    private static List<MegaCrit.Sts2.Core.MonsterMoves.Intents.AbstractIntent> PreviewIntents(
        CardModel card, MegaCrit.Sts2.Core.Entities.Creatures.Creature target,
        int pendingDrop, int pendingRise, int inTurnStrength)
    {
        return BossIntentBuilder.IntentsFor(card, target, pendingDrop, pendingRise,
            extraDamage: 0, extraHits: 0, strengthBonus: inTurnStrength);
    }
    /// <summary>她身上有没有「必备工具」（每回合开始抽 1 丢 1 —— 那份弃牌要算进铭记死亡）。</summary>
    internal static bool HasToolsOfTrade(Monsters.CharBossMonster? boss)
    {
        try
        {
            var powers = boss?.Creature?.Powers;
            if (powers == null) return false;
            foreach (var p in powers)
            {
                string entry;
                try { entry = p?.Id.Entry ?? string.Empty; } catch { continue; }
                if (entry.Contains("TOOLS_OF_THE_TRADE")) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    private static string SafeEntry(CardModel card)
    {
        try { return card.Id.Entry; }
        catch { return string.Empty; }
    }

    /// <summary>剧本里那一项**声明的类型**对应的模型 entry（不构造卡实例，快且不会失败）。</summary>
    private static string EntryOfScriptType(Type? cardType)
    {
        try
        {
            if (cardType == null) return string.Empty;
            return ModelDb.GetId(cardType)?.Entry ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private static bool IsBlade(CardModel card)
        => SafeEntry(card) == "SOVEREIGN_BLADE";

    /// <summary>这张牌是不是 X 费牌（根除）：X 的值 = 打出时花掉的全部剩余能量。</summary>
    internal static int XHitBonus(CardModel card, int cost)
    {
        try
        {
            // 判定只能问 EnergyCost.CostsX ——卡牌上的 HasEnergyCostX 是 protected，mod 里访问不到。
            if (!card.EnergyCost.CostsX) return 0;
            // ⚠ 2026-11-10（用户：「第 13 回合只有两费，崇拜花了一费，天际钻头应该只有一段伤害」）：
            //   以前**优先**用 `ResolveEnergyXValue()`（它读的是「已经捕获的 X」）——
            //   写在意图重算那一刻，这个值可能是上一张牌 / 上一次捕获留下的
            //   ⇒ 明明这一回合只剩 1 费，画面却是 2 段。现在反过来：**规划期算出来的 `cost`
            //   优先**（它才是「出手那一刻的剩余能量」），只有 `cost <= 0`（重算路径没给）才退回
            //   `ResolveEnergyXValue()`，而且要求它自己也 > 0。
            int x = Math.Max(0, cost);
            if (x <= 0)
            {
                try { x = card.ResolveEnergyXValue(); } catch { x = 0; }
            }
            // 天际钻头「X≥4 时 X 翻倍」由 `IntentsFor` 自己按 `xHits` 还原 X 再处理段数。
            return Math.Max(0, x - 1);
        }
        catch { return 0; }
    }

    /// <summary>
    /// 「重压」（Flatten）的免费条件：**只要本回合奥斯提已经攻击过，它就是 0 费**。
    /// 原版实现（<c>Flatten.AfterAttack</c>）：<c>Attacker == Owner.Osty</c> → <c>EnergyCost.SetThisTurn(0)</c>；
    /// <c>AfterCardEnteredCombat</c> 里若本回合奥斯提已经攻击过，进手牌时也直接变 0 费。
    /// 戳击（Poke）／出击（Unleash）都是 <c>FromOsty</c> 出伤 → 打完之后重压就是 0 费，规划期必须记账
    /// （用户实测：第 7 回合戳击打完重压变 0 费却没打出去）。
    ///
    /// ★ v0.2.339（用户 2026-11-03：「第二回合猛晃和增生应该打出，因为重压在**紧追不放**打出后为 0 费」）：
    ///   这张表以前只有戳击/重压/出击/碎骨，**漏了「紧追不放」（SicEm）、「猛晃」（Rattle）、
    ///   「击掌」（HighFive）、「响指」（Snap）** —— 它们同样是 `<c>OstyDamage</c>` 变量、
    ///   同样由奥斯提出手，可规划期没被算成「奥斯提攻击过」⇒ 重压仍按 2 费记账 ⇒
    ///   后面的猛晃/增生因「费不够」被跳过（用户实测）。
    ///   现在改成**数据判据**：只要这张牌带 <c>OstyDamage</c> 变量就算（<see cref="BossDamagePredictor.HasVar"/>），
    ///   以后原版再加新的奥斯提攻击牌也不用改这里。剩下两张不走 OstyDamage 的（出击 / 碎骨）由名单兜底。
    /// </summary>
    private static readonly HashSet<string> OstyAttackEntries = new(StringComparer.Ordinal)
    {
        "UNLEASH", "BONE_SHARDS",   // 用 CalculatedDamage / 别的变量出伤，不带 OstyDamage
    };

    private static bool CostsNothingWithOsty(CardModel card)
        => SafeEntry(card) == "FLATTEN";

    internal static bool IsOstyAttack(CardModel card)
    {
        try
        {
            if (OstyAttackEntries.Contains(SafeEntry(card))) return true;
            return BossDamagePredictor.HasVar(card, "OstyDamage");
        }
        catch { return false; }
    }

    /// <summary>
    /// 这张牌打出时会**生成几张牌**（超质量体的加伤按「生成过的牌数 × 4」算，规划期要能预告）。
    /// 数据上能看出来的：君权自授 / 发现 = 1 张；光谱偏移 = 它的 <c>Cards</c> 变量（每回合开始加 N 张）。
    /// </summary>
    private static int GeneratedBy(CardModel card)
    {
        var entry = SafeEntry(card);
        if (entry == "MANIFEST_AUTHORITY" || entry == "DISCOVERY") return 1;
        if (entry == "SPECTRUM_SHIFT") return Math.Max(0, (int)Math.Round(ReadVar(card, "Cards")));
        return 0;
    }

    /// <summary>
    /// 这张牌会给奥斯提加多少生命 —— 统一委托给 <see cref="BossDamagePredictor.OstyHpGainOf"/>：
    /// 召唤牌取 `Summon` 变量，「灵魂」在她有「吞噬生命」时取 `DevourLifePower.Amount`。
    /// 只有「出击」的伤害公式吃奥斯提生命（<c>CalculationBase + ExtraDamage × 奥斯提生命</c>），
    /// 所以这个数只用来补出击的预览。
    /// </summary>
    private static int OstyHpGainOf(CardModel card, CharBossMonster boss)
        => BossDamagePredictor.OstyHpGainOf(card, boss);

    /// <summary>「出击」每一张对应的「1 × 奥斯提生命」系数（ExtraDamage）；其它牌返回 0。</summary>
    private static int XtraOstyHpBonus(CardModel card)
    {
        try
        {
            if (!BossDamagePredictor.HasVar(card, "ExtraDamage")) return 0;
            if (!BossDamagePredictor.HasVar(card, "OstyDamage") && SafeEntry(card) != "UNLEASH") return 0;
            return Math.Max(0, (int)Math.Round(ReadVar(card, "ExtraDamage")));
        }
        catch { return 0; }
    }

    /// <summary>
    /// 他**回合开始**会到账的格挡 —— 给「全身撞击」的意图用。
    ///
    /// 为什么需要：格挡在**他自己回合开始**清空，所以玩家回合里看到的格挡数并不等于他出手时的格挡。
    /// 目前两处：
    ///   · 「绯红披风」`CrimsonMantlePower` —— 反编译：`AfterPlayerTurnStart` → `CreatureCmd.GainBlock(owner, Amount)`；
    ///   · 「自成型黏土」`SelfFormingClayPower` —— 遗物每次**未格挡伤害 +3 层**，回合开始按层数给格挡
    ///     （放血战一直在自伤/被打，这笔通常不小）。
    /// 以后若再加「回合开始给格挡」的牌/遗物，在这里补一行即可。
    /// </summary>
    internal static int TurnStartBlockOf(MegaCrit.Sts2.Core.Entities.Creatures.Creature? creature)
    {
        if (creature == null) return 0;
        int block = 0;
        try { block += Math.Max(0, creature.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.CrimsonMantlePower>()); }
        catch { /* 没有这层 power */ }
        try { block += Math.Max(0, creature.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.SelfFormingClayPower>()); }
        catch { /* 没有这层 power */ }
        return block;
    }

    /// <summary>这张牌打出时会给自己多少格挡（`BlockVar.BaseValue`；没有就 0）。</summary>
    internal static int BlockGainOf(CardModel card)
    {
        try { return Math.Max(0, (int)Math.Round(ReadVar(card, "Block"))); }
        catch { return 0; }
    }
    /// <summary>她有没有象牙麻将牌（IvoryTile）—— 耗能 ≥3 的牌打出后回 1 费。</summary>
    private static bool HasIvoryTile(CharBossMonster boss)
    {
        try
        {
            var relics = boss.Shadow?.Relics;
            if (relics == null) return false;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                if (relic.Id.Entry == "IVORY_TILE") return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    /// <summary>
    /// 熔炉（<c>FurnacePower</c>）在他**回合开始**铸造的量 —— 规划发生在玩家回合开始前，
    /// 那笔还没进君王之剑的卡面，所以要自己加到意图里（用户要求：意图能提前算上熔炉的铸造）。
    /// </summary>
    public static int PendingFurnaceForge(CharBossMonster boss)
    {
        try
        {
            var power = boss.Creature?.GetPower<MegaCrit.Sts2.Core.Models.Powers.FurnacePower>();
            if (power == null) return 0;
            return Math.Max(0, (int)Math.Round((double)power.Amount));
        }
        catch { return 0; }
    }

    /// <summary>
    /// 这张仆从牌在**这一手**会不会是「召唤」（而不是原版的伤害/格挡）。
    /// 规则（用户 2026-11-05）：仆从数不足 2 就改为召唤 ⇒ 按剧本顺序累加，前两张仆从牌是召唤。
    /// </summary>
    private static bool IsMinionSummonHere(CardModel? card, Monsters.CharBossMonster boss, ref int planned)
    {
        try
        {
            if (card == null) return false;
            string entry = SafeEntry(card);
            if (entry != "MINION_STRIKE" && entry != "MINION_DIVE_BOMB" && entry != "MINION_SACRIFICE") return false;
            if (boss.Creature?.GetPower<Powers.BossCallToArmsPower>() == null) return false;
            if (planned >= Powers.BossCallToArmsPower.MinionCap) return false;
            planned++;
            return true;
        }
        catch { return false; }
    }
}
