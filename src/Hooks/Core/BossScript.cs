using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Core;

/// <summary>剧本里的一张牌：哪张、是否升级、从哪来（只用于日志与说明）。</summary>
public sealed class ScriptCard
{
    public required Type Card { get; init; }
    public bool Upgraded { get; init; }

    /// <summary>来源标记：deck（卡组）/ ai（创造性AI印的）/ hologram（全息影像捞的）/ status（状态牌）。</summary>
    public string From { get; init; } = "deck";

    /// <summary>
    /// 这张牌**不打出**，而是被上一张牌（生存者这类「弃 1 张」的牌）丢掉。
    /// 剧本里写死比让自动选择器去猜可靠：用户设计第 6/11 回合都是「生存者丢掉防御」。
    /// </summary>
    public bool DiscardInstead { get; init; }

    /// <summary>
    /// **只在多人局**这张牌才是「被丢掉的那张」（单人照常打出）。
    /// 用途（用户 2026-10-26 的一层「奇巧猎」第 2 回合）：单人时那张稀有牌是「余像」（1 费打出），
    /// 多人时换成「鬼祟+」（2 费打不起，靠奇巧被丢弃后免费打出）。
    /// </summary>
    public bool DiscardInsteadWhenMultiplayer { get; init; }

    /// <summary>**只在单人局**这张牌才是「被丢掉的那张」（多人会真的把它打出去）。</summary>
    public bool DiscardInsteadSingleOnly { get; init; }

    /// <summary>
    /// 造这张牌时**直接给它「奇巧」**（<c>CardKeyword.Sly</c>，被丢弃时会自动打出）。
    ///
    /// 为什么要显式给：一层「奇巧猎」第 7 回合丢的是「后空翻」，而它本身**没有**奇巧 ——
    /// 设计稿说的是「因谋划专家而带有奇巧」。但 `MasterPlannerPower` 只给**打出去的牌**上奇巧
    /// （反编译：`AfterCardPlayed` → `CardCmd.ApplyKeyword(cardPlay.Card, Sly)`），手牌里那张吃不到
    /// ⇒ 由剧本点名给它，丢弃后才会真的自动打出。
    /// </summary>
    public bool Sly { get; init; }

    /// <summary>
    /// 这张牌**不打出**，而是被「烘焙手套」（<c>ToastyMittens</c>：每回合开始消耗手牌 1 张）烧掉。
    /// 标记后它会被登记成「优先消耗」，于是自动选择器一定挑它，而不是乱烧别的牌。
    /// </summary>
    public bool Burned { get; init; }

    /// <summary>
    /// 费用覆盖（-1 = 用卡面费用）。**只影响规划期**的记账（见 `BossScriptPlanner.Plan`）。
    /// 用途：奇巧（Sly）牌「被丢弃时自动打出」是不花钱的，按卡面 2 费规划会误判成"打不起"。
    /// </summary>
    public int CostOverride { get; init; } = -1;

    /// <summary>
    /// 本回合这张牌的**真实费用**（-1 = 用卡面）。造牌时直接改这张实例的 `EnergyCost`
    /// （`AddThisCombat(-n)`），所以卡面、规划、真正扣费三者一致。
    ///
    /// 用途（用户 2026-11-02 的「征服者储君」）：「王者之踢」原版是「每抽到这张牌 −1 费」
    /// （4 → 3 → 2 → 1 → 0），而我们的手牌每回合都是 <see cref="BossScriptRunner.BuildHand"/>
    /// **新建实例** ⇒ 抽牌减费根本不会发生。剧本逐回合写死：
    /// 第 2 回合 3（还在手里、打不出）、第 3 回合 2、第 6 回合 1、第 9/10 回合 0。
    /// </summary>
    public int CostThisTurn { get; init; } = -1;

    /// <summary>
    /// 这张牌带「**保留**」：回合结束时留在手里、不弃掉（用户 2026-11-03 的「蛇眼猎」：
    /// 特殊效果「计划妥当」= 每回合第一次抽到 3 费牌时将其保留）。造牌时给这张牌加
    /// <c>CardKeyword.Retain</c>。
    /// </summary>
    public bool Retain { get; init; }

    /// <summary>
    /// 打这张牌**之前先把她自己的抽牌堆清空**（牌全部进弃牌堆）。
    ///
    /// 用途：「华丽收场」（`GrandFinale`）原版的出牌条件是**抽牌堆必须为空**，而我们的剧本
    /// 手牌是每回合新建的、抽牌堆里还留着开局灌进去的整副卡组 ⇒ 这张牌永远「不满足条件」打不出。
    /// 用户 2026-11-09 的「华丽猎」第 9 回合就是要真的打出它，所以剧本标记一次：出手前把
    /// 抽牌堆清干净（视觉上也就是「她把牌库打空了再收场」），满足原版条件。
    /// </summary>
    public bool EmptyDrawPileFirst { get; init; }

    /// <summary>
    /// 这张牌**不算「她抽到的牌」**（谋杀 Murder 的加伤不数它）。
    /// 用途：华丽猎第 3 回合那三张「杂技」是**夜魇复制出来的**（剧本写在那儿），不是抽来的
    /// （用户 2026-11-09 的算法：1+5+8+3+3 ⇒ 第 4 回合 20，其中 T3 只算 3 张）。
    /// </summary>
    public bool NotDrawn { get; init; }

    /// <summary>
    /// 这张是**被「必备工具」在她回合开始时丢掉**的那一张（不是被别的牌丢的）。
    /// 用户 2026-11-09：「第七回合必备工具弃的牌也不对，提前把要上奇巧的防御给弃掉了」
    /// ⇒ 回合开始那次「弃 1 张」要专挑这一张，不能按牌名首字母去抢别的点名牌。
    /// </summary>
    public bool ToolsDiscard { get; init; }

    /// <summary>
    /// 「**万物一心**」（<c>AllForOne</c>）这一张要**返手**哪些牌（用户 2026-11-03 的三层「事件机」：
    /// 第 4 / 9 回合「万物一心(0费，返手超能光束，冰之长枪)」）。
    ///
    /// 原版 `AllForOne.OnPlay` 会把**弃牌堆里所有 0 费牌**（攻击/技能/能力都算）拿回手牌
    /// （反编译 `Filter`：`cost == 0 && !CostsX && type in {Attack, Skill, Power}`）。
    /// 事件机的特殊效果「一心化万」把它收窄成「**只返 0 费攻击牌**」，而且
    /// **每返一张 ⇒ 所有单位 +1 力量**。而她这回合手牌本来就是剧本重建的、
    /// 弃牌堆里的内容取决于前几回合真打过什么 ⇒ 不能让原版自己数，
    /// 所以由剧本**点名**这一张返哪几张，执行见
    /// <see cref="EventMachineMechanics.AllForOneReturnsAsync"/>。
    /// </summary>
    public Type[] ReturnsToHand { get; init; } = Array.Empty<Type>();

    /// <summary>
    /// 这一张会把哪几张牌**印进她的抽牌堆**（用户 2026-11-03 的三层「事件机」：
    /// 第 1 回合「羽化(印 0 费的万物一心、冰之长枪、超能光束)」）。
    ///
    /// 原版「羽化」（<c>Metamorphosis</c>）是「把 3 张**随机**攻击牌加入抽牌堆，本场免费打出」；
    /// 随机会让意图不可预测，所以原版那条随机印牌已经被
    /// `Patches.ShadowBossNoRandomCardPatch` 拦掉，改由剧本点名这三张。
    /// 造出来的牌与剧本后面打的同名卡**是两张不同的实例**（她的抽牌堆不会被抽牌，只用来放着）。
    /// 执行见 <see cref="EventMachineMechanics.MetamorphosisPrintsAsync"/>。
    /// </summary>
    public Type[] PrintsToDrawPile { get; init; } = Array.Empty<Type>();

    /// <summary>
    /// 配合 <see cref="PrintsToDrawPile"/>：印完之后**摆出来给玩家看一眼**。
    ///
    /// 用户 2026-11-03 的原话：「在玩家面前展示一下这三张牌，就像亡灵契约师使用能量汲取敲牌那样展示」。
    /// 为什么要展示：印到**抽牌堆**的牌玩家看不见（她的抽牌堆没有 UI），
    /// 而「疯狂科学 / 白噪声 / 声东击西」印的是**手牌**、直接看得见，所以那几张不用展示
    /// （用户 2026-11-03 明确：「疯狂科学印的牌不用展示，因为是直接印手牌的」）。
    /// 展示复用 <c>UI/BossCardShowcase</c>（纯展示，不写任何牌堆）。
    /// </summary>
    public bool ShowPrints { get; init; }

    /// <summary>
    /// 这张牌造出来时**附魔「漩涡」**（<c>Enchantments.Spiral</c>：Replay 1，多打一次）。
    /// 用户 2026-11-03 的三层「事件机」第 5 回合：「防御（附魔漩涡）」。
    /// 反编译 `Spiral`：效果全在 `EnchantPlayCount`（+Times 次），**没有** `OnEnchant` ⇒
    /// 挂上附魔即可，不需要像「灵魂之力」那样再手动调一次 `OnEnchant`。
    /// </summary>
    public bool EnchantSpiral { get; init; }

    /// <summary>
    /// 附魔「**灵魂之力**」（<c>SoulsPower</c>）。
    /// 用途（用户 2026-10-24）：三层「易伤战」剧本里第 3 / 第 9 回合那两张「主宰」是附魔过的。
    /// 实现见 <see cref="BossScriptRunner.BuildHand"/>（和 <see cref="EnchantSown"/> 同一套写法）。
    /// </summary>
    public bool EnchantSouls { get; init; }

    /// <summary>这张牌造出来时**附魔「皇室认证」**（`Enchantments.RoyallyApproved`，用户 2026-11-07 的「打击战」）。</summary>
    public bool EnchantRoyallyApproved { get; init; }

    /// <summary>
    /// 这张牌由「**地狱狂徒**」自动免费打出：打出时不扣费、**只打被跟踪锁定的那位玩家**，并带一次特效。
    /// 用户 2026-11-07 的「打击战」第 6～9 回合那几张。
    /// </summary>
    public bool HellraiserFree { get; init; }

    /// <summary>
    /// **伤害覆盖**（0 = 用卡面数值）。
    /// 用途（用户 2026-10-23）：二层灵魂骨第 3 回合的「巨镰」要打 **23**。
    /// 巨镰（<c>TheScythe</c>）的伤害是「本局永久成长」的（基础 13 + `Increase` 变量），
    /// 而她第 3 回合才第一次打，卡面只有 13 —— 剧本用这个字段直接把这张牌的伤害写死。
    /// 实现见 <see cref="BossScriptRunner.BuildHand"/>：改的是**这一张实例**的
    /// <c>DynamicVars.Damage.BaseValue</c>，所以意图与打出的伤害都是 23。
    /// </summary>
    public int DamageOverride { get; init; }

    /// <summary>
    /// **格挡覆盖**（0 = 用卡面数值）。
    /// 用途（用户 2026-10-26 的一层「黑球机」）：剧本第 4 回合的「遗传算法」要打 **16 点格挡**，
    /// 而这张牌是「每打出一次，本局格挡值永久 +Increase」，我们的手牌每回合重建 ⇒ 成长会丢。
    /// 实现见 <see cref="BossScriptRunner.BuildHand"/>：改的是这一张实例的 `Block` 变量。
    /// </summary>
    public int BlockOverride { get; init; }

    /// <summary>
    /// 这张牌**只用来显示意图**，不参与出牌。
    /// 用途：所向无敌（<c>I_AM_INVINCIBLE</c>）是在他回合结束时从摸牌堆顶免费打出的
    /// （见 <see cref="BossTurn.FreePlayTopEntry"/>），但玩家需要在他的意图里看到这 10 点格挡
    /// （用户要求）→ 把它当成一张"手上的展示牌"造出来，出牌阶段跳过它。
    /// </summary>
    public bool DisplayOnly { get; init; }

    /// <summary>
    /// 配合 <see cref="DisplayOnly"/>：这张展示牌**要画哪些意图**（用户 2026-11-10 的三层「抉择储」）。
    ///
    /// 背景：那 5 张候选技能牌由玩家挑一张、再被「抉择，抉择」重复打出 3 次 ——
    /// 它们**不出牌**（手牌栏压暗），但玩家必须能提前看到「选它会怎样」，所以要点名画意图：
    /// 铸墙 = 守势+强化、隐秘藏品 = 强化、共鸣 = 策略+强化、震荡波 = 策略。
    ///
    /// 画的顺序固定为 **守势 → 策略 → 强化**（用户给的口径正是这个顺序）。
    /// 与 <see cref="AutoPlayed"/> 的区别：那个是「本回合真的会打出去 ⇒ 画亮 + 用它自己的意图」，
    /// 这里只画意图、牌仍然压暗。
    /// </summary>
    public BossIntentTag ForceIntents { get; init; }

    /// <summary>
    /// 配合 <see cref="DisplayOnly"/>：这张展示牌下面标出「**天际钻头会因此多出多少伤害/段数**」
    /// （用户 2026-11-10：星位序列那一栏要显示，例如第一回合 8×12）。
    ///
    /// 实现见 <see cref="BossScriptPlanner"/>：按「若选了星位序列 ⇒ 能量 +6（3 次 × +2）⇒
    /// 天际钻头的 X 变大 ⇒ X≥4 翻倍」算出**多出来的段数**（= 变更后的段数 − 天际钻头原本的段数），
    /// 画成 <c>UI/BossHandCard</c> 里那条小标签。
    ///
    /// ⚠ 这条**不是意图**（不进 `Intents`、也不进原版 `MoveState.Intents`）⇒
    /// 薄荷尖塔那类「按原版意图统计本回合总伤害」的 mod **不会**把它算进去（用户明确要求）。
    /// </summary>
    public bool ShowsDrillGain { get; init; }


    /// <summary>
    /// 这张牌打出去时**还会额外造成多少「无来源群体伤害」**（0 = 没有）。
    ///
    /// 唯一用途（用户 2026-11-07 的彩虹机）：第 8 回合那张「彩虹+」会踩到遗物「节拍器」的第 5 颗球，
    /// 于是额外对所有敌人造成 30 点无来源伤害。游戏里这 30 点是真的会打出来的（`Metronome.AfterOrbChanneled`），
    /// 只是**意图上原来看不见** —— 用户要求把这条标在这张牌下面，所以剧本把数字写在这里，
    /// 由 <c>UI/BossHandCard.AddExtraAoeLabel</c> 画出来。
    /// ⚠ 只有**第一次**踩到的那回合标（循环里再打彩虹不会再触发节拍器：它的计数到 `OrbCount` 只结算一次）。
    /// </summary>
    public int ExtraAoe { get; init; }

    /// <summary>
    /// 配合 <see cref="DisplayOnly"/>：这张展示牌**本回合真的会被打出去**（只是不在出牌阶段，
    /// 而是回合结束时从摸牌堆顶免费打出，见 <see cref="BossTurn.FreePlayTopEntry"/>）。
    ///
    /// 为什么要单独标：手牌栏用「在不在 <c>Plan</c> 里」决定亮/暗，而展示牌一定不在 Plan 里，
    /// 于是它被画成了暗的 —— 用户实测指出「所向无敌应该是亮的」（它确实会打出去）。
    /// 标了 AutoPlayed 的展示牌按**亮**渲染，其余展示牌（剑圣：费不够打不出）保持暗。
    /// </summary>
    public bool AutoPlayed { get; init; }

    /// <summary>
    /// 达到这个进阶等级时，这一张牌是**升级版**（0 = 永不）。
    ///
    /// 为什么需要「按张」而不是按类型：第三幕无色储君的进阶 8 只升级**第 1 回合**那张「创世之柱」，
    /// 第 3 回合那张明确不升级（用户设计）；而按类型的 <c>AscensionUpgrades</c> 会把所有同名牌一起升级。
    /// </summary>
    public int UpgradeMinAscension { get; init; }

    /// <summary>
    /// 这张牌本回合**费用为 0**。
    /// 用途：第三幕无色储君第 1 回合的「超质量体」本来是烦人机关盒印出来的（原版会 `SetToFreeThisTurn()`），
    /// 我们把机关盒的印牌拦掉了，所以这个 0 费由剧本自己声明。
    /// </summary>
    public bool FreeThisTurn { get; init; }

    /// <summary>
    /// **即使有费也不出**，留在手里、下回合照常按剧本出（用户设计：根除骨第 2/6 回合的「根除」）。
    /// 规划期直接跳过这张牌（不耗费、不出牌、不产生意图），手牌里留着。
    /// </summary>
    public bool HoldInstead { get; init; }

    /// <summary>
    /// 这张牌**只有在「抉择，抉择」那一手玩家挑了指定的那张**时才打得出（null = 不受选择影响）。
    ///
    /// 唯一用途（用户 2026-11-10 的三层「抉择储」第 1 回合）：
    /// 「天际钻头」是 X 费牌，而第 1 回合打完 群星之子/崇拜/王国资产 正好把 3 点能量花光
    /// ⇒ 只有玩家挑了「星位序列」（它每次给 2 费，被重复打出 3 次 ⇒ +6）才有费打钻头。
    /// 规划期玩家的选择还不知道 ⇒ 这种牌**不规划**（手牌里压暗、没有意图）；
    /// 等选择落地后由 <see cref="ChoiceRegent.RunAfterPickAsync"/> 重新按剧本补打。
    /// </summary>
    public Type? OnlyIfChosenCard { get; init; }

    /// <summary>这张牌造出来时**附魔「播种」**（`Sown`）：打出时获得 1 费（用户设计：根除骨第 3 回合的钙化）。</summary>
    public bool EnchantSown { get; init; }

    /// <summary>
    /// 强制在这张牌下面画一条**策略（减益）意图**（剧本点名用）。
    ///
    /// ★ 2026-11-09（二层「虚无骨」）：女妖之嚎自己只造成伤害、卡面上没有任何减益变量，
    ///   所以意图生成器只会画一条攻势；但用户要求「**第 4 回合那一张**标出策略意图」
    ///   （那一手会通过特殊效果「虚空之唤」给所有玩家挂恶咒），后面第 7 回合那张不标
    ///   ⇒ 只能在剧本里按张点名。
    /// </summary>
    public bool ForceDebuffIntent { get; init; }

    /// <summary>
    /// 这张牌是**「杂耍」的复制品**（二层「杂耍战」的特殊效果「燃烧契约」，用户 2026-11-02）。
    ///
    /// 原版「杂耍」`JugglingPower.BeforeCardPlayed` 会每回合把前 3 张**攻击牌**各复制一张进手牌，
    /// 但那样既随机（复制哪张取决于她这回合打了什么）又没有「虚无 / 消耗」——
    /// 用户的要求是：「拦掉原版杂耍的复制，**提前在他手牌里加上对应的牌**（并且要带有虚无和消耗）」。
    ///
    /// 所以剧本自己把复制牌写进手牌，并把这个标记打开：
    ///   · 造牌时给这张牌加 `CardKeyword.Ethereal`（虚无：回合结束还在手里就消耗）与
    ///     `CardKeyword.Exhaust`（打出后消耗）；
    ///   · 它**不花能量**、也不由她打出（配合 <see cref="HoldInstead"/>），
    ///     而是被「惊逃」`StampedePower` 在**回合结束时**免费打出去
    ///     （手动派发见 <see cref="StampedePlays"/>）。
    /// </summary>
    public bool JugglingCopy { get; init; }

    /// <summary>
    /// 这张牌**不花她的能量**，而是由「惊逃」（`StampedePower`）在**回合结束时**打出去
    /// （二层「杂耍战」用户 2026-11-02：「第二回合…前 2 张费用打出，**第三张惊逃打出**」）。
    ///
    /// 与 `HoldInstead` 的区别：那个是「留着下回合再说」，这个**本回合一定会被打出**
    /// ⇒ 手牌栏要把它画亮、并在它下面画出攻击意图（做法同 `DisplayOnly + AutoPlayed`）。
    /// 执行见 <see cref="StampedePlays"/>。
    /// </summary>
    public bool StampedePlay { get; init; }

    /// <summary>
    /// 「**炼制药水**」（`ALCHEMIZE`）这张牌要炼的是**哪一瓶**（null = 原版随机）。
    /// 用户 2026-10-25 的三层「炼药猎」设计：剧本写死「炼格挡药水 / 炼复制药水 / 炼异鱼之油」。
    /// 实现：出牌前记进 <see cref="BossPotions.PendingBrew"/>，由 `Patches/AlchemizeBrewPatch` 换掉随机那一步。
    /// </summary>
    public Type? BrewPotion { get; init; }

    /// <summary>
    /// 打这张牌**之前先喝一瓶药水**（null = 不喝）。
    /// 用途（用户 2026-10-25 的三层炼药猎第 4/8 回合）：剧本是「预判 → 喝复制药水 → 坚韧之环（被复制）」，
    /// 也就是药水夹在牌中间，不能统一挪到回合开头。
    /// </summary>
    public Type? DrinkBefore { get; init; }
}

/// <summary>
/// 「展示牌要画哪些意图」（<see cref="ScriptCard.ForceIntents"/>）。
///
/// 用标签而不是直接塞 <c>AbstractIntent</c> 实例：意图对象要能按当前力量/易伤实时重算，
/// 所以剧本只声明「画哪几种」，真正的节点由规划期按同一把尺子造出来。
/// </summary>
[Flags]
public enum BossIntentTag
{
    None = 0,
    /// <summary>守势（格挡）。</summary>
    Defend = 1,
    /// <summary>强化（蓝上箭头）。</summary>
    Buff = 2,
    /// <summary>策略（红下箭头：给玩家上负面）。</summary>
    Debuff = 4,
}

/// <summary>剧本里「喝一瓶药水」：哪一瓶 + 备注（只用于日志）。</summary>
public sealed class ScriptPotion
{
    public required Type Potion { get; init; }
    public string Note { get; init; } = string.Empty;
}

/// <summary>一回合的剧本：**按顺序**尝试打出这些牌，能量不够的那张自动跳过。</summary>
public sealed class BossTurn
{
    public List<ScriptCard> Cards { get; init; } = new();

    /// <summary>
    /// 这一回合**先喝**的药水（用户 2026-10-25：炼药猎的剧本每回合都是「药水先、牌后」）。
    /// 顺序就是剧本里的顺序；带不动的（栏位里没有那瓶）自动跳过。
    /// </summary>
    public List<ScriptPotion> Potions { get; init; } = new();

    /// <summary>
    /// 这一回合**先真的洗一次她的抽牌堆**（用户 2026-11-05：三层「状态机」第 6 回合那个「洗牌」）。
    /// 为什么要真洗：她的「大～抱抱」(<c>BiiigHug</c>) 是在 `AfterShuffle` 里生成煤灰的，
    /// 不真洗牌就不会生成；顺带按用户要求**同时给每位真人玩家塞 1 张煤灰**。
    /// 见 <c>CharBossMonster.Turn.cs</c> 里的 `DoScriptedShuffleAsync`。
    /// </summary>
    public bool ShuffleDrawPile { get; init; }
    /// <summary>这一回合的备注（写设计意图用，不参与逻辑）。</summary>
    public string Note { get; init; } = string.Empty;

    /// <summary>
    /// 这一回合**自动选择器**必须按顺序挑中的牌（用户 2026-11-02 的「征服者储君」）。
    ///
    /// 为什么需要：原版「把一张牌放到抽牌堆顶」这类效果自己会**弹选牌**——
    ///   · 光子切割+「将手牌中的一张牌放到抽牌堆顶部」（从手牌选）；
    ///   · 宇宙冷漠「将弃牌堆中的一张牌放到你的抽牌堆的顶部」（从弃牌堆选）；
    ///   · 抉择，抉择「选择你手牌中的一张技能牌，并将其打出 3 次」（从手牌选技能）。
    /// Boss 出牌时这些选牌走 <see cref="BossCardSelector"/> 自动选，不点名就按「价值」乱挑
    /// ⇒ 用这个列表按剧本顺序钉死每一次选牌。
    /// 例：第 5 回合 = [宇宙冷漠（抉择抉择要打出的技能）, 王者之踢, 王者之拳, 所向无敌
    /// （后三个是宇宙冷漠被 3 次打出时各自返顶的目标）]。
    /// </summary>
    public List<Type> PickOrder { get; init; } = new();

    /// <summary>
    /// 这一回合的能量覆盖值（0 = 用 <c>BossDefinition.EnergyForAct</c> 的定义值）。
    /// 有些剧本里个别回合的能量与定义值不同（例如「第 7 回合起多一费」），
    /// 用数据写死比在代码里推断可靠。
    ///
    /// ⚠ 因为 **0 = 不覆盖**，「这一回合真的是 0 费」必须用 <see cref="ForceZeroEnergy"/> 表示
    /// （用户 2026-10-29 实测：黑球机第 1 回合本该 0 费 —— 3 − 涅奥 1 − 面包 2 ——
    /// 剧本里写 `Energy = 0` 被当成「不覆盖」，规划期按 2 费算，
    /// 于是把 2 费的「休眠」预告成会打出，实战却打不出）。
    /// </summary>
    public int Energy { get; init; }

    /// <summary>这一回合**强制 0 费**（压过定义值）。见 <see cref="Energy"/> 的注释。</summary>
    public bool ForceZeroEnergy { get; init; }

    /// <summary>
    /// 这一回合**结束时**把指定牌从牌堆挪到抽牌堆顶并免费打出（用户设计：
    /// 所向无敌 <c>I_AM_INVINCIBLE</c> 在他回合结束时正好躺在抽牌堆顶 → 0 费打出）。
    /// 只在这个标记为 true 的回合生效，否则那张牌会每回合都被白打一次。
    /// </summary>
    /// <summary>
    /// 这一回合的**意图栏整体左移多少像素**（0 = 不动）。
    ///
    /// 用途（用户 2026-11-07 的彩虹机）：第 7 / 12 回合要打 9 张牌，卡面下方的意图行加起来会顶到屏幕右边缘，
    /// 最右边那几条（防御）直接被切掉看不见。把**这两个回合**的意图行整体往左挪 120px 就都露出来了；
    /// 其它回合为 0 ⇒ 不受影响，也不会波及别的 Boss。
    /// </summary>
    public int IntentShiftLeft { get; init; }
    public string? FreePlayTopEntry { get; init; }

    /// <summary>
    /// 这一回合**结束时**，如果指定牌在**消耗牌堆**里，就调它自己的那条钩子把它打出来。
    ///
    /// 用户设计（三层「巨石战」2026-10-27）：「彼岸咆哮」（`HowlFromBeyond`）——
    /// 「在你的回合结束时，如果这张牌在你的消耗牌堆中，则将其打出」。
    /// 该逻辑写在 `HowlFromBeyond.AfterAutoPostPlayPhaseEntered` 里，而那条钩子只派发给真人玩家
    /// （影子玩家不在 `CombatState.Players`）⇒ 由这里手动调它自己的实现
    /// （和「所向无敌」用的是同一个钩子，见 `CharBossMonster` 的回合结束段）。
    /// </summary>
    public string? FreePlayExhaustEntry { get; init; }
}

/// <summary>
/// 某一场（某一幕）的整套剧本。
///
/// 为什么要有这个：角色 Boss 的「贪心 AI」打不出设计好的节奏
/// （比如先用「偏差认知」堆集中、再靠「双重释放」激发球、某些回合故意因为没费打不出牌）。
/// 剧本把「第几回合手里有哪几张牌、按什么顺序打」直接写死 —— 于是**意图是精确可预测的**，
/// 这也正是玩家能针对它做决策的前提。
///
/// 循环规则：回合数超过 <see cref="Turns"/> 长度后，从 <see cref="LoopFrom"/>（1 起）开始
/// 按 <see cref="LoopLength"/> 个回合一段循环；不填则停在最后一回合。
/// </summary>
public sealed class BossScript
{
    public List<BossTurn> Turns { get; init; } = new();

    /// <summary>循环起点（1 起，0 = 不循环）。</summary>
    public int LoopFrom { get; init; }

    /// <summary>一段循环包含几个回合（LoopFrom &gt; 0 时有效）。</summary>
    public int LoopLength { get; init; }

    /// <summary>
    /// 每一轮循环额外升级的牌（下标 0 = 第一轮循环，之后沿用最后一项）。
    /// 用户设计：第一轮循环升级出击+护卫，第二轮碎骨+死亡之门，再往后每次升级 1 对打击与防御。
    /// </summary>
    public List<Type[]> UpgradeByCycle { get; init; } = new();

    /// <summary>第 <paramref name="cycle"/>（1 起，0 = 还没进入循环）轮要额外升级哪些牌。</summary>
    public Type[] UpgradeSetFor(int cycle)
    {
        if (cycle <= 0 || UpgradeByCycle.Count == 0) return Array.Empty<Type>();
        return UpgradeByCycle[Math.Min(cycle, UpgradeByCycle.Count) - 1];
    }

    /// <summary>第 <paramref name="turn"/> 回合属于第几轮循环（0 = 第一遍还没走完）。</summary>
    public int CycleAt(int turn)
    {
        if (Turns.Count == 0 || LoopLength <= 0 || turn <= Turns.Count) return 0;
        return (turn - Turns.Count - 1) / LoopLength + 1;
    }

    public bool HasTurns => Turns.Count > 0;

    /// <summary>
    /// 可选的**动态解析**（非空时 <see cref="TurnAt"/> 先问它）。
    ///
    /// 用途（用户 2026-11-04 的一层「轰击储」）：有的 Boss 剧本长度**不固定** ——
    /// 她被击晕的回合数取决于「玩家什么时候攒够无色牌回合」，唤醒之后才进入两回合循环，
    /// 线性回合表表达不了。这类 Boss 在这里返回自己的回合。
    /// 返回 null = 交回给 <see cref="Turns"/>（正常流程）；
    /// 返回一个**空 <see cref="BossTurn"/>** = 这一回合她什么都不打（手牌为空）。
    /// </summary>
    public Func<int, BossTurn?>? Resolve { get; init; }

    /// <summary>取第 <paramref name="turn"/>（1 起）回合的剧本，含循环。</summary>
    public BossTurn? TurnAt(int turn)
    {
        if (Resolve != null)
        {
            try
            {
                var custom = Resolve(turn);
                if (custom != null) return custom;
            }
            catch { /* 动态解析失败就退回静态回合表 */ }
        }

        if (Turns.Count == 0) return null;

        int index = turn - 1;
        if (LoopFrom > 0 && LoopLength > 0 && turn >= LoopFrom)
        {
            index = LoopFrom - 1 + (turn - LoopFrom) % LoopLength;
        }
        if (index < 0 || index >= Turns.Count) index = Turns.Count - 1;
        return Turns[index];
    }
}

/// <summary>把剧本变成「这一回合的手牌」（不抽牌堆、不随机）。</summary>
public static class BossScriptRunner
{
    /// <summary>
    /// 用剧本重建手牌：先清空手牌，再按剧本顺序造牌（升级标记照剧本）。
    /// 造的是**全新卡实例**，与卡组无关 —— 「某张牌打出后就没了、后续靠创造性AI印」
    /// 这类规则直接由剧本本身表达，不需要模拟牌堆流转。
    /// </summary>
    /// <summary>
    /// **完美打击**（`PerfectedStrike`）的伤害接管（二层「杂耍战」）。
    ///
    /// 原版公式（反编译 `CalculatedVar.Calculate`）：
    /// <code>
    /// 伤害 = CalculationBase.BaseValue + ExtraDamage.BaseValue × (牌堆里带 Strike 标签的牌数)
    /// </code>
    /// 而这个「牌数」数的是 `PlayerCombatState.AllCards`（**所有牌堆**）。
    /// 我们的手牌每回合都是**重新造**的 ⇒ 前几回合的剧本牌全堆在弃牌堆里、也被数进去
    /// ⇒ 用户 2026-11-02 实测：本来该按 7 张算（43 伤害），实际按 10 张算成了 57。
    ///
    /// 所以这里按「设计上应该有几张」接管：
    ///   `卡组里带 Strike 标签的牌` + `本场已经生成的打击类复制牌`（`StrikeCopiesMade`）。
    /// 做法是把 `CalculationBase` 改成算好的总数、`ExtraDamage` 归零
    /// （这样 `Calculate` 就是「我们的数字 + 0 × 原版那个被污染的张数」）。
    /// 升级/附魔/易伤/力量的加成依然由原版管线自己算，我们只改「张数」这一项。
    /// </summary>
    /// <summary>
    /// 把「上一批剧本重建出来的旧牌」从她的手牌/抽牌/弃牌/消耗牌堆里**整份清掉** —— 只清
    /// **带 `Strike` 标签、且不在 `StrikeCopyInstances` 里**的那些。
    ///
    /// 为什么：她的手牌每回合都是 `BuildHand` **重新创建**的，旧的那批会留在弃牌堆里；
    /// 而「完美打击」的伤害数的是 `PlayerCombatState.AllCards`（所有牌堆）里带 Strike 标签的牌
    /// ⇒ 陈年副本一起被算进来，张数明显偏高（用户 2026-11-02：该 7 张，实际 10 张）。
    /// 「杂耍」生成的复制牌**不清**（那正是设计里「每个循环 +1 张打击」的来源）。
    ///
    /// ⚠ 只清 Strike 标签的牌：像「彼岸咆哮」这种要靠**消耗牌堆**参与回合结束自动打出的牌，
    /// 一旦被清掉 `FreePlayExhaustEntry` 就找不到它了。
    /// </summary>
    /// <summary>她所有牌堆里「带 Strike 标签」的牌数（= 原版完美打击会用的那个乘数）。</summary>
    private static int CountStrikeCardsInPiles(Monsters.CharBossMonster? boss)
    {
        try
        {
            var all = boss?.Shadow?.PlayerCombatState?.AllCards;
            if (all == null) return 0;
            int n = 0;
            foreach (var card in all)
            {
                if (card == null) continue;
                try { if (card.Tags != null && card.Tags.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardTag.Strike)) n++; }
                catch { /* 单张读不到就跳过 */ }
            }
            return n;
        }
        catch { return 0; }
    }
    /// <summary>
    /// 打击战「痛击」：把**完美打击**的总伤害写成 <paramref name="target"/>。
    ///
    /// 原版公式（反编译）：`伤害 = CalculationBase + ExtraDamage × (她所有牌堆里带 Strike 标签的牌数)`。
    /// 我们要「总数 = target、而卡面继续显示『每张 +2／+3』」⇒ **只改 CalculationBase**：
    ///   `CalculationBase = target − perCard × 牌堆里实际张数`。
    /// （第二次尝试在 `ExtraDamage` 上归零，结果卡面文案变成「伤害增加 0」，所以回滚了 —— 那条路是错的。）
    /// </summary>
    private static void ApplyBashToPerfectStrike(CardModel card, Monsters.CharBossMonster boss)
    {
        try
        {
            if (card == null) return;
            // ★ v0.2.481：基础 6、每张 2／3（进阶 9~10 为 3）**写死**，
            //   绝不再读卡上现在的 CalculationBase —— 上一版就是因为读写同一格，两套计算互相喂数
            //   （日志：写进 42，下一行读出 49）。
            int baseVal = 6;
            int perCard = CharacterBosses.Powers.BossBashPower.PerCard();

            int target = 6;
            try
            {
                var bashPower = boss.Creature?.GetPower<CharacterBosses.Powers.BossBashPower>();
                if (bashPower != null) target = bashPower.TargetDamage(boss);
            }
            catch { target = 6; }
            int live = CountStrikeCardsInPiles(boss);
            int calc = Math.Max(0, target - perCard * Math.Max(0, live));
            card.DynamicVars["CalculationBase"].BaseValue = calc;
            Logger.Log($"[CharBoss] 痛击：完美打击目标 {target}（基础 {baseVal} + {perCard}×统计张数）" +
                       $"牌堆现算 {live} 张 × {perCard} ⇒ CalculationBase = {calc}");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 痛击：写完美打击失败: " + e.Message); }
    }

    /// <summary>池子下降后重算她手上/牌堆里所有完美打击（Power 那边调用）。</summary>
    /// <summary>她现在手上那张完美打击的 CalculationBase（用于判断重算有没有改变数值）。</summary>
    internal static int BashCalcSnapshot(Monsters.CharBossMonster boss)
    {
        try
        {
            var all = boss?.Shadow?.PlayerCombatState?.AllCards;
            if (all == null) return -1;
            foreach (var card in all)
            {
                if (card == null) continue;
                string entry;
                try { entry = card.Id.Entry; } catch { continue; }
                if (entry != "PERFECTED_STRIKE") continue;
                try { return (int)Math.Round((double)card.DynamicVars["CalculationBase"].BaseValue); } catch { return -1; }
            }
        }
        catch { /* 忽略 */ }
        return -1;
    }
    internal static void RetuneBashPerfectStrike(Monsters.CharBossMonster boss)
    {
        try
        {
            var all = boss?.Shadow?.PlayerCombatState?.AllCards;
            if (all == null) return;
            foreach (var card in all)
            {
                if (card == null) continue;
                string entry;
                try { entry = card.Id.Entry; } catch { continue; }
                if (entry != "PERFECTED_STRIKE") continue;
                ApplyBashToPerfectStrike(card, boss);
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 痛击：重算完美打击失败: " + e.Message); }
    }
    /// <summary>把她的抽牌堆整堆挪进弃牌堆（让「华丽收场」满足原版的「抽牌堆为空」）。</summary>
    internal static void ClearDrawPileFor(PlayerCombatState? pcs)
    {
        try
        {
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
            Logger.Log($"[CharBoss] 华丽收场：抽牌堆已清空（{draw.Count} 张进弃牌堆）⇒ 这一手它可打出");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 清空抽牌堆失败: " + e.Message); }
    }

    private static void PurgeStaleStrikeCards(PlayerCombatState pcs, Monsters.CharBossMonster? boss)
    {
        try
        {
            if (pcs == null || boss == null) return;
            int removed = 0;
            foreach (var pile in new[] { pcs.Hand, pcs.DrawPile, pcs.DiscardPile, pcs.ExhaustPile })
            {
                if (pile == null) continue;
                foreach (var card in pile.Cards.ToList())
                {
                    if (card == null) continue;
                    bool strike;
                    try { strike = card.Tags != null && card.Tags.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardTag.Strike); }
                    catch { strike = false; }
                    if (!strike) continue;
                    if (boss.StrikeCopyInstances.Contains(card)) continue;   // 真复制牌：留着
                    try { pile.RemoveInternal(card, true); removed++; }
                    catch { /* 单张失败不影响其它 */ }
                }
            }
            if (removed > 0)
            {
                Logger.Log($"[CharBoss] 重建手牌：清掉上一批「打击」旧牌 {removed} 张" +
                           "（完美打击数牌堆时不再把陈年副本算进去；杂耍的复制牌保留）");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 清理旧打击牌失败（忽略）: " + e.Message);
        }
    }
    internal static void TakeOverPerfectedStrike(MegaCrit.Sts2.Core.Models.CardModel? card,
                                                 Monsters.CharBossMonster? boss)
    {
        try
        {
            if (card == null || boss == null) return;
            string entry;
            try { entry = card.Id.Entry; } catch { return; }
            if (!string.Equals(entry, "PERFECTED_STRIKE", StringComparison.Ordinal)) return;

            // ★ v0.2.481（读日志定位：v0.2.479 里我们写进去 42，紧接着又冒出「基础伤害 49」——
            //   说明**两套接管在互相覆盖**：这套「杂耍战」的算完又读回我们写的值当基础再算一遍。）
            //   打击战（IRONCLAD_STRIKE）由 `ApplyBashToPerfectStrike` 自己算，这里直接让路。
            try
            {
                if (boss.SpawnVariantKey == "IRONCLAD_STRIKE") return;
                if (boss.Def?.SpecialPowerForAct(boss.ActIndex) == typeof(CharacterBosses.Powers.BossBashPower)) return;
            }
            catch { /* 忽略 */ }

            int deckStrikes = 0;
            try
            {
                var deck = boss.Def.DeckForAct(boss.ActIndex);
                if (deck != null)
                {
                    foreach (var type in deck)
                    {
                        if (type == null) continue;
                        try
                        {
                            var canonical = ModelDb.GetById<MegaCrit.Sts2.Core.Models.CardModel>(ModelDb.GetId(type));
                            var tags = canonical?.Tags;
                            if (tags != null && tags.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardTag.Strike)) deckStrikes++;
                        }
                        catch { /* 单张读不到就跳过 */ }
                    }
                }
            }
            catch { /* 忽略 */ }

            int count = Math.Max(0, deckStrikes) + Math.Max(0, boss.StrikeCopiesMade);

            // 基础值 / 每张加成：读**卡面 BaseValue**（未升级 = 6 / 2；升级后才 3）。
            // ⚠ 不要用预览管线读 ExtraDamage —— 那会把升级值算进来（用户 2026-11-02：完美打击每张 +2，升级后才 +3，
            //   而 Boss 卡组里这张一直是未升级版）。
            int baseVal = 6, perCard = 2;
            try { baseVal = Math.Max(1, (int)Math.Round((double)card.DynamicVars["CalculationBase"].BaseValue)); }
            catch { baseVal = 6; }
            try { perCard = Math.Max(1, (int)Math.Round((double)card.DynamicVars["ExtraDamage"].BaseValue)); }
            catch { perCard = 2; }

            int target = baseVal + perCard * count;

            // 原版公式：CalculationBase + ExtraDamage × (AllCards 里带 Strike 的牌数)。
            // 我们要「总数 = target、且卡面继续显示『每张 +perCard』」⇒ 只改 CalculationBase：
            //   CalculationBase = target − perCard × 牌堆里实际张数（ExtraDamage 保持不动）。
            int live = CountStrikeCardsInPiles(boss);
            int calc = Math.Max(0, target - perCard * Math.Max(0, live));

            card.DynamicVars["CalculationBase"].BaseValue = calc;
            Logger.Log($"[CharBoss] 完美打击：设计张数 = 卡组 {deckStrikes} + 复制 {boss.StrikeCopiesMade} = {count} ⇒ " +
                       $"目标 {baseVal}+{perCard}×{count} = {target}；牌堆现算 {live} 张 ⇒ CalculationBase = {calc}（卡面仍显示「每张 +{perCard}」）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 完美打击接管失败（按原版算）: " + e.Message);
        }
    }
    public static void BuildHand(ICombatState? combat, Player? shadow, PlayerCombatState pcs, BossTurn turn,
                                 Type[]? extraUpgrades = null)
    {
        BossPiles.DiscardHandExceptTokens(pcs);   // 小刀 token 要留着（袖里乾坤塞的 3 张）
        if (combat == null || shadow == null) return;

        // 多人专属换牌要用「是哪只 Boss」判断幕数与职业；单人时 Resolve 原样返回
        var cardBoss = shadow.Creature?.Monster as Monsters.CharBossMonster;

        // ★ v0.2.315：把上一批「剧本重建出来的旧牌」从各牌堆里清掉（只清带 Strike 标签、且不是杂耍复制的）。
        //   不做这件事的话，完美打击数 AllCards 时会把陈年副本一起算上 ⇒ 伤害虚高
        //   （用户 2026-11-02 实测：该按 7 张算成 43，实际按 10 张算成 57）。
        try { PurgeStaleStrikeCards(pcs, cardBoss); } catch { /* 忽略 */ }

        // ★ v0.2.508（用户 2026-11-08：「这张加的蛇咬能不能放在所有会被打出的牌的右边？现在这样看着很怪」）：
        //   手牌显示顺序 = 牌堆顺序（`Hand.AddInternal(card, -1, …)` 追加到末尾 = 最右）。
        //   而「保留不出」的牌（蛇咬 / 心神不宁 / 睡眠不佳…）以前是**按剧本里的位置**混进去的，
        //   循环成长那几张更是在处理 `turn.Cards[0]` 时就塞进手里 ⇒ 会夹在要打出的牌中间。
        //   现在统一记一份，整手建完之后再把它们挪到手牌最右（`MoveToBottomInternal` = 移到末尾）。
        var keptToRight = new List<CardModel>();

        foreach (var entry in turn.Cards)
        {
            var cardType = MultiplayerCardSwap.Resolve(entry.Card, cardBoss!);
            var canonical = ModelDb.GetById<CardModel>(ModelDb.GetId(cardType));
            if (canonical == null && !ReferenceEquals(cardType, entry.Card))
            {
                // 换上的牌取不到 canonical（例如它在本作里不是普通 CardModel）
                // → **退回原牌**，绝不能让这张牌凭空消失（用户实测：只剩移除、没有新增）。
                Logger.Log($"[CharBoss] 多人换牌：{cardType.Name} 取不到 canonical → 退回原牌 {entry.Card.Name}");
                cardType = entry.Card;
                canonical = ModelDb.GetById<CardModel>(ModelDb.GetId(cardType));
            }
            if (canonical == null)
            {
                Logger.Log($"[CharBoss] 剧本卡 {entry.Card.Name} 找不到");
                continue;
            }

            try
            {
                // ⚠ 君王之剑是**铸造出来的那一把**，不是新建的：它身上的伤害（铸造值）在卡实例上，
                // 新建一张就等于"又重新铸了一把、而且不含铸造"（用户实测：打出时又铸造了一把、
                // 伤害与预览都没算铸造）。所以剧本要它时，优先把他牌堆里那把真实的剑搬进手牌。
                var card = TakeExistingBlade(pcs, entry.Card) ?? combat.CreateCard(canonical, shadow);
                if (card == null) continue;
                bool upgrade = entry.Upgraded || IsIn(extraUpgrades, entry.Card)
                               || (entry.UpgradeMinAscension > 0 && AscensionOf(shadow) >= entry.UpgradeMinAscension)
                               // 多人换牌表可以要求「换上那张是升级版」（第一幕：余像 → 鬼祟+）
                               || MultiplayerCardSwap.ForcesUpgrade(entry.Card, cardBoss);
                if (upgrade && !card.IsUpgraded) card.UpgradeInternal();
                // 剧本点名要给「奇巧」的牌（后空翻：被丢弃后自动打出，见 ScriptCard.Sly）
                if (entry.Sly)
                {
                    try
                    {
                        MegaCrit.Sts2.Core.Commands.CardCmd.ApplyKeyword(
                            card, MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Sly);
                        Logger.Log($"[CharBoss] 剧本给 {card.Id.Entry} 加上「奇巧」（被丢弃时会自动打出）");
                    }
                    catch (Exception e) { Logger.Log("[CharBoss] 加「奇巧」失败: " + e.Message); }
                }
                // 伤害覆盖（剧本写死这张牌打多少）：改这一张实例的 Damage 变量 ⇒ 意图与实际伤害都跟着变
                // ★ 打击战「痛击」（用户 2026-11-07）：她的**完美打击**伤害 = 卡面基础 6 + 池子剩余额度。
                //   实现**复用杂耍战那套接管**（见 `ApplyBashToPerfectStrike` 的注释）：
                //   只改 `CalculationBase`，`ExtraDamage` 保持原样 ⇒ 卡面仍然写「每张 +2／+3」，不会变成 +0。
                try
                {
                    if (card.Id.Entry == "PERFECTED_STRIKE" && cardBoss != null)
                    {
                        var bash = cardBoss.Creature?.GetPower<CharacterBosses.Powers.BossBashPower>();
                        if (bash != null)
                        {
                            ApplyBashToPerfectStrike(card, cardBoss);
                        }
                    }
                }
                catch (Exception e) { Logger.Log("[CharBoss] 痛击加成计算失败: " + e.Message); }
                if (entry.DamageOverride > 0)
                {
                    try
                    {
                        card.DynamicVars.Damage.BaseValue = entry.DamageOverride;
                        Logger.Log($"[CharBoss] 剧本伤害覆盖：{card.Id.Entry} = {entry.DamageOverride}");
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"[CharBoss] 剧本伤害覆盖失败（{card.Id.Entry}）: {e.Message}");
                    }
                }
                if (entry.FreeThisTurn) { try { card.SetToFreeThisTurn(); } catch { /* 忽略 */ } }
                // 「保留」（计划妥当）：给这张牌加 CardKeyword.Retain ⇒ 回合结束不会被弃掉
                if (entry.Retain)
                {
                    try
                    {
                        MegaCrit.Sts2.Core.Commands.CardCmd.ApplyKeyword(
                            card, MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Retain);
                    }
                    catch (Exception e) { Logger.Log("[CharBoss] 加「保留」失败: " + e.Message); }
                }
                // 本回合的**真实费用**（王者之踢：4 → 3 → 2 → 1 → 0 是按抽到次数递减的，
                // 而我们的牌每回合新建实例 ⇒ 由剧本写死）。改的是这一张实例的 EnergyCost，
                // 所以卡面数字、规划扣费、真正花掉的能量三者一致。
                if (entry.CostThisTurn >= 0)
                {
                    try
                    {
                        int now = card.EnergyCost.GetWithModifiers(
                            MegaCrit.Sts2.Core.Entities.Cards.CostModifiers.All);
                        if (now != entry.CostThisTurn)
                        {
                            // ★ v0.2.342（用户 2026-11-03：「有的卡牌的费用没有被强制修改成我想要的
                            //   （例如跟踪没有被修改成三费）」）：
                            //   以前是 `AddThisCombat(-delta, reduceOnly: true)` —— 那个 **reduceOnly**
                            //   只允许**降**费！所以剧本要「涨价」的牌（跟踪 2 → 3）静默失败，
                            //   只有要降价的牌（3 → 0）才生效。
                            //   现在改成 `SetThisCombat(目标值, reduceOnly: false)`：直接把本场费用**设成**目标值。
                            card.EnergyCost.SetThisCombat(entry.CostThisTurn, false);
                            Logger.Log($"[CharBoss] 剧本费用覆盖：{card.Id.Entry} {now} → {entry.CostThisTurn}");
                        }
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"[CharBoss] 剧本费用覆盖失败（{card.Id.Entry}）: {e.Message}");
                    }
                }
                // ★ v0.2.305（用户 2026-11-02：「第六回合完美打击的伤害显著偏高…你是不是把他原本卡组中的
                //   打击数量重算了一遍？」）：完美打击的伤害 = 基础 + 每张「带 Strike 标签的牌」×N，
                //   而原版数的是 `PlayerCombatState.AllCards`（**所有牌堆**）。
                //   我们的手牌每回合重建 ⇒ 前几回合的剧本牌会堆在弃牌堆里、也被数进去 ⇒ 数字偏高。
                //   这里改成「按设计应有的张数」接管：基础 + 每张 × (卡组里的打击 + 到目前为止生成的打击复制)。
                // 完美打击：**不再接管数值**（用户 2026-11-02：卡面被改成「每有 1 张打击伤害 +0」）——
                // 改成上面 PurgeStaleStrikeCards 修牌堆计数，原版公式与文案原样保留。
                try { TakeOverPerfectedStrike(card, cardBoss); } catch { /* 忽略 */ }
                // ★ v0.2.516（用户 2026-11-09：「轰击储的金斧把玩家出牌也算上了」）：
                //   原版金斧的倍率是「本场战斗**所有人**打完的牌数」；这只 Boss 按设计只数她自己打的。
                //   把这一张实例的倍率回调换掉 ⇒ 卡面、意图预览、真实伤害三处同时正确。
                if (SafeEntryOf(card) == "GOLD_AXE")
                {
                    try { BossDamagePredictor.MakeGoldAxeCountOwnerOnly(card); } catch { /* 忽略 */ }
                }
                // ★ v0.2.522（用户 2026-11-09：「第 9 回合预览中的华丽收场不应该被标为不可打出
                //   （1~3 回合的确实应该是不可打出）」）：
                //   原版要求「抽牌堆为空」才能打出；以前是**出手那一刻**才清空 ⇒ 面板按当时的抽牌堆
                //   判定成「不可打出」并给费用画红杠。现在改成**造手牌时**就清空（每回合只清一次），
                //   于是标了 EmptyDrawPileFirst 的那一回合它从一进手就是可打出的；
                //   其它回合（只显示、没标这个旗标）仍然是不可打出。
                if (entry.EmptyDrawPileFirst)
                {
                    try { ClearDrawPileFor(pcs); } catch { /* 忽略 */ }
                }
                // 「燃烧契约」（二层杂耍战）：杂耍的复制牌自带「虚无 + 消耗」
                if (entry.JugglingCopy)
                {
                    try
                    {
                        MegaCrit.Sts2.Core.Commands.CardCmd.ApplyKeyword(
                            card, MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Ethereal);
                        MegaCrit.Sts2.Core.Commands.CardCmd.ApplyKeyword(
                            card, MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Exhaust);
                        Logger.Log($"[CharBoss] 燃烧契约：{card.Id.Entry} 是杂耍的复制 ⇒ 加上「虚无 + 消耗」" +
                                   "（她不会花能量打它，由惊逃在回合结束打出）");
                    }
                    catch (Exception e) { Logger.Log("[CharBoss] 加「虚无/消耗」失败: " + e.Message); }
                    // 顺便统计「名字里带打击」的复制牌张数（完美打击的成长按这个算，见 TakeOverPerfectedStrike）
                    try
                    {
                        var tags = card.Tags;
                        if (tags != null && tags.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardTag.Strike)
                            && cardBoss != null)
                        {
                            cardBoss.StrikeCopiesMade++;
                            cardBoss.StrikeCopyInstances.Add(card);
                            Logger.Log($"[CharBoss] 完美打击计数：打击类复制 +1 ⇒ 累计 {cardBoss.StrikeCopiesMade}");
                        }
                    }
                    catch { /* 忽略 */ }
                }
                // 格挡覆盖（遗传算法要 16 点：它的格挡是按"本局打出次数"成长的，而手牌每回合重建 ⇒ 写死）
                if (entry.BlockOverride > 0)
                {
                    try
                    {
                        card.DynamicVars["Block"].BaseValue = entry.BlockOverride;
                        Logger.Log($"[CharBoss] 剧本格挡覆盖：{card.Id.Entry} = {entry.BlockOverride}");
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"[CharBoss] 剧本格挡覆盖失败（{card.Id.Entry}）: {e.Message}");
                    }
                }
                // 「忧郁」（Melancholy）：原版把「每死一个单位 -N 费」记在**卡实例**上，
                // 而我们手牌每回合重建 ⇒ 减费会丢。这里按本场已死亡数量给新卡补上。
                if (SafeEntryOf(card) == "MELANCHOLY")
                {
                    try
                    {
                        int deaths = Patches.MelancholyDeathCounterPatch.DeathsIn(combat);
                        if (deaths > 0)
                        {
                            int per = 1;
                            try { per = Math.Max(1, (int)Math.Round((double)card.DynamicVars["Energy"].BaseValue)); }
                            catch { per = 1; }
                            card.EnergyCost.AddThisCombat(-(per * deaths), true);
                            Logger.Log($"[CharBoss] 忧郁：本场已死 {deaths} 个单位 → 这张牌补减 {per * deaths} 费" +
                                       $"（现在 {card.EnergyCost.GetWithModifiers(MegaCrit.Sts2.Core.Entities.Cards.CostModifiers.All)}）");
                        }
                    }
                    catch (Exception e) { Logger.Log("[CharBoss] 忧郁补减费失败: " + e.Message); }
                }
                // 爪击（Claw）的成长：原版把「本场所有爪击 +Increase 伤害」记在**每张卡实例**上，
                // 而这里每回合都是新建实例 ⇒ 成长会丢。Boss 身上记着累计值（`ClawBonus`），新卡补上。
                // 反编译 `Claw.BuffFromClawPlay`（private）：`DynamicVars.Damage.BaseValue += extra;`
                // ⇒ 这里直接改同一个变量即可（卡面与意图读的都是它）。
                if (cardBoss != null && cardBoss.ClawBonus > 0 && SafeEntryOf(card) == "CLAW")
                {
                    try
                    {
                        card.DynamicVars.Damage.BaseValue += cardBoss.ClawBonus;
                        Logger.Log($"[CharBoss] 爪击带上累计成长 +{cardBoss.ClawBonus} → " +
                                   $"本回合这张爪击伤害 {card.DynamicVars.Damage.BaseValue}");
                    }
                    catch (Exception e) { Logger.Log("[CharBoss] 爪击成长应用失败: " + e.Message); }
                }
                // 「王者之拳」（KinglyPunch）的成长：原版反编译 `AfterCardDrawn` ——
                //   「每当你抽到这张牌 → `DynamicVars["Increase"]`（未升级 4 / 升级 6）加到 Damage 上」，
                // 本场永久。我们每回合新建实例、抽牌钩子不触发 ⇒ 记在 Boss 身上：
                // **先**把这一次的 Increase 累计进去，**再**把累计值加到卡面上
                //（＝原版语义：抽到那一手时卡面就已经涨过了，包括本场第一次抽到。
                //  用户 2026-11-02 明确：「与原版相同，第一次抽到就该涨」）。
                if (cardBoss != null && SafeEntryOf(card) == "KINGLY_PUNCH")
                {
                    try
                    {
                        int inc = 0;
                        try
                        {
                            if (card.DynamicVars.TryGetValue("Increase", out var incVar) && incVar != null)
                                inc = Math.Max(0, (int)Math.Round((double)incVar.BaseValue));
                        }
                        catch { inc = 0; }

                        // ⚠ 顺序按原版：`AfterCardDrawn` 是「抽到的那一刻就涨」⇒
                        //    先把这一次的 Increase 累计进去，再把**累计值**加到卡面上
                        //    ⇒ 她**第一次**打出这张牌时，卡面就已经涨过一次了（和玩家抽到它的那一手一致）。
                        cardBoss.KinglyPunchBonus += inc;
                        if (cardBoss.KinglyPunchBonus > 0)
                            card.DynamicVars.Damage.BaseValue += cardBoss.KinglyPunchBonus;

                        Logger.Log($"[CharBoss] 王者之拳成长：这次 +{inc} ⇒ 累计 +{cardBoss.KinglyPunchBonus}；" +
                                   $"本回合这张牌伤害 {card.DynamicVars.Damage.BaseValue}");
                    }
                    catch (Exception e) { Logger.Log("[CharBoss] 王者之拳成长应用失败: " + e.Message); }
                }
                // ★ 打击战（用户 2026-11-07）：完美打击附魔「皇室认证」(`RoyallyApproved`)。
                //   以前只写了设计稿没落地 ⇒ 卡面上看不到附魔。
                // ★ 地狱狂徒（用户 2026-11-07）：这张牌由她的「地狱狂徒」自动免费打出（见 HellraiserPlays）
                if (entry.HellraiserFree && cardBoss != null)
                {
                    HellraiserPlays.MarkFree(cardBoss, card);
                    Logger.Log($"[CharBoss] 地狱狂徒：{card.Id.Entry} 已登记为本回合自动免费打出");
                }
                if (entry.EnchantRoyallyApproved)
                {
                    try
                    {
                        var royal = ModelDb.Enchantment<MegaCrit.Sts2.Core.Models.Enchantments.RoyallyApproved>();
                        if (royal != null)
                        {
                            var mutableRoyal = royal.ToMutable();
                            card.EnchantInternal(mutableRoyal ?? royal, 1m);
                            Logger.Log($"[CharBoss] 附魔「皇室认证」已加在 {card.Id.Entry} 上");
                        }
                        else Logger.Log("[CharBoss] 取不到附魔「皇室认证」（RoyallyApproved）");
                    }
                    catch (Exception e) { Logger.Log("[CharBoss] 附魔皇室认证失败: " + e.Message); }
                }                if (entry.EnchantSown)
                {
                    try
                    {
                        var sown = ModelDb.Enchantment<MegaCrit.Sts2.Core.Models.Enchantments.Sown>();
                        if (sown != null)
                        {
                            // EnchantInternal 要的是**可变实例**（canonical 会被拒或静默失败，
                            // 用户实测：钙化上没出现「播种」）
                            var mutable = sown.ToMutable();
                            card.EnchantInternal(mutable ?? sown, 1m);
                            string kind = mutable == null ? "canonical" : "mutable";
                            Logger.Log($"[CharBoss] 附魔「播种」已加在 {card.Id.Entry} 上（{kind}）");
                        }
                        else Logger.Log("[CharBoss] 取不到附魔「播种」（Sown）");
                    }
                    catch (Exception e) { Logger.Log("[CharBoss] 附魔「播种」失败: " + e.Message); }
                }
                // 附魔「灵魂之力」（SoulsPower）：三层易伤战剧本里那两张「主宰」
                if (entry.EnchantSouls)
                {
                    try
                    {
                        var souls = ModelDb.Enchantment<MegaCrit.Sts2.Core.Models.Enchantments.SoulsPower>();
                        if (souls != null)
                        {
                            var mutable = souls.ToMutable();
                            var applied = mutable ?? souls;
                            card.EnchantInternal(applied, 1m);
                            // ⚠ `EnchantInternal` 只把「卡 + 层数」绑上去，**不会**调用 `OnEnchant`
                            // （反编译确认）。而「灵魂之力」的实际效果就写在 `OnEnchant` 里：
                            //   protected override void OnEnchant() => CardCmd.RemoveKeyword(Card, Exhaust);
                            // 主宰（Dominate）本来带 `CardKeyword.Exhaust` ⇒ 不补这一步它不会掉「消耗」
                            // （用户 2026-10-25：「附魔灵魂之力的主宰应当失去消耗」）。
                            try
                            {
                                var onEnchant = applied.GetType().GetMethod("OnEnchant",
                                    System.Reflection.BindingFlags.Instance
                                    | System.Reflection.BindingFlags.NonPublic
                                    | System.Reflection.BindingFlags.Public);
                                onEnchant?.Invoke(applied, null);
                            }
                            catch (Exception e) { Logger.Log("[CharBoss] 灵魂之力 OnEnchant 失败: " + e.Message); }

                            Logger.Log($"[CharBoss] 附魔「灵魂之力」已加在 {card.Id.Entry} 上" +
                                       $"（{(mutable == null ? "canonical" : "mutable")}，" +
                                       $"消耗={(card.Keywords?.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Exhaust) == true ? "有" : "无")}）");
                        }
                        else Logger.Log("[CharBoss] 取不到附魔「灵魂之力」（SoulsPower）");
                    }
                    catch (Exception e) { Logger.Log("[CharBoss] 附魔「灵魂之力」失败: " + e.Message); }
                }
                // 附魔「漩涡」（Spiral）：三层「事件机」第 5 回合的「防御」。
                // 反编译 `Spiral.EnchantPlayCount` = 原次数 + `Times`（1）⇒ 打出两次；没有 OnEnchant 要补。
                if (entry.EnchantSpiral)
                {
                    try
                    {
                        var spiral = ModelDb.Enchantment<MegaCrit.Sts2.Core.Models.Enchantments.Spiral>();
                        if (spiral != null)
                        {
                            var mutable = spiral.ToMutable();
                            card.EnchantInternal(mutable ?? spiral, 1m);
                            Logger.Log($"[CharBoss] 附魔「漩涡」已加在 {card.Id.Entry} 上（打出 2 次）");
                        }
                        else Logger.Log("[CharBoss] 取不到附魔「漩涡」（Spiral）");
                    }
                    catch (Exception e) { Logger.Log("[CharBoss] 附魔「漩涡」失败: " + e.Message); }
                }
                // 「疯狂科学」（MadScience，TinkerTime 事件牌）：它的 `Type` = `TinkerTimeType` 字段，
                // 而那个字段**默认是 `CardType.None`**（枚举 0）——
                // 反编译 `OnPlay` 里 `switch (TinkerTimeType) { Attack / Skill / Power; default: throw }`
                // ⇒ 不显式设成类型的话，打出这张牌会直接抛异常。
                // 用户 2026-11-03 定稿：「我要的是**技能牌**，效果是获得格挡，然后将一张随机牌添加到手牌，
                // 本回合其耗能为 0（**拦掉它的印牌，因为我已经提前写好了**）」
                // ⇒ `Skill`（= 获得 8 格挡、TargetType.Self）+ rider `None`（不自己印牌，
                //   那张「印出来的牌」由剧本以 `From = "print"` 摆在手上、0 费）。
                //   ⚠ 不能让 rider 跑 Chaos：它在 `CardFactory.GetDistinctForCombat` 返回空时会 `.First()` 抛异常。
                if (SafeEntryOf(card) == "MAD_SCIENCE")
                {
                    try
                    {
                        if (card is MegaCrit.Sts2.Core.Models.Cards.MadScience ms)
                        {
                            ms.TinkerTimeType = MegaCrit.Sts2.Core.Entities.Cards.CardType.Skill;
                            ms.TinkerTimeRider = MegaCrit.Sts2.Core.Models.Events.TinkerTime.RiderEffect.None;
                            Logger.Log("[CharBoss] 疯狂科学：按技能牌打出（获得格挡），rider = None" +
                                       $"（印的那张牌由剧本给；现在 Type={ms.Type}）");
                        }
                    }
                    catch (Exception e) { Logger.Log("[CharBoss] 疯狂科学设类型失败: " + e.Message); }
                }
                if (!pcs.Hand.Cards.Contains(card)) pcs.Hand.AddInternal(card, -1, true);
                // 「保留不出」的牌（DisplayOnly + Retain，且本回合不会被打出）记一份，整手建完挪到最右
                // v0.2.522：不再要求 Retain（华丽猎的「保留不出」不加保留关键字，靠计划妥当）\r\n                if (entry.DisplayOnly && !entry.AutoPlayed) keptToRight.Add(card);
            }
            catch (Exception e)
            {
                Logger.Log($"[CharBoss] 剧本卡 {canonical.Id.Entry} 造牌失败: {e.Message}");
            }
        }

        // ★ v0.2.501 / v0.2.507 / v0.2.508（用户 2026-11-08）：蛇咬猎的循环成长 —— 第 9 回合起**每一回合**
        //   额外塞 (1 + (回合号−9)/4) 张「保留 + 不出」的蛇咬（第 9~12 回合 +1、13~16 +2、17~20 +3…），
        //   让她的「飞镖」多段伤害。
        //   位置：从**循环内**搬到这里（原来挂在 `turn.Cards[0]` 上处理时就塞牌 ⇒ 那几张蛇咬会插在手牌中间，
        //   用户 2026-11-08：「这张加的蛇咬能不能放在所有会被打出的牌的右边？」）。
        if (cardBoss != null
            && turn.Cards.Count > 0
            && string.Equals(cardBoss.SpawnVariantKey, "SILENT_SNAKEBITE", StringComparison.Ordinal)
            && cardBoss.TurnNumber >= 9)
        {
            try
            {
                int extra = 1 + (cardBoss.TurnNumber - 9) / 4;
                var snakeCanonical = ModelDb.GetById<CardModel>(
                    ModelDb.GetId(typeof(MegaCrit.Sts2.Core.Models.Cards.Snakebite)));
                if (snakeCanonical != null && extra > 0)
                {
                    for (int k = 0; k < extra; k++)
                    {
                        var extraCard = combat.CreateCard(snakeCanonical, shadow);
                        if (extraCard == null) break;
                        try
                        {
                            MegaCrit.Sts2.Core.Commands.CardCmd.ApplyKeyword(
                                extraCard, MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Retain);
                        }
                        catch { /* 忽略 */ }
                        pcs.Hand.AddInternal(extraCard);
                        keptToRight.Add(extraCard);
                    }
                    Logger.Log($"[CharBoss] 蛇咬猎：第 {cardBoss.TurnNumber} 回合 ⇒ 循环成长再塞 {extra} 张「保留的蛇咬」（放最右）");
                }
            }
            catch (Exception e0) { Logger.Log("[CharBoss] 蛇咬猎循环成长失败: " + e0.Message); }
        }

        // ★ v0.2.521（用户 2026-11-09：「应该让因为奇巧打出的防御不夹在不会打出的牌中间」）：
        //   奇巧牌**会**（被丢弃后自动）打出去，所以它要和「本回合真的要打」的牌一样待在左边；
        //   而「保留不出 / 只显示 / 被丢弃但不会打出」的牌全部挪到最右。
        //   做法：先按剧本顺序建手，最后把「不会打出的」统一 MoveToBottomInternal（稳定分区）。
        var toRight = new List<CardModel>(keptToRight);
        foreach (var entry in turn.Cards)
        {
            try
            {
                bool willPlay = entry.Sly
                                || (!entry.DisplayOnly && !entry.HoldInstead && !entry.DiscardInstead
                                    && !entry.Burned && !entry.StampedePlay && !entry.JugglingCopy);
                if (willPlay) continue;
                // 找出这张剧本牌对应的手牌实例（按类型匹配，同名多张的取还没被收的）
                foreach (var handCard in pcs.Hand.Cards)
                {
                    if (handCard == null) continue;
                    if (toRight.Contains(handCard)) continue;
                    if (handCard.GetType() != MultiplayerCardSwap.Resolve(entry.Card, cardBoss!)) continue;
                    if (entry.Sly) continue;                 // 奇巧牌留在左边
                    toRight.Add(handCard);
                    break;
                }
            }
            catch { /* 单张失败不影响其它 */ }
        }

        // 把「保留不出」的牌统一挪到手牌最右（`MoveToBottomInternal` = 移到 `_cards` 末尾 = 画面最右）
        if (keptToRight.Count > 0)
        {
            try
            {
                foreach (var kept in toRight)
                {
                    if (kept == null) continue;
                    if (!pcs.Hand.Cards.Contains(kept)) continue;
                    pcs.Hand.MoveToBottomInternal(kept);
                }
            }
            catch (Exception e) { Logger.Log("[CharBoss] 手牌排序（保留牌挪到最右）失败: " + e.Message); }
        }
        // ★ v0.2.346（用户 2026-11-03 再确认）：「谋杀」是**同一张牌**（她一直把它保留在手里），
        //   ⇒ 加伤**只增不减**：起点只在它**第一次进她手牌**时钉一次，之后整场都不再重置，
        //   每多抽一张就 +1（打出后再进手也接着之前的累计走）。
        //
        //   背景：反编译 `Murder.CalculatedDamage = CalculationBase(1) + ExtraDamage × 本场抽到的牌数`，
        //   数的是战斗历史里 `CardDrawnEntry.Actor == 她` 的条数；她的牌是剧本每回合**重建**的、
        //   从不走原版抽牌 ⇒ 历史里一条都没有。所以「抽到的牌数」由我们自己记账
        //   （`CardsDrawnThisCombat` = 每回合手牌数累加），起点存 `MurderDrawBaseline`（-1 = 还没钉过）。
        if (cardBoss != null)
        {
            // ★ v0.2.523：先算「这一回合真正新进她手牌的张数」（谋杀加伤用）：
            //   排除夜魇印出来的（NotDrawn）与上一回合就被计划妥当留在手里的同类型保留牌。
            try
            {
                var keptThisTurn = new HashSet<Type>();
                int fresh = 0;
                foreach (var sc in turn.Cards)
                {
                    if (sc.DisplayOnly && !sc.AutoPlayed) keptThisTurn.Add(sc.Card);
                    if (sc.NotDrawn) continue;
                    // ★ v0.2.524（用户给的算法 1+5+8+3+3 ⇒ 20）：上一回合就被「计划妥当」留在手上的
                    //   同类型牌**不算新进手牌**（哪怕这一回合它换了角色：上回合保留、这回合被丢弃）。
                    if (cardBoss.LastKeptTypes.Contains(sc.Card)) continue;
                    fresh++;
                }
                cardBoss.LastKeptTypes = keptThisTurn;
                bool countsDrawn = false;
                try { countsDrawn = cardBoss.Def?.MurderCountsDrawnCardsFor(cardBoss.ActIndex) == true; }
                catch { countsDrawn = false; }
                if (countsDrawn)
                {
                    cardBoss.MurderDrawnCounter += fresh;
                    Logger.Log($"[CharBoss] 谋杀记账：本回合新进手牌 {fresh} 张 ⇒ 累计 {cardBoss.MurderDrawnCounter}");
                }
            }
            catch { /* 忽略 */ }

            try
            {
                int handNow = pcs.Hand.Cards.Count;
                int total = Math.Max(0, cardBoss.CardsDrawnThisCombat) + Math.Max(0, handNow);

                MegaCrit.Sts2.Core.Models.CardModel? murder = null;
                foreach (var candidate in pcs.Hand.Cards)
                {
                    if (candidate == null) continue;
                    if (SafeEntryOf(candidate) != "MURDER") continue;
                    murder = candidate;
                    break;
                }

                if (murder != null)
                {
                    if (!cardBoss.MurderSeenInHand)
                    {
                        // 第一次进手：起点归零（**进手那一手不算加伤**）
                        cardBoss.MurderSeenInHand = true;
                        // ★ v0.2.521（用户 2026-11-09「华丽猎的谋杀不按抽牌数加伤」）：
                        //   这套设计的谋杀只在指定回合出现、每次都是新建实例 ⇒ 归零会让伤害永远很小；
                        //   变体声明了 MurderCountsDrawnCards 时改成**按本场她已经拿到过的牌数起步**。
                        bool countDrawn = false;
                        try { countDrawn = cardBoss.Def?.MurderCountsDrawnCardsFor(cardBoss.ActIndex) == true; }
                        catch { countDrawn = false; }
                        // ★ v0.2.523：按「每回合新进手牌」累计（见 CharBossMonster.MurderDrawnCounter）
                        if (countDrawn) cardBoss.MurderDrawProgress = Math.Max(0, cardBoss.MurderDrawnCounter);
                        else cardBoss.MurderDrawProgress = 0;
                        Logger.Log($"[CharBoss] 谋杀：第一次进她手牌 ⇒ 加伤起点归零" +
                                   $"（累计拿到过 {Math.Max(0, cardBoss.CardsDrawnThisCombat)} 张，之后只在她仍保留在手时累积）");
                    }
                    else
                    {
                        // ★ v0.2.363（用户 2026-11-04：「谋杀的伤害好像偏高了，不是只在手牌中被保留时抽牌才增伤」）：
                        //   只有**这一手她手里确实还有谋杀**的时候，才把这一手的抽牌记进加伤进度。
                        //   以前是无条件 `total += handNow`（还带着一个整场不重置的起点）⇒
                        //   「谋杀被打出去、不在手里的那几个回合」抽的牌也被算进去了 ⇒ 伤害偏高。
                        cardBoss.MurderDrawProgress += Math.Max(0, handNow);
                    }

                    int since = Math.Max(0, cardBoss.MurderDrawProgress);
                    int baseVal = 1, perCard = 1;
                    try
                    {
                        if (murder.DynamicVars.TryGetValue("CalculationBase", out var b) && b != null)
                            baseVal = Math.Max(0, (int)Math.Round((double)b.BaseValue));
                        if (murder.DynamicVars.TryGetValue("ExtraDamage", out var e) && e != null)
                            perCard = Math.Max(0, (int)Math.Round((double)e.BaseValue));
                    }
                    catch { /* 读不到就用 1 / 1 */ }

                    int target = baseVal + perCard * since;

                    // ★ v0.2.525（用户 2026-11-09 最终口径）：华丽猎的谋杀**直接按回合查表**
                    //   （第 4 回合 20、第 8 回合 42、之后每回合 +7 ⇒ 第 11 回合 63、第 14 回合 84…）。
                    //   理由见 `GrandFinaleSilentScript.MurderDamageForTurn` 的注释：
                    //   记账口径（本场累计 / 每回合新进手牌）连改两版都被用户否掉，他直接给了数字。
                    try
                    {
                        if (string.Equals(cardBoss.SpawnVariantKey, "SILENT_GRAND_FINALE", StringComparison.Ordinal))
                        {
                            int byTable = GrandFinaleSilentScript.MurderDamageForTurn(cardBoss.TurnNumber);
                            Logger.Log($"[CharBoss] 谋杀：华丽猎按回合查表 ⇒ 第 {cardBoss.TurnNumber} 回合 = {byTable}" +
                                       $"（记账口径本来会算出 {target}，已忽略）");
                            target = byTable;
                        }
                    }
                    catch { /* 查表失败就走记账口径 */ }

                    murder.DynamicVars["CalculationBase"].BaseValue = target;
                    Logger.Log($"[CharBoss] 谋杀：她**保留在手**期间抽到 {since} 张 ⇒ 伤害 {baseVal} + {perCard}×{since} = {target}" +
                               $"（卡面仍写「每抽一张 +{perCard}」；不在手的那几回合不计）");
                }
                else if (cardBoss.MurderSeenInHand)
                {
                    // 谋杀不在她手上（已经打出去 / 还没进手）：这一手的抽牌**不计**入加伤进度。
                    // 只在数值变化时记一行，避免每回合刷屏。
                    Logger.Log($"[CharBoss] 谋杀：这一手不在她手里 ⇒ 本回合的手牌不计入加伤进度" +
                               $"（进度仍为 {Math.Max(0, cardBoss.MurderDrawProgress)}）");
                }

                cardBoss.CardsDrawnThisCombat = total;
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 谋杀接管失败（按原版算）: " + e.Message);
            }
        }
        var names = pcs.Hand.Cards.Select(c => c.Id.Entry + (c.IsUpgraded ? "+" : string.Empty));
        Logger.Log($"[CharBoss] 剧本手牌({pcs.Hand.Cards.Count}): [{string.Join(",", names)}]");

        MoveStrikesToTop(pcs);
    }

    /// <summary>这张牌的 entry（取不到返回空串）。</summary>
    private static string SafeEntryOf(CardModel card)
    {
        try { return card.Id.Entry; } catch { return string.Empty; }
    }

    /// <summary>
    /// 在**当回合剧本**里找出这张（已经造出来的）牌对应的剧本条目。
    ///
    /// 用途：剧本里有些效果写在**条目上**而不是牌上（万物一心的返手名单、羽化的印牌名单），
    /// 而执行发生在「这张牌打完」的那一刻（<c>CharBossMonster.PlayOne</c>）——
    /// 那时手上只有卡实例，得反查回条目。判据按**类型**（多人换牌后的类型也算）。
    /// </summary>
    public static ScriptCard? FindEntry(Monsters.CharBossMonster? boss, CardModel? card)
    {
        try
        {
            if (boss == null || card == null) return null;
            var turn = boss.Def?.ScriptForAct(boss.ActIndex)?.TurnAt(boss.TurnNumber);
            if (turn == null) return null;
            var type = card.GetType();
        foreach (var entry in turn.Cards)
            {
                if (entry?.Card == null) continue;
                var want = MultiplayerCardSwap.ResolveQuiet(entry.Card, boss) ?? entry.Card;
                if (type == want) return entry;
            }
        }
        catch { /* 取不到就当没有 */ }
        return null;
    }

    /// <summary>
    /// 从他的手牌/抽牌堆/弃牌堆里搬出那张**真实的**君王之剑（保留铸造后的伤害）。
    /// 找不到返回 null（调用方再新建）。
    /// </summary>
    /// <summary>当前局的进阶等级（拿不到按 0 算），给「按张升级」（<c>ScriptCard.UpgradeMinAscension</c>）用。</summary>
    internal static int AscensionOf(Player? shadow)
    {
        try
        {
            var run = shadow?.RunState;
            if (run != null) return run.AscensionLevel;
        }
        catch { /* 忽略 */ }
        try
        {
            var run = MegaCrit.Sts2.Core.Runs.RunManager.Instance?.DebugOnlyGetState();
            if (run != null) return run.AscensionLevel;
        }
        catch { /* 忽略 */ }
        return 0;
    }

    private static CardModel? TakeExistingBlade(PlayerCombatState pcs, Type cardType)
    {
        if (cardType != typeof(MegaCrit.Sts2.Core.Models.Cards.SovereignBlade)) return null;
        try
        {
            foreach (var pile in pcs.AllPiles)
            {
                if (pile == null) continue;
                foreach (var card in pile.Cards.ToList())
                {
                    if (card is not MegaCrit.Sts2.Core.Models.Cards.SovereignBlade) continue;
                    pile.RemoveInternal(card, true);
                    Logger.Log($"[CharBoss] 剧本取用已有的君王之剑（伤害 {BladeDamage(card)}）");
                    return card;
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 取用已有君王之剑失败: " + e.Message);
        }
        return null;
    }

    private static string BladeDamage(CardModel card)
    {
        try
        {
            if (card.DynamicVars.TryGetValue("Damage", out var v) && v != null) return v.BaseValue.ToString("0");
        }
        catch { /* 忽略 */ }
        return "?";
    }

    /// <summary>
    /// 让「抽 N 张」一定抽到**打击**。
    ///
    /// 原版抽牌是 <c>CardPileCmd.DrawInternal</c> → <c>drawPile.Cards.FirstOrDefault()</c>（牌堆顶 = 下标 0），
    /// 但**牌堆张数不够时会走 <c>ShuffleIfNecessary</c> 把弃牌堆随机洗回来** ——
    /// 只提前排序是没用的（用户实测：第 12 回合抽到荒疫打击、第 17 回合抽到碎骨）。
    /// 所以这里在真正要抽之前现场处理：先把"弃牌堆洗回牌堆"这件事自己做掉（确定性的），
    /// 再把所有打击搬到牌堆顶。
    /// </summary>
    public static void EnsureStrikesOnTop(PlayerCombatState pcs, int need)
    {
        try
        {
            int drawCount = pcs.DrawPile.Cards.Count;
            if (drawCount < Math.Max(1, need))
            {
                // 自己把弃牌堆搬回牌堆（不走原版随机洗牌，顺序可控）
                foreach (var card in pcs.DiscardPile.Cards.ToList())
                {
                    pcs.DiscardPile.RemoveInternal(card, true);
                    pcs.DrawPile.AddInternal(card, -1, true);
                }
            }

            var strikes = new List<CardModel>();
            foreach (var card in pcs.DrawPile.Cards)
            {
                try
                {
                    if (card != null && card.Id.Entry.Contains("STRIKE", StringComparison.Ordinal))
                        strikes.Add(card);
                }
                catch { /* 忽略 */ }
            }

            for (int i = strikes.Count - 1; i >= 0; i--)
            {
                pcs.DrawPile.RemoveInternal(strikes[i], true);
                pcs.DrawPile.AddInternal(strikes[i], 0, true);   // 下标 0 = 牌堆顶
            }

            Logger.Log($"[CharBoss] 抽牌前整理牌堆: 牌堆 {pcs.DrawPile.Cards.Count} 张，" +
                       $"把 {strikes.Count} 张打击排到最前（这次要抽 {need} 张）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 整理抽牌堆失败: " + e.Message);
        }
    }

    /// <summary>
    /// 把摸牌堆里的**打击**移到最前面（＝牌堆顶）。
    ///
    /// 为什么：她的剧本偶尔会让牌「抽 1 张」（鞭打 = <c>Scourge</c>，`Cards` 变量 1~2），
    /// 而她的摸牌堆是开局随机的 ⇒ 抽上来的可能是任意牌
    /// （用户实测：第三回合该抽到打击，却抽到了碎骨）。设计上抽牌就该抽打击，这里固定住。
    /// </summary>
    private static void MoveStrikesToTop(PlayerCombatState pcs)
    {
        try
        {
            var strikes = new List<CardModel>();
            foreach (var card in pcs.DrawPile.Cards)
            {
                try
                {
                    if (card != null && card.Id.Entry.Contains("STRIKE", StringComparison.Ordinal))
                        strikes.Add(card);
                }
                catch { /* 忽略 */ }
            }
            if (strikes.Count == 0) return;

            // 倒序插到最前面，保持原来的相对顺序
            for (int i = strikes.Count - 1; i >= 0; i--)
            {
                pcs.DrawPile.RemoveInternal(strikes[i], true);
                pcs.DrawPile.AddInternal(strikes[i], 0, true);
            }
            Logger.Log($"[CharBoss] 摸牌堆已把 {strikes.Count} 张打击排到最前（抽牌固定抽打击）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 调整摸牌堆失败: " + e.Message);
        }
    }

    /// <summary>
    /// 把牌堆里的某张牌（按 ID 找：摸牌堆 → 弃牌堆 → 手牌）挪到**摸牌堆顶**并返回它。
    /// 找不到返回 null。用途见 <see cref="BossTurn.FreePlayTopEntry"/>。
    /// </summary>
    public static CardModel? MoveCardToDrawTop(PlayerCombatState pcs, string entry)
    {
        try
        {
            var found = FindIn(pcs.DrawPile.Cards, entry)
                        ?? FindIn(pcs.DiscardPile.Cards, entry)
                        ?? FindIn(pcs.Hand.Cards, entry);
            if (found == null) return null;

            if (pcs.DrawPile.Cards.Contains(found)) pcs.DrawPile.RemoveInternal(found, true);
            else if (pcs.DiscardPile.Cards.Contains(found)) pcs.DiscardPile.RemoveInternal(found, true);
            else pcs.Hand.RemoveInternal(found, true);

            pcs.DrawPile.AddInternal(found, 0, true);   // 下标 0 = 牌堆顶
            return found;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 挪牌到摸牌堆顶失败: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// 在**消耗牌堆**里按 entry 找一张牌。找不到返回 null。
    /// 用途见 <see cref="BossTurn.FreePlayExhaustEntry"/>（彼岸咆哮：回合结束时从消耗牌堆自己打出）。
    /// </summary>
    public static CardModel? FindInExhaust(PlayerCombatState pcs, string entry)
    {
        try { return pcs == null ? null : FindIn(pcs.ExhaustPile.Cards, entry); }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 在消耗牌堆里找牌失败: " + e.Message);
            return null;
        }
    }
    private static CardModel? FindIn(IEnumerable<CardModel> cards, string entry)
    {
        foreach (var card in cards)
        {
            try { if (card != null && card.Id.Entry == entry) return card; }
            catch { /* 忽略 */ }
        }
        return null;
    }

    private static bool IsIn(Type[]? set, Type card)
    {
        if (set == null) return false;
        foreach (var t in set) if (t == card) return true;
        return false;
    }
}
