using CharacterBosses.Core;

namespace CharacterBosses.Encounters;

// ── 控制台专用：「一只 Boss 一个 id」的固定档位遭遇 ──
//
// 正常流程按**当前幕数**自动选档（同一只角色 Boss 在不同幕是三套设计，见 BossDefs 的 DeckByAct）。
// 那样用控制台打某一只很别扭：想打二层灵魂骨就得先 `act 2`。
// 这里给**每一个设计**配一个自己的遭遇 id，把档位钉死（`TierAct`），于是：
//
//   fight NECROBINDER_SOUL_BOSS_ENCOUNTER         → 不管在第几幕，都是二层「灵魂骨」
//   fight REGENT_COLORLESS_BOSS_ENCOUNTER          → 不管在第几幕，都是三层「无色储君」
//
// id = 类名按 StringHelper.Slugify（驼峰断词 → 全大写）算出来的，和本地化 key 同一套；
// 控制台 `fight <id>` 大小写不敏感，Tab 能补全。
//
// ⚠ 这些遭遇**不进**正常流程的阵容（`BossLineups` / `ForVanillaBoss` 用的是不带钉子的
// `*BossEncounter`），所以对单人和多人都是零回归。房间里仍然是真 Boss 房（RoomType.Boss），
// 打完照常走 Boss 结算；想在原地反复测就先 `act N` 再 `fight`。
//
// 静默猎手的两个（`SILENT_ENVENOM_BOSS_ENCOUNTER` 涂毒猎 / `SILENT_SHIV_BOSS_ENCOUNTER` 刀猎）
// 本来就因为地图图标不同而各有一个类，档位钉在它们自己的定义里，不在这里重复。
//
// id 用词按用户口径：灾厄 = Doom，涂毒 = Envenom（见 docs/plans/cards-silent.md 的卡牌对照）。

/// <summary>第一幕「愤怒战」的固定档位版（剧本 <c>Core/IroncladAngerScript.cs</c>）。</summary>
public sealed class IroncladAngerBossEncounter : IroncladBossEncounter
{
    public override int? TierAct => 0;
}

/// <summary>
/// 第一幕储君的**第二套设计「轰击储」**（用户 2026-11-04；第一套是「碎屑储君」）。
/// 特殊效果「环绕轨道」（`Powers/BossOrbitalBombardPower.cs`），剧本 `Core/RegentBombardScript.cs`，
/// 设计原文见 `docs/plans/boss-regent-bombard.md`。
/// 与「辉星储」「炼药猎」同一套机制：以**房间遭遇**为准，种子 50/50（`BossLineups.BombardActive`）。
/// 控制台直接打：<c>fight REGENT_BOMBARD_BOSS_ENCOUNTER</c>。
/// </summary>
public sealed class RegentBombardBossEncounter : RegentBossEncounter
{
    public override int? TierAct => 0;
}

/// <summary>
/// 第三幕「**巨石战**」的固定档位版（用户 2026-10-27 设计；剧本 <c>Core/BoulderIroncladScript.cs</c>，
/// 特殊效果「原始力量」=<c>Powers/BossPrimalForcePower.cs</c>）。
/// 它是三层铁甲战士的**第二套设计**：三层按种子 50/50 在「易伤战 / 巨石战」之间挑
/// （<c>BossLineups.BoulderActive</c>），控制台点名时以遭遇为准。
/// </summary>
public sealed class IroncladBoulderBossEncounter : IroncladBossEncounter
{
    public override int? TierAct => 2;
}
/// <summary>第三幕「易伤战」的固定档位版（剧本 <c>Core/VulnIroncladScript.cs</c>）。</summary>
public sealed class IroncladVulnBossEncounter : IroncladBossEncounter
{
    public override int? TierAct => 2;
}

/// <summary>
/// **第二幕铁甲战士**的固定档位版（用户 2026-10-26 补齐「二层：猎 骨 机 战 储」）。
/// 他**还没有专属剧本**：用 `BossDefs.Ironclad.DeckByAct[1]` 那套「易伤 + 残血涨力量」
/// （`PaperPhrog` / `RedSkull`），出牌走贪心 AI（`BossPlanner`），没有特殊效果。
/// </summary>
public sealed class IroncladBloodBossEncounter : IroncladBossEncounter
{
    public override int? TierAct => 1;
}

/// <summary>
/// **第一幕「黑球机」**的固定档位版（用户 2026-10-26 设计：故障机器人 · 暗黑充能球轴，
/// 剧本 <c>Core/DarkOrbBossScript.cs</c>，特殊效果「冷却剂」=<c>Powers/CoolantPower</c>）。
/// </summary>
public sealed class DefectDarkBossEncounter : DefectBossEncounter
{
    public override int? TierAct => 0;

    /// <summary>
    /// 地图图标（用户 2026-10-27 指定）：用**塔1崩坠**里「一层故障机器人」那张 ——
    /// 崩坠的 <c>BossSetPatch</c> 按幕给 Defect 换图标：第三幕 <c>defect_focus</c>（能力机在用）、
    /// 第二幕 <c>defect_bronze_orbs</c>、第一幕（其余）<c>defect_void</c>。
    /// 一层是「虚空 / 黑球」轴，正好对上 <c>defect_void</c>。
    /// </summary>
    public override string BossNodePath => "res://CharacterBosses/images/map/defect_void";
}

/// <summary>第二幕「能力机」的固定档位版（剧本 <c>Core/DefectScript.cs</c>）。</summary>
public sealed class DefectAiBossEncounter : DefectBossEncounter
{
    public override int? TierAct => 2;
}

/// <summary>第二幕「物理机」的固定档位版（爪击 / 0 费牌轴，剧本 <c>Core/ClawDefectScript.cs</c>）。</summary>
public sealed class DefectClawBossEncounter : DefectBossEncounter
{
    public override int? TierAct => 1;

    /// <summary>
    /// 地图图标（用户 2026-10-27 指定）：用**塔1崩坠**里「二层故障机器人」那张
    /// （崩坠的 <c>BossSetPatch</c>：TheCity → <c>defect_bronze_orbs</c>）。
    /// </summary>
    public override string BossNodePath => "res://CharacterBosses/images/map/defect_bronze_orbs";
}
/// <summary>
/// 三层故障机器人的**第三套设计「状态机」**（Key `DEFECT_STATUS`，用户 2026-11-05；原本在二层，现挪到三层）。
/// 特殊效果「同步」见 <c>Powers/BossSyncPower.cs</c>，剧本见 <c>Core/StatusDefectScript.cs</c>。
/// </summary>
public sealed class DefectStatusBossEncounter : DefectBossEncounter
{
    public override int? TierAct => 2;
}
/// <summary>
/// 第一幕铁甲战士的**第二套设计「打击战」**（Key `IRONCLAD_STRIKE`，用户 2026-11-07）。
/// 特殊效果「痛击」见 <c>Powers/BossBashPower.cs</c>，剧本见 <c>Core/IroncladStrikeScript.cs</c>。
/// </summary>
public sealed class IroncladStrikeBossEncounter : IroncladBossEncounter
{
    public override int? TierAct => 0;
}
/// <summary>
/// 第一幕故障机器人的**第二套设计「电球机」**（Key `DEFECT_THUNDER`，用户 2026-11-05）。
/// 特殊效果「引雷针」见 <c>Powers/BossLightningRodPower.cs</c>，剧本见 <c>Core/DefectThunderScript.cs</c>。
/// </summary>
public sealed class DefectThunderBossEncounter : DefectBossEncounter
{
    public override int? TierAct => 0;
}

/// <summary>
/// 一层静默猎手的**第二套设计「蛇咬猎」**（Key `SILENT_SNAKEBITE`，用户 2026-11-08）。
/// 特殊效果「群蛇形态」见 <c>Powers/BossSnakeFormPower.cs</c>，剧本见 <c>Core/SnakeBiteSilentScript.cs</c>。
/// </summary>
public sealed class SilentSnakebiteBossEncounter : SilentBossEncounter
{
    public override int? TierAct => 0;
}
/// <summary>
/// 第二幕故障机器人的**第二套设计「彩虹机」**（Key `DEFECT_RAINBOW`，用户 2026-11-07）。
/// 特殊效果「彩虹」见 <c>Powers/BossRainbowPower.cs</c>，剧本见 <c>Core/DefectRainbowScript.cs</c>。
/// </summary>
public sealed class DefectRainbowBossEncounter : DefectBossEncounter
{
    public override int? TierAct => 1;
}

/// <summary>
/// 二层储君的**第三套设计「仆从储」**（Key `REGENT_MINION`，用户 2026-11-05）。
/// 特殊效果「征召上前」见 <c>Powers/BossCallToArmsPower.cs</c>，剧本见 <c>Core/RegentMinionScript.cs</c>。
/// </summary>
public sealed class RegentMinionBossEncounter : RegentBossEncounter
{
    public override int? TierAct => 1;
}

/// <summary>第一幕「灾厄骨」的固定档位版（剧本 <c>Core/NecrobinderScript.cs</c>）。灾厄 = Doom。</summary>
public sealed class NecrobinderDoomBossEncounter : NecrobinderBossEncounter
{
    public override int? TierAct => 0;

    /// <summary>
    /// 地图徽章：**不覆写** ⇒ 走 TheKinBoss 占位 ⇒ 由
    /// <c>Patches/BossMapPointCharacterIconPatch</c> 换成**亡灵契约师的角色徽记**。
    ///
    /// ⚠ 2026-10-30 用户澄清：曾经在这里写「亡灵契约师统一用群魂 / 骷髅」并把 `soul_bone` 套给
    /// 灾厄骨与根除骨，但**用户否认说过这句话**（那张图是 2026-10-26 只给「灵魂骨」的）。
    /// 现在按用户口径：**只有灵魂骨**用 `soul_bone`，其余亡灵档位一律角色徽记。
    /// </summary>
}

/// <summary>
/// 第二幕「灵魂骨」的固定档位版（剧本 <c>Core/SoulBoneScript.cs</c>，灵魂绑定）。
///
/// 地图图标（用户 2026-10-26 指定）：换成「群魂 / 骷髅」那张图 ——
/// 资源 <c>res://CharacterBosses/images/map/soul_bone</c>（+.png / _outline.png，随 PCK 打包，
/// 见 tools\art\pck_extras.txt；生成脚本 tools\art\make-soul-bone-icon.ps1）。
/// </summary>
public sealed class NecrobinderSoulBossEncounter : NecrobinderBossEncounter
{
    public override int? TierAct => 1;

    public override string BossNodePath => "res://CharacterBosses/images/map/soul_bone";
}

/// <summary>
/// 第三幕「**养手骨**」的固定档位版（用户 2026-11-03；三层亡灵契约师的**第二套设计**，第一套是「根除骨」）。
/// 剧本 <c>Core/OstyKeeperScript.cs</c>，特殊效果「不死」= <c>Powers/BossOstyUndyingPower.cs</c>。
/// 与「刀猎/炼药猎」「易伤战/巨石战」「无色储君/辉星储」同一套机制：以**房间遭遇**为准，种子 50/50。
/// </summary>
public sealed class NecrobinderOstyBossEncounter : NecrobinderBossEncounter
{
    public override int? TierAct => 2;
}
/// <summary>第三幕「根除骨」的固定档位版（剧本 <c>Core/NecrobinderAct3Script.cs</c>）。</summary>
public sealed class NecrobinderEradicateBossEncounter : NecrobinderBossEncounter
{
    public override int? TierAct => 2;

    /// <summary>
    /// 地图徽章：**根除骨没有专属美术**（用户 2026-10-30 明确指出：只有「灵魂骨」才用 `soul_bone` 那张）。
    /// 所以这里**不覆写** `BossNodePath` ⇒ 走基类的 TheKinBoss 占位 ⇒ 由
    /// <c>Patches/BossMapPointCharacterIconPatch</c> 换成**亡灵契约师的角色徽记**
    /// （这样三层两颗徽章也不会撞成同一张图）。
    /// </summary>
}

/// <summary>第一幕「碎屑储君」的固定档位版（剧本 <c>Core/DebrisRegentScript.cs</c>）。</summary>
public sealed class RegentDebrisBossEncounter : RegentBossEncounter
{
    public override int? TierAct => 0;
}

/// <summary>第二幕「铸剑储君」的固定档位版（剧本 <c>Core/RegentScript.cs</c> 的铸造线）。</summary>
public sealed class RegentForgeBossEncounter : RegentBossEncounter
{
    public override int? TierAct => 1;
}

/// <summary>
/// 第二幕储君的**第二套设计「征服者储君」**（用户 2026-11-02；第一套是「铸剑储君」）。
/// 与「放血战 / 杂耍战」「无色储君 / 辉星储」同一套机制：以**房间遭遇**为准，种子 50/50
/// （见 <c>BossLineups.KingActive</c>）。剧本 <c>Core/KingRegentScript.cs</c>，
/// 特殊效果「征服者」= <c>Powers/BossConquerorPower.cs</c>。
/// </summary>
public sealed class RegentKingBossEncounter : RegentBossEncounter
{
    public override int? TierAct => 1;
}

/// <summary>第三幕「无色储君」的固定档位版（剧本 <c>RegentScript.BuildAct3</c>）。</summary>
public sealed class RegentColorlessBossEncounter : RegentBossEncounter
{
    public override int? TierAct => 2;
}

/// <summary>
/// 第三幕储君的**第二套设计「辉星储」**（用户 2026-10-28；第一套是「无色储君」）。
/// 与「刀猎/炼药猎」「易伤战/巨石战」同一套机制：以**房间遭遇**为准，种子 50/50
/// （见 <c>BossLineups.RegentStarAt3</c> / <c>StarRegentActive</c>）。
/// </summary>
public sealed class RegentStarBossEncounter : RegentBossEncounter
{
    public override int? TierAct => 2;
}

/// <summary>
/// 第三幕「**炼药猎**」的固定档位版（群友投稿、用户 2026-10-25 实验；剧本 <c>Core/SilentBrewerScript.cs</c>）。
///
/// ⚠ 它**不是**纯控制台遭遇：三层静默猎手出场时按种子 **50/50** 在「刀猎 / 炼药猎」里挑一个
/// （<c>BossLineups.SilentBrewerAt3</c>），挑中炼药猎时 <c>CharBossEncounters.TierEncounterFor</c> 就返回它。
/// 控制台也照样能打：`fight SILENT_BREWER_BOSS_ENCOUNTER`。
/// </summary>
public sealed class SilentBrewerBossEncounter : SilentBossEncounter
{
    public override int? TierAct => 2;

    // 地图图标（用户 2026-10-29 修正）：用**涂毒/药剂**那张 `silent_poison`。
    // 历史：
    //   · 最初借「刀猎」的 `silent_shiv` —— 地图上分不出刀猎和炼药猎（用户 2026-10-27 指出）；
    //   · 之后改成**占位图标**（= 基类兜底的 TheKinBoss 剪影）—— 但那张是披甲武士，
    //     用户 2026-10-29 实机看到「提示写静默猎手、图标却是战士的头像」，更糟；
    //   · 现在用同角色、同主题的 `silent_poison`（炼药/涂毒都是药剂轴），既不是别人的脸，
    //     也和同幕的刀猎（`silent_shiv`）区分得开 —— 第三幕静默猎手 50/50 才会二选一。
    public override string BossNodePath => "res://CharacterBosses/images/map/silent_poison";
}

/// <summary>
/// 二层「**蛇眼猎**」的固定档位版（用户 2026-11-03；二层静默猎手的**第二套设计**，第一套是「涂毒猎」）。
/// 剧本 <c>Core/SnakeEyesSilentScript.cs</c>，特殊效果「计划妥当」= <c>Powers/BossWellLaidPlansPower.cs</c>。
/// 异蛇之眼的费用随机被拦掉，改由剧本逐张写死（`ScriptCard.CostThisTurn`）。
/// </summary>
public sealed class SilentSneckoBossEncounter : SilentBossEncounter
{
    public override int? TierAct => 1;
}

/// <summary>
/// 三层「**事件机**」的固定档位版（用户 2026-11-03；三层故障机器人的**第二套设计**，第一套是「能力机」）。
/// 剧本 <c>Core/EventMachineScript.cs</c>，特殊效果「一心化万」= <c>Powers/BossAllForOnePower</c>
/// （判定落在 <c>Core/EventMachineMechanics</c> 与 <c>Patches/BossAllForOneFilterPatch</c>）。
///
/// 地图徽章**不覆写** ⇒ 与「能力机」一样走 <c>defect_focus</c>。
/// 三层正常流程一场只有一个 Boss；进阶 10 的两场由 <c>BossKeys.ComplementVariant</c>
/// 保证是**不同的变体**（能力机 / 事件机），而这两只本来就同角色同主题，
/// 复用同一张图标不会出现「名字写 A、画的是别人的脸」那种错配。
/// </summary>
public sealed class DefectEventBossEncounter : DefectBossEncounter
{
    public override int? TierAct => 2;
}
