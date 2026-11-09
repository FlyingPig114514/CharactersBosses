using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Runs;
using CharacterBosses.Core;

namespace CharacterBosses.Monsters;

/// <summary>
/// 「原版角色当 Boss」的怪物基类。
/// 本体仍是一只普通怪物（Side=Enemy），但它额外持有一个影子 Player：
/// 卡组 / 手牌 / 能量 / 遗物 都挂在影子 Player 上，出牌走原版卡牌效果。
/// </summary>
public abstract partial class CharBossMonster : MonsterModel
{
    internal Player? Shadow;
    internal readonly List<PlannedPlay> Plan = new();

    /// <summary>
    /// 本回合**计划要喝的药水**（炼药猎）：`PrepareTurn` 里从剧本的 `BossTurn.Potions` 抄一份，
    /// 药水栏用它在对应图标下面标「强化」（用户 2026-10-25）。空列表 = 这回合不喝药。
    /// </summary>
    internal List<Type> PlannedPotions = new();

    /// <summary>
    /// 本场战斗里「杂耍」生成过多少张**带 Strike 标签**的复制牌（二层「杂耍战」）。
    /// 完美打击的伤害按「卡组里的打击 + 这个数」算（见 BossScriptRunner.TakeOverPerfectedStrike）。
    /// </summary>
    internal int StrikeCopiesMade;

    /// <summary>
    /// 本回合**计划里第一张攻击牌**（规划期由 <c>BossScriptPlanner.Plan</c> 填入）。
    ///
    /// 用途：「致死性」（`LethalityPower`：只让每回合第一张攻击牌增伤 50%）的**意图预览**。
    /// 原版靠战斗历史判断「是不是第一张」，而意图是在玩家回合里算的、那时历史里一张牌都还没打
    /// ⇒ 补丁 `Patches/LethalityFirstAttackOnlyPatch` 要自己判断，可它在**规划期**读不到
    /// `Plan`（那时还在构建）⇒ 之前把每一张攻击牌的 50% 都压掉了（用户 2026-11-03 实测：
    /// 「伤害预览没有计算致死性的加成」）。规划器把这个字段填上，补丁就能在规划期认出第一张。
    /// </summary>
    internal MegaCrit.Sts2.Core.Models.CardModel? PlannedFirstAttackCard;

    /// <summary>
    /// 她**自己回合开始时**的生命值（遗物「情感芯片」用：下一个回合开始若比它低 = 上一回合掉过血）。
    /// 0 = 还没记过（第 1 回合）。
    /// </summary>
    internal int HpAtOwnTurnStart;

    /// <summary>
    /// 「情感芯片」用的**战斗历史游标**：上次她回合开始时历史里有多少条。
    /// 下一个回合开始只看游标之后的 `DamageReceivedEntry` ⇒ 不依赖生命快照也不会重复计数。
    /// （2026-11-04 加：生命快照那条判据实测一次都没触发过，用历史并联兜底。）
    /// </summary>
    internal int EmotionChipHistoryMark;

    /// <summary>
    /// 「无界命匣」的**战斗开始召唤 5** 是否已经给过（用户 2026-11-04 要求改到战斗开始那一刻）。
    /// 由 `BossOsty.TryCombatStartSummon` 在「战斗真的开始了」的第一帧置位，一只 Boss 每场只给一次。
    /// </summary>
    internal bool CombatStartSummonDone;

    /// <summary>
    /// 本场战斗里她**一共拿到过多少张牌**（= 剧本每回合重建的手牌数累加）。
    ///
    /// 用途：「谋杀」（`Murder`）的伤害是「1 + 每张抽到的牌 +1」，而她的手牌是剧本重建的、
    /// 从不走原版抽牌 ⇒ 战斗历史里一条 `CardDrawnEntry` 都没有，原版公式永远是 1 点。
    /// 由 `BossScriptRunner.BuildHand` 记账（用户 2026-11-03：「谋杀没有根据她抽牌数量增伤」）。
    /// </summary>
    internal int CardsDrawnThisCombat;

    /// <summary>
    /// 「谋杀」（Murder）用的加伤计数：**每回合真正新进她手牌的张数**累加。
    /// ★ v0.2.523（用户 2026-11-09 给的算法 1+5+8+3+3 ⇒ 第 4 回合 20；再 +4+4+7+7 ⇒ 第 8 回合 42）：
    ///   · **夜魇复制出来的牌不算**（ScriptCard.NotDrawn）；
    ///   · **被「计划妥当」留在手上的牌不算**（上一回合就已经在手、类型相同的那几张）。
    /// </summary>
    internal int MurderDrawnCounter;

    /// <summary>上一回合「保留不出」的牌类型（用来判断这一回合哪些保留牌是新来的）。</summary>
    internal HashSet<Type> LastKeptTypes = new();

    /// <summary>
    /// 「谋杀」（`Murder`）**已经见过它在手牌里**没有（-1 时代已废弃，见下面两个字段）。
    /// </summary>
    internal int MurderDrawBaseline = -1;

    /// <summary>
    /// 「谋杀」是否**已经在手牌里出现过**一次（= 加伤进度从此开始累积）。
    /// </summary>
    internal bool MurderSeenInHand;

    /// <summary>
    /// 「谋杀」的**加伤进度**：她在**保留着谋杀的那几个回合**里一共抽到多少张牌
    /// （每张 = +1 伤害，卡面 `ExtraDamage`）。
    ///
    /// ★ v0.2.363（用户 2026-11-04：「谋杀的伤害好像还是不是只在手牌中被保留时抽牌才增伤，好像偏高了」）：
    ///   旧写法是 `累计 = 上回合累计 + 本回合手牌数`，**无条件**累加，
    ///   于是「谋杀已经打出去、手里没有它的那几个回合」抽的牌也被算进去了 ⇒ 伤害偏高。
    ///   现在只在**这一手她手里确实还有谋杀**的时候才把 `handNow` 加进来；
    ///   它第一次进手的那一手不计（起点归零），之后只增不减（与她设计稿一致）。
    /// </summary>
    internal int MurderDrawProgress;

    /// <summary>
    /// 「一心化万」里**还没结算**的「弃牌堆 → 手牌」张数（三层「事件机」）。
    ///
    /// `Patches/BossDiscardToHandStrengthPatch` 只在这里 `++`（它不能自己起异步链去加力量，
    /// 那会和出牌队列互相等 ⇒ 她的回合卡死，用户 2026-11-03 报的「没法结束回合」），
    /// 真正「所有单位 +N 力量」在 `PlayOne` 里、和她这一手出牌同一个 await 链里做。
    /// </summary>
    internal int PendingReturnStrength;

    /// <summary>
    /// 「抱抱先生」这一笔伤害**最后一次是哪个回合**打出去的（0 = 还没打过）。
    ///
    /// 用途（用户 2026-11-03）：那笔伤害在她**回合开始**结算，结算完就不该再挂在意图里，
    /// 等下个回合规划（`TurnNumber` ++）再显示新的数字 ⇒ <c>MrStrugglesPlays.UpcomingDamage</c> 拿它判。
    /// </summary>
    internal int MrStrugglesFiredForTurn;


    /// <summary>
    /// 这张牌是不是「本回合计划里的**第一张攻击牌**」（给「致死性」用）。
    ///
    /// 两级判据（和补丁/意图构造两侧共用，避免两边各写一套）：
    ///   ① 规划期：`Plan` 还在构建 ⇒ 用规划器记下的 <see cref="PlannedFirstAttackCard"/>；
    ///   ② 计划已填好（出牌期重算意图）：按 `Plan` 顺序找第一张非「惊逃」的攻击牌。
    /// </summary>
    internal bool IsFirstPlannedAttackCard(MegaCrit.Sts2.Core.Models.CardModel? card)
    {
        if (card == null) return false;
        try
        {
            var hint = PlannedFirstAttackCard;
            if (hint != null) return ReferenceEquals(hint, card);
        }
        catch { /* 忽略 */ }
        try
        {
            foreach (var play in Plan)
            {
                var c = play?.Card;
                if (c == null) continue;
                if (play!.StampedeOnly) continue;
                MegaCrit.Sts2.Core.Entities.Cards.CardType type;
                try { type = c.Type; } catch { continue; }
                if (type != MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack) continue;
                return ReferenceEquals(c, card);
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    /// <summary>计划里是不是**确实有这张刀牌**（按实例比对）。</summary>
    internal bool PlanContainsShivCard(MegaCrit.Sts2.Core.Models.CardModel? card)
    {
        if (card == null) return false;
        try
        {
            foreach (var play in Plan)
            {
                if (play?.Card != null && ReferenceEquals(play.Card, card)) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    /// <summary>
    /// 这张刀牌是不是「本回合计划里的**第一把刀**」。
    ///
    /// 用途（用户 2026-11-03 的玩家反馈：「幻刃的增伤会错误的显示到后续小刀上导致预计伤害存在误差」）：
    /// 「幻影之刃」的增伤只给**每回合第一把小刀**（反编译 `PhantomBladesPower.ModifyDamageAdditive`
    /// 靠「本回合还没有刀打完」判断），可意图是在她出手**之前**算的、历史里一把刀都没有
    /// ⇒ 它会给**每一把刀**都 +9。补丁 `Patches/PhantomBladesFirstShivPatch` 用这里的判据把后面的压回 0。
    /// </summary>
    internal bool IsFirstPlannedShivCard(MegaCrit.Sts2.Core.Models.CardModel? card)
    {
        if (card == null) return false;
        try
        {
            foreach (var play in Plan)
            {
                var c = play?.Card;
                if (c == null) continue;
                bool shiv;
                try { shiv = Core.ShivPlays.IsShiv(c) || play!.GeneratedShivs > 0; }
                catch { shiv = false; }
                if (!shiv) continue;
                return ReferenceEquals(c, card);
            }
        }
        catch { /* 忽略 */ }
        return false;
    }
    /// <summary>
    /// 本回合**计划里排在 <paramref name="card"/> 之前**的「奥斯提攻击」张数（猛晃的段数要用）。
    ///
    /// 为什么要按计划数：反编译 `Rattle.CalculatedHits` 数的是战斗历史里「本回合奥斯提攻击过几次」，
    /// 而意图是在她还没出手时算的（历史是空的）⇒ 永远显示 1 段
    /// （用户 2026-11-03：「猛晃不会正确显示它被打出的段数」）。
    /// 规划期 `Plan` 还是空的，那边由 <c>BossScriptPlanner</c> 自己的计数器补（见那里的 extraHits）。
    /// </summary>
    internal int OstyAttacksBefore(MegaCrit.Sts2.Core.Models.CardModel? card)
    {
        if (card == null) return 0;
        int n = 0;
        try
        {
            foreach (var play in Plan)
            {
                var c = play?.Card;
                if (c == null) continue;
                if (ReferenceEquals(c, card)) return n;
                if (Core.BossScriptPlanner.IsOstyAttack(c) && !play!.StampedeOnly) n++;
            }
        }
        catch { /* 忽略 */ }
        return n;
    }

    /// <summary>
    /// 「杂耍」造出来的**复制牌实例**（引用集合）。
    ///
    /// 用途：`BuildHand` 每回合都会**重新创建**剧本牌，旧的那批会留在弃牌堆里
    /// ⇒ 完美打击（数 `AllCards` 里带 Strike 标签的牌）会把这些陈年副本也算进去（虚高）。
    /// 所以每回合重建手牌前，把「带 Strike 标签、但不是这里登记的复制牌」的旧实例从各牌堆里清掉
    /// ⇒ 牌堆里始终只有「这套牌的 6 张」+「杂耍真正生成的复制」（后者正是设计要的每循环 +1）。
    /// </summary>
    internal readonly HashSet<MegaCrit.Sts2.Core.Models.CardModel> StrikeCopyInstances =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// 「王者之拳」（<c>KinglyPunch</c>）在本场战斗里累计成长了多少伤害（二层「征服者储君」，用户 2026-11-02）。
    ///
    /// 原版（反编译 `KinglyPunch.AfterCardDrawn`）：**每当你抽到这张牌**，就把
    /// `DynamicVars["Increase"]`（未升级 4 / 升级 6）加到这张牌的 `Damage` 上，本场永久有效。
    /// 而我们的手牌每回合都是 `BuildHand` **新建实例** ⇒ 抽牌钩子根本不触发、成长全丢
    /// （用户实测：「王者之拳没有在每次抽到时增加伤害」）。
    /// 所以改成记在 Boss 身上：每次造这张牌时先把累计值补进 `Damage`，再把这次的 `Increase` 累计进去
    /// （＝原版「抽到就涨」的语义：**第一次抽到那一手也已经在卡面上涨过了**）。
    /// </summary>
    internal int KinglyPunchBonus;

    /// <summary>
    /// **这只是哪一套设计**（<c>BossKeys</c> 的 Key，例如 `SILENT_BREWER` / `REGENT_KING` / `IRONCLAD_JUGGLER`），
    /// 由**生成它的那个遭遇**在 `GenerateMonsters` 里写下（主 Boss 与伙伴都会写）。
    ///
    /// 为什么必须记下来（v0.2.333，用户 2026-11-03：「炼药猎死亡提示不对，显示成刀猎的了」）：
    /// 死亡提示 / 跟踪锁定的名字 / 地图徽章原来都是「按 (角色, 幕) 再算一次变体」
    /// （<c>CharBossEncounters.TierEncounterFor</c> 里的 `SilentBrewerAt3()` / `KingActive()` …），
    /// 而那些判据是**本场锁定 + 从当前 CombatState 的房间遭遇读**——玩家死亡那一刻战斗正在结束，
    /// CombatState 已经换了/空了 ⇒ 锁定表被清空、重新算就落到同一档的**主变体**上
    /// （炼药猎 → 刀猎、王储 → 铸剑储君、杂耍战 → 放血战…）。
    /// 记在实例上就没有这个问题：它跟着这只 Boss 出生，永不重算。
    /// </summary>
    internal string? SpawnVariantKey;

    /// <summary>这场战斗的药水栏位/初始药水是否已经处理过（只做一次）。</summary>
    internal bool PotionsPrepared;

    /// <summary>
    /// 本回合**已经喝掉几瓶药水**（`PrepareTurn` 清零，`BossPotions.DrinkAsync` 每喝一瓶 +1）。
    ///
    /// 用途：爬行动物饰品「每喝一瓶本回合 +3 力量」那笔力量，
    /// 意图里既要提前补上（否则玩家回合看到的攻击意图全都低 9 点），
    /// 又要在她真的喝下去之后**撤掉同样的量** —— 否则「预补 + 真实力量」会叠成 +18，
    /// 面板先高 9 点再跳回去（用户 2026-10-26 实测「修正后的伤害基础上 +9 然后再跳回去」）。
    /// 见 <c>Core/BladesGrowth.PotionStrengthRise</c>。
    /// </summary>
    internal int PotionsDrunkThisTurn;

    /// <summary>本回合「炼制药水」要炼的几瓶（按剧本顺序排队，出牌时逐张取）。</summary>
    internal Queue<Type> PlannedBrews = new();

    /// <summary>本回合「打某张牌之前先喝哪瓶」（复制药水夹在牌中间）。</summary>
    internal Queue<(Type Card, Type Potion)> PlannedDrinks = new();

    /// <summary>
    /// 本回合手牌里那些**不在 <see cref="Plan"/> 里、但确实会被打出去**的展示牌
    /// （目前只有所向无敌：回合结束从摸牌堆顶免费打出）。
    ///
    /// 复用 <see cref="PlannedPlay"/> 只为了带上它的 <c>Intents</c>：手牌栏要把这张牌
    /// **下面的意图**（所向无敌 = 守势）画出来（用户实测要求），但出牌阶段不会打它。
    /// 由 <see cref="Core.BossScriptPlanner.Plan"/> 每回合重填；有它在就画亮。
    /// </summary>
    internal readonly List<PlannedPlay> DisplayPlays = new();

    /// <summary>
    /// 本回合**还没发生的「印牌事件」次数**（共鸣：她回合开始 1 次 + 本回合打君权自授/发现各 1 次）。
    ///
    /// 用途：「超质量体」的伤害按"自己生成过几张牌"加伤，而意图要**提前**把这回合还会发生的
    /// 印牌算进去；每真的发生一次就减 1（补多少收多少），减完重算意图。
    /// 用户实测要求：一回合印两次就分两次收。
    /// </summary>
    internal int PendingGenerations;

    /// <summary>一次「印牌」给超质量体加的伤害（超质量体+ 的 ExtraDamage = 4）。</summary>
    internal const int GenerationBonusPer = 4;

    /// <summary>一次印牌事件真的发生了：把提前补的那份收回去，并重算意图。</summary>
    internal void NoteGenerationFired()
    {
        try
        {
            if (PendingGenerations <= 0) return;
            PendingGenerations--;
            RefreshPlannedIntents();
            CharacterBosses.UI.BossHandUi.RefreshForce(this);
            Logger.Log($"[CharBoss] 印牌已发生：超质量体回收 {GenerationBonusPer} 点预补，剩余预补 " +
                       $"{PendingGenerations} 次");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 印牌后重算意图失败: " + e.Message);
        }
    }

    /// <summary>这一场战斗是否已经替影子 Player 触发过「战斗开始」类遗物钩子。</summary>
    internal bool CombatStartFired;

    /// <summary>本场战斗内的回合序号（1 起，剧本按它取回合）。</summary>
    internal int TurnNumber;

    /// <summary>**这一回合**她已经打出的攻击牌张数（终结技段数要用它，见 <see cref="Core.BladesGrowth"/>）。</summary>
    internal int AttacksPlayed;

    /// <summary>
    /// 她**回合开始时**（手套那 +1 已到账之后）的力量快照。
    /// 出牌期算"手里剑这把到底涨了多少"时靠它：<c>当前力量 − 快照</c> 才是本回合真实获得的量，
    /// 补正只补"还没到账"的部分（获得几点撤几点）。
    /// </summary>
    internal int StrengthAtTurnStart;

    /// <summary>
    /// **本场战斗爪击累计涨了多少伤害**（爪击原版把成长记在每张卡实例上，而我们的手牌每回合重建
    /// ⇒ 必须记在 Boss 身上，造新手牌时补上）。见 <see cref="Core.BossScriptRunner.BuildHand"/>。
    /// </summary>
    internal int ClawBonus;

    /// <summary>「充电」这类「下回合 +N 能量」的累计，下个 PrepareTurn 用掉后清零。</summary>
    internal int NextTurnEnergyBonus;

    /// <summary>本回合的计划是否已经算过（即使算出来是空的也算）。用于读档等「没走过 RollMove」的情况。</summary>
    internal bool PlanReady;

    /// <summary>
    /// 她此刻是否处于**击晕**状态（用户 2026-11-04：「被吹哨等牌击晕时立刻压暗所有牌、撤掉所有意图、
    /// 本回合不行动；下回合补打上回合没完成的那份计划」）。
    ///
    /// 反编译依据（`references/sts2.dll`）：
    /// <code>
    /// public bool IsStunned => Monster?.NextMove.Id == "STUNNED";     // Creature.get_IsStunned
    /// </code>
    /// 击晕 = 把移动状态机切到一个 `MoveState("STUNNED", 空 Task)`（`Creature.StunInternal`）。
    /// 我们这只 Boss 因此**天然**做到了「本回合不行动」：`TakeTurn → PerformMove` 跑的是那个空 Task，
    /// `PlayPlannedCards` 根本没进 ⇒ `Plan` / `PlanReady` / `TurnNumber` / 手牌原封不动，
    /// 这就是「下回合补打」所需要的全部前提（详见 `docs/plans/stun-behavior.md`）。
    /// 唯一需要自己拦的是 `PrepareTurn`（见那里的闸门）。
    /// </summary>
    internal bool Stunned
    {
        get
        {
            try { return Creature?.IsStunned == true; }
            catch { return false; }   // NextMove 还没建好等边缘情况：当作没被晕
        }
    }

    private bool _darkPassiveSuppressed;

    /// <summary>
    /// 「本回合她的**暗黑充能球不使用被动**」（一层「黑球机」的冷却剂：玩家打出「冷光」时置位）。
    /// 由 <c>Powers/CoolantPower</c> 置位、她那回合结束时清零，
    /// 拦截点在 <c>Patches/DarkOrbPassiveSuppressPatch</c>（`DarkOrb.BeforeTurnEndOrbTrigger` 前缀）。
    ///
    /// ★ v0.2.469（用户 2026-11-07 的「彩虹机」设计：「**黑暗充能球的被动不会触发**」）：
    ///   这一幕挂着 `BossRainbowPower` 时**恒为 true** —— 即她的黑暗球整场不打被动，
    ///   不再需要等玩家打冷光。
    /// </summary>
    internal bool DarkPassiveSuppressed
    {
        get
        {
            if (_darkPassiveSuppressed) return true;
            try { return Def?.SpecialPowerForAct(ActIndex) == typeof(Powers.BossRainbowPower); }
            catch { return false; }
        }
        set => _darkPassiveSuppressed = value;
    }

    /// <summary>
    /// 正在打出的这张牌**要不要拦掉它自己的抽牌**（用户 2026-10-27：三层「巨石战」的「战斗专注」）。
    ///
    /// 为什么只按牌拦、不打开整个 Boss 的 <c>BlocksVanillaDraw</c>：巨石战那张「祭品」也会抽 3 张，
    /// 用户只要求拦「战斗专注」，所以这里做成**逐张牌**的开关 —— 出牌期间置位、
    /// <c>ShadowBossNoDraw.ShouldBlock</c> 看到非空就拦（只拦这一次出牌里的抽牌）。
    /// </summary>
    internal string? DrawBlockEntry;

    /// <summary>
    /// **正在打出的那张牌的 entry**（出牌期间置位、打完清掉）。
    ///
    /// 用途：给「按牌判断」的补丁一个窗口 —— 例如「切断」（`SEVERANCE`）会印 3 张灵魂，
    /// 而剧本里已经写好了那张灵魂（用户 2026-10-29：「把切断的印牌拦了」），
    /// `SeveranceNoCardGenerationPatch` 靠它判断「现在被打的是不是被点名的牌」。
    /// </summary>
    internal static string? CurrentPlayingEntry;

    /// <summary>
    /// **印牌要被拦掉的牌**（按 entry 点名）：它们生成出来的牌，剧本里已经摆好了，
    /// 再印一张就会让手牌/计划对不上。
    /// 目前只有「切断」（`SEVERANCE` → 3 张灵魂：抽牌堆随机一张 / 弃牌堆一张 / **手牌一张**）。
    /// </summary>
    internal static readonly HashSet<string> BlocksGeneratedCardsEntries = new(StringComparer.Ordinal)
    {
        "SEVERANCE",
    };

    public override async Task AfterAddedToRoom()
    {
        await base.AfterAddedToRoom();
        CombatStartFired = false;   // 新的一场战斗
        TurnNumber = 0;
        NextTurnEnergyBonus = 0;
        FixActHp();                 // 按真实幕数校正血量（创建生物时拿不到幕数，只能在这里补）
        try
        {
            Shadow = ShadowPlayerFactory.Attach(this);
            // 影子 Player 不在 RunState.Players 里，必须先补一条本层统计，
            // 否则玩家打 Boss 时伤害结算会在「查玩家统计」处抛异常、牌卡在半空（实测）
            BossRunStats.Ensure(CombatState, Shadow);
            // 「跟踪锁定」跨战斗不能带过来：残留会让层数越叠越多，而两边历史不同
            // ⇒ 联机校验时 LOCK_ON_POWER 的层数不一致 → 状态分歧掉线（2026-10-22 实测）。
            Core.BossLockOn.CleanupStaleForNewFight(this);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] shadow player setup failed: " + e);
        }

        // 角色立绘是朝右画的，站到敌方一侧后会背对玩家 → 水平镜像
        BossVisuals.FaceThePlayer(this);

        // 充能球：用原版球位节点（我们这只怪被判成「不是玩家」，原版不会替它挂球位）
        CharacterBosses.UI.BossOrbDisplay.Attach(this);

        // ★ 2026-11-04（玩家反馈「铁甲战士皮肤『绯刃战姬』换不了 Boss 的皮肤」）：
        //   有一类皮肤 mod **根本不用** `CharacterModel.VisualsPath` / `CreateVisuals()`，
        //   而是在 `NCreature._Ready` 的后置补丁里判 `entity.Player?.Character.Id.Entry`
        //   然后把自己的场景塞进立绘节点。而我们在 `_Ready` 那一刻 `Entity.Player` 还是 null
        //   （影子玩家刚在上面的 try 里才绑上）⇒ 它们一律 return，Boss 永远是原版模型。
        //   这里在影子玩家绑好之后，把那些补丁对这只 Boss 的节点**补跑一次**。
        //   （为什么不干脆提前绑玩家：见 SkinReadyPatchReplay 的说明 ——
        //     `Creature.IsPlayer => Player != null` 会让原版挂球位管理器、隐藏血条、把 Boss 摆到玩家侧。）
        if (Shadow != null)
        {
            try
            {
                Patches.SkinReadyPatchReplay.Replay(
                    MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.GetCreatureNode(Creature),
                    "AfterAddedToRoom");
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 皮肤补跑（立绘）失败（忽略）: " + e.Message);
            }
        }
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var think = new BossThinkState();
        var seed = new MoveState(PlayStateId, _ => Task.CompletedTask) { FollowUpStateId = ThinkStateId };
        return new MonsterMoveStateMachine(new MonsterState[] { think, seed }, think);
    }

    /// <summary>
    /// 按本回合剧本的顺序喝药（炼药猎）。栏位里没有那一瓶就跳过（并记日志）。
    /// 「强化类」药水的意图由药水栏负责显示，这里只管真的喝下去。
    ///
    /// ⚠ 每喝一瓶都要**立刻刷面板 + 停一下**（用户 2026-10-26：「喝多瓶时喝第一瓶，这回合要喝的药就全没了」）。
    /// 原因：三瓶 `OnUseWrapper` 之间没有任何等待，引擎在**同一帧**就把三瓶全结算了，
    /// 于是界面只看到「8 瓶 + 3 个意图」直接跳到「5 瓶 + 0 个意图」，中间那两瓶像凭空消失。
    /// 现在每瓶之间隔 <see cref="PotionDrinkGapSeconds"/>，一瓶一瓶消失。
    /// </summary>
    private const float PotionDrinkGapSeconds = 0.45f;

    private async Task DrinkPlannedPotionsAsync(PlayerChoiceContext ctx, Player shadow)
    {
        try
        {
            if (PlannedPotions == null || PlannedPotions.Count == 0) return;
            if (shadow == null) return;
            int heal = Def.HealPerPotionForAct(ActIndex);
            foreach (var potionType in PlannedPotions.ToList())
            {
                if (potionType == null) continue;
                PotionModel? match = null;
                foreach (var held in BossPotions.Held(shadow))
                {
                    if (held != null && held.GetType() == potionType) { match = held; break; }
                }
                if (match == null)
                {
                    Logger.Log($"[CharBoss] 剧本要喝 {potionType.Name}，但栏位里没有 → 跳过");
                    // 这一瓶「处理完了」（不会有它的力量），计数也要往前走，
                    // 否则爬行动物饰品的预补会一直替它留一份 +3（见 BladesGrowth.PotionStrengthRise）
                    PotionsDrunkThisTurn++;
                    continue;
                }
                await BossPotions.DrinkAsync(ctx, shadow, match, heal, Def.PotionsForAct(ActIndex));
                await ShowPotionGoneAsync();
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 喝药水阶段失败: " + e.Message);
        }
    }

    /// <summary>喝完一瓶之后：立刻重画状态条，并停一小会儿让对方**看得见这一瓶消失**。</summary>
    private async Task ShowPotionGoneAsync()
    {
        // ⚠ 顺序很关键：**先按新的真实力量重算意图，再重画**。
        // 少这一句就会「预补的那笔力量 + 已经真到账的力量」叠在一起 ⇒ 攻击意图先高出一截再跳回去
        // （用户 2026-10-26 实测：喝 3 瓶药水时精确切击的伤害 +9 之后又跳回）。
        try { RefreshPlannedIntents(); }
        catch { /* 重算失败也不影响战斗 */ }
        try
        {
            CharacterBosses.UI.BossHandUi.Refresh(this);
        }
        catch { /* 画不出来不影响战斗 */ }
        try { await Cmd.Wait(PotionDrinkGapSeconds); }
        catch { /* 同上 */ }
    }

    /// <summary>
    /// 「夜魇」（`NightmarePower`）：原版 `BeforeHandDraw` 里「把选中的牌复制 `Amount` 张进手牌，然后移除自己」。
    /// 那条钩子只派发给 `CombatState.Players` 里的玩家 ⇒ **影子玩家收不到** ⇒ power 永远挂着不消失
    /// （用户 2026-10-25：「夜魇 buff 不会清除」）。
    ///
    /// 这里在她回合开始时替它结算一次：
    ///   · 用反射读 power 的内部数据（`GetInternalData&lt;Data&gt;().selectedCard`）拿到那张被选中的牌；
    ///   · 复制 `Amount` 张进手牌（照抄原版 `CreateClone` + `AddGeneratedCardToCombat(..., PileType.Hand, player)`）；
    ///   · 无论如何都把 power 移除（读不到选中牌也至少让 buff 不再残留）。
    /// </summary>
    private async Task SettleNightmareAsync(PlayerChoiceContext ctx)
    {
        try
        {
            var power = Creature?.GetPower<MegaCrit.Sts2.Core.Models.Powers.NightmarePower>();
            if (power == null || Shadow == null) return;

            // ★ v0.2.520（用户 2026-11-09：「拦掉夜魇印牌，因为印的杂技我都在剧本里写好了」）：
            //   「华丽猎」这一套的剧本已经把复制品写在后面几回合了 ⇒ 这里**只把 power 移除**，
            //   不再凭空往手牌里塞 N 张复制（否则手牌里会多出一份，剧本的点名就对不上了）。
            bool noClones = false;
            try { noClones = Def?.BlocksNightmareClonesFor(ActIndex) == true; } catch { noClones = false; }

            int amount = 0;
            try { amount = (int)power.Amount; } catch { amount = 0; }
            MegaCrit.Sts2.Core.Models.CardModel? selected = SelectedCardOf(power, Shadow);

            if (noClones)
            {
                Logger.Log("[CharBoss] 夜魇：这套设计拦掉复制印牌（复制品已写在剧本里）⇒ 只移除这层 power");
                await PowerCmd.Remove(power);
                return;
            }

            if (selected != null && amount > 0)
            {
                for (int i = 0; i < amount; i++)
                {
                    var clone = selected.CreateClone();
                    await MegaCrit.Sts2.Core.Commands.CardPileCmd.AddGeneratedCardToCombat(
                        clone, MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand, Shadow);
                }
                Logger.Log($"[CharBoss] 夜魇：复制 {selected.Id.Entry} ×{amount} 进手牌，并移除这层 power");
            }
            else
            {
                Logger.Log($"[CharBoss] 夜魇：读不到被选中的牌（或层数 0）→ 只移除这层 power");
            }
            await PowerCmd.Remove(power);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 夜魇结算失败: " + e.Message);
        }
    }

    /// <summary>反射读 `NightmarePower` 内部数据里的 `selectedCard`（拿不到返回 null）。</summary>
    private static MegaCrit.Sts2.Core.Models.CardModel? SelectedCardOf(
        MegaCrit.Sts2.Core.Models.Powers.NightmarePower power, Player shadow)
    {
        try
        {
            var dataType = typeof(MegaCrit.Sts2.Core.Models.Powers.NightmarePower)
                .GetNestedType("Data", System.Reflection.BindingFlags.NonPublic);
            var getter = typeof(MegaCrit.Sts2.Core.Models.PowerModel)
                .GetMethod("GetInternalData", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (dataType == null || getter == null) return null;

            var data = getter.MakeGenericMethod(dataType).Invoke(power, null);
            if (data == null) return null;

            var cardField = dataType.GetField("selectedCard");
            return cardField?.GetValue(data) as MegaCrit.Sts2.Core.Models.CardModel;
        }
        catch { return null; }
    }

    /// <summary>
    /// 诊断：她出手前打一行「玩家侧都有谁、还剩多少血/格挡、身上挂没挂『为你而死』」。
    ///
    /// 起因（用户 2026-11-04）：「我的奥斯提有的时候不能成功替我承担伤害」——
    /// 光看现有日志只能看到「奥斯提 死亡」这种**结果**，看不到「她这一手之前奥斯提还在不在、有几滴血」，
    /// 于是没法判断是「它已经不在了」还是「这一手的伤害本来就不归它挡」。
    /// 只读、只打一行，不改变任何战斗行为。
    /// </summary>
    private void LogPlayerSideSnapshot()
    {
        try
        {
            var combat = CombatState;
            if (combat == null) return;
            var parts = new List<string>();
            var seen = new HashSet<Creature>();
            foreach (var creature in combat.PlayerCreatures)
            {
                AppendPlayerSideCreature(parts, seen, creature, "真人在列");
                // ⚠ 玩家的**宠物/召唤物不在 `PlayerCreatures` 里**（实测：奥斯提只出现在 `Player.Pets` 上）
                //   —— 而「奥斯提还在不在、有几滴血」正是这一行要回答的东西，必须单独走一遍 Pets。
                try
                {
                    foreach (var pet in creature.Pets)
                        AppendPlayerSideCreature(parts, seen, pet, "宠物");
                }
                catch { /* 忽略 */ }
            }
            Logger.Log($"[CharBoss] 玩家侧快照（她第 {TurnNumber} 回合出手前）：" +
                       (parts.Count == 0 ? "（没有活着的玩家侧生物）" : string.Join(" | ", parts)));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 玩家侧快照失败（不影响战斗）: " + e.Message);
        }
    }

        /// <summary>
    /// 把她**消耗牌堆**里那些「回合开始时自动复燃」的牌打出来。
    ///
    /// 原版钩子：<c>AbstractModel.AfterAutoPrePlayPhaseEnteredEarly(ctx, player)</c>
    /// （反编译 `Bombardment` 的实现：`if (Pile?.Type == Exhaust &amp;&amp; player == Owner) await CardCmd.AutoPlay(ctx, this, null);`）。
    /// 派发对象是 `CombatState.Players` 里的真人玩家 ⇒ 影子玩家永远收不到，所以这里自己扫一遍。
    /// 只读消耗牌堆、逐个 try/catch：一张出事不影响其它的。
    /// </summary>
    private async Task PlayExhaustPrePlayCardsAsync(PlayerChoiceContext ctx, MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState pcs)
    {
        try
        {
            if (pcs == null || Shadow == null) return;
            List<MegaCrit.Sts2.Core.Models.CardModel> cards;
            try { cards = new List<MegaCrit.Sts2.Core.Models.CardModel>(pcs.ExhaustPile.Cards); }
            catch { return; }
            if (cards.Count == 0) return;

            foreach (var card in cards)
            {
                if (card == null) continue;
                if (Creature == null || Creature.IsDead) return;
                try
                {
                    await card.AfterAutoPrePlayPhaseEnteredEarly(ctx, Shadow);
                    Logger.Log($"[CharBoss] 回合开始复燃：{card.Id.Entry}（在消耗牌堆里）已按其自身钩子打出");
                }
                catch (Exception e)
                {
                    Logger.Log($"[CharBoss] 回合开始复燃失败（{SafeEntryOfCard(card)}）: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 消耗牌堆复燃扫描失败: " + e.Message);
        }
    }

    /// <summary>调试用的安全取牌名。</summary>
    private static string SafeEntryOfCard(MegaCrit.Sts2.Core.Models.CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }

    /// <summary>快照里的一行：名字 / 阵营 / 血量 / 格挡 / 全部 power（含「为你而死」）。</summary>
    private static void AppendPlayerSideCreature(List<string> parts, HashSet<Creature> seen,
                                                 Creature? creature, string source)
    {
        try
        {
            if (creature == null || creature.IsDead) return;
            if (!seen.Add(creature)) return;
            string powers;
            // ★ v0.2.514（用户 2026-11-08：「仆从储也会给玩家加力量」）：把**层数**也打出来 ——
            //   以前只打能力名，看不出「力量到底涨没涨、涨了几点」，定位不了是谁给的。
            try { powers = string.Join(",", creature.Powers.Select(p => p.Id.Entry + ":" + p.Amount)); }
            catch { powers = "?"; }
            string who = creature.Monster == null ? "真人玩家" : source;
            parts.Add($"{creature.Name}({who}) hp={creature.CurrentHp}/{creature.MaxHp} " +
                      $"格挡={creature.Block} powers=[{powers}]");
        }
        catch { /* 单个失败不影响其它 */ }
    }

    /// <summary>
    /// 这一张是不是剧本里标了「出手前先清空她的抽牌堆」的（`ScriptCard.EmptyDrawPileFirst`）。
    /// 目前只有「华丽猎」第 9 回合的「华丽收场」（原版要求抽牌堆为空才能打出）。
    /// </summary>
    private bool ScriptCardWantsEmptyDrawPile(CardModel? card)
    {
        try
        {
            if (card == null) return false;
            var turn = Def?.ScriptForAct(ActIndex)?.TurnAt(TurnNumber);
            if (turn?.Cards == null) return false;
            var type = card.GetType();
            foreach (var sc in turn.Cards)
            {
                if (!sc.EmptyDrawPileFirst) continue;
                if (sc.Card == type) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    /// <summary>把她自己的抽牌堆整堆挪进弃牌堆（为了让「华丽收场」满足「抽牌堆为空」）。</summary>
    private void EmptyOwnDrawPile()
    {
        try
        {
            var pcs = Shadow?.PlayerCombatState;
            if (pcs == null) return;
            var draw = new List<CardModel>();
            foreach (var c in pcs.DrawPile.Cards) if (c != null) draw.Add(c);
            if (draw.Count == 0) return;
            foreach (var c in draw)
            {
                try
                {
                    pcs.DrawPile.RemoveInternal(c, true);
                    pcs.DiscardPile.AddInternal(c, -1, true);
                }
                catch { /* 单张失败不影响其它 */ }
            }
            Logger.Log($"[CharBoss] 华丽收场：先把她的抽牌堆清空（{draw.Count} 张进弃牌堆）⇒ 满足原版出牌条件");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 清空抽牌堆失败: " + e.Message); }
    }

    private async Task PlayPlannedCards(IReadOnlyList<Creature> targets)
    {
        // ★ v0.2.351：进这个方法的**第一行**就记日志 —— 事件机上「回合卡住」时，
        //   有这行说明卡在她自己的出牌流程里；没有这行就说明游戏根本没把她这一手派下来。
        Logger.Log($"[CharBoss] 敌方行动入口：PlayPlannedCards（第 {TurnNumber} 回合，Plan={Plan.Count} 张）");

        // 读档续战等情况下可能没走过 RollMove（没算过计划），这里补算一次，
        // 否则 Boss 会白站一回合。注意用 PlanReady 区分「算过了但没牌可打」。
        if (!PlanReady)
        {
            Logger.Log("[CharBoss] perform move without a plan (reloaded?), planning now");
            PrepareTurn();
        }

        var pcs = Shadow?.PlayerCombatState;
        if (pcs == null)
        {
            Logger.Log("[CharBoss] perform move: no shadow player");
            PlanReady = false;
            return;
        }

        var ctx = new BlockingPlayerChoiceContext();

        // ★ 2026-11-05（用户设计）：三层「状态机」第 6 回合那个「洗牌」要**真的洗**
        //   —— 她的「大～抱抱」是在 `AfterShuffle` 里生成煤灰的；顺带按用户要求给每位真人玩家也塞 1 张煤灰。
        try { await DoScriptedShuffleAsync(ctx); }
        catch (Exception e) { Logger.Log("[CharBoss] 剧本洗牌失败（忽略）: " + e.Message); }

#if STS2_107
        // ★ 2026-11-05（用户实测 107）：烘焙手套在 v0.107 走 `BeforeHandDraw` —— 那时她的手牌还没重建、是空的
        //   ⇒ 原版「+1 力量」给不到她、「烧 1 张」也烧不动（日志：`此刻她手牌为空 ⇒ 跳过原版的烧牌`）。
        //   后果两个：① 涂毒猎每回合少了 +1 力量；② 本该被烧掉的那张还留在手里，等「生存者」丢弃时
        //   被我们的选牌器误选 ⇒ 奇巧的「连续反弹」没被丢掉、也就没被免费打出（用户：第二回合不打连续反弹）。
        //   ⇒ 旧版由我们自己补：先 +1 力量，再消耗剧本点名的那张。
        try { await ApplyToastyMittensForLegacy(ctx); }
        catch (Exception e) { Logger.Log("[CharBoss] 烘焙手套（旧版补）：失败 " + e.Message); }
#endif

        // Boss 出牌期间顶上自动选择器：让「抽 3 弃 1」这类需要选择的牌自动结算，
        // 否则它会等一个永远不到来的玩家选择，把整回合挂死。using 保证一定弹出。
        using var selectorScope = BossCardSelector.Push();

        // 替影子 Player 补触发「玩家回合开始」的钩子：它的遗物/能力（召 Osty、给辉星、
        // 下回合加抽/加能量…）都挂在这上面，而它不在 CombatState.Players 里，原版不会替它触发。
        // ⚠ v0.2.351（用户 2026-11-03：「我回合结束按钮呢？事件机怎么还是不让我结束回合？」）：
        //   她的回合必须**一定能走完**。这里给「回合开始的遗物/能力钩子」加 3 秒上限：
        //   哪个钩子不返回就不再等它（日志里会留一行），继续往下走 —— 绝不把整个回合挂死。
        {
            var turnStartTask = BossHookBridge.FireTurnStart(this, ctx);
            var turnStartDone = await Task.WhenAny(turnStartTask, Task.Delay(3000));
            if (!ReferenceEquals(turnStartDone, turnStartTask))
            {
                Logger.Log("[CharBoss] ⚠ 回合开始钩子超过 3000ms 没返回 ⇒ 不再等它，继续这一回合（玩家不会再被卡住）");
            }
            else await turnStartTask;
        }
        Logger.Log("[CharBoss] 步骤：回合开始钩子完成");

        // 「情感芯片」（EmotionChip）：她上一回合掉过血 ⇒ 本回合开始触发所有充能球的被动
        // （原版那条按「上一个玩家回合」判历史，对影子玩家从不成立，所以自己记账，见 Core/EmotionChipPlays）。
        try { await Core.EmotionChipPlays.RunAtOwnTurnStart(this, ctx); }
        catch (Exception e) { Logger.Log("[CharBoss] 情感芯片结算失败: " + e.Message); }
        Logger.Log("[CharBoss] 步骤：情感芯片完成");

        // 药水先喝（用户设计：炼药猎每回合都是「药水 → 牌」）：按剧本顺序喝她栏位里有的那几瓶。
        await DrinkPlannedPotionsAsync(ctx, Shadow);
        // 喝完药水立刻刷新一次面板/状态条：状态条是按「内容指纹」节流重建的，
        // 指纹里没包含药水列表 ⇒ 不主动刷这一下，药水的增减就不会实时更新（用户 2026-10-26 实测）。
        try { CharacterBosses.UI.BossHandUi.RefreshForce(this); CharacterBosses.UI.BossHandUi.Refresh(this); } catch { /* 忽略 */ }

        // 「夜魇」：原版在 `BeforeHandDraw` 里复制选中的牌再**移除自己**，而那条钩子只派发给
        // `CombatState.Players` ⇒ 影子玩家收不到 ⇒ 用户 2026-10-25 实测「夜魇 buff 不会清除」。
        // 这里在她回合开始替它结算一次（复制 N 张进手牌 + 移除这层 power）。
        await SettleNightmareAsync(ctx);


        // 袖里乾坤的「每回合一次」以**她自己的回合**为界重置
        //（用户 2026-10-24：玩家拿到额外回合时以前每个玩家回合都重置一次，一次塞 6 把刀）。
        try { Creature?.GetPower<Powers.SleeveTrickPower>()?.ResetForBossTurn(); }
        catch (Exception e) { Logger.Log("[CharBoss] 袖里乾坤重置失败: " + e.Message); }

        // 「命运同担」（一层奇巧猎）的「她这回合第一次弃牌」同样以她自己的回合为界重置
        try { Creature?.GetPower<Powers.SharedFatePower>()?.ResetForBossTurn(); }
        catch (Exception e) { Logger.Log("[CharBoss] 命运同担重置失败: " + e.Message); }

        // 偏差认知刚刚在「Boss 回合开始」扣掉了集中（力量镜像跟着扣）→
        // 把规划时预扣的那一点从意图里还原，否则行动时显示会偏低一点（用户实测）。
        // 奥斯提收尾（清尸体 / 爪牙标记 / 镜像站位）—— 必须在 FireTurnStart 的召唤之后
        CharacterBosses.Core.BossOsty.Update(this, ctx);
        Logger.Log("[CharBoss] 步骤：奥斯提收尾完成");

        // ★ 2026-11-04（用户：「我的奥斯提有的时候不能成功替我承担伤害，是不是…」）：
        //   把**玩家侧**每个生物在「她出手前」的 HP/格挡/关键 power 打一行。
        //   以后再有人说「奥斯提没替我挡」，看这一行 + 后面那张牌的日志就能直接判定：
        //     · 奥斯提不在了（hp=0 或者压根没列出来）⇒ 前一回合就已经死了；
        //     · 奥斯提在、但这一手的伤害是 `Unpowered`（黑洞/群蛇/球被动/炸弹/冰雹…）
        //       ⇒ 原版「为你而死」只挡**有力量加成的攻击**（`props.IsPoweredAttack()`），它本来就不挡；
        //     · 奥斯提在、被这一手打死 ⇒ 下一个回合就没有它了。
        LogPlayerSideSnapshot();

        AttacksPlayed = 0;   // 新的一回合：攻击牌计数从 0 开始（终结技段数靠它）
        // 力量快照要在 FireTurnStart 之后取：烘焙手套每回合那 +1 已经到账，
        // 这样"本回合真实获得的力量"就只反映手里剑这类出牌过程中触发的成长。
        StrengthAtTurnStart = Core.BladesGrowth.CurrentStrength(this);

        // 出牌前**用真实能量重规划一次**（用户 2026-10-23：意图显示了要打、最后没打）。
        // 原因：牌面耗能会变（例：忧郁 `Melancholy`「每当有生物死亡，耗能 -1」），
        // 而计划是**玩家回合里**算的，那时读到的耗能与她真正出牌时可能差 1 点
        // ⇒ 计划里留着实际打不出的牌。此刻 `pcs.Energy` 正是本回合能量，重算才与执行一致。
        ReplanAtPlayStart();

        // 她回合真的开始了：「缚魂命匣」这类「回合开始时给奥斯提加生命」已经到账 ⇒
        // 以**此刻**的真实值为基准重新冻结预补（否则出击会少算回合开始那一笔，用户实测差 1）。
        // 此时她一张牌都还没打，三份「已实现」计数都还是 0，重新冻结是安全的。
        try { Core.BladesGrowth.RefreshCompensation(this, Plan); }
        catch (Exception e) { Logger.Log("[CharBoss] 重新冻结意图预补失败: " + e.Message); }
        RefreshPlannedIntents();
        // 意图刚被重算过（她回合开始已拿到力量 → 撤掉规划期"提前补的那点"）
        // ⇒ 必须整排重建手牌栏，否则增量同步会留下规划期的旧数字（用户实测）。
        CharacterBosses.UI.BossHandUi.RefreshForce(this);

        var swAll = System.Diagnostics.Stopwatch.StartNew();
        long lastMs = 0;
        Logger.Log($"[CharBoss] 回合准备完成（第 {TurnNumber} 回合）：开始逐张出牌");
        Logger.Log("[CharBoss] 出牌阶段开始");

        // ★ 一层「轰击储」：原版 `CardModel.AfterAutoPrePlayPhaseEnteredEarly(ctx, player)` 这条钩子
        //   我们**从来没接管过**（只接管了 `AfterAutoPostPlayPhaseEntered` 那一族）。而「轰击」
        //   （`Bombardment`）正是靠它在**回合开始、出牌之前**把自己从消耗牌堆里再打一遍
        //   （反编译：`if (Pile?.Type == Exhaust && player == Owner) await CardCmd.AutoPlay(ctx, this, null);`）。
        //   影子玩家不在 `CombatState.Players` 里 ⇒ 原版不会派发 ⇒ 必须自己扫一遍她的消耗牌堆。
        await PlayExhaustPrePlayCardsAsync(ctx, pcs);

        // ★ 彩虹机：先把「模仿学习」攒下的复制品（以及「无尽刀刃」那张小刀）免费打出
        try { await Core.RainbowMimic.PlayPendingAsync(this, pcs, ctx); }
        catch (Exception e) { Logger.Log("[CharBoss] 彩虹机·模仿学习结算失败: " + e.Message); }

        foreach (var play in Plan.ToList())
        {
            if (Creature.IsDead) break;
            try
            {
                // 「惊逃」那张：留着不动，回合结束由 StampedePlays 打出去
                //（它在 Plan 里只是为了和普通出牌共用意图重算 / 预补撤回逻辑）
                if (play.StampedeOnly) continue;
                // 计划里的牌必须还在手牌里（回合中可能被弃掉/消耗掉）→ 不在就跳过
                if (!pcs.Hand.Cards.Any(c => ReferenceEquals(c, play.Card)))
                {
                    Plan.Remove(play);
                    continue;
                }

                // 打这一张**之前**再按当前真实力量重算一遍意图：
                // 上一张牌触发手里剑（+1 力量）是异步到账的，若只在"打完"时重算，
                // 屏幕上可能有一瞬间还挂着上一轮的补正（用户看到的"残留 +1"）。
                // ★ v0.2.358（用户 2026-11-04：「补偿疯狂进食和一心化万获得了力量后，
                //   没有在力量实际到账后移除补偿。参考一下其他 boss」）：
                //   走到这里**有球 / 没球要分开**：没球的 Boss 走 `BladesGrowth.RecomputeAll`（那一套是
                //   为小刀/君王之剑写的）；**有球的 Boss 必须走它自己那套** `RefreshPlannedIntents`
                //   —— 后者会一并把「本回合待到手的力量」（疯狂进食 / 一心化万返手）算进去，
                //   并且减掉**已经真到账**的那部分（`PlannedStrengthGainedThisTurn`），
                //   同时保住「本回合会激发哪颗球」的意图。以前两条路都用 `RecomputeAll`
                //   ⇒ 有球的 Boss 既丢了激发意图、又把这份力量算错。
                bool orbBoss = false;
                try { orbBoss = pcs.OrbQueue.Capacity > 0; } catch { orbBoss = false; }
                if (orbBoss) RefreshPlannedIntents();
                else BladesGrowth.RecomputeAll(this, Plan, planTime: false);

                await PlayOne(ctx, pcs, play);
                try { Core.RainbowMimic.OnCardPlayed(this, play.Card); } catch { /* 忽略 */ }
                lastMs = swAll.ElapsedMilliseconds;      // ← 之前一直没赋值（日志里「最后一张牌后」永远等于总时长）

                // 小刀流：**生成刀的那张牌打出之后**，刀才进手牌 → 这时把它们摆到最前面
                //（亮起、每把刀下面各自的攻势）并立刻打掉 —— 塔 1 就是这个观感。
                if (play.GeneratedShivs > 0)
                {
                    var shivPlays = SilentBlades.FrontloadRealShivs(this, planTime: false);
                    CharacterBosses.UI.BossHandUi.Refresh(this);
                    foreach (var shivPlay in shivPlays)
                    {
                        if (Creature.IsDead) break;
                        if (!pcs.Hand.Cards.Any(c => ReferenceEquals(c, shivPlay.Card))) continue;
                        await PlayOne(ctx, pcs, shivPlay);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Log($"[CharBoss] play {play.Card.Id.Entry} FAILED: {e}");
            }
        }

        // 「有的时候有费不出牌」（用户 2026-10-23）：
        // 计划是**回合开始前**按当时的能量算的，而她在出牌过程中可能又拿到能量
        //（自动化：每抽 10 张 +1 —— 灵魂绑定让计数飞涨；鬼火 +1 等）。
        // 那些当时因「费不够」被跳过的牌（用户写「有费就打」的正是这种）不会自动补上，
        // 于是回合结束时手里还留着、能量也还剩。这里按剧本顺序**重算一次并补打**：
        // 已经打出去的牌不在手上了，所以重算出来的就是「还没打且现在打得起的」那些。
        //
        // ⚠ **只给灵魂骨用**（身上有「灵魂绑定」）。别的 Boss 的剧本是照着「没费就打不出来」写的
        // （储君第三幕的防御、根除骨第 3 回合的洁净+、能力机的打击…），对它们补打等于改设计。
        // ★ v0.2.338（用户 2026-11-03）：「让其它 3 层 boss 在第三回合加一个判定：没费的牌有费就打」
        //   ⇒ **所有第三幕 Boss 的第 3 回合**也走这条补打（其余回合仍只有灵魂骨）。
        try
        {
            bool act3Turn3 = ActIndex == 2 && TurnNumber == 3;
            bool honorLeftoverReplay = Creature?.GetPower<Powers.BossSoulBoundPower>() != null || act3Turn3;
            var scriptedTurn = Def.ScriptForAct(ActIndex)?.TurnAt(TurnNumber);
            if (honorLeftoverReplay && scriptedTurn != null && !Creature.IsDead
                && pcs.Hand.Cards.Count > 0 && pcs.Energy > 0)
            {
                var extra = Core.BossScriptPlanner.Plan(this, pcs, scriptedTurn, TurnNumber);
                if (extra.Count > 0)
                {
                    Logger.Log($"[CharBoss] 回合中又拿到资源 → 按剧本补打" +
                               $"{(act3Turn3 ? "（三层第 3 回合：没费的牌有费就打）" : string.Empty)}： " +
                               $"[{string.Join(",", extra.ConvertAll(x => x.Card.Id.Entry))}]（余费 {pcs.Energy}）");
                    foreach (var extraPlay in extra)
                    {
                        if (Creature.IsDead) break;
                        if (!pcs.Hand.Cards.Any(c => ReferenceEquals(c, extraPlay.Card))) continue;
                        await PlayOne(ctx, pcs, extraPlay);
                    }
                    CharacterBosses.UI.BossHandUi.RefreshForce(this);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 补打（余费）失败: " + e.Message);
        }

        // 保险：手牌里还剩的小刀（例如袖里乾坤在别处塞的），有多少打多少
        try
        {
            var leftovers = SilentBlades.FrontloadRealShivs(this, planTime: false);
            foreach (var shivPlay in leftovers)
            {
                if (Creature.IsDead) break;
                if (!pcs.Hand.Cards.Any(c => ReferenceEquals(c, shivPlay.Card))) continue;
                await PlayOne(ctx, pcs, shivPlay);
            }
            if (leftovers.Count > 0) Logger.Log($"[CharBoss] 补充打出 {leftovers.Count} 张小刀");
            CharacterBosses.UI.BossHandUi.Refresh(this);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 额外小刀结算失败: " + e.Message);
        }

        // 第一回合出完牌后说一句话（用户设计：能力机＝低语耳环那句「这样才对」）。
        // 原版低语耳环就是 `TalkCmd.Play(new LocString("relics", "WHISPERING_EARRING.approval"),
        // Owner.Creature, VfxColor.Purple)` —— 文本本身带 [red][shake] 样式，气泡颜色是紫色。
        SayFirstTurnLine();

        // 「惊逃」（STAMPEDE，二层「杂耍战」）：回合结束时随机打出手牌里的攻击牌（不花能量）。
        // 原版这条钩子只派发给 CombatState.Players 里的真人，影子玩家收不到 ⇒ 手动调它自己的实现。
        // 必须在**丢掉手牌之前**、也在「杂耍复制牌」（带虚无+消耗）还在手里的时候跑。
        try { await Core.StampedePlays.RunAtTurnEnd(this, ctx); }
        catch (Exception e) { Logger.Log("[CharBoss] 惊逃（回合结束）失败: " + e.Message); }

        // 「所向无敌」（I_AM_INVINCIBLE）：用户设计 —— 他回合结束时这张牌正好躺在摸牌堆顶 → 免费打出。
        // 原版这张牌自己就实现了这件事：IAmInvincible.AfterAutoPostPlayPhaseEntered 里判断
        // 「自己是不是摸牌堆顶」→ 是就 CardPileCmd.AutoPlayFromDrawPile 免费打出。
        // 但那条钩子只对真 Player 触发，影子玩家没注册成监听者 ⇒ 收不到（和天赋君权同一类问题）。
        // 所以在剧本标了 FreePlayTopEntry 的回合：把那张真牌挪到堆顶，再直接调它自己的那条钩子。
        try
        {
            var turnScript = Def.ScriptForAct(ActIndex)?.TurnAt(TurnNumber);
            string? freeEntry = turnScript?.FreePlayTopEntry;
            if (!string.IsNullOrEmpty(freeEntry))
            {
                var freeCard = BossScriptRunner.MoveCardToDrawTop(pcs, freeEntry!);
                if (freeCard == null)
                {
                    Logger.Log($"[CharBoss] {freeEntry}：牌堆里没找到这张牌，跳过回合结束免费打出");
                }
                else
                {
                    int energyBefore = pcs.Energy, starsBefore = pcs.Stars;
                    await freeCard.AfterAutoPostPlayPhaseEntered(ctx, Shadow!);
                    Logger.Log($"[CharBoss] 回合结束免费打出 {freeEntry}（摸牌堆顶）：" +
                               $"能量 {energyBefore}→{pcs.Energy}，辉星 {starsBefore}→{pcs.Stars}");
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 回合结束免费打出失败: " + DescribePlayFailure(e));
        }

        // 「彼岸咆哮」（HOWL_FROM_BEYOND）：用户设计（三层「巨石战」）——
        // 「在你的回合结束时，如果这张牌在你的消耗牌堆中，则将其打出」。
        // 那条实现写在 HowlFromBeyond.AfterAutoPostPlayPhaseEntered 里，只对真人玩家派发 ⇒ 手动调。
        try
        {
            var turnScript2 = Def.ScriptForAct(ActIndex)?.TurnAt(TurnNumber);
            string? exhaustEntry = turnScript2?.FreePlayExhaustEntry;
            if (!string.IsNullOrEmpty(exhaustEntry))
            {
                var exhaustCard = BossScriptRunner.FindInExhaust(pcs, exhaustEntry!);
                if (exhaustCard == null)
                {
                    Logger.Log($"[CharBoss] {exhaustEntry}：消耗牌堆里没找到，跳过回合结束自动打出");
                }
                else
                {
                    await exhaustCard.AfterAutoPostPlayPhaseEntered(ctx, Shadow!);
                    Logger.Log($"[CharBoss] 回合结束自动打出 {exhaustEntry}（来自消耗牌堆）");
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 回合结束从消耗牌堆自动打出失败: " + DescribePlayFailure(e));
        }
        // 球的被动结算在**Boss 自己的回合结束**（原版时机），而不是玩家回合结束时
        Logger.Log($"[CharBoss] 出牌阶段结束: 共 {swAll.ElapsedMilliseconds}ms（最后一张牌后 {swAll.ElapsedMilliseconds - lastMs}ms）" +
                   $"（此段含：补打/回合结束免费打出/消耗堆自动打出）");
        var swEnd = System.Diagnostics.Stopwatch.StartNew();
        await CharacterBosses.UI.BossOrbDisplay.ResolveTurnEnd(this, ctx);
        long orbMs = swEnd.ElapsedMilliseconds;
        Logger.Log($"[CharBoss] 回合结束耗时: 球结算 {orbMs}ms");

        // 她**遗物**的「回合结束」钩子（奥利哈钢：回合结束没有格挡 → +6 格挡）。
        // 影子玩家的遗物收不到原版派发（只派发给 CombatState.Players 的遗物）⇒ 这里手动跑一次。
        // 只有 BossDefinition.FireBossRelicHooks = true 的 Boss（目前是愤怒战）才生效。
        var swRelic = System.Diagnostics.Stopwatch.StartNew();
        try { await CharacterBosses.Patches.BossRelicHookBridge.FireSideTurnEnd(this, ctx); }
        catch (Exception e) { Logger.Log("[CharBoss] 回合结束遗物钩子失败: " + e.Message); }

        // 「斗篷扣」（CloakClasp，二层「蛇眼猎」的遗物）：回合结束时「每张手牌 → N 点格挡」。
        // 原版挂在 `BeforeSideTurnEnd`，影子玩家收不到那条广播 ⇒ 这里照卡面补一次。
        // ⚠ 必须在**丢掉手牌之前**（`BossPiles.DiscardHand` 在后面），否则手里已经没牌了。
        try { await Core.CloakClaspPlays.RunAtTurnEnd(this, ctx); }
        catch (Exception e) { Logger.Log("[CharBoss] 斗篷扣结算失败: " + e.Message); }
        // ★ v0.2.314（用户 2026-11-02：「第二回合打击因为虚无在他回合结束时消耗然后因为无惧疼痛起甲
        //   是和奥利哈钢的起甲时机相同的，所以那一回合他应该会获得 6+3 或 4 点格挡」）：
        //   **must 放在奥利哈钢之后** —— 奥利哈钢的条件是「回合结束时**没有**格挡」，
        //   若先把「虚无消耗 → 无惧疼痛」给的这 3 点算上，奥利哈钢那 6 点就永远拿不到了。
        //   （第三回合那张是被「惊逃」**打出**时消耗的，发生在她出牌阶段 ⇒ 天然早于奥利哈钢，
        //    所以那一回合仍是「只有无惧疼痛的 3/4 点」，维持现状。）
        try { await Core.EtherealHandExhaust.RunAtTurnEnd(this, ctx); }
        catch (Exception e) { Logger.Log("[CharBoss] 虚无消耗（回合结束）失败: " + e.Message); }

        // 诅咒「羞耻」这类「回合结束时若在手牌」的效果：原版由 CombatManager 对
        // CombatState.Players 的手牌派发 `OnTurnEndInHand`，影子玩家不在里面 ⇒ 永远不触发（用户实测）。
        // 必须放在丢手牌之前结算。
        var swCurse = System.Diagnostics.Stopwatch.StartNew();
        await RunCurseTurnEndHooks(ctx, pcs);
        long relicMs = swRelic.ElapsedMilliseconds, curseMs = swCurse.ElapsedMilliseconds;

        Plan.Clear();
        PlanReady = false;
        var swDiscard = System.Diagnostics.Stopwatch.StartNew();
        BossPiles.DiscardHand(pcs);

        // 用户 2026-10-29：「炸弹/冰雹风暴的结算在最后一个 Boss 出完牌后又隔了快 2 秒」
        // ⇒ 这一行把**我们这边**回合收尾的每一段耗时都摊开；日志里「炸弹巡检」那行的时间戳
        //    与本行对比，就能看出那 2 秒到底花在我们这里、还是花在原版（我们返回之后）。
        Logger.Log($"[CharBoss] 回合收尾耗时: 球 {orbMs}ms / 遗物 {relicMs}ms / 诅咒 {curseMs}ms / " +
                   $"弃手牌 {swDiscard.ElapsedMilliseconds}ms（我们这边总计 {swAll.ElapsedMilliseconds}ms）");

        // 记一个时间戳：原版派发「敌方回合结束」（炸弹/冰雹风暴等）时会打出与这里的差值，
        // 用来分辨那 2 秒的延迟是在我们这边还是在原版（见 SideTurnEndTimingPatch）。
        CharacterBosses.Patches.SideTurnEndTimingPatch.MarkOwnTurnEnded();
    }

    /// <summary>
    /// 自动打出 / 免费打出失败时的日志文本（**带归因**）。
    ///
    /// ★ v0.2.465（2026-11-07 玩家上报的那条）：
    /// `KeyNotFoundException: The given key 'MegaCrit.Sts2.Core.Entities.Players.Player' was not present
    ///  in the dictionary. at YuYuYu.Scripts.Singletons.CardsSeenSingleton.AfterCardPlayed(...)`
    /// —— 这是**别的模组**在「一张牌被打出之后」的钩子里拿 Player 去查它自己维护的字典，
    /// 而我们的 Boss 用的是**影子 Player**（不在 `RunState.Players` 里）⇒ 字典里没这个键。
    /// 它炸在我们 `AfterAutoPostPlayPhaseEntered` 返回之后 ⇒ **这张牌本身已经结算完了**，
    /// 坏的只是那个模组自己的统计。所以这里把栈里第一个「既不是我们、也不是原版」的帧当成抛出方。
    /// </summary>
    private static string DescribePlayFailure(Exception e)
    {
        try
        {
            string text = e.GetType().Name + ": " + e.Message;
            string stack = e.StackTrace ?? "";

            string? foreign = null;
            foreach (string raw in stack.Split('\n'))
            {
                string line = raw.Trim();
                int at = line.IndexOf("at ", StringComparison.Ordinal);
                if (at < 0) continue;
                string rest = line.Substring(at + 3).Trim();
                int dot = rest.IndexOf('.');
                if (dot <= 0) continue;
                string root = rest.Substring(0, dot);
                if (root == "CharacterBosses" || root == "MegaCrit" || root == "System" ||
                    root == "HarmonyLib" || root == "Godot" || root == "Microsoft") continue;
                foreign = rest;
                break;
            }

            if (foreign != null)
            {
                return text + " ⇒ 由**第三方模组**抛出（" + foreign + "）：它拿影子玩家去查自己的字典/表，" +
                       "而影子玩家不在原版 Players 列表里。这张牌/这次自动打出的**本体效果已结算完毕**，" +
                       "炸掉的只是那个模组自己的统计，可以忽略。";
            }

            string frames = "";
            string[] lines = stack.Split('\n');
            for (int i = 0; i < lines.Length && i < 3; i++)
            {
                string t = lines[i].Trim();
                if (t.Length > 0) frames += " | " + t;
            }
            return text + frames;
        }
        catch { return e.GetType().Name + ": " + e.Message; }
    }

    /// <summary>
    /// 回合结束时**手牌里**的诅咒/状态效果（原版钩子：<c>CardModel.OnTurnEndInHand</c>）。
    ///
    /// 为什么要自己跑：原版 `CombatManager.DoTurnEnd` 是遍历 **`CombatState.Players` 的手牌**、
    /// 只对 `HasTurnEndInHandEffect == true` 的牌派发 `OnTurnEndInHandWrapper` 的；
    /// 影子 Player 不在那个列表里 ⇒ 她手牌里的这类牌**一个都不会触发**
    /// （用户 2026-10-23 实测：羞耻不给脆弱）。
    ///
    /// 所以这里按**同一套判定**（`HasTurnEndInHandEffect`）对手牌逐张调它们**自己的**实现
    /// `OnTurnEndInHandWrapper` —— 用户要求「先提前把所有类似诅咒都做一下」，
    /// 目前原版这一族有：羞耻 <c>Shame</c>（+1 脆弱）、厄运 <c>BadLuck</c>、召唤 <c>Beckon</c>、
    /// 灼烧 <c>Burn</c>、债务 <c>Debt</c>、腐朽 <c>Decay</c>、怀疑 <c>Doubt</c>、感染 <c>Infection</c>；
    /// 以后原版再加新的一种也**自动生效**，不需要改这里。
    ///
    /// 必须在**丢掉手牌之前**调用（她回合结束时的 `BossPiles.DiscardHand`）。
    /// </summary>
    private async Task RunCurseTurnEndHooks(PlayerChoiceContext ctx, PlayerCombatState pcs)
    {
        try
        {
            if (Creature == null) return;

            // ★ v0.2.465（2026-11-07 玩家上报：「回合结束手牌效果失败: Collection was modified;
            //   enumeration operation may not execute.」）：这些诅咒/状态牌的 `OnTurnEndInHandWrapper`
            //   **会真的改动手牌**（摧毁自己、塞新牌、转化自己…），所以不能直接 foreach 活的手牌集合。
            //   先拍一份快照再遍历；每张牌结算前再确认它「还在手牌里」，避免对已经被吃掉/变形的牌重复结算。
            var snapshot = new List<MegaCrit.Sts2.Core.Models.CardModel>();
            try
            {
                foreach (var c in pcs.Hand.Cards) if (c != null) snapshot.Add(c);
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 回合结束手牌效果：读手牌失败（跳过）: " + e.Message);
                return;
            }

            foreach (var card in snapshot)
            {
                bool stillInHand;
                try { stillInHand = pcs.Hand.Cards.Contains(card); } catch { stillInHand = false; }
                if (!stillInHand) continue;

                bool has;
                string entry;
                try
                {
                    has = card.HasTurnEndInHandEffect;
                    entry = card.Id.Entry;
                }
                catch { continue; }
                if (!has) continue;

                try
                {
                    await card.OnTurnEndInHandWrapper(ctx);
                    Logger.Log($"[CharBoss] 回合结束手牌效果：{entry} 已对 {Id.Entry} 结算" +
                               "（原版 OnTurnEndInHand 只对 CombatState.Players 的手牌派发）");
                }
                catch (Exception e)
                {
                    Logger.Log($"[CharBoss] 回合结束手牌效果 {entry} 结算失败: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 回合结束手牌效果失败: " + e.Message);
        }
    }

    /// <summary>
    /// 「恶魔护盾」要给谁：**同一侧的友方 Boss**里，挑「格挡最少」的那一位。
    ///
    /// 用户 2026-10-27 的口径：
    ///   · 只给**已经行动过**的友方 —— 否则他这回合结束就会把格挡清掉，等于白给；
    ///   · 储君（`RegentBoss`）在她之后行动，所以**除非队友只剩储君**，否则不给储君；
    ///   · 友方全倒了 ⇒ 返回 null（调用方会跳过这张牌）。
    /// 实现取「同一侧、活着的其它 CharBossMonster」，按上面两条排序后取格挡最少的。
    /// </summary>
    /// <summary>快照「所有真人玩家 → 当前生命」（给「原始力量」判定未被格挡伤害用）。</summary>
    private System.Collections.Generic.Dictionary<MegaCrit.Sts2.Core.Entities.Creatures.Creature, int> SnapshotRealPlayerHp()
    {
        var map = new System.Collections.Generic.Dictionary<MegaCrit.Sts2.Core.Entities.Creatures.Creature, int>();
        try
        {
            foreach (var p in Core.BossLockOn.RealPlayers(CombatState))
            {
                var c = p?.Creature;
                if (c == null) continue;
                map[c] = c.CurrentHp;
            }
        }
        catch { /* 忽略 */ }
        return map;
    }

    private MegaCrit.Sts2.Core.Entities.Creatures.Creature? ChooseDemonicShieldTarget()
    {
        try
        {
            var combat = CombatState;
            var me = Creature;
            if (combat == null || me == null) return null;

            var others = new List<MegaCrit.Sts2.Core.Entities.Creatures.Creature>();
            foreach (var list in new[] { combat.Enemies, combat.Allies })
            {
                if (list == null) continue;
                foreach (var c in list)
                {
                    if (c == null || ReferenceEquals(c, me) || !c.IsAlive) continue;
                    if (c.Monster is not CharBossMonster) continue;      // 只认友方 Boss
                    try { if (c.Side != me.Side) continue; } catch { continue; }
                    if (!others.Contains(c)) others.Add(c);
                }
            }
            if (others.Count == 0) return null;

            // ① 先把储君排到最后（除非只剩他）
            var nonRegent = new List<MegaCrit.Sts2.Core.Entities.Creatures.Creature>();
            foreach (var c in others)
            {
                try { if (c.Monster is RegentBoss) continue; } catch { /* 忽略 */ }
                nonRegent.Add(c);
            }
            var pool = nonRegent.Count > 0 ? nonRegent : others;

            // ② 取格挡最少的那位（同格挡时按 NetId，保证两端一致）
            MegaCrit.Sts2.Core.Entities.Creatures.Creature? best = null;
            foreach (var c in pool)
            {
                if (best == null) { best = c; continue; }
                int a = 0, b = 0;
                try { a = c.Block; } catch { a = 0; }
                try { b = best.Block; } catch { b = 0; }
                if (a < b) best = c;
                else if (a == b)
                {
                    try { if (c.Player != null && best.Player != null && c.Player.NetId < best.Player.NetId) best = c; }
                    catch { /* 忽略 */ }
                }
            }
            return best;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 恶魔护盾选友方失败: " + e.Message);
            return null;
        }
    }
    /// <summary>
    /// 打出一张计划里的牌（主循环与小刀子循环共用）。
    /// </summary>
    private async Task PlayOne(PlayerChoiceContext ctx, PlayerCombatState pcs, PlannedPlay play)
    {
        // 告诉自动选择器「现在是谁的回合」——它按 (Boss, 回合号) 取「点名要丢的牌」，
        // 否则规划下一回合时那次清空会把本回合的点名抹掉（用户 2026-10-29：杂技丢了触不可及）。
        // ★ v0.2.525：连「**是哪一张牌**在选牌」一起告诉它 —— 同一回合里两个丢弃者
        //   （杂技丢战术大师 / 隐秘匕首丢两张防御）不会再互相抢牌，「手上技法」也不会
        //   把别人要丢的那张抢去加奇巧（用户 2026-11-09 的第 4、5 条 bug）。
        try
        {
            Core.BossCardSelector.ActingBoss = this;
            Core.BossCardSelector.ActingTurn = TurnNumber;
            Core.BossCardSelector.ActingCard = play.Card;
        }
        catch { /* 忽略 */ }

        // ★ 地狱狂徒（用户 2026-11-07）：标了「（地狱狂徒）」的牌由**自动免费打出**通道处理
        //   —— 0 费、只打被锁定的玩家、带特效；成功就返回，不再走普通出牌。
        try
        {
            if (await Core.HellraiserPlays.TryPlayAsync(this, pcs, ctx, play.Card)) return;
        }
        catch (Exception e) { Logger.Log("[CharBoss] 地狱狂徒通道异常（退回普通出牌）: " + e.Message); }

        // ★ v0.2.501（用户 2026-11-08）：「令逃脱计划一定会抽到技能牌，每次她打出它都会获得对应格挡」
        //   原版 EscapePlan = 「抽 1 张；若是技能牌则获得格挡」⇒ 先往她抽牌堆顶放一张「防御」（技能），
        //   于是必定抽到技能牌、必定吃满格挡；抽牌本身照常发生（速行者照旧触发）。
        try
        {
            if (SafeEntryOf(play.Card) == "ESCAPE_PLAN")
            {
                var defCanonical = MegaCrit.Sts2.Core.Models.ModelDb.GetById<MegaCrit.Sts2.Core.Models.CardModel>(
                    MegaCrit.Sts2.Core.Models.ModelDb.GetId(typeof(MegaCrit.Sts2.Core.Models.Cards.DefendSilent)));
                if (defCanonical != null)
                {
                    var put = CombatState?.CreateCard(defCanonical, Shadow!);
                    if (put != null)
                    {
                        pcs.DrawPile.AddInternal(put, 0, true);
                        Logger.Log("[CharBoss] 逃脱计划：已把一张「防御」放到抽牌堆顶（必定抽到技能牌）");
                    }
                }
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 逃脱计划放牌失败（忽略）: " + e.Message); }
        var target = play.Target;
        if (target == null || target.IsDead || !target.IsAlive) target = BossPlanner.ChooseTarget(this, play.Card);

        // ⚠ 出牌这一刻以「**跟踪锁定**」为准，而不是计划里存的那个玩家
        //   （用户 2026-10-29 实测：储君攻击了**被别的 Boss 锁定**的那名玩家，却把碎屑塞给了自己锁的那位）。
        //
        // 为什么两者会不一致：计划是在**玩家回合／她回合开始**算的，而锁定关系在之后可能被改派
        // （`ReassignDeadLocks` / `Rebalance`：有人阵亡、开局分配晚于规划…）。
        // 而「碎屑洗给谁」「生成牌给谁」这类效果用的是**出牌瞬间**的 `TargetFor` ⇒
        // 攻击按旧计划、碎屑按新的锁，同一张牌就打到两个不同的人身上。
        // 所以只要这张牌是「需要对手」的，就用实时的锁重新取一次；取了也不影响群攻（群攻按全体结算）。
        try
        {
            if (WantsOpponent(play.Card))
            {
                var live = CharacterBosses.Core.BossLockOn.TargetFor(this)?.Creature;
                // ⚠ 斗蛐蛐里 Boss 可能被塞进玩家侧：此时"锁定到的玩家"和它同阵营，
                //   实时改判会把正确目标（对面那只怪）覆盖成自己人 ⇒ 必须做同阵营过滤。
                bool sameSide = live != null && Creature != null && live.Side == Creature.Side;
                if (sameSide) Logger.Log($"[CharBoss] {play.Card.Id.Entry} 忽略同阵营实时锁定：{DescribePlayer(live)}");
                live = sameSide ? null : live;
                if (live != null && live.IsAlive && !live.IsDead && !ReferenceEquals(live, target))
                {
                    Logger.Log($"[CharBoss] {play.Card.Id.Entry} 的出牌目标按实时锁定改判：" +
                               $"{DescribePlayer(target)} → {DescribePlayer(live)}" +
                               (play.Target != null && ReferenceEquals(play.Target, target) ? "（计划里那份已过期）" : string.Empty));
                    target = live;
                }
            }
        }
        catch { /* 忽略：取不到就用原目标 */ }


        // 「恶魔护盾」（第二幕「放血战」多人换上的那张，用户 2026-10-27 设计）：
        // 效果是「给予**另一位玩家**你当前格挡值的格挡」—— 对我们这只 Boss 来说，
        // 「另一位玩家」应该是**同一侧的友方 Boss**（把格挡给队友，而不是给真人玩家）。
        // 目标规则（用户口径）：给**已行动**的友方里**格挡最少**的那位；
        //   储君（`RegentBoss`）在她之后行动，所以只要还有别的友方就不给他；
        //   友方全倒了 ⇒ **这张牌干脆不出**。
        if (SafeEntryOf(play.Card) == "DEMONIC_SHIELD")
        {
            var allyTarget = ChooseDemonicShieldTarget();
            if (allyTarget == null)
            {
                Logger.Log(CharacterBosses.Core.MultiplayerCardSwap.IsMultiplayer(this)
                    ? "[CharBoss] 恶魔护盾：没有可给的友方 Boss（队友都倒了）→ 这张牌不出"
                    : "[CharBoss] 恶魔护盾：单人局没有友方 Boss → 这张牌不出"
                      + "（单人剧本里本不该有这张；见 BleedIroncladScript 与 MultiplayerCardSwap）");
                return;
            }
            target = allyTarget;
            Logger.Log($"[CharBoss] 恶魔护盾 → 给 {allyTarget.Name}（友方中格挡最少）");
        }
        if (target == null) return;

        // **执行期兜底**（用户 2026-10-23：「怎么有的时候有的牌没费也出」）：
        // 规划期的能量是我们自己记账模拟出来的，而牌面上的 `Energy` 变量并不都等于「现在真的回能」
        //（拖延 = 下回合才给、自动化 = 每抽 10 张才给）⇒ 偶尔会把后面的牌算成"打得起"。
        // 而 `CardCmd.AutoPlay` **既不检查也不扣费**，所以不在这里挡住就会白打出去。
        try
        {
            if (play.Cost > pcs.Energy || play.StarCost > pcs.Stars)
            {
                Logger.Log($"[CharBoss] 跳过 {play.Card.Id.Entry}（执行期资源不够：" +
                           $"费 {play.Cost}>{pcs.Energy}，星 {play.StarCost}>{pcs.Stars}）");
                return;
            }
        }
        catch { /* 读不到资源就照旧打 */ }

        // 「炼制药水」：把这一张要炼的那瓶登记给 AlchemizeBrewPatch（剧本顺序排队）。
        try
        {
            // 先处理「打这张牌之前先喝一瓶」（剧本第 4/8 回合：预判 → 喝复制药水 → 坚韧之环被复制）
            if (PlannedDrinks.Count > 0 && Shadow != null)
            {
                var front = PlannedDrinks.Peek();
                if (front.Card == play.Card?.GetType())
                {
                    PlannedDrinks.Dequeue();
                    PotionModel? drink = null;
                    foreach (var p in BossPotions.Held(Shadow))
                        if (p != null && p.GetType() == front.Potion) { drink = p; break; }
                    if (drink != null)
                    {
                        await BossPotions.DrinkAsync(ctx, Shadow, drink, Def.HealPerPotionForAct(ActIndex), Def.PotionsForAct(ActIndex));
                        await ShowPotionGoneAsync();      // 同上：立刻重画 + 停一下，让这一瓶肉眼可见地消失
                    }
                    else
                        Logger.Log($"[CharBoss] 剧本要在 {play.Card?.Id.Entry} 前喝 {front.Potion.Name}，但栏位里没有 → 跳过");
                }
            }

            if (play.Card is MegaCrit.Sts2.Core.Models.Cards.Alchemize
                && PlannedBrews.Count > 0 && Shadow != null)
            {
                BossPotions.PendingBrew[Shadow] = PlannedBrews.Dequeue();
            }
        }
        catch { /* 忽略 */ }

        // 「鞭打」（Scourge）会让牌抽 1~2 张。原版抽的是 DrawPile.Cards[0]，但**牌堆空时
        // ShuffleIfNecessary 会随机洗牌**，提前排好的顺序会被打乱（用户实测：第 12 回合抽到荒疫打击、
        // 第 17 回合抽到碎骨）。所以在真正打出鞭打之前，现场保证"牌堆顶是打击"。
        bool isScourge = false;
        try { isScourge = play.Card.Id.Entry == "SCOURGE"; } catch { }
        if (isScourge)
        {
            int drawCount = 1;
            try
            {
                if (play.Card.DynamicVars.TryGetValue("Cards", out var cardsVar) && cardsVar != null)
                    drawCount = Math.Max(1, (int)cardsVar.BaseValue);
            }
            catch { /* 用 1 */ }
            Core.BossScriptRunner.EnsureStrikesOnTop(pcs, drawCount);
        }

        // 「能量汲取」的升级是**固定顺序**（用户设计），而原版是随机 TakeRandom。
        // 做法：打之前把它的 Cards 变量临时置 0（原版就不升了），打完恢复并由我们按表升级。
        bool isDrainPower = false;
        decimal savedDrainCards = 0m;
        MegaCrit.Sts2.Core.Localization.DynamicVars.DynamicVar? drainCardsVar = null;
        try
        {
            isDrainPower = play.Card.Id.Entry == "DRAIN_POWER";
            if (isDrainPower && play.Card.DynamicVars.TryGetValue("Cards", out drainCardsVar)
                && drainCardsVar != null)
            {
                savedDrainCards = drainCardsVar.BaseValue;
                drainCardsVar.BaseValue = 0m;
            }
        }
        catch { /* 忽略 */ }

        // 每次出牌前把「收窄选靶」的残留状态清干净（用户 2026-10-29 四人局实测：
        // 群蛇形态那套全局收窄会串到别人的牌上，播种因此只打了一个人）。
        try { CharacterBosses.Patches.HittableEnemiesSidePatch.ResetNarrowing(); } catch { /* 忽略 */ }

        // ★ v0.2.519（华丽猎「华丽收场」）：原版这张牌要求**抽牌堆为空**才能打出，而我们的剧本手牌
        //   是每回合新建的、抽牌堆里还留着开局灌进去的整副卡组 ⇒ 剧本标记了 `EmptyDrawPileFirst` 时
        //   先把她自己的抽牌堆清空（都进弃牌堆），让这张牌真的打得出去。
        try
        {
            if (ScriptCardWantsEmptyDrawPile(play.Card)) EmptyOwnDrawPile();
        }
        catch (Exception e) { Logger.Log("[CharBoss] 清空抽牌堆（华丽收场）失败: " + e.Message); }

        Logger.Log($"[CharBoss] PLAY {play.Card.Id.Entry} (cost {play.Cost}) self={GetType().Name}(side={SafeSide(Creature)}) -> {DescribePlayer(target)}" +
                   $"（hp={target.CurrentHp} side={SafeSide(target)}）");

        // 出牌动画（用户 2026-11-08：「卡牌没有正确的打出动画」）：把这一格从手牌栏里摘出来飞向目标。
        // 只能在**真的出手这一刻**做 —— 回合结束时 Plan 已清空，Rebuild 那时只能看到「牌少了一张」。
        try { CharacterBosses.UI.BossHandUi.PanelFor(this)?.PlayCardFx(play.Card, target); }
        catch (Exception e) { Logger.Log("[CharBoss] 出牌动画调用失败: " + e.Message); }

        // 群攻牌诊断（用户 2026-10-29：「灾厄骨的播种不群攻」）：
        // 把「这张牌打出那一刻，`HittableEnemies` 到底解析成谁」打出来 ——
        // 我们的影子 Player 在敌方阵营，靠 `HittableEnemiesSidePatch` 在出牌期间翻到玩家侧；
        // 若这里只有 1 个目标（或 0 个），就是翻侧/收窄那条路出问题了，看数字就能定位。
        try
        {
            if (play.Card.TargetType == MegaCrit.Sts2.Core.Entities.Cards.TargetType.AllEnemies)
            {
                var combat = CombatState;
                var hittable = combat?.HittableEnemies;
                var names = new List<string>();
                if (hittable != null)
                {
                    foreach (var c in hittable)
                    {
                        if (c == null) continue;
                        names.Add($"{c.Name}({SafeSide(c)})");
                    }
                }
                Logger.Log($"[CharBoss] 群攻牌 {play.Card.Id.Entry}：HittableEnemies 解析到 {names.Count} 个目标" +
                           $" [{string.Join(" | ", names)}]（翻侧深度={Patches.HittableEnemiesSidePatch.BossActingDepth}" +
                           $"，收窄深度={Patches.HittableEnemiesSidePatch.NarrowToTargetDepth}）");
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 群攻牌诊断失败: " + e.Message); }

        // 「精确切击」的伤害 = CalculationBase − ExtraDamage × 手牌里的**其他**牌数（反编译，见
        // BossDamagePredictor.PreciseCutDamage）。这里记下**出手时的真实手牌数**，
        // 和意图日志里的 `+手N` 一对照就能确认预测对不对（用户 2026-10-26 两次报「预测 18/16、实打 20」）。
        try
        {
            if (play.Card.Id.Entry == "PRECISE_CUT")
            {
                int handNow = pcs.Hand.Cards.Count;
                Logger.Log($"[CharBoss] 精确切击：出手时真实手牌 {handNow} 张（其他 {Math.Max(0, handNow - 1)} 张）" +
                           $"—— 应等于意图日志里的 +手N");
            }
        }
        catch { /* 诊断而已 */ }
        // 加超时保护：和球相关的牌（引导/激发）内部会 await 球的动画/音效，
        // 一旦某个资源没加载完就可能永远不返回，表现为「牌卡在空中、整回合卡死」。
        // 超时后跳过这张牌继续，至少不锁死战斗。
        //
        // ⚠ 出牌期间才把 `HittableEnemies` 翻成「玩家侧」（用户 2026-10-24 报的两个 bug 就在这条边界上）：
        //   以前判据是「当前阵营 == 敌方」，于是**玩家自己的**延迟效果在她的回合结算时也被翻
        //   （亡灵精通的奥斯提反伤、崩坠「灼魂」引爆都会打到玩家自己）。
        //   现在只在她真的打这张牌的这段时间翻（计数跨越 await，覆盖整个 AutoPlay 生命周期）。
        Patches.HittableEnemiesSidePatch.BossActingDepth++;
        // 斗蛐蛐红队：她自己就在玩家侧 ⇒ 不能把「可命中敌人」翻成玩家侧，否则打自己（见该字段注释）
        bool playerSideBoss = SafeSide(Creature) == "Player";
        if (playerSideBoss) Patches.HittableEnemiesSidePatch.PlayerSideBossDepth++;
        // 冰寒（Chill）不需要额外处理：用户 2026-10-24 订正为「只检测玩家数量」，
        // 而上面那条翻侧规则本来就把「可命中敌人」解释成**真人玩家**（宠物/召唤物不算），
        // 于是原版 `Chill` 自然就是「每有一名玩家 → 生成 1 个冰霜球」。以前那套
        // 「玩家方单位数（排除佩尔士兵/异鸟宝宝/崩坠史莱姆）」的临时清单已经删掉。
        // ⚠ 「一张牌打几次」**不要自己实现**：原版 `CardModel.OnPlayWrapper` 里已经有这条循环 ——
        // <code>
        // GeneratePlayCount: playCount = GetEnchantedReplayCount() + 1
        //                    → Hook.ModifyCardPlayCount(...)          // 爆发 / 复制药水 在这里 +1
        //                    → Hook.AfterModifyingCardPlayCount(...)  // 并在这里消耗层数
        // OnPlayWrapper:     for (i = 0; i < playCount; i++) { CurrentPlayIndex = i; 打一次 }
        // </code>
        // 而 `CardCmd.AutoPlay` 就是调 `OnPlayWrapper` ⇒ **走 AutoPlay 也会照常打两遍**。
        // 我曾在 v0.2.13~v0.2.19 里手动补了一轮重放（当时误以为 AutoPlay 绕过这条钩子，
        // 因为 `CardCmd` 里搜不到 `ModifyCardPlayCount`），结果是**残影被打出了 3 次**
        // （原版 2 次 + 我补的 1 次，用户 2026-10-26 实测）。现在整段删除，完全交回原版。
        // 意图上那个「4×2」只是显示（见 Core/BladesGrowth 的 playCount），不影响结算。
        Task playTask;
        // 「原始力量」：巨石造成**未被格挡**伤害时该玩家抽 1 张（用出牌前后血量差判定，不依赖伤害钩子）
        var rockHpBefore = SafeEntryOf(play.Card) == "GIANT_ROCK" ? SnapshotRealPlayerHp() : null;
        // 「抽牌卡」的手牌快照（只有声明了 `KeepsScriptedHand` 的 Boss 才有意义）：
        // 一层「奇巧猎」的杂技 / 早有准备 / 后空翻 / 本能反应**必须真的抽牌**
        // —— 速行者（`SpeedsterPower`）就是靠「每抽 1 张 → 对所有敌人 2 点」结算的，
        // 拦掉抽牌就等于把她的群伤也拦掉了。抽完再把这几张放回抽牌堆顶，手牌仍然等于剧本。
        List<MegaCrit.Sts2.Core.Models.CardModel>? handBeforePlay = null;
        if (Def.KeepsScriptedHandForAct(ActIndex) && Shadow != null)
        {
            try { handBeforePlay = BossPotions.SnapshotHandForCards(Shadow); }
            catch { handBeforePlay = null; }
        }
        // 「对所有敌人 / 所有友方」的牌：**整段豁免「只打锁定玩家」的收窄**。
        // 为什么（用户 2026-10-29：播种 Sow 依旧只打一个人）：原版群攻牌的打击列表和
        // 随机单体牌用的是同一个来源 —— `AttackCommand.GetPossibleTargets()` 在多目标时返回
        // `CombatState.GetOpponentsOf(Attacker)`，而我们在那上面做了「只留锁定玩家」的收窄。
        bool multiTargetCard = false;
        try
        {
            var tt = play.Card.TargetType;
            multiTargetCard = tt == MegaCrit.Sts2.Core.Entities.Cards.TargetType.AllEnemies
                              || tt == MegaCrit.Sts2.Core.Entities.Cards.TargetType.AllAllies;
        }
        catch { /* 忽略：读不到就按单目标处理 */ }
        if (multiTargetCard) Patches.HittableEnemiesSidePatch.MultiTargetCardDepth++;
        try
        {
            // 「战斗专注」这类牌：用户要求**拦掉它自己的抽牌**（手牌由剧本给，抽进来的会打乱剧本）
            try
            {
                string playingEntry = play.Card?.Id.Entry ?? string.Empty;
                // 用户 2026-10-28：**祭品**的抽牌也要拦掉（巨石战的剧本手牌写死，抽牌只会打乱它）
                if (playingEntry == "BATTLE_TRANCE" || playingEntry == "OFFERING") DrawBlockEntry = playingEntry;
                // 正在打哪张牌（「切断」的印牌要靠它拦，见 BlocksGeneratedCardsEntries）
                CurrentPlayingEntry = playingEntry;
            }
            catch { /* 忽略 */ }

            // 「鬼祟」（`SneakyPower`：**每当其他玩家打出攻击牌** → 获得 N 点格挡）：
            // 用户 2026-10-29 实测「真人玩家打出攻击牌会触发，其它 Boss 打出攻击牌不会」——
            // 她这份能力的触发靠原版 `Hook.AfterCardPlayed` 扇出，而我们 Boss 的牌是
            // `CardCmd.AutoPlay` 打出来的、所在生物又不在 `CombatState.Players` 里，扇出会漏掉。
            // 这里在出牌前后各拍一次「持有者格挡」，**原版没给就补上**（给了就不动，避免双份）。
            var sneakyBefore = Patches.SneakyOnBossAttack.Snapshot(this, play.Card, out var sneakyAmount);

            playTask = CardCmd.AutoPlay(ctx, play.Card, target, AutoPlayType.Default);
            // ★ v0.2.450（联机不同步取证）：出牌前后各记一次「翻侧决策的全部输入」。
            //   这套记账（深度计数 + 真人出牌登记 + 15 秒过期）是纯本机的，一旦两端不同，
            //   同一张群攻牌就会一端打玩家、另一端打 Boss。两端 mod_log 逐字一比即可确认。
            try
            {
                Logger.Log($"[CharBoss] 翻侧状态（出牌前 {SafeEntryOf(play.Card)}）：" +
                           Patches.HittableEnemiesSidePatch.SideFlipState());
            }
            catch { /* 忽略 */ }
            var finished = await Task.WhenAny(playTask, Task.Delay(5000));
            if (!ReferenceEquals(finished, playTask))
            {
                Logger.Log($"[CharBoss] PLAY {play.Card.Id.Entry} 超时(5s)，跳过继续");
            }

            try { await Patches.SneakyOnBossAttack.EnsureGrantedAsync(this, play.Card, sneakyBefore, sneakyAmount); }
            catch { /* 忽略：绝不影响出牌 */ }

            // 原版出牌队列会把「怪物打出的牌」当成远程玩家出牌挂一张卡面在屏幕中上方，且没人收走
            // ⇒ 打完之后立刻扫一遍把它藏掉（慢车道 4Hz 只是兜底，见 StrayBossCardQueuePatch）。
            try { Patches.StrayBossCardQueuePatch.Reap(); } catch { /* 忽略 */ }
        }
        finally
        {
            Patches.HittableEnemiesSidePatch.BossActingDepth--;
            if (playerSideBoss && Patches.HittableEnemiesSidePatch.PlayerSideBossDepth > 0)
                Patches.HittableEnemiesSidePatch.PlayerSideBossDepth--;
            if (multiTargetCard && Patches.HittableEnemiesSidePatch.MultiTargetCardDepth > 0)
                Patches.HittableEnemiesSidePatch.MultiTargetCardDepth--;
            DrawBlockEntry = null;      // 出牌结束就撤掉（只影响这一次出牌里的抽牌）
            CurrentPlayingEntry = null; // 出牌结束就撤掉（印牌拦截只看这一次出牌）
            // 「原始力量」的第二半：巨石的真伤落定后，被砸的玩家各抽 1 张
            try
            {
                if (rockHpBefore != null)
                {
                    foreach (var kv in rockHpBefore)
                    {
                        var victim = kv.Key;
                        int lost = kv.Value - (victim?.CurrentHp ?? kv.Value);
                        if (lost <= 0) continue;
                        if (victim?.Player == null) continue;
                        _ = MegaCrit.Sts2.Core.Commands.CardCmd.DiscardAndDraw(
                                new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(),
                                new System.Collections.Generic.List<MegaCrit.Sts2.Core.Models.CardModel>(), 1);
                        Logger.Log($"[CharBoss] 原始力量：巨石对 {victim.Name} 造成 {lost} 点未被格挡伤害 → 该玩家抽 1 张牌");
                    }
                }
            }
            catch (Exception ex) { Logger.Log("[CharBoss] 原始力量抽牌失败: " + ex.Message); }
            if (handBeforePlay != null && Shadow != null)
            {
                try { BossPotions.KeepScriptedHand(Shadow, handBeforePlay, $"{play.Card.Id.Entry} "); }
                catch (Exception e) { Logger.Log("[CharBoss] 把抽到的牌放回牌堆失败: " + e.Message); }
            }
        }
        // 「眼部攻击」buff 的结算：**牌已经打完**（AutoPlay 已返回）之后才补 1 层虚弱。
        // ⚠ 不能放在 `AfterCardPlayed` 钩子里 await —— 那样牌会停在半空（用户 2026-10-27 实测）。
        try
        {
            if (SafeEntryOf(play.Card) == "GO_FOR_THE_EYES")
            {
                var aim = Creature?.GetPower<Powers.GoForTheEyesAimPower>();
                if (aim != null && aim.PlayerAttackHit && target != null && target.IsAlive)
                {
                    await MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<
                        MegaCrit.Sts2.Core.Models.Powers.WeakPower>(ctx, target, 1m, Creature, play.Card);
                    Logger.Log("[CharBoss] 眼部攻击 buff：玩家这回合用攻击牌打到了她 → " +
                               $"她这记眼部攻击补上 1 层虚弱（目标 {target.Name}）");
                }
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 眼部攻击 buff 结算失败: " + e.Message); }

        // ★ v0.2.338（用户 2026-11-03 的三层「事件机」）：两个「剧本点名」的效果要在**牌打完之后**补结算
        //   —— 它们都要动她的手牌/抽牌堆，而 `CardCmd.AutoPlay` 已经返回、原版那半已经被拦掉了：
        //   · 「万物一心」：原版 Filter 被关（BossAllForOneFilterPatch）⇒ 按剧本名单搬回 0 费攻击牌，
        //     并且每搬回一张就给**所有单位** +1 力量（特殊效果「一心化万」）；
        //   · 「羽化」：原版随机印牌被拦 ⇒ 把剧本点名的 3 张 0 费攻击牌印进抽牌堆，并摆出来给玩家看一眼。
        // 「一心化万」：这一手牌（全息影像这类）可能把牌从弃牌堆捞回手牌 ⇒ 每张给她/玩家 +1 力量。
        // 记账在 BossDiscardToHandStrengthPatch（它只 ++），这里在**同一个 await 链**里结算，
        // 不再从补丁里另起异步链（那会让她的回合卡住，用户 2026-11-03 报的「没法结束回合」）。
        try
        {
            int pendingReturn = PendingReturnStrength;
            // ★ 2026-11-05（用户实测：一层「电球机」打全息影像也给所有单位加力量）：
            //   这段结算属于**事件机专属**的特殊效果「一心化万」，以前却无条件执行 ⇒
            //   任何 Boss 只要有一张牌把弃牌堆的牌捞回手牌（电球机第 5 回合的全息影像）都会误触发。
            //   现在必须她身上真的有 `BossAllForOnePower` 才结算；没有就把池子清零。
            bool hasOneForAll = false;
            try { hasOneForAll = Creature?.GetPower<Powers.BossAllForOnePower>() != null; } catch { hasOneForAll = false; }
            if (!hasOneForAll) PendingReturnStrength = 0;
            if (pendingReturn > 0 && hasOneForAll)
            {
                PendingReturnStrength = 0;
                await Core.EventMachineMechanics.GrantStrengthToAllUnitsAsync(this, ctx, play.Card, pendingReturn);
                Logger.Log($"[CharBoss] 一心化万：本手共 {pendingReturn} 张从弃牌堆回到手牌 ⇒ 所有单位各 +{pendingReturn} 力量");
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 一心化万（弃牌堆→手牌）结算失败: " + e.Message); }

        try
        {
            string specialEntry = SafeEntryOf(play.Card);
            if (specialEntry == "ALL_FOR_ONE")
                await Core.EventMachineMechanics.AllForOneReturnsAsync(this, ctx, play.Card);
            else if (specialEntry == "METAMORPHOSIS")
                await Core.EventMachineMechanics.MetamorphosisPrintsAsync(this, ctx, play.Card);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 事件机特殊结算失败: " + e); }

        // AutoPlay 既不扣能量也不扣星星，都得自己记账（扣完再由 PlayerCombatState 广播事件）
        // ★ 2026-11-06（用户：「凡是自动打出的牌都不应该被『预借时间』计算，它说的是『花费 10 能量』」）：
        //   判据从「在消耗牌堆里」推广成「**不是从手牌打出的**」—— 消耗牌堆复燃的「轰击」、
        //   从抽牌堆/弃牌堆被自动打出的牌全都不花她的能量 ⇒ 不扣能量、也不往战斗历史里记
        //   `EnergySpentEntry`（骨妹的「预借时间」正是读这条历史，之前把轰击也算进去了）。
        bool autoPlayedFromPile = false;
        try
        {
            var pileObj = play.Card?.Pile;
            var pileType = pileObj?.Type ?? MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand;
            autoPlayedFromPile = pileType != MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand
                                 && pileType != MegaCrit.Sts2.Core.Entities.Cards.PileType.Play;
        }
        catch { autoPlayedFromPile = false; }
        bool fromExhaust = autoPlayedFromPile;   // 变量名沿用，语义已推广
        if (!fromExhaust && play.Cost > 0) pcs.LoseEnergy(play.Cost);
        // ⚠ 还要往**战斗历史**里补一条 `EnergySpentEntry`：原版是玩家真的付钱时记的，
        //   而我们走 `CardCmd.AutoPlay` 从不经过那条路径。读这条历史的牌会因此算错 ——
        //   实测：螺旋钻击（`HelixDrill`）的命中数 = 本回合已花费的能量之和 ⇒ 少了这条就恒为 0 段，
        //   打出去一点伤害都没有（用户 2026-10-24：「螺旋钻机没有正确打出，并且没有被视为多段」）。
        if (!fromExhaust && play.Cost > 0)   // ★ 消耗牌堆免费复燃打出的牌不算「花了能量」
        {
            try
            {
                MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.History?
                    .EnergySpent(CombatState, play.Cost, Shadow);
            }
            catch (Exception e) { Logger.Log("[CharBoss] 记录花费能量失败: " + e.Message); }
        }
        if (play.StarCost > 0)
        {
            pcs.LoseStars(play.StarCost);
            // 黑洞：**花星这一刻**也要对真人玩家结算（原版 AfterCardPlayed 那条对影子玩家会按
            // HittableEnemies 解析成敌方阵营 ⇒ 打到自己/友方，玩家这边看不到伤害；用户 2026-10-28 实测）
            try
            {
                int bhSpend = 0;
                try { bhSpend = Creature?.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.BlackHolePower>() ?? 0; }
                catch { bhSpend = 0; }
                if (bhSpend > 0)
                {
                    var bhVictims = new System.Collections.Generic.List<MegaCrit.Sts2.Core.Entities.Creatures.Creature>();
                    foreach (var real in BossLockOn.RealPlayers(CombatState))
                    {
                        if (real?.Creature == null || !real.Creature.IsAlive) continue;
                        bhVictims.Add(real.Creature);
                    }
                    if (bhVictims.Count > 0)
                    {
                        await MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(
                            ctx ?? new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(),
                            bhVictims, bhSpend, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, Creature);
                        Logger.Log($"[CharBoss] 黑洞：花星 {play.StarCost} → 对 {bhVictims.Count} 名玩家各 {bhSpend} 点（无来源）");
                    }
                }
            }
            catch { /* 忽略 */ }

            // 「迷你储君」(MiniRegent)：**每回合第一次花费辉星时 +1 力量**。
            // 原版靠 <c>Hook.AfterStarsSpent(combat, amount, spender)</c> 派发，而影子 Player
            // 不在 CombatState.Players 里 ⇒ 这条事件永远不落到他身上（用户实测：迷你储君不给力量）。
            // 这里手动替影子玩家广播一次：MiniRegent.AfterStarsSpent 内部有
            // `spender == Owner && !UsedThisTurn` 守卫，所以只会给这一件的持有人加、且每回合只加一次
            //（UsedThisTurn 由 MiniRegent.BeforeSideTurnStart 每回合重置）。
            try { await MegaCrit.Sts2.Core.Hooks.Hook.AfterStarsSpent(CombatState, play.StarCost, Shadow); }
            catch (Exception e) { Logger.Log("[CharBoss] 花费辉星事件失败: " + e.Message); }
        }

        // 这一回合已打出的攻击牌张数（小刀也是攻击牌）—— 终结技的段数靠它，
        // 不然出牌期重算会把"已经打掉的那些"算丢，显示成 9×1（用户实测：段数不对、实际伤害对）。
        try { if (play.Card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack) AttacksPlayed++; }
        catch { /* 忽略 */ }

        // 「复制药水 / 爆发」的**重复打出由原版自己完成**（见上面那段注释），这里不再做任何重放。

        // 爪击（Claw）：原版把「本场所有爪击 +Increase 伤害」记在**每张爪击卡实例**上
        // （反编译 `Claw.OnPlay`：`foreach (AllCards.OfType<Claw>()) c.BuffFromClawPlay(Increase)`），
        // 而我们的手牌每回合都是**新建**的卡实例 ⇒ 成长每回合清零
        // （用户 2026-10-24：「爪击的伤害似乎没有正常增加」）。
        // 这里把成长记在 Boss 身上，新建手牌时一次性补上（见 `ClawBonus` 与 BossScriptRunner.BuildHand）。
        try
        {
            if (play.Card.Id.Entry == "CLAW")
            {
                int inc = 0;
                try { inc = (int)Math.Round((double)play.Card.DynamicVars["Increase"].BaseValue); }
                catch { inc = 2; }
                ClawBonus += Math.Max(0, inc);
                Logger.Log($"[CharBoss] 爪击成长：+{inc} ⇒ 本场累计 +{ClawBonus}（新造的爪击会带上这个数）");
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 记录爪击成长失败: " + e.Message); }

        // 「一心化万」：原版把它挂在**所有玩家**身上，她打出来时该挂给**所有 Boss**
        //（用户 2026-10-24：「物理机多人时的一心化万会给所有玩家而非所有boss提供效果」）。
        try
        {
            // ★ 2026-11-05：**万物一心用的是 1 代老卡 `AllForOne`**（两版都有）；
//   `OneForAll` 是 111 才新增的另一张牌，只在 111 上才需要做「把 power 从玩家改挂到 Boss」这一步。
            if (play.Card.Id.Entry == "ONE_FOR_ALL") await RetargetOneForAll(ctx, play.Card);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 一心化万改挂失败: " + e.Message); }
        // 灵魂风暴的伤害 = 消耗牌堆里每张灵魂 +4（反编译确认 `SoulStorm` 的 multiplier 数 `ExhaustPile` 里的 `Soul`），
        // 而「灵魂」带 `CardKeyword.Exhaust` ⇒ 她本回合打出几张灵魂，消耗牌堆就多几张。
        // 牌已经结算完（上面 await 返回）⇒ 这时候撤预补是安全的：真实值已经涨了，「增加多少撤多少」。
        try
        {
            if (play.Card.Id.Entry == "SOUL")
            {
                SoulsPlayedThisTurn++;
                WithdrawSoulCompensation(1);
            }
        }
        catch { /* 忽略 */ }
        // 「烙印」这类自己加力量的牌：意图里已把这回合**将要**拿到的力量提前补进攻击预览了，
        // 这里真的到账后就把预补撤掉（用户 2026-10-23：加力量后要撤回来）。
        try
        {
            int strGain = BossDamagePredictor.StrengthGainOfPlanned(play.Card, this);
            // 这张牌自己掉血 ⇒ 「撕裂」也已经真的给了力量（用户 2026-10-28：预览要算上这笔，
            // 真的到账后同样要撤回，否则后面的意图会一路叠加）。
            int ruptureGain = 0;
            if (BossDamagePredictor.LosesHp(play.Card))
            {
                try { ruptureGain = Math.Max(0, Creature?.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.RupturePower>() ?? 0); }
                catch { ruptureGain = 0; }
            }
            strGain += ruptureGain;
            if (strGain > 0)
            {
                PlannedStrengthGainedThisTurn += strGain;
                Logger.Log($"[CharBoss] {play.Card.Id.Entry} 给了 {strGain} 力量" +
                           (ruptureGain > 0 ? $"（其中撕裂 {ruptureGain}）" : string.Empty) + " → " +
                           $"撤掉意图里预补的同量（本回合已到账 {PlannedStrengthGainedThisTurn}）");
            }
        }
        catch { /* 忽略 */ }
        // 「出击」的伤害吃奥斯提生命：这回合真的加了多少生命要记下来，
        // 并把意图里提前补的那份**按同样的量**撤掉（召唤牌 + 灵魂×吞噬生命）。
        try
        {
            int hpGain = BossDamagePredictor.OstyHpGainOf(play.Card, this);
            if (hpGain > 0)
            {
                OstyHpGainedThisTurn += hpGain;
                WithdrawOstyCompensation(hpGain);
            }
        }
        catch { /* 忽略 */ }
        // 「充电」：下回合 +1 能量（原版那套能量重置对影子玩家不生效，自己记账）
        if (IsChargeBattery(play.Card)) NextTurnEnergyBonus += 1;
                    // 「祭品」这类**立刻回能**的牌：影子玩家的能量原版不加（只加到真 Player 上），
            // 规划期却按牌面 Energy 记了账 ⇒ 出现「意图里有、实际没费打不出」（用户 2026-10-28 实测）。
            // 这里在出牌结束后把牌面的 Energy 变量真实补进 pcs.Energy（下回合类由 PendingNextTurnEnergyCredit 负责）。
            try
            {
                decimal gainE = 0m;
                try { if (play.Card.DynamicVars.TryGetValue("Energy", out var ev) && ev != null) gainE = ev.BaseValue; }
                catch { gainE = 0m; }
                int addE = (int)Math.Round((double)gainE);
                if (addE > 0 && !Core.BossScriptPlanner.DeferredEnergyCard(play.Card))
                {
                    pcs.Energy += addE;
                    Logger.Log($"[CharBoss] {play.Card.Id.Entry} 回能 +{addE} → 本回合能量 {pcs.Energy}");
                }
            }
            catch { /* 忽略 */ }

        Logger.Log($"[CharBoss] done {play.Card.Id.Entry}: bossHp={Creature.CurrentHp} " +
                   $"block={Creature.Block} energy={pcs.Energy} stars={pcs.Stars} " +
                   $"orbs=[{CharacterBosses.UI.BossOrbDisplay.DescribeOrbs(this)}]");

        // 「彗星」：她生成的碎屑要改判给玩家的弃牌堆。
        // 必须在**牌真的结算完**之后搬（生成发生在牌的效果里），而且要 await —— 异步并发搬牌会把
        // 牌堆状态搞坏（用户实测：之后玩家洗不了牌）。
        await CharacterBosses.Patches.CometDebrisRedirect.RedirectDebrisToPlayers(this);
        // 特殊效果「偏差认知」：每打一张牌就对账一次（暴涨 +2 力量 → 集中也 +2）
        await Powers.DeviationPower.ReconcileFor(this);
        // 打出去的牌要从计划里摘掉：球的「待激发」高亮是用计划重新模拟出来的，
        // 留着已打出的牌会让它去激发「下一颗」球，于是那颗球被误标成待激发（用户实测）。
        Plan.Remove(play);

        // 打出去的刀必须离开手牌（AutoPlay 之后它可能还躺在手牌数据里）
        if (ShivPlays.IsShiv(play.Card))
        {
            try
            {
                if (pcs.Hand.Cards.Any(c => ReferenceEquals(c, play.Card)))
                {
                    pcs.Hand.RemoveInternal(play.Card, true);
                    pcs.DiscardPile.AddInternal(play.Card, -1, true);
                }
            }
            catch (Exception e) { Logger.Log("[CharBoss] 小刀移出手牌失败: " + e.Message); }
        }

        // 「能量汲取」：恢复它的升级数量，然后按**固定表**升级（不是随机），并把升到的牌展示出来。
        // 只展示、不写入任何牌堆（用户要求：别塞进玩家弃牌堆）。
        if (isDrainPower)
        {
            try { if (drainCardsVar != null) drainCardsVar.BaseValue = savedDrainCards; } catch { }

            int want = (int)Math.Max(0m, savedDrainCards);
            var upgraded = UpgradeDrainTargets(pcs, want);

            // 展示交给**原版「怪物往你牌堆里塞牌」那套**（BiiigHug 塞煤灰）：
            //   `CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Discard, 本地玩家)`
            //   → 原版把牌居中展示、再飞进**玩家弃牌堆**；随后 `CardCmd.Preview` 补一次居中展示。
            // ⚠ 用户要求：**她的牌不能留在玩家弃牌堆** ⇒ 塞进去的是「归本地玩家的副本」，
            //   展示完立刻从弃牌堆里静默摘掉，玩家自己的牌堆内容一件不多。
            // 展示：**原版 `CardCmd.Preview(多张, …)`** —— 牌会并排一起出现（用户要求"两张一起"）。
            //   · 副本仍是 `CombatState.CreateCard(canonical, 本地玩家)`（`ToMutable()` 的孤儿模型会被原版拒绝，
            //     而 `CreateCard` 已设好 Owner，**不要再写 copy.Owner**）；
            //   · 原版要求 `card.Pile != null` 才肯展示 ⇒ 先**静默**塞进本地玩家弃牌堆，展示完再 `RemoveFromCombat`；
            //   · 「飞进弃牌堆」那段动画用 `SuppressPreviewFlyPatch` 掐掉（副本登记过就不生成 NCardFlyVfx，
            //     牌展示完原地消失）——「飘向 Boss」做不到，因为飞行目标是"牌主"，而牌主必须是本地玩家。
            try
            {
                var local = LocalRealPlayer();
                var pcsLocal = local?.PlayerCombatState;
                var combatState = local?.Creature?.CombatState;
                if (local == null || pcsLocal == null || combatState == null)
                {
                    Logger.Log("[CharBoss] 能量汲取：取不到本地真人玩家 ⇒ 无法展示（跳过）");
                }
                else
                {
                    var copies = new List<MegaCrit.Sts2.Core.Models.CardModel>();
                    foreach (var shown in upgraded)
                    {
                        MegaCrit.Sts2.Core.Models.CardModel? copy = null;
                        try
                        {
                            var canonical = MegaCrit.Sts2.Core.Models.ModelDb
                                .GetById<MegaCrit.Sts2.Core.Models.CardModel>(shown.Id);
                            copy = combatState.CreateCard(canonical, local);
                            if (copy == null) continue;

                            // 展示的是**升级后**的样子
                            try { if (!copy.IsUpgraded) copy.UpgradeInternal(); } catch { /* 忽略 */ }

                            pcsLocal.DiscardPile.AddInternal(copy, -1, true);   // 静默进堆（只为过原版的 Pile 检查）
                            Patches.SuppressPreviewFlyPatch.Suppress(copy);      // 掐掉飞行动画
                            copies.Add(copy);
                        }
                        catch (Exception e)
                        {
                            Logger.Log($"[CharBoss] 能量汲取：造展示副本失败（{SafeEntryOf(shown)}）: " + e);
                        }
                    }

                    if (copies.Count > 0)
                    {
                        MegaCrit.Sts2.Core.Commands.CardCmd.Preview(
                            copies, 1.4f, MegaCrit.Sts2.Core.Nodes.CommonUi.CardPreviewStyle.HorizontalLayout);
                        await Cmd.Wait(1.8f);
                        Logger.Log($"[CharBoss] 能量汲取：已展示 {copies.Count} 张升级牌（并排显示，无飞行动画）");
                    }

                    // 收尾：副本不留在玩家弃牌堆里（用户要求）
                    foreach (var copy in copies)
                    {
                        try
                        {
                            Patches.SuppressPreviewFlyPatch.Release(copy);
                            await MegaCrit.Sts2.Core.Commands.CardPileCmd.RemoveFromCombat(copy, true);
                        }
                        catch (Exception e)
                        {
                            Logger.Log("[CharBoss] 能量汲取：摘掉展示副本失败: " + e.Message);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 能量汲取：展示升级牌失败: " + e);
            }

            var names = new List<string>();
            foreach (var card in upgraded) names.Add(SafeEntryOf(card));
            Logger.Log($"[CharBoss] 能量汲取：按固定顺序升级了 {upgraded.Count} 张（{string.Join(",", names)}）");
        }

        // 打完一张牌：按**真实当前力量 + 已真实获得的成长**重算剩下每张牌的意图。
        // 手里剑每 3 张攻击 +1 力量是在 AutoPlay 里通过遗物钩子**异步**加的 ⇒ 先等一拍再读，
        // 否则读到的仍是旧力量，补正会多留 1 点（用户实测：触发手里剑后那张牌的意图还残留 +1）。
        await Cmd.CustomScaledWait(0.05f, 0.12f);

        // ★ v0.2.450（联机不同步）：**这张牌有没有消耗牌 ⇒「卡戎之灰」的 3 点群伤**必须在
        //   **这张牌真正打完之后的这个同步点**结算。以前它是 `Hook.AfterCardExhausted`（void 钩子）
        //   postfix 里的 `_ = RunCharonsAshesAsync(...)` —— fire-and-forget 的任务会**跨过**
        //   回合结束的校验和：房主赶在 `After enemy turn end` 之前落地、客人没有（玩家 2026-11-06 的
        //   诊断包里两名玩家恰好各差 7 点 = 速行者 4 + 卡戎之灰 3，见 docs/plans/desync-2026-11-06-review.md）。
        //   现在改成「钩子里只登记、这里 await 结算」，两端必然落在同一个代码点。
        try { await CharacterBosses.Patches.BossRelicHookBridge.FlushPendingAsync(this); }
        catch (Exception e) { Logger.Log("[CharBoss] 卡戎之灰结算失败（忽略）: " + e.Message); }

        BladesGrowth.RecomputeAll(this, Plan, planTime: false);

        CharacterBosses.UI.BossHandUi.RefreshForce(this);   // 意图重算过 → 整排重建
    }

    /// <summary>
    /// 「能量汲取」的**固定**升级顺序（用户设计，不是随机）。
    ///
    /// 用户 2026-10-24 改后的顺序：第 1 次 鞭打 + 死亡之门；第 2 次 死亡使者 + 播种；
    /// 第 3 次 荒疫打击 + 无处可逃（**进阶 9 跳过**，那时这两张本来就是升级版）；
    /// **第 4～7 次各升一对「打击+防御」**（卡组里正好 4 对）。
    /// 尖塔的牌只能升级一次 ⇒ 第 8 次起就没有可升的了，能量汲取什么也不升（不会重复升级）。
    /// </summary>
    private static readonly string[][] DrainUpgradeOrder =
    {
        // ★ 2026-11-07 用户订正（原顺序不对，而且漏了「出击 / 护卫」这一对）：
        //   鞭打+死亡之门 → 无处可逃+荒疫打击（进阶 9 及以上跳过，因为战斗开始就升过了）
        //   → 出击+护卫 → 播种+死亡使者 → 之后每次升级一对「打击+防御」。
        new[] { "SCOURGE", "DEATHS_DOOR" },                     // 第 1 次：鞭打、死亡之门
        new[] { "NO_ESCAPE", "BLIGHT_STRIKE" },                 // 第 2 次：无处可逃、荒疫打击（进阶 9 跳过）
        new[] { "UNLEASH", "BODYGUARD" },                       // 第 3 次：出击、护卫
        new[] { "SOW", "DEATHBRINGER" },                        // 第 4 次：播种、死亡使者
        new[] { "STRIKE_NECROBINDER", "DEFEND_NECROBINDER" },   // 第 5 次
        new[] { "STRIKE_NECROBINDER", "DEFEND_NECROBINDER" },   // 第 6 次
        new[] { "STRIKE_NECROBINDER", "DEFEND_NECROBINDER" },   // 第 7 次
        new[] { "STRIKE_NECROBINDER", "DEFEND_NECROBINDER" },   // 第 8 次
    };

    /// <summary>
    /// **第三幕（根除骨）**的「能量汲取」升级顺序（用户给的完整顺序，第 4 次之后不再升级）：
    /// 违逆+吸引仇恨 → 恐惧+戳击 → 根除+重压 → 出击。
    /// 这 7 张之外，第三幕卡组里的死亡之舞、友谊、钙化、洁净在进阶/剧本里本来就是升级版，
    /// 所以走完这 4 次就「除能量汲取外全升级完」。
    /// </summary>
    private static readonly string[][] DrainUpgradeOrderAct3 =
    {
        new[] { "DEFY", "PULL_AGGRO" },        // 第 1 次：违逆、吸引仇恨
        new[] { "FEAR", "POKE" },              // 第 2 次：恐惧、戳击
        new[] { "ERADICATE", "FLATTEN" },      // 第 3 次：根除、重压
        new[] { "UNLEASH" },                   // 第 4 次：出击
    };

    /// <summary>当前进阶下实际生效的升级顺序（进阶 9 去掉「无处可逃+荒疫打击」那一对）。</summary>
    private List<string[]> EffectiveDrainOrder()
    {
        // 第三幕（根除骨）用上面那张专用表 —— 她第一幕那张表里的牌（鞭打/死亡使者…）第三幕卡组完全没有，
        // 之前第三幕一张都匹配不上，能量汲取打完什么也不显示、也不升级（用户实测）。
        if (ActIndex + 1 >= 3)
        {
            var act3 = new List<string[]>(DrainUpgradeOrderAct3);

            // 多人换牌 A2：「吸引仇恨」被换成「骸骨军团」⇒ 第 1 组要升级的也换成骸骨军团
            //（用户明确要求：`{ "DEFY", "PULL_AGGRO" }` → `{ "DEFY", "LEGION_OF_BONE" }`）。
            // 单人局 IsSwapped 恒为 false，这一行不会改任何东西。
            if (CharacterBosses.Core.MultiplayerCardSwap.IsSwapped(
                    typeof(MegaCrit.Sts2.Core.Models.Cards.PullAggro), this))
            {
                act3[0] = new[] { "DEFY", "LEGION_OF_BONE" };
                Logger.Log("[CharBoss] 多人换牌：能量汲取第 1 组改为 违逆 + 骸骨军团");
            }
            return act3;
        }

        var list = new List<string[]>();
        // 「进阶 9 跳过无处可逃那一对」只对**第一幕（灾厄骨）**成立：
        // 用户 2026-10-24 把「厄运之衣 / 友谊 / 无处可逃 / 荒疫打击」的进阶升级从「所有幕」收窄成
        // 「只有第一幕」，所以第二幕（灵魂骨，用的也是这张表）那两张**没有**预升级，
        // 不能再跳 —— 否则它在进阶 9 会白白少升一对。
        bool skipNoEscapePair = AscensionLevel >= 9 && ActIndex + 1 <= 1;
        foreach (var pair in DrainUpgradeOrder)
        {
            if (skipNoEscapePair && Array.IndexOf(pair, "NO_ESCAPE") >= 0) continue;
            list.Add(pair);
        }
        return list;
    }

    /// <summary>这张能量汲取是第几次（决定升哪一对）。</summary>
    internal int DrainUpgradeStep;

    /// <summary>
    /// 「能量汲取」升级过的卡（按 <c>Id.Entry</c> 记）。手牌每回合是按剧本**重新造**的，
    /// 不记这个的话弃牌堆里已经是升级版、手里却又冒出未升级的原版（用户实测要求：之后的循环用升级版）。
    /// </summary>
    private readonly HashSet<string> _persistentlyUpgraded = new(StringComparer.Ordinal);

    /// <summary>把「已经被能量汲取升级过」的卡类型给回去（给 <see cref="Core.BossScriptRunner.BuildHand"/> 用）。</summary>
    private List<Type> PersistentlyUpgradedTypes()
    {
        var result = new List<Type>();
        if (_persistentlyUpgraded.Count == 0) return result;
        try
        {
            var deck = Def.DeckForAct(ActIndex);
            if (deck == null) return result;
            foreach (var type in deck)
            {
                try
                {
                    if (_persistentlyUpgraded.Contains(MegaCrit.Sts2.Core.Models.ModelDb.GetId(type).Entry))
                        result.Add(type);
                }
                catch { /* 忽略 */ }
            }
        }
        catch { /* 忽略 */ }
        return result;
    }

    /// <summary>按固定顺序升级她弃牌堆里的牌，返回真正被升级的那几张（用于展示）。</summary>
    private List<MegaCrit.Sts2.Core.Models.CardModel> UpgradeDrainTargets(PlayerCombatState pcs, int count)
    {
        var result = new List<MegaCrit.Sts2.Core.Models.CardModel>();
        try
        {
            int want = count > 0 ? count : 2;
            var order = EffectiveDrainOrder();
            // 顺序走完（第 8 次起）就什么都不升 —— 牌只能升级一次，没有"重复升级"这回事。
            if (DrainUpgradeStep >= order.Count)
            {
                Logger.Log($"[CharBoss] 能量汲取：升级顺序已走完（{order.Count} 次），这次不再升级卡牌");
                return result;
            }
            var pair = order[DrainUpgradeStep];
            DrainUpgradeStep++;

            var pile = pcs.DiscardPile.Cards;
            foreach (var entry in pair)
            {
                if (result.Count >= want) break;
                MegaCrit.Sts2.Core.Models.CardModel? target = null;
                foreach (var card in pile)
                {
                    try
                    {
                        if (card == null || card.IsUpgraded) continue;
                        if (card.Id.Entry != entry) continue;
                        target = card;
                        break;
                    }
                    catch { /* 忽略 */ }
                }
                if (target == null) continue;

                MegaCrit.Sts2.Core.Commands.CardCmd.Upgrade(
                    target, MegaCrit.Sts2.Core.Nodes.CommonUi.CardPreviewStyle.None);
                result.Add(target);
                try { _persistentlyUpgraded.Add(target.Id.Entry); } catch { /* 忽略 */ }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 能量汲取固定升级失败: " + e.Message);
        }
        return result;
    }

    /// <summary>
    /// 剧本点名「这一回合先洗牌」（状态机第 6 回合）：真洗一次她的抽牌堆
    /// ⇒ 触发她的「大～抱抱」(<c>BiiigHug</c>) 生成煤灰；再按用户要求**给每位真人玩家也塞 1 张煤灰**。
    /// </summary>
    private async Task DoScriptedShuffleAsync(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext ctx)
    {
        var turn = Def?.ScriptForAct(ActIndex)?.TurnAt(TurnNumber);
        if (turn?.ShuffleDrawPile != true) return;

        var shadow = Shadow;
        var pcs = shadow?.PlayerCombatState;
        if (shadow == null || pcs == null) return;

        try
        {
            await MegaCrit.Sts2.Core.Commands.CardPileCmd.Shuffle(ctx, shadow);
            Logger.Log("[CharBoss] 剧本洗牌：已洗她的抽牌堆（大抱抱会因此生成煤灰）");
            // ★ 群蛇形态（用户 2026-11-08）：她洗牌 ⇒ 自己抽牌堆 +1 张蛇咬（不放动画）
            try
            {
                if (Creature?.GetPower<Powers.BossSnakeFormPower>() != null)
                    Powers.BossSnakeFormPower.AddSnakebiteToHer(this);
            }
            catch (Exception e2) { Logger.Log("[CharBoss] 群蛇形态：洗牌加蛇咬失败: " + e2.Message); }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 剧本洗牌：洗牌失败 " + e.Message); }

        // ★ v0.2.474（玩家反馈：「状态机 1 次洗牌会塞 2 张煤灰」）：这里**不再手动塞煤灰**。
        //   洗牌会让「大～抱抱」(BiiigHug) 生成 1 张煤灰，而她的特殊效果「同步」
        //   (`Powers/BossSyncPower.AfterCardGeneratedForCombat`) 本来就会把**她生成的状态牌**复制一份
        //   给被锁定的玩家 ⇒ 手动再塞一张就是**重复实现**，于是玩家收到 2 张。
        //   「只给被锁定的人」这条规则也已经写在 BossSyncPower 里（`SpecialEffectAppliesTo`）。
    }
#if STS2_107
    /// <summary>
    /// 旧版（v0.107）烘焙手套补救：+1 力量 + 消耗剧本点名「被烧掉」的那张。
    ///
    /// 原因：v0.107 的 `ToastyMittens` 挂在 `BeforeHandDraw`（抽牌之前）—— 那一刻她的手牌还是空的，
    /// 原版因此既给不到她力量、也烧不掉牌。连带后果是「本该被烧掉的那张」留在手里，
    /// 被「生存者」的丢弃提示误选（用户实测：涂毒猎第 2 回合不打连续反弹）。
    /// 111 上由原版 `AfterPlayerTurnStart` 正常结算，所以这里只在 107 编译。
    /// </summary>
    private async Task ApplyToastyMittensForLegacy(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext ctx)
    {
        var shadow = Shadow;
        var pcs = shadow?.PlayerCombatState;
        if (shadow == null || pcs == null || Creature == null) return;

        bool has = false;
        decimal amount = 1m;
        try
        {
            foreach (var relic in shadow.Relics)
            {
                if (relic == null) continue;
                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry != "TOASTY_MITTENS") continue;
                has = true;
                try { amount = relic.DynamicVars["Strength"].BaseValue; } catch { /* 用 1 */ }
                break;
            }
        }
        catch { /* 忽略 */ }
        if (!has) return;

        if (amount > 0m)
        {
            await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>(
                ctx, Creature, amount, Creature, null);
            Logger.Log($"[CharBoss] 烘焙手套（旧版补）：+{amount:0} 力量（107 原版钩子在她手牌为空时不生效）");
        }

        // 剧本点名「被烧掉」的那张：先消耗掉，后面的丢弃点名就不会误选它
        try
        {
            var turnScript = Def?.ScriptForAct(ActIndex)?.TurnAt(TurnNumber);
            if (turnScript == null) return;
            foreach (var sc in turnScript.Cards)
            {
                if (!sc.Burned) continue;
                string want;
                try { want = ModelDb.GetId(sc.Card).Entry; } catch { continue; }

                MegaCrit.Sts2.Core.Models.CardModel? target = null;
                foreach (var c in pcs.Hand.Cards)
                {
                    try { if (c != null && c.Id.Entry == want) { target = c; break; } } catch { /* 忽略 */ }
                }
                if (target == null) continue;

                await MegaCrit.Sts2.Core.Commands.CardCmd.Exhaust(ctx, target);
                Logger.Log($"[CharBoss] 烘焙手套（旧版补）：烧掉剧本点名的 {want}");
                break;
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 烘焙手套（旧版补）烧牌失败: " + e.Message); }
    }
#endif

    /// <summary>日志用：卡牌条目名。</summary>
    private static string SafeEntryOf(MegaCrit.Sts2.Core.Models.CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; }
        catch { return "?"; }
    }

    /// <summary>
    /// 「一心化万」（<c>OneForAll</c>）：原版 `OnPlay` 对 **`CombatState.Players` 里的每一个玩家**
    /// 挂 `OneForAllPower`（卡面 `TargetType.AllAllies` = "所有友方"）。
    /// 但她打出这张牌时，"友方"是**我们这一侧的 Boss**，不是真人玩家
    /// （用户 2026-10-24：「物理机多人时的一心化万会给所有玩家而非所有boss提供效果」）。
    ///
    /// 做法：把刚挂到玩家身上的那层**撤掉**，改成挂给所有 Boss（她自己 + 同一侧的队友 Boss）。
    /// 反编译确认 `OneForAllPower.ModifyDamageAdditive` 要求 `cardSource.Owner.Creature == Owner`
    /// ⇒ 挂在谁身上，就只给谁的 0 费牌加伤。
    /// </summary>
    private async Task RetargetOneForAll(PlayerChoiceContext ctx, CardModel card)
    {
        var combat = CombatState;
        var me = Creature;
        if (combat == null || me == null) return;

        decimal amount = 3m;
        try { amount = card.DynamicVars["OneForAllPower"].BaseValue; } catch { /* 用 3 */ }
        if (amount <= 0m) return;

        // ① 撤掉原版挂在真人玩家身上的那一层
        var removed = 0;
        try
        {
            foreach (var player in combat.Players)
            {
                var creature = player?.Creature;
                if (creature == null) continue;
#if STS2_111
                var power = creature.GetPower<MegaCrit.Sts2.Core.Models.Powers.OneForAllPower>();
                if (power == null) continue;
                await PowerCmd.Remove(power);
                removed++;
#else
                // 旧版（v0.107）没有 OneForAllPower ⇒ 没有「一心化万」那层可撤
                _ = creature;
#endif
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 一心化万：撤掉玩家身上的那层失败: " + e.Message); }

        // ② 挂给所有 Boss（她自己 + 同侧队友 Boss）
        var targets = new List<Creature> { me };
        try
        {
            foreach (var c in combat.GetTeammatesOf(me))
            {
                if (c == null || !c.IsAlive) continue;
                if (c.Monster is not CharBossMonster) continue;
                if (!targets.Contains(c)) targets.Add(c);
            }
        }
        catch { /* 忽略：至少给自己 */ }

        var applied = 0;
        foreach (var target in targets)
        {
            try
            {
#if STS2_111
                await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.OneForAllPower>(
                    ctx, target, amount, me, card);
                applied++;
#else
                // 旧版（v0.107）没有 OneForAllPower ⇒ 这一条机制在这版上没有
                await Task.CompletedTask;
                _ = target;
#endif
            }
            catch (Exception e) { Logger.Log("[CharBoss] 一心化万：挂给 Boss 失败: " + e.Message); }
        }

        Logger.Log($"[CharBoss] 一心化万改挂：从 {removed} 名玩家身上撤掉 → 挂给 {applied} 只 Boss" +
                   $"（每张 0 费攻击牌 +{amount}）");
    }

    /// <summary>
    /// 第一回合「出完牌之后」说的那句话（<see cref="BossDefinition.FirstTurnLine"/>）。
    ///
    /// 用法来源：能力机（三层故障机器人）第一回合结束时说低语耳环那句「这样才对」。
    /// 原版低语耳环就是这么做的（反编译 <c>WhisperingEarring</c>）：
    /// <code>
    /// LocString line = new LocString("relics", "WHISPERING_EARRING.approval");
    /// TalkCmd.Play(line, Owner.Creature, VfxColor.Purple);
    /// </code>
    /// 文本本身在游戏本地化里是 <c>[red][shake]这样才对[/shake][/red]</c>，我们只负责把它说出来。
    ///
    /// ⚠ 这是**遗物台词**，所以必须她身上真有那件遗物才说。`BossDefinition` 是**按角色**共用的，
    /// 只看定义的话，同为故障机器人的二层「物理机」（没带低语耳环）也会说这句
    /// （用户 2026-10-24：「它为什么会说'这样才对'？这不是3层带了低语耳环的能力机才有的吗」）。
    /// </summary>
    private void SayFirstTurnLine()
    {
        if (TurnNumber != 1) return;
        var line = Def.FirstTurnLine;
        if (line == null) return;
        if (Creature == null || Creature.IsDead) return;

        string requiredRelic = RelicEntryOfLine(line.Value);
        if (requiredRelic.Length > 0 && !HasRelic(requiredRelic))
        {
            Logger.Log($"[CharBoss] 第一回合台词跳过：她身上没有 {requiredRelic}" +
                       $"（{line.Value.Table}:{line.Value.Key}）");
            return;
        }

        try
        {
            var loc = new LocString(line.Value.Table, line.Value.Key);
            MegaCrit.Sts2.Core.Commands.TalkCmd.Play(loc, Creature, MegaCrit.Sts2.Core.Nodes.Vfx.VfxColor.Purple);
            Logger.Log($"[CharBoss] 第一回合出完牌，说了一句：{line.Value.Table}:{line.Value.Key}");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 第一回合台词播放失败: " + e.Message);
        }
    }

    /// <summary>台词 key 里那件遗物的 entry（`WHISPERING_EARRING.approval` → `WHISPERING_EARRING`）。</summary>
    private static string RelicEntryOfLine((string Table, string Key) line)
    {
        try
        {
            if (line.Table != "relics") return string.Empty;      // 不是遗物台词 ⇒ 不设条件
            int dot = line.Key?.IndexOf('.') ?? -1;
            return dot > 0 ? line.Key!.Substring(0, dot) : string.Empty;
        }
        catch { return string.Empty; }
    }

    /// <summary>她身上有没有某件遗物（按模型 id 的 entry 判）。</summary>
    private bool HasRelic(string entry)
    {
        try
        {
            var relics = Shadow?.Relics;
            if (relics == null) return false;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                try { if (relic.Id.Entry == entry) return true; } catch { /* 忽略 */ }
            }
        }
        catch { /* 忽略 */ }
        return false;
    }


    /// <summary>本局的进阶等级（拿不到按 0 算）。</summary>
    internal int AscensionLevel
    {
        get
        {
            try
            {
                var run = Shadow?.RunState ?? CombatState?.RunState;
                if (run != null) return run.AscensionLevel;
            }
            catch { /* 忽略 */ }
            return 0;
        }
    }

    /// <summary>
    /// 当前进阶下需要额外升级的卡（来自 <see cref="BossDefinition.AscensionUpgrades"/>
    /// 与按幕的 <see cref="BossDefinition.AscensionUpgradesByAct"/>）。
    /// </summary>
    internal List<Type> AscensionUpgradeTypes()
    {
        var result = new List<Type>();
        int asc = AscensionLevel;
        foreach (var (minAsc, type) in Def.AscensionUpgrades)
        {
            if (asc >= minAsc) result.Add(type);
        }
        // 变体（如三层「巨石战」）自己的进阶升级表：接管时只看它，主定义那一幕的行不生效。
        var variantUpgrades = Def.VariantAscensionUpgrades(ActIndex);
        if (variantUpgrades != null)
        {
            foreach (var (minAsc, type) in variantUpgrades)
            {
                if (asc >= minAsc) result.Add(type);
            }
            return result;
        }
        int act = ActIndex + 1;   // 按幕的条目用 1 起幕号
        foreach (var (minAsc, byAct, type) in Def.AscensionUpgradesByAct)
        {
            if (asc < minAsc) continue;
            if (byAct != 0 && byAct != act) continue;
            result.Add(type);
        }
        return result;
    }


    private static string SafeSide(Creature? creature)
    {
        try { return creature?.Side.ToString() ?? "?"; }
        catch { return "?"; }
    }

    /// <summary>
    /// 日志里描述一名玩家：`<昵称|?>/<角色类名>/NetId=N`。
    ///
    /// 为什么需要（用户 2026-10-29 实测无法核对）：以前日志只打 `Creature.Name`，
    /// 而**远端玩家的名字在本机经常解析不出来**，三个人一起打印全是 `[unknown]`，
    /// 于是「储君打了谁、碎屑给了谁」在日志里完全分不清。加上 NetId 与角色类名才可区分。
    /// </summary>
    internal static string DescribePlayer(Creature? creature)
    {
        try
        {
            if (creature == null) return "?";
            string name = string.Empty;
            try { name = creature.Name ?? string.Empty; } catch { name = string.Empty; }
            name = name.Trim();
            if (name.Length == 0 || name == "[unknown]" || name == "unknown" || name == "__") name = "?";

            string who = "?";
            string netId = "?";
            try
            {
                if (creature.Player != null)
                {
                    who = creature.Player.Character?.GetType().Name ?? "?";
                    netId = creature.Player.NetId.ToString();     // NetId 是 ulong
                }
            }
            catch { /* 忽略 */ }

            return $"{name}/{who}/NetId={netId}";
        }
        catch { return "?"; }
    }

    /// <summary>这张牌是不是「需要一个对手当目标」（与 <see cref="BossPlanner.ChooseTarget"/> 同一判据）。</summary>
    private static bool WantsOpponent(MegaCrit.Sts2.Core.Models.CardModel? card)
    {
        try
        {
            if (card == null) return false;
            var t = card.TargetType;
            return t == MegaCrit.Sts2.Core.Entities.Cards.TargetType.AnyEnemy
                || t == MegaCrit.Sts2.Core.Entities.Cards.TargetType.AllEnemies
                || t == MegaCrit.Sts2.Core.Entities.Cards.TargetType.RandomEnemy;
        }
        catch { return false; }
    }

    /// <summary>
    /// 本地那位**真人**玩家（影子 Player 不算；拿不到返回 null）。
    /// 用途：能量汲取要「展示升级的牌」，而原版 `CardCmd.Preview` 只认本地玩家那一侧的牌 ——
    /// 她那几张属于影子 Player，直接预览界面上什么都不出现（用户实测）。
    /// </summary>
    private MegaCrit.Sts2.Core.Entities.Players.Player? LocalRealPlayer()
    {
        try
        {
            var combat = CombatState;
            if (combat == null) return null;
            foreach (var creature in combat.PlayerCreatures)
            {
                var player = creature?.Player;
                if (player == null || creature!.Monster != null) continue;
                return player;      // 单人就是那一位；多人时取第一位（只是用来展示）
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

}

