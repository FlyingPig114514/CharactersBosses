using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 静默猎手（三层：刀猎）的「成长」修正 —— 让**每张牌自己的意图**都等于它真正打出时的伤害。
///
/// 问题的由来（用户实测）：她的遗物
///   · **手里剑** <c>Shuriken</c>：每打出 3 张攻击牌 → +1 力量；
///   · 苦无 <c>Kunai</c>：每 3 张攻击 → +1 敏捷（只影响格挡，意图不显示数字）；
///   · 精致折扇 <c>OrnamentalFan</c>：每 3 张攻击 → +4 格挡（同上）；
/// 是在**她出牌的过程中**逐张累加的，而意图是在**玩家回合开始**一次性算死的
/// ⇒ 越靠后的攻击牌，预览就越低（触发过袖里乾坤塞小刀时，一回合能多出一到两次成长，差 2 点）。
///
/// 做法：按**计划顺序**走一遍，边算边累加「已经打出的攻击牌张数」，
/// 把 <c>floor(已打攻击数 / 3)</c> 当作这张牌要补的力量，喂进
/// <see cref="BossIntentBuilder.IntentsFor"/> 的 <c>pendingRise</c>（它只是加在基础值上，
/// 意图节点之后还会再补一次当前力量 —— 两者相加正好等于出手那一刻的真实力量）。
///
/// 注意：
///   · 攻击牌按**张**计数（手里剑的原文就是"每 3 张攻击牌"），小刀也是攻击牌；
///   · 生成刀的那张牌（刀刃之舞等）会把小刀当回合打出去 → 那些小刀也计入攻击张数；
///   · 终结技的段数另算（段数＝本回合在它之前打出的攻击牌数），这里一并重算。
/// </summary>
public static class BladesGrowth
{
    private const string ShurikenEntry = "SHURIKEN";
    private const int AttacksPerTrigger = 3;

    /// <summary>她有手里剑吗。</summary>
    private static bool HasShuriken(CharBossMonster boss)
    {
        try
        {
            var relics = boss.Shadow?.Relics;
            if (relics == null) return false;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                if (relic.Id.Entry == ShurikenEntry) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    /// <summary>她当前的力量（读数用）。</summary>
    public static int CurrentStrength(CharBossMonster boss)
    {
        try { return boss.Creature?.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>() ?? 0; }
        catch { return 0; }
    }

    /// <summary>
    /// 按计划顺序重算**所有**牌的意图（含手里剑成长与终结技段数）。
    /// 只用于不会激发充能球的 Boss（静默猎手）—— 有球的 Boss 的激发意图是另外追加的，重算会丢。
    ///
    /// <paramref name="planTime"/> = true（规划期，玩家回合开始）：把"还没到手的力量"提前补进预览
    /// （烘焙手套每回合 +1、手里剑每 3 张攻击 +1）。
    ///
    /// = false（**出牌期**）：不再一刀切清零，而是按
    /// <c>补正 = 预测总成长 − 本回合已真实获得的成长</c> 逐张算 ——
    /// 也就是"获得几点撤几点"：她打完第 3、6…张攻击牌、手里剑真的给力量之后，
    /// 后面那些牌上提前补的那点才会相应减少；还没触发之前不该提前撤（用户实测纠正过）。
    /// 没有手里剑的 Boss 预测恒为 0 ⇒ 这里等于不补任何东西，不受影响。
    /// </summary>
    public static void RecomputeAll(CharBossMonster boss, List<PlannedPlay> plan, bool planTime = true)
    {
        if (boss?.Creature == null || plan == null || plan.Count == 0) return;

        try
        {
            // 有球位就别动（球激发意图是 BossOrbSim 额外加的，这里重算会覆盖掉）
            if ((boss.Shadow?.PlayerCombatState?.OrbQueue.Capacity ?? 0) > 0) return;

            bool shuriken = HasShuriken(boss);
            int attacks = planTime ? 0 : Math.Max(0, boss.AttacksPlayed);

            // 灵魂绑定（二层灵魂骨）：死亡行军的伤害 = 基础 + 4 × 本回合抽到的牌数。
            // 她「本回合还会额外抽的牌」要提前算进意图（用户 2026-10-23），
            // 已经真的抽掉的（HerOwnDrawsThisTurn）从预补里扣掉，不和历史条目重复。
            bool soulBound = HasSoulBound(boss);

            // 预补池：**规划期一次算好就冻结**，之后靠「真的增加多少就撤多少」维护
            // （用户 2026-10-23：补偿要在伤害确实增加后再撤回，别在她出牌过程中来回闪）。
            if (planTime) PlanCompensation(boss, plan, soulBound, includeTurnStartSummon: true);
            int drawComp = boss.DrawCompensation;
            int soulComp = boss.SoulStormCompensation;
            int ostyComp = boss.UnleashCompensation;

            // 「烙印」这类自己加力量的牌：它**之后**那些攻击的伤害预览要先把这笔力量算进去，
            // 等它真打出去、力量到账后再按同样数量撤回（用户 2026-10-23）。
            // 逐张累计，并减掉本回合**已经真的拿到**的那部分（`PlannedStrengthGainedThisTurn`）。
            int plannedStrength = 0;

            // ── 「撕裂」（Rupture）带来的力量（用户 2026-10-28：「伤害预览有时不会计算自伤卡配合撕裂获得的力量」）──
            // 反编译：`RupturePower` = 「每当你在你的回合内失去生命 → 力量 +层数」，
            // 而血墙 / 御血术 / 突破 / 烙印都是「**先**扣自己的血、**再**打伤害/给格挡」
            // ⇒ 这条力量对**它自己那一下**也生效，对后面所有攻击牌当然也生效。
            // `ruptureNow` 从她身上现有的层数起算，剧本里打到「撕裂」时再加上那张的层数。
            int ruptureNow = RuptureStacksOf(boss);

            // 「迷你储君」遗物：**每回合第一次花费辉星**时 +N 力量（用户 2026-10-28：耗星牌带来的力量
            // 有时没被伤害预览算进去）。规划期按剧本顺序盯「第一张有星费的牌」，打到它就记一笔待补力量；
            // 出牌期那笔由原版 MiniRegent.AfterStarsSpent 真实到账（我们扣星时已经广播过这个钩子）。
            int miniRegentStrength = 0;
            try
            {
                var relicsM = boss.Shadow?.Relics;
                for (int i = 0; i < (relicsM?.Count ?? 0); i++)
                {
                    var r = relicsM![i];
                    if (r == null) continue;
                    if (r.Id.Entry != "MINI_REGENT") continue;
                    try { miniRegentStrength = Math.Max(0, (int)Math.Round((double)r.DynamicVars["Strength"].BaseValue)); }
                    catch { miniRegentStrength = 1; }
                    break;
                }
            }
            catch { miniRegentStrength = 0; }
            bool miniRegentUsed = false;


            // 规划期：手套这类"回合开始前会涨"的力量先补上；出牌期：它已经真到账，不再补。
            int plannedBaseRise = planTime ? BossIntentBuilder.PendingRise(boss.Creature) : 0;

            // ── 喝药水带来的**临时力量**（爬行动物饰品 ReptileTrinket）──
            // 用户 2026-10-26：「提前算好攻击牌的实际伤害，比如喝药会因为爬行动物饰品增加临时力量」。
            // 反编译：`ReptileTrinket.AfterPotionUsed` → `PowerCmd.Apply<ReptileTrinketPower>(..., 3)`
            // 而 `ReptileTrinketPower : TemporaryStrengthPower` ⇒ **每喝一瓶 +3 力量（本回合）**。
            // ⚠ 这里按「**还没喝的那几瓶**」补，不是一次补满本回合全部：
            //   她喝掉一瓶，这笔预补就少一瓶、真实力量多一瓶 ⇒ 显示值恒等于「真实力量 + 预补」，
            //   不会出现「先 +9 再跳回去」（用户 2026-10-26 实测的第二个问题）。
            int potionRise = PotionStrengthRise(boss, planTime);
            if (potionRise > 0) plannedBaseRise += potionRise;

            // ── 精确切击 / 群蛇形态 ──
            // 精确切击的伤害取决于**打出那一刻**的手牌数（13 − 2 × 其他牌数），
            // 群蛇形态让「每张牌」都多打 4 点（随机敌人、不吃力量）⇒ 都要按计划顺序逐张算。
            int handStart = LiveHandCount(boss);      // 规划期：现在手里有几张
            // 「精确切击」要看**打出那一刻**手里还有几张。手牌的收缩不是「每张牌 −1」这么简单：
            // 生存者会额外弃 1 张（反编译 `FromHandForDiscard(…, 1)`）、杂技抽 3 弃 1（净 +2）…
            // ⇒ 逐张累加「这张牌打完之后手牌净少几张」，见 HandDeltaOf（用户 2026-10-26 实测：
            // 预测 18、实打 20，差的正是生存者弃掉的那 1 张）。
            int playedShrink = 0;     // 计划期：这张牌之前所有牌造成的收缩
            int pendingShrink = 0;    // 出牌期：这张牌之前**还没打出去**的那些牌造成的收缩
            int serpent = ExistingSerpentDamage(boss); // 她身上已有的群蛇形态层数（后续回合才是非 0）

            // 规划期还要补「他回合开始才会到账」的熔炉铸造（FurnacePower.AfterSideTurnStart 每回合铸 5）：
            // 计划是在玩家回合开始前算的，那时这笔还没进剑的卡面，意图就先把它算上（用户实测要求）。
            // ⚠ **判据不再用 planTime**（用户 2026-10-24 反馈「君王之剑意图还是被低估」）：
            //    「何人僭越」在玩家回合里触发时会用 planTime=false 重算意图，旧写法一重算就把这笔抹成 0。
            //    改成看「现在是谁在行动」：玩家侧 ⇒ 她这回合的铸造还没发生，要补；她自己行动 ⇒ 已到账，不补。
            bool playerSideActing = false;
            try
            {
                playerSideActing = boss.CombatState?.CurrentSide
                                   == MegaCrit.Sts2.Core.Combat.CombatSide.Player;
            }
            catch { /* 忽略：拿不到就按"不补"处理，宁可低也不要重复 */ }

            int bladePendingForge = playerSideActing ? BossScriptPlanner.PendingFurnaceForge(boss) : 0;
            // 「剑圣」的重放次数（剧本里还没打出的那几张剑圣带来的）
            int bladePendingHits = 0;

            // ── 「这张牌会被打几次」（复制药水 / 爆发）──
            // 反编译（两者都是「把 playCount +1 然后消耗一层」）：
            //   DuplicationPower.ModifyCardPlayCount：任意牌 +1（`card.Owner.Creature == Owner`）
            //   BurstPower.ModifyCardPlayCount：**只对 CardType.Skill** +1（回合结束自己移除）
            // 而 `CardCmd.AutoPlay` 不跑这两条钩子 ⇒ 运行时由 `CharBossMonster.PlayOne` 手动重放，
            // 显示侧就在这里按同一套规则算：`dupLeft` / `burstLeft` 各消耗一层。
            int dupLeft = SafePowerAmount<MegaCrit.Sts2.Core.Models.Powers.DuplicationPower>(boss);
            int burstLeft = SafePowerAmount<MegaCrit.Sts2.Core.Models.Powers.BurstPower>(boss);
            // 本回合「已经（将会）丢弃几张」：给「铭记死亡」的伤害用（沿计划累加，见 DiscardsOf）
            //
            // ★ v0.2.527（用户 2026-11-09：「第九回合铭记死亡的伤害预览还是低了 4」）：
            //   「必备工具」在**她回合开始**就先丢 1 张，那一刻发生在整套计划之前
            //   ⇒ 铭记死亡打出时她本回合已经丢了 2 张（必备工具 1 + 杂技 1），预览只算了 1 张
            //   ⇒ 少 4 点（`MementoMori` 每丢一张 +4）。实测日志：面板 13、真实 17。
            //   规划期 `BossScriptPlanner` 早就有这个起算值，这里漏了 ⇒ 那条「每帧重算」把规划值盖掉。
            int discardsSoFar = BossScriptPlanner.HasToolsOfTrade(boss) ? 1 : 0;
            // 本回合「打某张牌之前先喝一瓶」的队列（复制药水夹在牌中间那种）：按剧本顺序取用
            var pendingDrinks = new List<(Type Card, Type Potion)>();
            try { foreach (var d in boss.PlannedDrinks ?? new Queue<(Type Card, Type Potion)>()) pendingDrinks.Add(d); }
            catch { /* 忽略 */ }
            int drinkIndex = 0;
            // 「打某张牌之前先喝一瓶」带来的爬行动物饰品力量（只有规划期需要预补）
            int drinkRise = 0;

            // 出牌期：**已经该到手**的成长，由我们自己数的攻击张数算 ——
            // 不去读"当前力量"来判断（手里剑那 +1 是遗物钩子异步加的，读太早会读到旧值，
            // 于是补正留到力量真到账之后 ⇒ 所有剩余攻击牌都高 1，用户实测）。
            // 用 attacksPlayed/3 这个纯计数，和意图节点渲染时补的力量是同一把尺子，天然对齐。
            int appliedGrowth = (!planTime && shuriken)
                ? Math.Max(0, boss.AttacksPlayed / AttacksPerTrigger)
                : 0;

            var snapshot = new List<string>();   // 调试用：每张牌实际用到的补正/段数

            // 幻影之刃（用户 2026-10-24 削刀猎）：
            // 原版 `PhantomBladesPower.ModifyDamageAdditive` 要求 `cardSource` 带 Shiv 标签，
            // 而意图节点传的 `cardSource` 是 null ⇒ **原版不会替意图加这 9 点**，必须我们自己补；
            // 反过来也说明补了不会重复（反编译原文与判据见 PhantomShivs）。
            // 它只加在「本回合**第一把**小刀」上 ⇒ 由 phantomPending 保证只发一次。
            int phantom = PhantomShivs.AmountOf(boss);
            bool phantomPending = phantom > 0 && PhantomShivs.Available(boss);
            // 幻影之刃此刻**是否已经在她身上**：决定「生成小刀」的预览要不要扣回原版多给的那 9 点
            //（见 BossIntentBuilder 的 shivBonusBakedIn 注释）。
            bool phantomOnHer = phantom > 0;
            // 那张幻影之刃是不是还**在她手里**（= 本回合还没打出去）：是的话，它之后的小刀要吃这 9 点。
            // 用它来判断而不是看"谁在行动" —— 她的回合开始时意图也会重算（那时牌还在手上，也要补）。
            bool phantomInHand = false;
            try
            {
                var handCards = boss.Shadow?.PlayerCombatState?.Hand.Cards;
                if (handCards != null)
                {
                    foreach (var handCard in handCards)
                    {
                        if (!PhantomShivs.IsCard(handCard)) continue;
                        phantomInHand = true;
                        break;
                    }
                }
            }
            catch { phantomInHand = false; }

            // 「非凡技艺」（Prowess）= 力量 +1、敏捷 +1。它**之后**的攻击牌预览要提前 +1；
            // 等她真的打出去，那 +1 就已经在角色身上了 ⇒ 出牌期（planTime=false）不再预补，
            // 由 <paramref name="rise"/> 那套"还没到账的力量"负责，不会重复也不会漏（用户要求）。
            bool prowessSeen = false;

            // 「星星点点+」（Patter）= 获得 3 点**活力**（VigorPower）。活力只加**第一张**攻击牌的伤害。
            // 但意图节点自己会跑 Hook.ModifyDamage，只要她身上有活力，**每张**攻击意图都会被加 3。
            // 所以：
            //   · 规划期（还没有活力）：Patter 之后的第一张攻击牌 +3（预补），后面的不加；
            //   · 出牌期（Patter 已打出、活力在身上）：第一张保持（节点会正确加 3），
            //     后面的从基础值里扣回 3（用 pendingDrop 通道），不让节点给它加成。
            const int VigorAmount = 3;
            bool vigorSeen = false;
            int vigorAttacks = 0;

            // 「全身撞击」的伤害 = 出手那一刻的格挡（用户 2026-10-28 实测：意图没算上他本回合会拿到的格挡）。
            // 这里是**最后一处**重写意图的地方（规划期和出牌期都会走），所以两处都要记这笔：
            //   · 规划期（玩家回合）：格挡马上就要在他回合开始清空 ⇒ 只能按「回合开始会到账的格挡」起算；
            //   · 出牌期（他真在打）：此刻真实格挡已经在身上 ⇒ 直接用它起算。
            // 然后按计划顺序把每张牌自己的 `BlockVar` 累加进去（血墙 16/20、坚毅 7、防御 5…）。
            int simBlock = planTime
                ? BossScriptPlanner.TurnStartBlockOf(boss.Creature)
                : Math.Max(0, SafeBlockOf(boss.Creature));

            foreach (var play in plan)
            {
                var card = play.Card;
                if (card == null) continue;

                string entry = SafeEntry(card);

                // 群蛇形态：**这张牌之前**如果打过群蛇（或者她本来就带着），之后每张牌都多打它的层数。
                // 群蛇自己那张不标（打它的时候能力还没上身）。
                if (entry == "SERPENT_FORM")
                {
                    int fromCard = SerpentFormCardAmount(card);
                    if (fromCard > 0) serpent = fromCard;
                }
                int serpentHere = entry == "SERPENT_FORM" ? 0 : serpent;

                // 「打这张牌之前先喝一瓶」（复制药水 / 瓶中船夹在牌中间）：
                //   · 复制药水 → 给后面加一层复制；
                //   · **任何药水**都会触发爬行动物饰品的 +N 力量（`ReptileTrinket.AfterPotionUsed`），
                //     所以这之后那些攻击牌的意图也要把这一笔算进去（planTime 时预补，出牌期力量已真实到账、不补）。
                if (drinkIndex < pendingDrinks.Count
                    && pendingDrinks[drinkIndex].Card == card.GetType())
                {
                    var drinkPotion = pendingDrinks[drinkIndex].Potion;
                    drinkIndex++;
                    if (drinkPotion != null && drinkPotion == typeof(MegaCrit.Sts2.Core.Models.Potions.Duplicator))
                    {
                        dupLeft += DuplicatorLayers();
                    }
                    if (planTime && drinkPotion != null) drinkRise += PotionStrengthPerDrink(boss);
                }
                // 这张牌本身是「爆发」：**打完它之后**，本回合接下来的技能牌才会被多打一次。
                // ⚠ 必须在判断「这张牌打几次」**之后**再加它的层数 ——
                //    爆发自己是技能牌，先加就会把自己也算成被爆发影响（用户 2026-10-26 订正：
                //    「不是给爆发变 4×2，而是给受爆发影响的牌（残影）变 4×2」）。
                bool isBurstCard = entry == "BURST";

                // 这张牌会被打几次（1 = 正常；2 = 被复制药水/爆发影响）
                bool dupHere = dupLeft > 0;
                bool burstHere = burstLeft > 0 && card.Type == CardType.Skill;
                int playCount = 1 + (dupHere ? 1 : 0) + (burstHere ? 1 : 0);
                if (dupHere) dupLeft--;
                if (burstHere) burstLeft--;

                // 现在才轮到「爆发给后面加层」
                if (isBurstCard)
                {
                    int layers = BurstCardLayers(card);
                    if (layers > 0) burstLeft += layers;
                }

                // 精确切击按「**这张牌**打出那一刻」的手牌数算：
                //   · 规划期：开局手牌 − 前面那些牌造成的收缩（含它们额外的弃牌/抽牌）；
                //   · 出牌期：当前真实手牌 − **还没打出去**的那些牌将要造成的收缩。
                // 出牌期以前直接用「当前手牌数」⇒ 每打一张，剩下的牌数字都跟着变（用户看到的「一直跳」）。
                int handAtPlay = planTime
                    ? Math.Max(1, handStart - playedShrink)
                    : Math.Max(1, LiveHandCount(boss) - pendingShrink);

                // 「铭记死亡」的伤害 = 9 + 4 × **本回合已经丢弃的张数**（反编译见
                // BossDamagePredictor.MementoMoriDamage）。规划期历史里一张弃牌都没有 ⇒ 沿计划累加。
                int discardsAtPlay = discardsSoFar;

                // 这回合剧本里那张幻影之刃**还没打出去** ⇒ 打出它之后的小刀就吃那 9 点（升级 12）。
                // 她已经打出去之后就只读身上的层数，不再预判，避免重复加。
                if (phantomInHand && PhantomShivs.IsCard(card))
                {
                    phantom = PhantomShivs.AmountOf(boss) + VarInt(card, PhantomShivs.VarKey);
                    phantomPending = phantom > 0;
                    // 这张打出之后 power 就上身了 ⇒ 往后那些「生成小刀」的卡，原版会把 9 点算进 shivDamage
                    phantomOnHer = phantom > 0;
                }
                // 本回合的第一把刀（真刀，或者某张牌要印出来的第一把）才吃这份增伤
                int phantomHere = 0;
                if (phantomPending && (ShivPlays.IsShiv(card) || play.GeneratedShivs > 0))
                {
                    phantomHere = phantom;
                    phantomPending = false;
                }

                bool isAttack = card.Type == CardType.Attack;
                if (SafeEntry(card) == "PROWESS") prowessSeen = true;
                int prowessPending = (planTime && prowessSeen && isAttack) ? 1 : 0;

                // 活力（Patter+ 给的 3 点）：只有第一张攻击牌吃
                if (SafeEntry(card) == "PATTER") { vigorSeen = true; vigorAttacks = 0; }
                int vigorBonus = 0, vigorPenalty = 0;
                if (vigorSeen && isAttack)
                {
                    vigorAttacks++;
                    if (vigorAttacks == 1) { if (planTime) vigorBonus = VigorAmount; }
                    else if (!planTime) vigorPenalty = VigorPenaltyFor(boss, play.Target);
                }

                // 到这张牌为止，手里剑累计该给她多少力量
                int predicted = plannedBaseRise + (shuriken ? attacks / AttacksPerTrigger : 0);
                int rise = Math.Max(0, predicted - appliedGrowth) + drinkRise;   // 还没到账的那部分才补（含中途喝药带来的力量）

                // 剧本里「自己加力量」的牌（烙印）带来的、**这张牌还没拿到**的那部分力量。
                // 力量对伤害是 1:1（每段都加），所以直接加进 extraDamage。
                int strengthPending = Math.Max(0, plannedStrength - boss.PlannedStrengthGainedThisTurn);

                // 这张牌**自己**掉血 ⇒ 撕裂立刻涨 `ruptureNow` 点力量，而且这笔对**它自己那一下**
                // 也算得上（反编译：血墙/御血术/突破/烙印都是先扣血、再结算效果）。
                int ruptureHere = BossDamagePredictor.LosesHp(card) ? Math.Max(0, ruptureNow) : 0;
                if (ruptureHere > 0) rise += ruptureHere;

                if (SafeEntry(card) == "FINISHER")
                {
                    int perHit = BossDamagePredictor.HitDamage(card, play.Target) + rise + strengthPending;
                    int hits = Math.Max(1, attacks) * playCount;   // 被复制/爆发影响的牌真的会打两遍
                    // 只有 1 段时用单段意图（否则终结技会显示「4×1」）
                    var finisherIntents = new List<AbstractIntent> { AttackIntent.Multi(perHit, hits) };
                    if (serpentHere > 0) finisherIntents.Add(SerpentIntent(serpentHere, playCount));
                    play.Intents = finisherIntents;
                    snapshot.Add($"FINISHER(段={hits},补={rise}{(playCount > 1 ? $"×{playCount}" : "")})");
                }
                else
                {
                    // X 费牌（根除）：段数 = X = 打出那一刻的**剩余能量**。
                    // 规划期用计划里的剩余能量；出牌期（每张牌打出前重算）直接读**当前真实能量** ——
                    // 否则「自动化/鬼火」中途给的能量、或别的牌耗能变了，段数就会与实战对不上（用户 2026-10-23 实测）。
                    int xHits;
                    if (BossScriptPlanner.IsXCost(card))
                    {
                        int liveEnergy = 0;
                        try { liveEnergy = boss.Shadow?.PlayerCombatState?.Energy ?? 0; } catch { /* 0 */ }
                        xHits = Math.Max(0, (planTime ? Math.Max(0, play.Cost) : Math.Max(0, liveEnergy)) - 1);
                    }
                    else
                        xHits = BossScriptPlanner.XHitBonus(card, play.Cost);

                    // ⚠ 这里**故意不再**做「奥斯提死了就把后面的牌显示 0」那套判断（用户 2026-10-23：
                    // 「你之前修的有问题，导致所有手牌的伤害预览全变成 0 了。把那一次修的直接去掉」）。
                    // 那套判断依赖 `boss.Shadow.Osty` 是否活着，而**没有随从的 Boss（储君/猎手/故障）**
                    // 这个值永远是 null ⇒ 被判成「已死」⇒ 配上 `IsOstyAttack` 的宽判据后会吃掉别的牌的意图。
                    // 现在交还给原版：奥斯提真死了，`OstyScaledDamage` 自己就会按 0 生命算。

                    play.Intents = BossIntentBuilder.IntentsFor(card, play.Target, vigorPenalty, rise,
                        // ⚠ v0.2.362：**她自己的力量**（`strengthPending`）单独走 `strengthBonus` ——
                        //   奥斯提的牌不吃她的力量，混在 extraDamage 里会让出击多算（用户 2026-11-04 实测「多了 2」）。
                        extraDamage: (SafeEntry(card) == "SOVEREIGN_BLADE" ? bladePendingForge : 0)
                                     + prowessPending + vigorBonus
                                     + (SafeEntry(card) == "SUPERMASSIVE"
                                            ? Math.Max(0, boss.PendingGenerations) * PerUnit(card, 4)
                                            : 0)
                                     // 死亡行军 / 灵魂风暴 / 出击：从冻结的预补池里取
                                     //（真实增量到账后逐笔撤回，见 CharBossMonster.Withdraw*Compensation）
                                     + (SafeEntry(card) == "DEATH_MARCH" ? drawComp : 0)
                                     + (SafeEntry(card) == "SOUL_STORM" ? soulComp : 0)
                                     + (SafeEntry(card) == "UNLEASH" ? ostyComp : 0)
                                     // 幻影之刃：**这张牌自己就是一把真小刀**时，那 +9 直接进它的伤害
                                     //（生成刀的那几张走下面的 shivBonus 参数，只加在第一把刀上）
                                     // ⚠ v0.2.350（玩家反馈：「幻刃的增伤会错误的显示到后续小刀上」）：
                                     //   幻影之刃**已经在身上**时，真刀的 +N 由原版 power 在渲染期自己加
                                     //   （补丁 PhantomBladesFirstShivPatch 负责把后面的刀压回 0）
                                     //   ⇒ 这里不能再手工加，否则第一把会变成 +2N。
                                     //   它**还没打出去**（只在剧本里 / 手里）时 power 还没上身，才由我们补在第一位。
                                     + (ShivPlays.IsShiv(card) && !phantomOnHer ? phantomHere : 0),
                        // 君王之剑的**重放**：剧本里还没打出的「剑圣」带来的次数（以前出牌期重算完全没传，
                        // 于是「何人僭越」那三次重算把 ×N 抹回 ×1，玩家看到的意图就偏低了）
                        extraHits: (SafeEntry(card) == "SOVEREIGN_BLADE" ? bladePendingHits : 0),
                        xHits: xHits,
                        // 幻影之刃：这张牌印出来的**第一把小刀** +9（其余刀不加）
                        shivBonus: phantomHere,
                        shivBonusBakedIn: phantomOnHer,
                        // 精确切击：打出那一刻的手牌数（用户 2026-10-26 要求「按真实手牌数提前算」）
                        handAtPlay: handAtPlay,
                        // 群蛇形态：每张牌额外打 4（随机敌人、不吃力量；只画图，不额外造成伤害）
                        serpentDamage: serpentHere,
                        // 这张牌会被打几次（复制药水 / 爆发）：2 就要画成「4×2」
                        playCount: playCount,
                        // 铭记死亡：打出这张牌之前本回合已丢弃几张（规划期靠剧本累加）
                        discardsThisTurn: discardsAtPlay,
                        // 全身撞击：用手牌栏里这一手真正会有的格挡值（见上面 simBlock 注释）
                        bodySlamBlock: entry == "BODY_SLAM" ? simBlock : -1,
                        // ★ v0.2.412：烟囱段数 / 开信刀档位 / 散射炮的待生成状态牌都在规划期定死了，重算原样复用
                        smokePerHit: play.SmokePerHit,
                        smokeHits: play.SmokeHits,
                        letterOpenerHit: play.LetterOpenerHit,
                        pendingStatusCards: play.PendingStatusCards,
                        aoeFromCaller: true,
                        // ★ v0.2.527（用户 2026-11-09 截图：「钢铁风暴还是**不显示伤害意图**」）：
                        //   「钢铁风暴」的段数 = 本回合丢掉的张数，而牌面上**没有**这个变量
                        //   ⇒ 规划期是靠 `BossScriptPlanner` 按剧本算好、用 `shivOverride` 传进去的
                        //   （`PlannedPlay.GeneratedShivs` 里存的就是那个数）。
                        //   可是这条「出牌期每帧重算」的路以前**没传** ⇒ 退化成 `ShivsFrom(card)` = 0
                        //   （STORM_OF_STEEL 不在小刀生成者白名单里）⇒ 一条小刀意图都没有、
                        //   只剩一个「强化」（蓝箭头）。规划期的日志是对的
                        //   （`意图 STORM_OF_STEEL: [LiveMultiAttackIntent]`），面板上却被这里盖掉了。
                        shivOverride: SafeEntry(card) == "STORM_OF_STEEL"
                            ? Math.Max(0, play.GeneratedShivs) : 0,
                        // 烙印这类「本回合会加力量」的牌：后面的攻击先把这笔力量补上
                        //（⚠ v0.2.362：单独传，奥斯提的牌不吃她的力量）
                        strengthBonus: strengthPending);
                    snapshot.Add($"{SafeEntry(card)}(补={rise}{(strengthPending > 0 ? $"+力{strengthPending}" : "")}" +
                                 $"{(playCount > 1 ? $"×{playCount}" : "")}" +
                                 $"{(phantomHere > 0 ? $"+幻{phantomHere}" : "")}" +
                                 $"{(serpentHere > 0 ? $"+蛇{serpentHere}" : "")}" +
                                 $"{(SafeEntry(card) == "PRECISE_CUT" ? $"+手{handAtPlay}" : "")}" +
                                 $"{(SafeEntry(card) == "BODY_SLAM" ? $"+挡{simBlock}" : "")}" +
                                 // 撕裂诊断：这张牌自己掉血时补了多少力量（用户 2026-10-28 要看这个对不对）
                                 $"{(ruptureHere > 0 ? $"+撕{ruptureHere}" : "")}" +
                                 $"{(SafeEntry(card) == "RUPTURE" ? $"+撕裂层{BossDamagePredictor.RuptureAmountOf(card)}" : "")}" +
                                 $"{(SafeEntry(card) == "SOVEREIGN_BLADE" ? $"+铸{bladePendingForge}+段{bladePendingHits}" : "")})");
                }

                // 这张牌自己会给她加多少力量 → 给**后面**那些牌算加伤（真的到账后由 PlayOne 撤回）
                // ⚠ 「非凡技艺」（Prowess）**不算进这里**：它的 +1 力量由上面的 `prowessPending` 单独补；
                //    两个都算就会变成 +2（用户 2026-10-24 实测：同一回合预测 63、实际 61）。
                if (SafeEntry(card) != "PROWESS") plannedStrength += BossDamagePredictor.StrengthGainOfPlanned(card, boss);
                // 这张牌自己掉血 → 撕裂给的力量已到账，也要记进「后面那些牌该拿到的力量」里
                plannedStrength += ruptureHere;
                // 迷你储君：本回合**第一次花星** → +N 力量（记给后面那些攻击牌的预览）
                if (miniRegentStrength > 0 && !miniRegentUsed)
                {
                    try
                    {
                        if (BossPlanner.StarCostOf(card) > 0)
                        {
                            miniRegentUsed = true;
                            plannedStrength += miniRegentStrength;
                        }
                    }
                    catch { /* 忽略 */ }
                }
                // 剧本打到「撕裂」这张牌 → 之后的掉血才开始涨力量
                ruptureNow += BossDamagePredictor.RuptureAmountOf(card);

                // 这张牌会给**后面**的君王之剑带来什么：铸造、剑圣的重放
                //（用户 2026-10-24 反馈「君王之剑意图还是被低估」—— 出牌期重算时这两笔以前全丢了）
                // ★ v0.2.319（用户 2026-11-02：「君王之剑的伤害意图没有计算本回合战火铸就会对它的铸造」）：
                //   以前只认「追踪之刃」一张，现在**凡是带 `Forge` 变量的牌**都算进来
                //   （追踪之刃 7、**战火铸就 7**（二层「征服者储君」）、以后别的铸造牌）。
                //   ⚠ 能力牌不算：熔炉那种是「回合开始铸造」，已由 `PendingFurnaceForge` 单独补（会重复）。
                if (card.Type != MegaCrit.Sts2.Core.Entities.Cards.CardType.Power)
                {
                    int forgeNow = VarInt(card, "Forge");
                    if (forgeNow > 0) bladePendingForge += forgeNow;
                }
                if (SafeEntry(card) == "SWORD_SAGE") bladePendingHits += VarInt(card, "SwordSagePower");

                if (isAttack) attacks++;
                attacks += Math.Max(0, play.GeneratedShivs);   // 这张牌生出来的小刀也是攻击牌

                // 这张牌打完之后手牌的净收缩：
                //   · 它自己离手 = 1 张；
                //   · 牌面额外效果 **是反方向的**：`HandDeltaOf` 里负数表示「还会少几张」（生存者弃 1），
                //     正数表示「还会多几张」（杂技抽 3 弃 1 ⇒ +2）⇒ 所以是 `1 − delta`。
                // ⚠ 这里一开始写成了 `1 + delta`，生存者那项就变成 1+(−1)=0（等于没算弃牌），
                //    手牌被估成 4 张 ⇒ 精确切击显示 16（用户 2026-10-26 实测）。
                //   · 生出来的小刀**当回合就会被打出去**（PlayOne 里的补充循环）⇒ 净变化 0，不加也不减。
                int shrink = 1 - HandDeltaOf(card);
                playedShrink += shrink;
                if (!planTime && StillInHand(boss, card)) pendingShrink += shrink;
                // 这张牌自己会让她弃几张（铭记死亡的倍率数它）
                discardsSoFar += DiscardsOf(card);

                // 这张牌打完 → 给**后面**那张「全身撞击」多一笔格挡（排在前面的才算得到）
                simBlock += BossScriptPlanner.BlockGainOf(card);
            }

            if (planTime)
            {
                Logger.Log($"[CharBoss] 成长修正（规划期）: 手里剑={shuriken} 本回合攻击牌={attacks} " +
                           $"补正上限={plannedBaseRise + (shuriken ? Math.Max(0, (attacks - 1) / AttacksPerTrigger) : 0)}" +
                           $" | {string.Join(" ", snapshot)}");
            }
            else
            {
                Logger.Log($"[CharBoss] 成长修正（出牌期）: 已打攻击={boss.AttacksPlayed} " +
                           $"已该到手成长={appliedGrowth} 力量快照={boss.StrengthAtTurnStart} " +
                           $"当前力量={CurrentStrength(boss)} | {string.Join(" ", snapshot)}");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 成长修正失败: " + e.Message);
        }
    }

    private static string SafeEntry(CardModel card)
    {
        try { return card.Id.Entry; } catch { return string.Empty; }
    }

    /// <summary>他现在有几层「撕裂」（读不到就 0）。</summary>
    private static int RuptureStacksOf(CharBossMonster boss)
    {
        try
        {
            var creature = boss.Creature;
            if (creature == null) return 0;
            return Math.Max(0, creature.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.RupturePower>());
        }
        catch { return 0; }
    }

    /// <summary>他现在的真实格挡（出牌期给「全身撞击」起算用；读不到就 0）。</summary>
    private static int SafeBlockOf(MegaCrit.Sts2.Core.Entities.Creatures.Creature? creature)
    {
        try { return creature?.Block ?? 0; } catch { return 0; }
    }

    // ── 喝药水带来的临时力量（爬行动物饰品）/ 手牌数 / 群蛇形态 ──────────────

    private const string ReptileTrinketEntry = "REPTILE_TRINKET";

    /// <summary>
    /// 她这回合**还没喝的那几瓶药水**会带来多少临时力量（爬行动物饰品）。
    ///
    /// 反编译：`ReptileTrinket.AfterPotionUsed` → 若 `potion.Owner == Owner` 且战斗进行中
    /// → `PowerCmd.Apply&lt;ReptileTrinketPower&gt;(…, DynamicVars.Strength.BaseValue)`，
    /// 而 `ReptileTrinketPower : TemporaryStrengthPower`（本回合有效，回合结束移除）。
    ///
    /// ⚠ **按「还没喝的那几瓶」算**（不是一次补满本回合全部）：用户 2026-10-26 实测
    /// 「第一回合喝 3 瓶药水时精确切击的伤害会在修正后的伤害基础上 +9 然后再跳回去」——
    /// 就是因为以前整天补 +9，而她每喝一瓶真实力量就 +3，两者叠起来先显示 +18。
    /// 现在喝掉一瓶、预补就少一瓶 ⇒ `真实力量 + 预补 = 每瓶力量 × 计划瓶数` 恒定，数字不再来回跳。
    /// </summary>
    private static int PotionStrengthRise(CharBossMonster boss, bool planTime)
    {
        try
        {
            int per = PotionStrengthPerDrink(boss);
            if (per <= 0) return 0;

            int planned = boss.PlannedPotions?.Count ?? 0;
            int drunk = Math.Max(0, boss.PotionsDrunkThisTurn);
            int remaining = planTime ? planned : Math.Max(0, planned - drunk);
            int rise = per * remaining;
            if (rise > 0 && planTime)
            {
                Logger.Log($"[CharBoss] 爬行动物饰品：本回合要喝 {planned} 瓶药水 → 规划期把 +{rise} 力量" +
                           $"（每瓶 +{per}；之后每喝一瓶就撤 {per}，数字不会来回跳）算进攻击意图");
            }
            return rise;
        }
        catch { return 0; }
    }

    /// <summary>她身上有没有爬行动物饰品；有就返回「每喝一瓶给多少力量」（没有返回 0）。</summary>
    private static int PotionStrengthPerDrink(CharBossMonster boss)
    {
        try
        {
            var relics = boss.Shadow?.Relics;
            for (int i = 0; i < (relics?.Count ?? 0); i++)
            {
                var relic = relics![i];
                if (relic == null) continue;
                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry != ReptileTrinketEntry) continue;
                return StrengthPerDrink(relic);
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    /// <summary>爬行动物饰品「每喝一瓶给多少力量」（读遗物上的 Strength 变量，读不到按 3）。</summary>
    private static int StrengthPerDrink(MegaCrit.Sts2.Core.Models.RelicModel relic)
    {
        try
        {
            foreach (var key in new[] { "Strength", "StrengthPower" })
            {
                if (relic.DynamicVars.TryGetValue(key, out var v) && v != null)
                {
                    int amount = (int)Math.Round((double)v.BaseValue);
                    if (amount > 0) return amount;
                }
            }
        }
        catch { /* 读不到就用反编译出来的常数 */ }
        return 3;
    }

    /// <summary>她现在手里有几张牌（拿不到按 0）。</summary>
    private static int LiveHandCount(CharBossMonster boss)
    {
        try { return boss.Shadow?.PlayerCombatState?.Hand?.Cards?.Count ?? 0; }
        catch { return 0; }
    }

    /// <summary>这张牌还在不在她手里（出牌期用来判断「哪些计划项已经打出去了」）。</summary>
    private static bool StillInHand(CharBossMonster boss, CardModel card)
    {
        try
        {
            var hand = boss.Shadow?.PlayerCombatState?.Hand?.Cards;
            if (hand == null) return false;
            foreach (var c in hand)
            {
                if (ReferenceEquals(c, card)) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    /// <summary>
    /// 这张牌打出时会让她**弃几张**（反编译逐张核对过；只列真的会弃牌的那几张）。
    /// 用途：「铭记死亡」的伤害 = 9（升级 11）+ 4（升级 5）× 本回合已弃张数。
    ///
    /// ⚠ 不用「剧本里 DiscardInstead 的条数」来数：那些条目本来就不在计划里（见
    /// <c>BossScriptPlanner</c>），而是由**这里这几张牌**在打出时弃掉的。
    /// </summary>
    private static int DiscardsOf(CardModel card)
    {
        try
        {
            switch (SafeEntry(card))
            {
                case "PREPARED": return Math.Max(0, VarInt(card, "Cards"));          // 抽几张就弃几张（1，升级 2）
                case "ACROBATICS": return 1;                                        // 抽 3/4 弃 1
                case "SURVIVOR": return 1;                                          // 格挡 + 弃 1
                case "HIDDEN_DAGGERS": return Math.Max(0, VarInt(card, "Cards"));   // 弃 2 换 2 刀
                default: return 0;
            }
        }
        catch { return 0; }
    }

    /// <summary>
    /// 打出这张牌**除了自己离手（那 1 张另算）之外**，手牌还会净变几张：
    /// **负数 = 还会少几张**（弃牌），**正数 = 还会多几张**（抽牌）。
    ///
    /// 为什么需要（用户 2026-10-26 两次实测）：精确切击的伤害 = 13 − 2 × 手牌里的其他牌数，
    /// 而她的剧本里「生存者」会**额外弃 1 张**（反编译 `Survivor.OnPlay`：
    /// `GainBlock(...)` 之后 `CardSelectCmd.FromHandForDiscard(prefs, 1)`）⇒ 真实手牌比「每张牌 −1」少 1 张。
    /// 目前只列**反编译验证过**的：生存者 −1（弃 1）、杂技 +2（抽 3 弃 1）。其它牌一律 0（宁可不猜）。
    /// 要加新牌请先看它的 `OnPlay` 里有没有 `CardPileCmd.Draw` / `FromHandForDiscard`。
    /// </summary>
    private static int HandDeltaOf(CardModel card)
    {
        switch (SafeEntry(card))
        {
            case "SURVIVOR": return -1;
            case "ACROBATICS": return +2;
            default: return 0;
        }
    }

    // ── 「这张牌会被打几次」（复制药水 / 爆发）────────────────────────────────

    /// <summary>她身上某层 power 的层数（没有 = 0）。</summary>
    private static int SafePowerAmount<T>(CharBossMonster boss) where T : MegaCrit.Sts2.Core.Models.PowerModel
    {
        try { return Math.Max(0, boss.Creature?.GetPowerAmount<T>() ?? 0); }
        catch { return 0; }
    }

    /// <summary>复制药水会给几层「复制」（`Duplicator` 的 `PowerVar&lt;DuplicationPower&gt;`，读不到按 1）。</summary>
    private static int DuplicatorLayers()
    {
        try
        {
            var canonical = MegaCrit.Sts2.Core.Models.ModelDb.GetById<MegaCrit.Sts2.Core.Models.PotionModel>(
                MegaCrit.Sts2.Core.Models.ModelDb.GetId(typeof(MegaCrit.Sts2.Core.Models.Potions.Duplicator)));
            if (canonical != null)
            {
                foreach (var v in canonical.DynamicVars.Values)
                {
                    if (v == null) continue;
                    if (v.GetType().FullName?.Contains("Duplication", StringComparison.Ordinal) != true) continue;
                    int amount = (int)Math.Round((double)v.BaseValue);
                    if (amount > 0) return amount;
                }
            }
        }
        catch { /* 忽略 */ }
        return 1;
    }

    /// <summary>「爆发」这张牌会给几层（`PowerVar&lt;BurstPower&gt;`，读不到按 1）。</summary>
    private static int BurstCardLayers(CardModel card)
    {
        try
        {
            foreach (var v in card.DynamicVars.Values)
            {
                if (v == null) continue;
                if (v.GetType().FullName?.Contains("Burst", StringComparison.Ordinal) != true) continue;
                int amount = (int)Math.Round((double)v.BaseValue);
                if (amount > 0) return amount;
            }
            int byKey = VarInt(card, "Burst");
            if (byKey > 0) return byKey;
        }
        catch { /* 忽略 */ }
        return 1;
    }

    /// <summary>
    /// 群蛇形态那条额外攻击意图：被打两遍的牌要画成「4×2」。
    /// 用 Raw 系（不吃 <c>Hook.ModifyDamage</c>），所以数字恒等于层数。
    /// </summary>
    private static AbstractIntent SerpentIntent(int perPlay, int playCount)
        => playCount > 1 ? new RawMultiAttackIntent(perPlay, playCount) : new RawAttackIntent(perPlay);

    /// <summary>她身上「群蛇形态」的层数（= 每打一张牌对随机敌人造成多少伤害）；没有就是 0。</summary>
    private static int ExistingSerpentDamage(CharBossMonster boss)
    {
        try
        {
            return Math.Max(0, boss.Creature?
                .GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.SerpentFormPower>() ?? 0);
        }
        catch { return 0; }
    }

    /// <summary>
    /// 「群蛇形态」这张牌自己会给多少层（= 之后每张牌打多少）。
    /// 牌面上是 `PowerVar&lt;SerpentFormPower&gt;`（key 可能是 `SerpentForm` 或 `SerpentFormPower`），
    /// 所以按**变量类型名里含 SerpentForm** 扫一遍，读不到就退回原版常数 4。
    /// </summary>
    private static int SerpentFormCardAmount(CardModel card)
    {
        try
        {
            foreach (var v in card.DynamicVars.Values)
            {
                if (v == null) continue;
                if (v.GetType().FullName?.Contains("SerpentForm", StringComparison.Ordinal) != true) continue;
                int amount = (int)Math.Round((double)v.BaseValue);
                if (amount > 0) return amount;
            }
            // 备用 key（万一以后改成普通 DynamicVar）
            int byKey = VarInt(card, "SerpentForm");
            if (byKey <= 0) byKey = VarInt(card, "SerpentFormPower");
            if (byKey > 0) return byKey;
        }
        catch { /* 忽略 */ }
        return 4;   // 反编译/本地化一致：群蛇形态 = 每张牌 4 点
    }

    /// <summary>读一张牌某个动态变量的整数值（读不到返回 0）。</summary>
    private static int VarInt(CardModel card, string key)
    {
        try
        {
            if (card.DynamicVars.TryGetValue(key, out var v) && v != null)
                return Math.Max(0, (int)Math.Round((double)v.BaseValue));
        }
        catch { /* 忽略 */ }
        return 0;
    }

    // ── 灵魂绑定（二层灵魂骨）× 死亡行军 ─────────────────────────────────────

    private static bool HasSoulBound(CharBossMonster boss)
    {
        try { return boss.Creature?.GetPower<Powers.BossSoulBoundPower>() != null; }
        catch { return false; }
    }

    /// <summary>
    /// **规划期一次算好、之后冻结**的三份「预补池」（用户 2026-10-23 的口径）：
    /// 意图里先把「本回合将要发生、但还没发生」的加伤算进去，
    /// 之后每有一笔真实增量到账，就用 <c>Withdraw*Compensation</c> 撤掉**同样的量**
    /// ⇒ 显示值 = 真实值 + 池子里剩的，两边同步移动，数字只会单向变化，不会来回闪。
    ///
    /// 三个来源（都反编译确认过）：
    ///   · **死亡行军**：`8 + 4 × 本回合抽到的牌数`。她这回合会打出的「灵魂」（`Cards` 张）还没真的抽到；
    ///   · **灵魂风暴**：`ExtraDamage × 消耗牌堆里的 Soul 张数`。她这回合会打出的灵魂还没进消耗牌堆；
    ///   · **出击**：`CalculationBase + ExtraDamage × 奥斯提生命`。召唤牌 / 灵魂×吞噬生命加的
    ///     奥斯提生命还没到账（见 <see cref="BossDamagePredictor.OstyHpGainOf"/>）。
    /// </summary>
    /// <summary>
    /// 她**回合真的开始**之后重新冻结一次预补（`CharBossMonster` 出牌阶段开头调用）。
    ///
    /// 为什么需要第二次：规划发生在**玩家回合**（她回合开始之前），而「缚魂命匣」这类
    /// 「你的回合开始时给奥斯提加生命」的遗物是在她回合开始才到账的 —— 实测规划期 ostyHp=27、
    /// 到她的回合变成 28，出击的预览就少 1 点。此刻她一张牌都还没打（
    /// `HerOwnDrawsThisTurn` / `SoulsPlayedThisTurn` / `OstyHpGainedThisTurn` 都还是 0），
    /// 所以重新冻结是安全的：显示值 = 此刻真实值 + 本回合还会发生的加伤。
    /// </summary>
    public static void RefreshCompensation(CharBossMonster boss, List<PlannedPlay> plan)
        => PlanCompensation(boss, plan, HasSoulBound(boss), includeTurnStartSummon: false);

    /// <summary>
    /// 她**回合开始**时「缚魂命匣」会给奥斯提召唤 1 ⇒ 奥斯提 +1 生命。
    ///
    /// 规划发生在**玩家回合**，那一刻这笔还没到账，而「出击」的伤害是
    /// `CalculationBase + ExtraDamage × 奥斯提当前生命` ⇒ 预览正好少 `ExtraDamage × 1`
    /// （用户 2026-10-24 实测报的就是这个）。
    /// 所以规划期把这 +1 也算进预补；等她回合开始这笔真的到账后，
    /// <see cref="RefreshCompensation"/> 以 `includeTurnStartSummon: false` 重新冻结，
    /// 那时真实生命里已经含这 +1，不会重复。
    /// </summary>
    private static int TurnStartSummonGain(CharBossMonster boss)
    {
        try
        {
            var relics = boss.Shadow?.Relics;
            for (int i = 0; i < (relics?.Count ?? 0); i++)
            {
                string entry;
                try { entry = relics![i]?.Id.Entry ?? string.Empty; } catch { continue; }
                if (entry == "BOUND_PHYLACTERY") return 1;    // 缚魂命匣：每回合开始召唤 1
                // 无界命匣（v0.2.345，欧洛巴斯之触升级来的）：每回合开始召唤 `StartOfTurn`（= 2）
                // ⇒ 「出击」的意图要按这个数预补奥斯提生命，否则预览偏低。
                if (entry == "PHYLACTERY_UNBOUND") return 2;
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    private static void PlanCompensation(CharBossMonster boss, List<PlannedPlay> plan, bool soulBound,
                                         bool includeTurnStartSummon)
    {
        // 每回合重新冻结（即使这回合没有这些牌，也要把旧值清掉）
        boss.DrawCompensation = 0;
        boss.SoulStormCompensation = 0;
        boss.UnleashCompensation = 0;
        boss.DeathMarchPerDraw = 0;
        boss.SoulStormPerSoul = 0;
        boss.UnleashPerOstyHp = 0;

        try
        {
            int draws = 0, souls = 0;
            // 规划期先把「她回合开始那笔召唤（缚魂命匣 +1）」算进来 —— 出击的预览靠它才不留 1 点
            int ostyHp = includeTurnStartSummon ? TurnStartSummonGain(boss) : 0;
            // 「吞噬生命」可能**就在这回合里**才被打出来（她的剧本第 5 回合就是先打吞噬生命再打两张灵魂），
            // 所以规划期不能只读她身上现在的能力 —— 一边走剧本一边把这个数带上。
            int devour = BossDamagePredictor.DevourAmountOf(boss);

            foreach (var play in plan)
            {
                var card = play?.Card;
                if (card == null) continue;
                string entry = SafeEntry(card);

                // 走到这张牌时，前面那些牌带来的「还没发生」的加伤就是它的预补
                if (entry == "DEATH_MARCH" && soulBound)
                {
                    int per = PerUnit(card);
                    boss.DeathMarchPerDraw = per;
                    // 除了剧本里「她还会抽的」，玩家上个回合内额外抽的牌也算（那几笔在预览里就是池子的一部分，
                    // 她回合开始时才折算成真实历史条目）。
                    boss.DrawCompensation = per * draws + per * Math.Max(0, boss.PendingDrawCredits);
                }
                if (entry == "SOUL_STORM" && souls > 0)
                {
                    int per = PerUnit(card);
                    boss.SoulStormPerSoul = per;
                    boss.SoulStormCompensation = per * souls;
                }
                if (entry == "UNLEASH" && ostyHp > 0)
                {
                    int per = PerUnit(card, 1);       // 出击的 ExtraDamage 是 1
                    boss.UnleashPerOstyHp = per;
                    boss.UnleashCompensation = per * ostyHp;
                }

                // 这张牌自己会给后面带来什么
                draws += InTurnDrawsOf(card, soulBound);
                if (entry == "SOUL") souls++;
                if (entry == "DEVOUR_LIFE")
                    devour = Math.Max(devour, BossDamagePredictor.DevourLifeCardAmount(card));
                ostyHp += BossDamagePredictor.OstyHpGainOf(card, devour);
            }

            if (boss.DrawCompensation > 0 || boss.SoulStormCompensation > 0 || boss.UnleashCompensation > 0)
            {
                Logger.Log($"[CharBoss] 意图预补（第 {boss.TurnNumber} 回合，之后真的增加多少就撤多少）：" +
                           $"死亡行军 +{boss.DrawCompensation}（{draws} 张待抽 ×{boss.DeathMarchPerDraw}）、" +
                           $"灵魂风暴 +{boss.SoulStormCompensation}（{souls} 张待打 ×{boss.SoulStormPerSoul}）、" +
                           $"出击 +{boss.UnleashCompensation}（奥斯提待加 {ostyHp} 生命 ×{boss.UnleashPerOstyHp}）");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 计算意图预补失败: " + e.Message);
        }
    }

    /// <summary>牌面上的 `ExtraDamage`（每单位加伤），取不到就用 <paramref name="fallback"/>。</summary>
    private static int PerUnit(CardModel card, int fallback = 0)
    {
        int per = (int)Math.Round((double)VarOf(card, "ExtraDamage"));
        return per > 0 ? per : fallback;
    }

    /// <summary>
    /// 这张剧本牌打出后会让她「额外抽几张」。目前只有「灵魂」（<c>Soul</c>：抽 `Cards` 张，
    /// 未升级 2 / 升级 3 —— 反编译确认它走 `CardPileCmd.Draw(ctx, DynamicVars.Cards.BaseValue, owner)`）。
    /// </summary>
    private static int InTurnDrawsOf(CardModel card, bool soulBound)
    {
        try
        {
            if (!soulBound) return 0;
            if (SafeEntry(card) != "SOUL") return 0;
            return Math.Max(0, (int)Math.Round((double)VarOf(card, "Cards")));
        }
        catch { return 0; }
    }

    private static decimal VarOf(CardModel card, string key)
    {
        try
        {
            if (card.DynamicVars.TryGetValue(key, out var v) && v != null) return v.BaseValue;
        }
        catch { /* 忽略 */ }
        return 0m;
    }

    /// <summary>
    /// 要在**基础值**上扣多少，才能抵消意图节点给的多余活力。
    ///
    /// 关键：原版活力是 <c>VigorPower.ModifyDamageAdditive(...) =&gt; Amount</c> —— **加法**，
    /// 也就是在「乘法系数（易伤/虚弱…）算完之后」才加上去的。
    /// 意图节点显示 `基础值 × 乘法系数 + 活力`；要取消那 3 点活力，
    /// 必须在基础值上扣 `3 ÷ 乘法系数`，**不能直接扣 3**
    /// （易伤时节点加的是 4.5，只扣 3 还剩 1.5，用户实测就是这个）。
    ///
    /// 乘法系数按原版数值：易伤 ×1.5；储君自己中虚弱 ×0.75。
    /// </summary>
    private static int VigorPenaltyFor(CharBossMonster boss, MegaCrit.Sts2.Core.Entities.Creatures.Creature? target)
    {
        try
        {
            double mult = 1.0;
            if (target != null && target.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.VulnerablePower>() > 0)
                mult *= 1.5;
            if (boss.Creature != null && boss.Creature.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.WeakPower>() > 0)
                mult *= 0.75;

            if (mult <= 0.01) return 3;
            return Math.Max(1, (int)Math.Round(3.0 / mult));   // 活力固定 3 点
        }
        catch
        {
            return 3;
        }
    }
}
