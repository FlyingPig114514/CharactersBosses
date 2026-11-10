using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// Boss 定义的登记表。★用户设计 Boss 主要编辑这里。
///
/// 5 个原版角色各占一个定义，各有 4 档（第 1/2/3/终 幕）HP、能量、手牌数。
/// 铁甲战士三档已按「易伤流」主题设计好（样板）；
/// 其余 4 个角色的卡组留空 → 由 <see cref="BossLoadout"/> 用该角色起始卡组占位，
/// 等你把自己的设计填进 DeckByAct 即可，不需要改其它文件。
/// </summary>
public static class BossDefs
{
    // ─────────── 铁甲战士的三套卡组（按幕） ───────────
    // 第 1 幕：易伤流 —— 交付的样板
    private static readonly List<Type> IroncladAct1 = new()
    {
        typeof(Bash), typeof(Bash),          // 痛击：伤害 + 易伤
        typeof(Tremble),                     // 战栗：纯易伤
        typeof(Thunderclap),                 // 闪电霹雳：AOE 伤害 + 易伤
        typeof(Taunt),                       // 挑衅：格挡 + 易伤
        typeof(Uppercut),                    // 上勾拳：伤害 + 虚弱 + 易伤
        typeof(Break),                       // 破击：伤害 + 易伤
        typeof(Bully),                       // 欺凌：每层易伤额外伤害
        typeof(Cruelty),                     // 残酷：易伤敌人额外受伤（能力）
        typeof(Dominate),                    // 主宰：每层易伤换力量（能力）
        typeof(IronWave),                    // 铁斩波：格挡 + 伤害
        // 注意：巨像(Colossus) 这类「本回合」自保牌对 Boss 是失效的，见下面 SkipCards 的说明
        typeof(StrikeIronclad), typeof(StrikeIronclad),
        typeof(DefendIronclad), typeof(DefendIronclad),
    };

    // 第 3 幕：恶魔形态 + 重锤的力量爆发，配壁垒格挡引擎
    private static readonly List<Type> IroncladAct3 = new()
    {
        typeof(DemonForm),
        typeof(Inflame), typeof(Inflame),
        typeof(Bludgeon), typeof(Bludgeon),
        typeof(TwinStrike),
        typeof(Uppercut),
        typeof(Bash),
        typeof(Impervious), typeof(Impervious),
        typeof(Barricade),                   // 壁垒：格挡不清空
        typeof(Juggernaut),                  // 势不可当：格挡换伤害
        typeof(StrikeIronclad), typeof(StrikeIronclad),
        typeof(DefendIronclad), typeof(DefendIronclad),
    };

    /// <summary>铁甲战士：易伤流 → 易伤+力量 → 力量爆发。</summary>
    public static readonly BossDefinition Ironclad = new()
    {
        Key = "IRONCLAD_BOSS",
        VisualScene = "ironclad",
        CharacterType = typeof(MegaCrit.Sts2.Core.Models.Characters.Ironclad),
        // 基础血量 = 角色起始血量（统一，不按幕递增；逐幕变强只靠涅奥祝福：拾起 +120、每进一幕 +180）。运行时直接读 CharacterModel.StartingHp
        Hp = new[] { 80, 80, 80, 80 },
        HandSize = new[] { 5, 5, 6, 6 },
        // 第一幕：用户 2026-10-23 设计的「愤怒战」（Core/IroncladAngerScript.cs）
        //   遗物 燃烧之血 / 涅奥的祝福 / 锚 / 卡戎之灰；进阶 8 升坚毅 + 奥利哈钢，进阶 9 升烙印。
        //   特殊效果「群情激奋」：她打出愤怒 → 所有玩家的弃牌堆各得一张复制品；
        //                         玩家打出愤怒 → 她本回合失去 1 点力量（黑暗镣铐的 debuff）。
        DeckByAct = new List<Type>?[]
        {
            IroncladAngerScript.Deck(),
            BleedIroncladScript.Deck(),      // 第二幕：放血战（用户 2026-10-27 设计）
            VulnIroncladScript.Deck(),   // 第三幕：**易伤战**（用户 2026-10-24）
            VulnIroncladScript.Deck(),   // 终幕沿用第三幕
        },
        RelicsByAct = new List<Type>?[]
        {
            IroncladAngerScript.Relics(),                                            // 愤怒战
            // 二层「铁甲战士」还没有自己的剧本（贪心 AI），遗物只有这两件 + 涅奥的祝福。
            BleedIroncladScript.Relics(),                                          // 放血战
            VulnIroncladScript.Relics(),                                             // 易伤战
            VulnIroncladScript.Relics(),
        },
        ScriptByAct = new BossScript?[]
        {
            IroncladAngerScript.Build(), BleedIroncladScript.Build(), VulnIroncladScript.Build(), null,
        },
        // 三层铁甲战士的**第二套设计「巨石战」**（用户 2026-10-27；第一套是「易伤战」）。
        // 与三层静默猎手（刀猎 / 炼药猎）同一套机制：以**房间遭遇**为准，拿不到遭遇才按种子 50/50。
        VariantsByAct = new BossVariant?[]
        {
            // ★ 一层铁甲战士的**第二套设计「打击战」**（Key `IRONCLAD_STRIKE`，用户 2026-11-07）。
            //   特殊效果「痛击」见 Powers/BossBashPower.cs，剧本见 Core/IroncladStrikeScript.cs。
            new BossVariant
            {
                Key = "IRONCLAD_STRIKE",
                Enabled = Core.BossLineups.StrikeActive,
                Deck = Core.IroncladStrikeScript.Deck(),
                Relics = Core.IroncladStrikeScript.Relics(),
                Script = Core.IroncladStrikeScript.Build(),
                SpecialLoc = "IRONCLAD_BOSS.bash",
                SpecialPower = typeof(Powers.BossBashPower),
                // 用户 2026-11-07：进阶 8 = 升级岩石铠甲 + 获得护喉甲；进阶 9 = 升级完美打击、预备打击
                AscensionUpgrades = new List<(int, Type)>
                {
                    (8, typeof(MegaCrit.Sts2.Core.Models.Cards.StoneArmor)),
                    (9, typeof(MegaCrit.Sts2.Core.Models.Cards.PerfectedStrike)),
                    (9, typeof(MegaCrit.Sts2.Core.Models.Cards.SetupStrike)),
                },
                AscensionRelics = new List<(int, Type)>
                {
                    (8, typeof(MegaCrit.Sts2.Core.Models.Relics.Gorget)),
                },
            },
            // 二层铁甲战士的**第二套设计「杂耍战」**（用户 2026-11-02；第一套是「放血战」）。
            // 特殊效果「**燃烧契约**」：杂耍生成的攻击牌获得「虚无 + 消耗」——
            // 拦掉原版杂耍的复制（Patches/JugglingNoClonePatch），改由剧本提前把复制牌
            // 摆进她的手牌并加上这两个关键字（ScriptCard.JugglingCopy），
            // 回合结束由「惊逃」自动打出（Core/StampedePlays.cs）。
            new BossVariant
            {
                Key = "IRONCLAD_JUGGLER",
                Enabled = Core.BossLineups.JugglerActive,
                Deck = Core.JugglerIroncladScript.Deck(),
                Relics = Core.JugglerIroncladScript.Relics(),
                Script = Core.JugglerIroncladScript.Build(),
                SpecialLoc = "IRONCLAD_BOSS.pact",
                SpecialPower = typeof(Powers.BossBurningPactPower),
                // 进阶（用户 2026-11-02，v0.2.321 削弱后重排）：8 = 升级无惧疼痛、**邪眼**；
                // 9 = 升级**完美打击**、彼岸咆哮（原来的预备打击/踩踏已从剧本下线）。
                AscensionUpgrades = new List<(int, Type)>
                {
                    (8, typeof(FeelNoPain)),
                    (8, typeof(EvilEye)),
                    (9, typeof(PerfectedStrike)),
                    (9, typeof(HowlFromBeyond)),
                },
            },
            new BossVariant
            {
                Key = "IRONCLAD_BOULDER",
                Enabled = Core.BossLineups.BoulderActive,
                Deck = BoulderIroncladScript.Deck(),
                Relics = BoulderIroncladScript.Relics(),
                Script = BoulderIroncladScript.Build(),
                SpecialLoc = "IRONCLAD_BOSS.boulder",          // 特殊效果「原始力量」
                SpecialPower = typeof(Powers.BossPrimalForcePower),
                // 进阶（用户 2026-10-27 / 2026-11-07）：8 = 升级「无惧疼痛」+ 获得遗物「奥利哈钢」；
                //   9 = 获得遗物「金刚杵」＋升级「彼岸咆哮」（用户 2026-11-07 追加）。
                // 变体自己的表 ⇒ 不会漏到同层的「易伤战」身上。
                AscensionUpgrades = new List<(int, Type)>
                {
                    (8, typeof(FeelNoPain)),
                    (9, typeof(HowlFromBeyond)),
                },
                AscensionRelics = new List<(int, Type)>
                {
                    (8, typeof(Orichalcum)),
                    (9, typeof(Vajra)),
                },
            },
            null,
        },
        SpecialLocByAct = new string?[] { "IRONCLAD_BOSS.anger", "IRONCLAD_BOSS.rending", "IRONCLAD_BOSS.dominate", null },
        SpecialPowerByAct = new Type?[]
        {
            typeof(Powers.IroncladRagePower), typeof(Powers.BossRendingPower), typeof(Powers.DominateWeakPower), null,
        },
        // 让她的遗物真的收到战斗内钩子（卡戎之灰：消耗牌→3 伤；奥利哈钢：回合结束无格挡→+6 格挡）。
        // 影子 Player 的遗物默认收不到这些钩子，需要 Patches/BossRelicHookBridge 手动补跑。
        FireBossRelicHooks = true,
        // 进阶（用户设计）：
        //   · 第一幕（愤怒战）：8 = 升级坚毅 + 获得护喉甲；9 = 升级烙印
        //     （2026-10-24 用户削弱：进阶 8 的奥利哈钢 → **护喉甲**）；
        //   · 第三幕（易伤战，2026-10-25 大改版）：8 = 升级岩石铠甲 + **获得护喉甲**；9 = 升级主宰。
        AscensionUpgradesByAct = new List<(int, int, Type)>
        {
            (8, 1, typeof(TrueGrit)),
            (9, 1, typeof(Brand)),
            // 第二幕「放血战」（用户 2026-10-27）：8 = 血墙升级。
            // ⚠ 2026-10-28 第二次削弱：绯红披风已从剧本/卡组下线 ⇒ 那一条也已去掉（没有那张牌可升）。
            // ⚠ 2026-10-28 第三次削弱（用户：「进阶9不再升级撕裂，改为升级烙印和突破」→ 再改「进阶9不再升级突破，其它不动」）：
            //   撕裂保持 1 层（进阶不再变强，正是要的效果），进阶 9 **只升「烙印」**，突破不升。
            (8, 2, typeof(BloodWall)),
            (9, 2, typeof(Brand)),
            (8, 3, typeof(StoneArmor)),
            (9, 3, typeof(Dominate)),
        },
        AscensionRelicsByAct = new List<(int, int, Type, Type?)>
        {
            (8, 1, typeof(Gorget), null),   // 护喉甲：战斗开始获得 4 层覆甲（原为奥利哈钢）
            (8, 3, typeof(Gorget), null),   // 易伤战进阶 8：护喉甲
        },
        // 低语耳环那句「这样才对」：`SayFirstTurnLine` 会先检查她身上**真的有没有**这件遗物
        // ⇒ 只有带低语耳环的「易伤战」（三层）会说，愤怒战/二层的铁甲战士不会。
        FirstTurnLine = ("relics", "WHISPERING_EARRING.approval"),
        Priority = new List<Type>
        {
            // 能力牌先落地
            typeof(DemonForm), typeof(Cruelty), typeof(Dominate),
            typeof(Barricade), typeof(Juggernaut), typeof(Inflame),
            // 铺易伤
            typeof(Tremble), typeof(Taunt), typeof(Bash), typeof(Thunderclap),
            typeof(Uppercut), typeof(Break),
            // 收益
            typeof(Bully),
            // 基础输出
            typeof(TwinStrike), typeof(Bludgeon), typeof(StrikeIronclad),
            // 防御最后
            typeof(Impervious), typeof(IronWave), typeof(DefendIronclad),
        },
        // 巨像的实现在「敌方回合结束时递减」（ColossusPower.AfterSideTurnEnd 里写死 side == Enemy）。
        // 玩家在自己回合打出时正好撑过玩家回合 + 整个敌方回合；但 Boss 是在自己回合末尾打出，
        // 下一秒敌方回合就结束 → 立刻到期，玩家的攻击完全没被减伤。这类「本回合」自保牌不要给 Boss 用。
        SkipCards = new List<Type> { typeof(Colossus) },
    };

    // ───────────────── 其余 4 个角色：框架已就位，卡组待你设计 ─────────────────
    // DeckByAct 全空 → BossLoadout 用该角色起始卡组占位（能打，但没有设计感）。
    // 你只要填 DeckByAct / RelicsByAct，立刻生效。

    public static readonly BossDefinition Silent = new()
    {
        Key = "SILENT_BOSS",
        VisualScene = "silent",
        CharacterType = typeof(MegaCrit.Sts2.Core.Models.Characters.Silent),
        // 基础血量 = 角色起始血量 70（不按幕递增；逐幕变强只靠涅奥祝福：拾起 +120、每进一幕 +180）
        // 第二幕「涂毒猎」= 70 + 150×2 = 370
        Hp = new[] { 70, 70, 70, 70 },
        Energy = new[] { 3, 3, 3, 4 },
        HandSize = new[] { 5, 5, 6, 6 },
        // 第一幕：**奇巧猎**（用户 2026-10-26 晚设计，奇巧/弃牌轴，剧本见 Core/SilentSlyScript.cs）
        // 第二幕：涂毒猎（毒流，剧本见 Core/SilentPoisonScript.cs）
        // 第三幕：刀猎（小刀流，剧本见 Core/SilentScript.cs；能量由剧本逐回合写死 2）
        DeckByAct = new List<Type>?[]
        {
            SilentSlyScript.Deck(), SilentPoisonScript.Deck(), SilentScript.Deck(), null,
        },
        RelicsByAct = new List<Type>?[]
        {
            SilentSlyScript.Relics(), SilentPoisonScript.Relics(), SilentScript.Relics(), null,
        },
        ScriptByAct = new BossScript?[]
        {
            SilentSlyScript.Build(), SilentPoisonScript.Build(), SilentScript.Build(), null,
        },
        // 第一幕「奇巧猎」的被动（速行者）：她的抽牌卡要**真的抽**，
        // 抽完再把新进手牌放回抽牌堆顶（手牌仍等于剧本）。见 BossDefinition.KeepsScriptedHandByAct。
        KeepsScriptedHandByAct = new[] { true, false, false, false },
        // 特殊效果按幕区分：
        //   · 第一幕「奇巧猎」= 命运同担（她每回合第一次弃牌 → 玩家下回合开始弃 1 张）；
        //   · 第三幕「刀猎」= 袖里乾坤；
        //   · 第二幕「涂毒猎」= 带毒刺击（中毒改成玩家回合结束结算、可格挡，未格挡伤害转移 1 点中毒给她）。
        SpecialLocByAct = new string?[] { "SILENT_BOSS.sly", "SILENT_BOSS.special_act2", "SILENT_BOSS.special", null },
        SpecialPowerByAct = new Type?[]
        {
            typeof(Powers.SharedFatePower), typeof(Powers.PoisonStrikePower), typeof(Powers.SleeveTrickPower), null,
        },
        // 进阶加成（用户设计）：
        //   · **奇巧猎（第一幕）**进阶 8：**中和**升级；进阶 9：**连续反弹**、**铭记死亡**升级
        //     （用户 2026-10-26）；
        //   · 涂毒猎（第二幕）进阶 8：中和升级；进阶 9：**连续反弹**、毒雾升级
        //     （2026-10-25 用户订正：原来写的是「弹跳药瓶」，但她的 23 张卡组里**没有**这张牌 ⇒ 那条升级空转，
        //       改成卡组里真有的「连续反弹」；牌名 entry 见 Core/SilentPoisonScript.cs 第 2 回合那张）
        //   · 刀猎（第三幕）进阶 8：**中和升级**（用户 2026-10-25 削弱：原来是「多一件遗物 风的女儿」，
        //     现在中和在剧本里是未升级的，进阶 8 才升；风的女儿**不再出现**）；
        //                 进阶 9：斗篷与匕首、隐秘匕首、刀刃之舞升级
        // 必须**按幕**配：中和在两套卡组里都有，不分幕会漏到刀猎身上。
        AscensionUpgradesByAct = new List<(int, int, Type)>
        {
            (8, 1, typeof(Neutralize)),          // 奇巧猎：进阶 8 中和+
            (9, 1, typeof(Ricochet)),            // 奇巧猎：进阶 9 连续反弹+
            (9, 1, typeof(FlickFlack)),          // 奇巧猎：进阶 9 翻越撑击+（2026-11-07 用户：铭记死亡已移除）
            (8, 2, typeof(Neutralize)),
            (9, 2, typeof(Ricochet)),
            (9, 2, typeof(NoxiousFumes)),
            (8, 3, typeof(Neutralize)),
            (9, 3, typeof(CloakAndDagger)),
            (9, 3, typeof(HiddenDaggers)),
            (9, 3, typeof(BladeDance)),
        },
        // ⚠ 刀猎（第三幕）原来进阶 8 会多给一件「风的女儿」（插在钗前面）；用户 2026-10-25 把进阶 8
        //   改成「升级中和」⇒ 这件遗物不再发放，所以这张表里已经没有猎手的条目。
        AscensionRelicsByAct = new List<(int, int, Type, Type?)>
        {
        },
        // 三层静默猎手的**第二套设计「炼药猎」**（群友投稿、用户 2026-10-25 实验）：
        //   刀猎 / 炼药猎 **50/50**，由「本局种子 + salt」定（`BossLineups.SilentBrewerAt3`，两端一致）。
        //   特殊效果「调制」= 8 个药水栏位 + 战斗中使用药水。HealPerPotion 原本是 5（每瓶回 5 血），
        //   **用户 2026-10-26 要求削弱：去掉「喝药回血」这一条** ⇒ 现在填 0（回血代码保留在
        //   BossPotions.HealAfterPotionAsync，填 >0 就能一键恢复）。
        VariantsByAct = new BossVariant?[]
        {
            // ⚠ 下标 = **幕下标（0 起）**：0 = 一层、1 = 二层、2 = 三层。
            //   v0.2.340（用户 2026-11-03 实测 `fight SILENT_SNECKO_BOSS_ENCOUNTER` 打出来的却是涂毒猎）：
            //   v0.2.337 加「蛇眼猎」时把这一项**插到了数组最前面**（= 一层），
            //   于是二层等于没有变体（退回主设计 = 涂毒猎），而「炼药猎」也被挤到了下标 3（= 四层）。
            //   现在按幕摆正，并加上注释免得下次再插错位置。
            // ★ 一层静默猎手的**第二套设计「蛇咬猎」**（Key `SILENT_SNAKEBITE`，用户 2026-11-08）。
            //   特殊效果「群蛇形态」见 Powers/BossSnakeFormPower.cs，剧本见 Core/SnakeBiteSilentScript.cs。
            new BossVariant
            {
                Key = "SILENT_SNAKEBITE",
                Enabled = Core.BossLineups.SnakeBiteActive,
                Deck = Core.SnakeBiteSilentScript.Deck(),
                Relics = Core.SnakeBiteSilentScript.Relics(),
                Script = Core.SnakeBiteSilentScript.Build(),
                SpecialLoc = "SILENT_BOSS.snake_form",
                SpecialPower = typeof(Powers.BossSnakeFormPower),
                // 用户 2026-11-08：进阶 8 = 升级残影、逃脱计划；进阶 9 = 升级背刺、飞镖
                AscensionUpgrades = new List<(int, Type)>
                {
                    (8, typeof(Blur)),
                    (8, typeof(EscapePlan)),
                    (9, typeof(Backstab)),
                    (9, typeof(Flechettes)),
                },
            },
            // 二层静默猎手的**第二套设计「蛇眼猎」**（Key `SILENT_SNECKO`，用户 2026-11-03；第一套是「涂毒猎」）。
            // 异蛇之眼轴：卡牌费用随机 —— 原版随机被拦掉，改由剧本逐张写死费用（`ScriptCard.CostThisTurn`）。
            // 特殊效果「计划妥当」：每回合第一次抽到 3 费牌 → 保留（剧本里 `ScriptCard.Retain`）。
            new BossVariant
            {
                Key = "SILENT_SNECKO",
                Enabled = Core.BossLineups.SneckoActive,
                Deck = Core.SnakeEyesSilentScript.Deck(),
                Relics = Core.SnakeEyesSilentScript.Relics(),
                Script = Core.SnakeEyesSilentScript.Build(),
                SpecialLoc = "SILENT_BOSS.well_laid",
                SpecialPower = typeof(Powers.BossWellLaidPlansPower),
                // 进阶（用户 2026-11-04）：8 = 升级究极防御、闪躲翻滚；9 = 升级匕首雨、冲刺。
                // （升级是**按类型**的 ⇒ 剧本里所有同名牌一起升级。）
                AscensionUpgrades = new List<(int, Type)>
                {
                    (8, typeof(UltimateDefend)),
                    (8, typeof(DodgeAndRoll)),
                    (9, typeof(DaggerSpray)),
                    (9, typeof(Dash)),
                },
            },
            new BossVariant
            {
                Key = "SILENT_BREWER",
                // 以**房间遭遇**为准（控制台 `fight SILENT_BREWER_BOSS_ENCOUNTER` 也才会真的是炼药猎），
                // 拿不到遭遇时才退回按种子 —— 见 BossLineups.BrewerActive 的说明。
                Enabled = Core.BossLineups.BrewerActive,
                Deck = SilentBrewerScript.Deck(),
                Relics = SilentBrewerScript.Relics(),
                Script = SilentBrewerScript.Build(),
                SpecialLoc = "SILENT_BOSS.brewer",   // 特殊效果「调制」
                SpecialPower = typeof(Powers.BrewModPower),
                Potions = SilentBrewerScript.InitialPotions(),
                HealPerPotion = 0,      // 用户 2026-10-26：削弱，去掉「喝药回 5 血」
                PotionSlots = 8,
                // 遗物：`SilentBrewerScript.Relics()` 里第 1 件是蛇之戒指（起始）、
                // 第 2 件固定是「涅奥的祝福」—— 所有 Boss 都这样（用户 2026-10-27 口径），
                // 所以走主定义的 `CarriesNeowBlessing`（默认 true），这里不用单独特判。
            },
            null,
        },
        // ★ 三层静默猎手的**第三套设计「华丽猎」**（Key SILENT_GRAND_FINALE，用户 2026-11-09）。
        //   一幕只能放一套 VariantsByAct（下标 2 已给炼药猎），第三套必须挂在 ExtraVariants：
        //   判定顺序 = 先 VariantsByAct 那一套（炼药猎），再按本表顺序；取第一个 Enabled() 为真的。
        //   特殊效果「压制」= Powers/BossSuppressionPower（她打出华丽收场 ⇒ 所有玩家抽牌堆顶塞虚空），
        //   剧本 = Core/GrandFinaleSilentScript.cs（10 回合，之后 8~10 循环）。
        ExtraVariants = new Dictionary<int, List<BossVariant>>
        {
            [2] = new List<BossVariant>
            {
                new BossVariant
                {
                    Key = "SILENT_GRAND_FINALE",
                    Enabled = Core.BossLineups.GrandFinaleActive,
                    Deck = Core.GrandFinaleSilentScript.Deck(),
                    Relics = Core.GrandFinaleSilentScript.Relics(),
                    Script = Core.GrandFinaleSilentScript.Build(),
                    SpecialLoc = "SILENT_BOSS.suppression",       // 特殊效果「压制」
                    SpecialPower = typeof(Powers.BossSuppressionPower),
                    // 用户 2026-11-09：「拦掉原版的抽牌」—— 剧本手牌写死了，杂技/肾上腺素不再真抽
                    BlocksVanillaDraw = true,
                    // 用户 2026-11-09：「拦掉夜魇印牌，因为印的杂技我都在剧本里写好了」
                    BlocksNightmareClones = true,
                    // 用户 2026-11-09：「她的谋杀没有计算她抽牌的数量而增加伤害（这个和蛇眼猎的不一样）」
                    MurderCountsDrawnCards = true,
                    // 进阶（用户 2026-11-09）：8 = 升级中和；9 = 升级背刺、钢铁风暴、隐秘匕首。
                    // （升级是**按类型**的 ⇒ 剧本里所有同名牌一起升级。）
                    AscensionUpgrades = new List<(int, Type)>
                    {
                        (8, typeof(Neutralize)),
                        (9, typeof(Backstab)),
                        (9, typeof(StormOfSteel)),
                        (9, typeof(HiddenDaggers)),
                    },
                },
            },
        },
    };

    public static readonly BossDefinition Defect = new()
    {
        Key = "DEFECT_BOSS",
        VisualScene = "defect",
        CharacterType = typeof(MegaCrit.Sts2.Core.Models.Characters.Defect),
        // 基础血量 = 角色起始血量 75（不按幕递增）；三层 = 75 + 120 + 180 + 180 = 555
        // 能量 3 = 定义 3 + 遗物(祝福-1、低语耳环+1)
        Hp = new[] { 75, 75, 75, 75 },
        Energy = new[] { 3, 4, 3, 5 },
        HandSize = new[] { 5, 5, 6, 6 },
        // 物理机第 4/8 回合的「超越光速」会抽 1 张牌，而剧本已经把「抽上来的那张」写好了
        // ⇒ 拦掉原版抽牌（用户 2026-10-24：「你把原版的抽牌和野性的回手都拦了吧，都用我已经写好的」）。
        BlocksVanillaDraw = true,
        // 第一幕：**黑球机**（暗黑充能球轴，剧本见 Core/DarkOrbBossScript.cs；用户 2026-10-26）
        // 第二幕：**物理机**（爪击 / 0 费牌轴，剧本见 Core/ClawDefectScript.cs）
        // 第三幕：能力机（剧本见 Core/DefectScript.cs）
        DeckByAct = new List<Type>?[]
        {
            DarkOrbBossScript.Deck(), ClawDefectScript.Deck(), DefectScript.Deck(), null,
        },
        RelicsByAct = new List<Type>?[]
        {
            DarkOrbBossScript.Relics(), ClawDefectScript.Relics(), DefectScript.Relics(), null,
        },
        ScriptByAct = new BossScript?[]
        {
            DarkOrbBossScript.Build(), ClawDefectScript.Build(), DefectScript.Build(), null,
        },
        // 特殊效果按幕区分：
        //   · 第一幕「黑球机」= **冷却剂**（战斗开始给玩家 3 张冷光+；打出冷光 ⇒ 她的黑球这回合不打被动）
        //   · 第二幕「物理机」= 人工合成（战斗开始时获得 3 层人工制品）
        //   · 第三幕「能力机」= 偏差认知
        SpecialLocByAct = new string?[]
        {
            "DEFECT_BOSS.dark", "DEFECT_BOSS.synthesis", "DEFECT_BOSS.special", null,
        },
        SpecialPowerByAct = new Type?[]
        {
            typeof(Powers.BossCoolantPower), typeof(Powers.SynthesisPower), typeof(Powers.DeviationPower), null,
        },
        // 进阶加成（用户设计）：
        //   · 第一幕（黑球机，用户 2026-10-26）：进阶 8 升级「飞跃」+ 获得遗物「不安油灯」；
        //     进阶 9 升级「趁势打击」（弹回已按用户 2026-10-27 取消）。
        //   · 第二幕（物理机）：进阶 8 升级「启动流程」「充电」；进阶 9 升级「冷却剂」「一心化万」「超能光束」
        //     （数据磁盘已改成常备遗物，不再是进阶奖励；爪击也不再在进阶 9 升级）；
        //   · 第三幕（能力机）：进阶 8 充电、究极防御、高速脱离升级；进阶 9 多一件「数据磁盘」。
        AscensionUpgradesByAct = new List<(int, int, Type)>
        {
            (8, 1, typeof(MegaCrit.Sts2.Core.Models.Cards.Leap)),            // 黑球机：进阶 8 飞跃+
            // 黑球机进阶 9：**只有**「趁势打击」升级（用户 2026-10-27：弹回不再升级）
            (9, 1, typeof(MomentumStrike)),                                  // 黑球机：进阶 9 趁势打击+
            (8, 2, typeof(BootSequence)),
            (8, 2, typeof(ChargeBattery)),
            (9, 2, typeof(Coolant)),
#if STS2_111
            (9, 2, typeof(OneForAll)),                                  // 一心化万（111 独有的能力牌）
#endif
            (9, 2, typeof(Hyperbeam)),
            (8, 3, typeof(ChargeBattery)),
            (8, 3, typeof(UltimateDefend)),
            (8, 3, typeof(BoostAway)),
        },
        AscensionRelicsByAct = new List<(int, int, Type, Type?)>
        {
            // 黑球机（一层）：进阶 8 获得「不安油灯」= 每场战斗第一次给敌人上负面时，效果翻倍
            (8, 1, typeof(MegaCrit.Sts2.Core.Models.Relics.UnsettlingLamp), null),
            (9, 3, typeof(DataDisk), null),   // 能力机（三层）：进阶 9
        },
        // 第一回合出完牌后说一句低语耳环的「这样才对」（红字 + 抖动，气泡紫色）
        FirstTurnLine = ("relics", "WHISPERING_EARRING.approval"),
        VariantsByAct = new BossVariant?[]
        {
            // ★ 一层故障机器人的**第二套设计「电球机」**（Key `DEFECT_THUNDER`，用户 2026-11-05）：
            //   闪电球 / 集中轴；特殊效果「引雷针」见 Powers/BossLightningRodPower.cs，
            //   剧本见 Core/DefectThunderScript.cs。
            new BossVariant
            {
                Key = "DEFECT_THUNDER",
                Enabled = Core.BossLineups.ThunderActive,
                Deck = Core.DefectThunderScript.Deck(),
                Relics = Core.DefectThunderScript.Relics(),
                Script = Core.DefectThunderScript.Build(),
                SpecialLoc = "DEFECT_BOSS.thunder",
                SpecialPower = typeof(Powers.BossLightningRodPower),
                // 用户 2026-11-05：进阶 8 = 升级缓冲、充电；进阶 9 = 升级扫荡射线、集中打击。
                AscensionUpgrades = new List<(int, Type)>
                {
                    (8, typeof(MegaCrit.Sts2.Core.Models.Cards.Buffer)),
                    (8, typeof(ChargeBattery)),
                    (9, typeof(SweepingBeam)),
                    (9, typeof(FocusedStrike)),
                },
            },
            // ⚠ 下标 = **幕下标（0 起）**：0 = 第一幕（电球机，上面那段）、1 = 第二幕、2 = 第三幕。
            //   ★ v0.2.447（用户 2026-11-06「怎么还是打能力机？」）—— 又是同一个坑：
            //   加「电球机」时我在这一项前面多写了一个 `null,`，于是「事件机」被挤到下标 3（= 第四幕），
            //   `VariantsForAct(2)` 读到的是 `VariantsByAct[2] == null` ⇒ 事件机**连判定都不会被问**，
            //   三层故障永远落回主设计（能力机）。静默猎手那边 v0.2.337 犯过一模一样的错。
            //   现在只有**一个** null（第二幕 = 物理机没有备选设计），事件机回到下标 2。
            // ★ 二层故障机器人的**第二套设计「彩虹机」**（Key `DEFECT_RAINBOW`，用户 2026-11-07）：
            //   充能球 / 集中轴；特殊效果「彩虹」见 Powers/BossRainbowPower.cs，
            //   剧本见 Core/DefectRainbowScript.cs。
            new BossVariant
            {
                Key = "DEFECT_RAINBOW",
                Enabled = Core.BossLineups.RainbowActive,
                Deck = Core.DefectRainbowScript.Deck(),
                Relics = Core.DefectRainbowScript.Relics(),
                Script = Core.DefectRainbowScript.Build(),
                SpecialLoc = "DEFECT_BOSS.rainbow",
                SpecialPower = typeof(Powers.BossRainbowPower),
                // 用户 2026-11-07：进阶 8 = 升级启动流程、冷却剂；
                // 用户 2026-11-08 改：进阶 9 = 升级**冰雹风暴、雷霆**（原来是「冰雹风暴、同步」）。
                AscensionUpgrades = new List<(int, Type)>
                {
                    (8, typeof(BootSequence)),
                    (8, typeof(Coolant)),
                    (9, typeof(Hailstorm)),
                    (9, typeof(Thunder)),
                },
            },
            // 三层故障机器人的**第二套设计「事件机」**（Key `DEFECT_EVENT`，用户 2026-11-03；
            // 第一套是「能力机」`DefectScript`）。
            // 特殊效果「**一心化万**」：万物一心只返 0 费**攻击**牌，且每返一张 ⇒ **所有单位** +1 力量。
            // 剧本见 Core/EventMachineScript.cs，落点见 Core/EventMachineMechanics.cs 与
            // Patches/BossAllForOneFilterPatch.cs；设计记录见 docs/plans/boss-snake-eyes-and-event-machine.md。
            new BossVariant
            {
                Key = "DEFECT_EVENT",
                Enabled = Core.BossLineups.EventMachineActive,
                Deck = Core.EventMachineScript.Deck(),
                Relics = Core.EventMachineScript.Relics(),
                Script = Core.EventMachineScript.Build(),
                SpecialLoc = "DEFECT_BOSS.one_for_all",        // 特殊效果「一心化万」
                SpecialPower = typeof(Powers.BossAllForOnePower),
                // 进阶（用户 2026-11-04）：8 = 升级所有防御；9 = 升级疯狂进食。
                // （升级按类型 ⇒ 剧本里的防御全部变「防御+」。）
                AscensionUpgrades = new List<(int, Type)>
                {
                    (8, typeof(DefendDefect)),
                    (9, typeof(FeedingFrenzy)),
                },
                // 用户 2026-11-03：「因为大蘑菇，额外给他添加 20 最大生命值」——
                // 大蘑菇的 +20 是「拾起时」钩子给的，影子玩家收不到 ⇒ 写进变体的 HpBonus。
                HpBonus = 20,
            },
        },
        // ★ v0.2.419：三层故障机器人有**两套非主设计**（事件机 / 状态机）。
        //   `VariantsByAct` 一幕只放得下一套（事件机），第二套（状态机）必须挂在 ExtraVariants，
        //   否则 `ActiveVariant` 永远读不到它 —— 那正是「fight DEFECT_EVENT_BOSS_ENCOUNTER 打成能力机」的真根因。
        //   判定顺序：先 VariantsByAct 那一套，再按本表顺序；取第一个 Enabled() 为真的。
        ExtraVariants = new Dictionary<int, List<BossVariant>>
        {
            [2] = new List<BossVariant>
            {
                // ★ 三层故障机器人的**第三套设计「状态机」**（Key `DEFECT_STATUS`，用户 2026-11-05）。
                //   原本是二层的「同步机」，用户 2026-11-05 决定挪到三层当第 3 个故障 Boss
                //   （"它的伤害已经比所有三层 Boss 都高了，削弱到二层强度只能重做，但我挺喜欢这个设计"），
                //   顺带吃到三层血量，并削弱特殊效果（首次抽到状态牌不再给她力量）。
                //   特殊效果「同步」见 Powers/BossSyncPower.cs，剧本见 Core/StatusDefectScript.cs。
                new BossVariant
                {
                    Key = "DEFECT_STATUS",
                    Enabled = Core.BossLineups.StatusActive,
                    Deck = Core.StatusDefectScript.Deck(),
                    Relics = Core.StatusDefectScript.Relics(),
                    Script = Core.StatusDefectScript.Build(),
                    SpecialLoc = "DEFECT_BOSS.status",
                    SpecialPower = typeof(Powers.BossSyncPower),
                    // 用户 2026-11-05：进阶 8 = 升级「强撑」「高速脱离」；进阶 9 = 升级「污秽攻击」+ 获得「数据磁盘」
                    AscensionUpgrades = new List<(int, Type)>
                    {
                        (8, typeof(MegaCrit.Sts2.Core.Models.Cards.FightThrough)),
                        (8, typeof(MegaCrit.Sts2.Core.Models.Cards.BoostAway)),
                        (9, typeof(MegaCrit.Sts2.Core.Models.Cards.GunkUp)),
                    },
                    AscensionRelics = new List<(int, Type)>
                    {
                        (9, typeof(MegaCrit.Sts2.Core.Models.Relics.DataDisk)),
                    },
                },
            },
        },
        // ★待设计：第 1 幕冰球 / 第 2 幕雷球
    };

    public static readonly BossDefinition Necrobinder = new()
    {
        Key = "NECROBINDER_BOSS",
        VisualScene = "necrobinder",
        CharacterType = typeof(MegaCrit.Sts2.Core.Models.Characters.Necrobinder),
        // 基础血量 66；第一幕 = 66 + 120 = 186
        // ⚠ 第三幕「根除骨」= 66 + 480 + **31（布质果实）** = 577。那 31 点靠遗物的「拾起时」钩子
        //    对影子玩家不触发（实测只有 516），所以直接写进第三幕的定义值，别再让遗物重复加。
        Hp = new[] { 66, 66, 97, 66 },
        Energy = new[] { 3, 3, 4, 4 },
        HandSize = new[] { 5, 5, 6, 6 },
        // 第二幕「灵魂骨」的「拖延+」：**下回合 +2 能量要真的到账**
        // （3 基础 + 2 = 5，正好够第 2 回合的「守墓人+ 死者苏生+死亡行军」；
        //  其余两幕的剧本自己把能量写死，维持「只删不发」的老行为）
        HonorsNextTurnEnergyActs = new[] { 2 },
        // 第一幕：灾厄骨（NecrobinderScript）
        // 第二幕：**灵魂骨**（SoulBoneScript，「灵魂绑定」= 玩家抽牌也算她抽牌）
        // 第三幕：根除骨（NecrobinderAct3Script）
        DeckByAct = new List<Type>?[]
        {
            NecrobinderScript.Deck(), SoulBoneScript.Deck(), NecrobinderAct3Script.Deck(), null,
        },
        RelicsByAct = new List<Type>?[]
        {
            NecrobinderScript.Relics(), SoulBoneScript.Relics(), NecrobinderAct3Script.Relics(), null,
        },
        ScriptByAct = new BossScript?[]
        {
            NecrobinderScript.Build(), SoulBoneScript.Build(), NecrobinderAct3Script.Build(), null,
        },
        // 特殊效果按幕：第一幕「时候未到」；第二幕「灵魂绑定」；第三幕「预借时间」
        SpecialLocByAct = new string?[]
        {
            "NECROBINDER_BOSS.special", "NECROBINDER_BOSS.soul_bound",
            "NECROBINDER_BOSS.borrowed_time", null,
        },
        SpecialPowerByAct = new Type?[]
        {
            typeof(Powers.NotYetPower), typeof(Powers.BossSoulBoundPower),
            typeof(Powers.BorrowedTimeBossPower), null,
        },
        // 进阶加成（用户设计，2026-10-24 用户要求：**原来"所有幕都吃"的四条只对第一幕生效**）：
        //   · 第一幕灾厄骨：进阶 8 厄运之衣+；进阶 9 友谊+、无处可逃+、荒疫打击+
        //   · 进阶 8（第二幕）：升级「死亡之舞」「违逆」；进阶 9（第二幕）：升级「死亡行军」「切断」
        //   · 进阶 8（第三幕）：升级「死亡之舞」（根除骨自己也带这张）
        //   · 进阶 9（第三幕）：升级「友谊」「钙化」
        AscensionUpgradesByAct = new List<(int, int, Type)>
        {
            (8, 1, typeof(Shroud)),
            (9, 1, typeof(Friendship)),
            (9, 1, typeof(NoEscape)),
            (9, 1, typeof(BlightStrike)),
            (8, 2, typeof(DanseMacabre)),
            (8, 2, typeof(Defy)),
            (9, 2, typeof(DeathMarch)),
            (9, 2, typeof(Severance)),
            (8, 3, typeof(DanseMacabre)),
            (9, 3, typeof(Friendship)),
            (9, 3, typeof(Calcify)),
        },
        // ⚠ 这里**故意留空**：用户 2026-10-24 要求把「厄运之衣 / 友谊 / 无处可逃 / 荒疫打击」
        //   从「所有幕」收窄成「只有第一幕」，所以它们全部搬到上面的 AscensionUpgradesByAct(…, 1, …)。
        //   注意连带影响：`CharBossMonster.EffectiveDrainOrder` 里「进阶 9 跳过无处可逃那一对」的判定
        //   也改成只看第一幕 —— 否则第二幕（灵魂骨）会用第一幕的表却少升一对。
        AscensionUpgrades = new List<(int, Type)>
        {
        },
        // 进阶遗物（**按幕**）：
        //   · 第一幕灾厄骨：进阶 8 的「不死符文」（以前写在全局 AscensionRelics 里，
        //     结果第三幕的根除骨也吃到了一件 —— 那不是同一个 Boss，用户实测）
        //   · 第三幕根除骨：进阶 8 的「骇人头盔」
        AscensionRelicsByAct = new List<(int, int, Type, Type?)>
        {
            (8, 1, typeof(UndyingSigil), null),
            (8, 3, typeof(IntimidatingHelmet), null),
        },
        // 三层亡灵契约师的**第二套设计「养手骨」**（Key `NECROBINDER_OSTY`，用户 2026-11-03；
        // 第一套是「根除骨」）。奥斯提轴：召唤 → 奥斯提攻击牌。
        // 特殊效果「不死」（`Powers/BossOstyUndyingPower`）：奥斯提不会死；它替她挡未格挡的攻击伤害
        // 直到只剩 1 点生命，之后由她替它承担（与「为你而死」兼容、不递归）。
        VariantsByAct = new BossVariant?[]
        {
            // ⚠ 这里以前**误粘了一份「一层铁甲战士第二套设计 打击战 IRONCLAD_STRIKE」**。
            //   危害（玩家 2026-11-08 实测截图：「boss 杂交了，意图是打击战但人和初始遗物是亡灵契约师」）：
            //   `StrikeActive()` 只看「本场遭遇/阵容里有没有 IRONCLAD_STRIKE」，**不看是谁的变体**
            //   ⇒ 一层阵容里排到打击战时，**亡灵契约师这一档也命中** ⇒ 灾厄骨拿了打击战的
            //   卡组 / 遗物（燃烧之血、护喉甲）/ 特殊效果（痛击）/ 剧本。
            //   第一幕 = **血肉骨**（下面这块，用户 2026-11-10；剧本 <c>Core/FleshBoneScript.cs</c>）；
            //   第二幕 = 虚无骨（用户 2026-11-09）；第三幕 = 养手骨。
            new BossVariant
            {
                Key = "NECROBINDER_FLESH",
                Enabled = Core.BossLineups.FleshActive,
                Deck = Core.FleshBoneScript.Deck(),
                Relics = Core.FleshBoneScript.Relics(),
                Script = Core.FleshBoneScript.Build(),
                SpecialLoc = "NECROBINDER_BOSS.devour_life",   // 特殊效果「吞噬生命」
                SpecialPower = typeof(Powers.BossDevourLifePower),
                // 进阶（用户 2026-11-10）：8 = 升级护卫、吸引仇恨（多人换上的骸骨军团同样吃这一条：
                //   进阶升级是**按卡类型**生效的，剧本里那张吸引仇恨换成骸骨军团后仍按同一套走）；
                //   9 = 升级苦难、摧残、出击。
                AscensionUpgrades = new List<(int, Type)>
                {
                    (8, typeof(Bodyguard)),
                    (8, typeof(PullAggro)),
                    (9, typeof(Misery)),
                    (9, typeof(Debilitate)),
                    (9, typeof(Unleash)),
                },
            },
            new BossVariant
            {
                Key = "NECROBINDER_ETHEREAL",
                Enabled = Core.BossLineups.EtherealActive,
                Deck = Core.EtherealBoneScript.Deck(),
                Relics = Core.EtherealBoneScript.Relics(),
                Script = Core.EtherealBoneScript.Build(),
                SpecialLoc = "NECROBINDER_BOSS.void_call",       // 特殊效果「虚空之唤」
                SpecialPower = typeof(Powers.BossVoidCallPower),
                // 进阶（用户 2026-11-09）：8 = 升级灰烬之灵、违逆；9 = 升级友谊。
                AscensionUpgrades = new List<(int, Type)>
                {
                    (8, typeof(SpiritOfAsh)),
                    (8, typeof(Defy)),
                    (9, typeof(Friendship)),
                },
            },
            new BossVariant
            {
                Key = "NECROBINDER_OSTY",
                Enabled = Core.BossLineups.OstyActive,
                Deck = Core.OstyKeeperScript.Deck(),
                Relics = Core.OstyKeeperScript.Relics(),
                Script = Core.OstyKeeperScript.Build(),
                SpecialLoc = "NECROBINDER_BOSS.undying",       // 特殊效果「不死」
                SpecialPower = typeof(Powers.BossOstyUndyingPower),
                // 进阶（用户 2026-11-03）：8 = 升级死亡之舞、来生 + 获得遗物「骇人头盔」；
                //                          9 = 升级致死性。
                // ★ v0.2.367（用户 2026-11-04）：「削弱，把钙化换成来生」⇒ 钙化已从她的卡组/剧本里移除，
                //   第 5 回合现在是**两张来生**；进阶 8 的来生升级按**类型**生效 ⇒ 两张一起升。
                AscensionUpgrades = new List<(int, Type)>
                {
                    (8, typeof(DanseMacabre)),
                    (8, typeof(Afterlife)),
                    (9, typeof(Lethality)),
                },
                AscensionRelics = new List<(int, Type)>
                {
                    (8, typeof(IntimidatingHelmet)),
                },
            },
            null,
        },
    };

    public static readonly BossDefinition Regent = new()
    {
        Key = "REGENT_BOSS",
        VisualScene = "regent",
        CharacterType = typeof(MegaCrit.Sts2.Core.Models.Characters.Regent),
        // 基础血量 = 角色起始血量 75（不按幕递增）
        Hp = new[] { 75, 75, 75, 75 },
        // 能量：**基础 3**（用户订正）。涅奥的祝福 −1 ⇒ 实际 2；有加费遗物才到 3
        //（第三幕无色储君带了天鹅绒颈圈 +1 ⇒ 3）。第二幕的逐回合能量由剧本自己写死。
        Energy = new[] { 3, 3, 3, 3 },
        HandSize = new[] { 6, 5, 6, 6 },
        // 第一幕：碎屑储君（生成碎屑 + 彗星特殊效果，剧本见 Core/DebrisRegentScript.cs）
        // 第二幕：铸剑储君（铸造/君王之剑，剧本见 Core/RegentScript.cs；能量由剧本逐回合写死）
        // 第三幕：无色储君（生成牌轴 + 共鸣，剧本见 RegentScript.BuildAct3）
        DeckByAct = new List<Type>?[]
        {
            DebrisRegentScript.Deck(), RegentScript.Deck(), RegentScript.DeckAct3(), null,
        },
        RelicsByAct = new List<Type>?[]
        {
            DebrisRegentScript.Relics(), RegentScript.Relics(), RegentScript.RelicsAct3(), null,
        },
        ScriptByAct = new BossScript?[]
        {
            DebrisRegentScript.Build(), RegentScript.Build(), RegentScript.BuildAct3(), null,
        },
        // 特殊效果按幕区分：
        //   · 第一幕「碎屑储君」= 彗星（生成碎屑改判给玩家、打出碎屑清负面）
        //   · 第二幕「铸剑储君」= 何人僭越
        //   · 第三幕「无色储君」= 共鸣（玩家生成卡牌时也触发储君的创世之柱）
        SpecialLocByAct = new string?[]
        {
            "REGENT_BOSS.comet", "REGENT_BOSS.special", "REGENT_BOSS.resonance", null,
        },
        SpecialPowerByAct = new Type?[]
        {
            typeof(Powers.CometBossPower), typeof(Powers.KnowThyPlacePower), typeof(Powers.ResonancePower), null,
        },
        // 进阶加成（用户设计）：
        //   · 第一幕：进阶 8 获得遗物「星系尘埃」+ 群星之子升级；进阶 9 第一张碰撞轨迹、太阳打击升级
        //     （按张升级写在剧本里：ScriptCard.UpgradeMinAscension）
        //   · 第二幕：进阶 8 招架+、所向无敌+；进阶 9 熔炉+
        //   · 第三幕：进阶 8 = 升级第 1 回合那张「创世之柱」（写在剧本里：`UpgradeMinAscension = 8`）
        //     + 获得遗物「君王矿石」；进阶 9 炸弹+
        AscensionUpgradesByAct = new List<(int, int, Type)>
        {
            (8, 2, typeof(Parry)),
            (8, 2, typeof(IAmInvincible)),
            (9, 2, typeof(Furnace)),
            (9, 3, typeof(TheBomb)),
        },
        AscensionRelicsByAct = new List<(int, int, Type, Type?)>
        {
            (8, 1, typeof(MegaCrit.Sts2.Core.Models.Relics.GalacticDust), null),   // 星系尘埃
            (8, 3, typeof(Regalite), null),                                       // 君王矿石（2026-10-24 从常备改成进阶奖励）
        },
        // 进阶 8 的奖励遗物「奥利哈钢」要看「回合结束时有没有格挡」，而影子玩家的遗物收不到
        // 原版派发 ⇒ 必须由 BossRelicHookBridge 手动补跑。储君这一族本来没有任何「战斗内触发」
        // 的遗物（天赋君权在进房间、护喉甲在战斗开始、红面具由 BossHookBridge 管），
        // 所以对另外三幕是空操作。
        FireBossRelicHooks = true,
        // 二层「征服者储君」：抉择，抉择（抽 3）与光子切割+（抽 1）本来会往手牌里塞牌，
        // 而她的手牌恒等于剧本 ⇒ 抽牌照常发生、抽进来的牌当场挪回抽牌堆顶
        // （用户 2026-11-02：「拦掉抉择抉择和光子切割+的抽牌」）。
        // ⚠ 不用 BlocksVanillaDraw：那是**整只 Boss 级**的开关，会连带影响另外三幕。
        KeepsScriptedHandByAct = new[] { false, true, false, false },
        // 二层储君的**第二套设计「王储」**（Key `REGENT_KING`，用户 2026-11-02；第一套是铸剑储君）；
        // 三层储君的**第二套设计「辉星储」**（用户 2026-10-28；第一套是无色储君）。
        // 与「刀猎/炼药猎」「易伤战/巨石战」同一套机制：以**房间遭遇**为准，拿不到遭遇才按种子 50/50。
        VariantsByAct = new BossVariant?[]
        {
            // ★ 一层储君的**第二套设计「轰击储」**（用户 2026-11-04）。特殊效果「**环绕轨道**」：
            //   你以 1 层「光谱偏移」开局；她第一回合被击晕，第二回合打出「轰击」之后
            //   就被击晕到「你打出过无色牌的第五个回合」为止（轰击在她消耗牌堆里每回合自动复燃）；
            //   唤醒之后轰击反过来打她自己，进入两回合循环。设计原文见 docs/plans/boss-regent-bombard.md。
            //   与「铸剑/王储」「无色/辉星」同一套机制：以**房间遭遇**为准，拿不到遭遇才按种子 50/50
            //   （第一套是「碎屑储君」`DebrisRegentScript`）。
            new BossVariant
            {
                Key = "REGENT_BOMBARD",
                Enabled = Core.BossLineups.BombardActive,
                Deck = Core.RegentBombardScript.Deck(),
                Relics = Core.RegentBombardScript.Relics(),
                Script = Core.RegentBombardScript.Build(),
                SpecialLoc = "REGENT_BOSS.orbital",           // 特殊效果「环绕轨道」
                SpecialPower = typeof(Powers.BossOrbitalBombardPower),
                // 进阶（用户 2026-11-04）：8 = 升级群星斗篷、拳斗；9 = 升级星灭 + 获得遗物「金刚杵」。
                AscensionUpgrades = new List<(int MinAscension, Type Card)>
                {
                    (8, typeof(CloakOfStars)),
                    (8, typeof(Fisticuffs)),
                    (9, typeof(DyingStar)),
                },
                AscensionRelics = new List<(int MinAscension, Type Relic)>
                {
                    (9, typeof(MegaCrit.Sts2.Core.Models.Relics.Vajra)),
                },
            },
            // 二层储君的**第二套设计「王储」**（Key `REGENT_KING`）。特殊效果「**征服者**」：
            // 她每**把一张牌放回抽牌堆顶部**（光子切割 / 宇宙冷漠），所有玩家各得 1 层「麻痹」
            // （`Powers/ParalysisPower`：下一张攻击牌伤害 -50%，每多 1 层再 -50%、2 层即 -100%，
            // 打出下一张攻击牌后失效）。剧本见 Core/KingRegentScript.cs，
            // 设计记录见 docs/plans/boss-king-regent.md。
            new BossVariant
            {
                Key = "REGENT_KING",
                Enabled = Core.BossLineups.KingActive,
                Deck = Core.KingRegentScript.Deck(),
                Relics = Core.KingRegentScript.Relics(),
                Script = Core.KingRegentScript.Build(),
                SpecialLoc = "REGENT_BOSS.conqueror",          // 特殊效果「征服者」
                SpecialPower = typeof(Powers.BossConquerorPower),
                // 进阶（用户 2026-11-02）：8 = 升级宇宙冷漠 + 获得遗物「奥利哈钢」；
                //   · v0.2.324 削弱：不再升级王者之拳（进阶 9 的名单里原本只有它，
                //     用户原话写的是「王者之踢」，但王者之踢从来不在这张表里）。
                //   · v0.2.325（用户 2026-11-02）：「战火铸就改征召上前，进阶 9 改成升级征召上前
                //     并添加抱抱先生」⇒ 进阶 9 = 升级**征召上前** + 获得遗物**抱抱先生**
                //     （`MrStruggles`：她回合开始对所有玩家造成「她的回合数」点无来源伤害）。
                AscensionUpgrades = new List<(int, Type)>
                {
                    (8, typeof(CosmicIndifference)),
                    (9, typeof(SummonForth)),
                },
                AscensionRelics = new List<(int, Type)>
                {
                    (8, typeof(Orichalcum)),   // 奥利哈钢：回合结束没有格挡 → +6 格挡
                    // 抱抱先生：回合开始对所有玩家造成「她的回合数」点 Unpowered 伤害
                    //（原版钩子对影子玩家不触发 ⇒ 由 Core/MrStrugglesPlays 在她的回合开始手动结算）
                    (9, typeof(MegaCrit.Sts2.Core.Models.Relics.MrStruggles)),
                },
            },
            new BossVariant
            {
                Key = "REGENT_STAR",
                Enabled = Core.BossLineups.StarRegentActive,
                Deck = Core.RegentStarScript.Deck(),
                Relics = Core.RegentStarScript.Relics(),
                Script = Core.RegentStarScript.Build(),
                SpecialLoc = "REGENT_BOSS.stellar",            // 特殊效果「群星荟萃」
                SpecialPower = typeof(Powers.BossStellarGatheringPower),
                // 进阶（用户 2026-10-28）：8 = 获得遗物「星际尘埃」；9 = 升级「剑雨」+「王者之拳」。
                AscensionUpgrades = new List<(int MinAscension, Type Card)>
                {
                    // 2026-11-07 用户：「辉星储进阶8应该添加遗物星系尘埃并升级卡牌群星斗篷」。
                    (8, typeof(CloakOfStars)),
                    (9, typeof(Salvo)),
                    (9, typeof(KinglyPunch)),
                },
                AscensionRelics = new List<(int MinAscension, Type Relic)>
                {
                    // 2026-11-07：进阶 8 = 星系尘埃（GalacticDust）；进阶 9 的「赤牛」按用户要求删除。
                    (8, typeof(MegaCrit.Sts2.Core.Models.Relics.GalacticDust)),
                },
            },
            null,
        },
        // ★ v0.2.420（用户 2026-11-05）：二层储君现在有**两套非主设计**（王储 / 仆从储）。
        //   `VariantsByAct` 一幕只放得下一套（王储），第二套（仆从储）挂 ExtraVariants[1]。
        ExtraVariants = new Dictionary<int, List<BossVariant>>
        {
            [1] = new List<BossVariant>
            {
                // 二层储君的**第三套设计「仆从储」**（Key `REGENT_MINION`）：
                //   特殊效果「征召上前」= 打出仆从牌时不足 2 只就改为召唤 + 自己用牌涨力量时友方同涨。
                //   三只仆从怪物见 Monsters/MinionMonsters.cs；剧本见 Core/RegentMinionScript.cs。
                new BossVariant
                {
                    Key = "REGENT_MINION",
                    Enabled = Core.BossLineups.MinionActive,
                    Deck = Core.RegentMinionScript.Deck(),
                    Relics = Core.RegentMinionScript.Relics(),
                    Script = Core.RegentMinionScript.Build(),
                    SpecialLoc = "REGENT_BOSS.minion",
                    SpecialPower = typeof(Powers.BossCallToArmsPower),
                    // 用户 2026-11-05：进阶 8 = 升级护驾！！！、群星之子；
                    //                 进阶 9 = 升级下去！、冲锋！！、类星体（连带它印出来的震荡波 / 究极打击）。
                    AscensionUpgrades = new List<(int, Type)>
                    {
                        (8, typeof(Guards)),
                        (8, typeof(ChildOfTheStars)),
                        (9, typeof(Begone)),
                        (9, typeof(Charge)),
                        (9, typeof(Quasar)),
                        (9, typeof(Shockwave)),
                        (9, typeof(MegaCrit.Sts2.Core.Models.Cards.UltimateDefend)),
                        // ★ 2026-11-05（用户）：进阶 9 同样升级「仆从俯冲」「仆从打击」；进阶 8 升级「仆从捐躯」。
                        (9, typeof(MegaCrit.Sts2.Core.Models.Cards.MinionDiveBomb)),
                        (9, typeof(MegaCrit.Sts2.Core.Models.Cards.MinionStrike)),
                        (8, typeof(MegaCrit.Sts2.Core.Models.Cards.MinionSacrifice)),
                    },
                },
            },
        },
    };

    /// <summary>按角色逐个列出，索引与 <c>CharBossEncounters</c> 的映射表一致。</summary>
    public static readonly BossDefinition[] ByCharacter =
    {
        Ironclad, Silent, Defect, Necrobinder, Regent,
    };

    /// <summary>按 Key 索引（日志/调试用）。</summary>
    public static readonly IReadOnlyDictionary<string, BossDefinition> All =
        new Dictionary<string, BossDefinition>
        {
            [Ironclad.Key] = Ironclad,
            [Silent.Key] = Silent,
            [Defect.Key] = Defect,
            [Necrobinder.Key] = Necrobinder,
            [Regent.Key] = Regent,
        };
}
