using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using CharacterBosses.Core;
using CharacterBosses.Monsters;

namespace CharacterBosses.Encounters;

// 5 个角色 Boss 的遭遇类。类名同样决定本地化 key（IroncladBossEncounter → IRONCLAD_BOSS_ENCOUNTER）。

public class IroncladBossEncounter : CharBossEncounterBase<IroncladBoss>
{
}

/// <summary>
/// **杂耍战** = 第二幕铁甲战士的第二套设计（用户 2026-11-02）。
/// 档位钉死第二幕 ⇒ 控制台 `fight IRONCLAD_JUGGLER_BOSS_ENCOUNTER` 永远打这一套。
/// </summary>
public sealed class IroncladJugglerBossEncounter : IroncladBossEncounter
{
    public override int? TierAct => 1;
}

public class SilentBossEncounter : CharBossEncounterBase<SilentBoss>
{
}

/// <summary>
/// 第二幕「涂毒猎」：地图 Boss 图标用**崩坠（Downfall）**里静默猎手毒流那套
/// （`downfallResources/images/ui/map/icon/silent_poison.png` + `..._outline.png`）。
/// 两个图标文件随 pck 一起打包（见 tools/art/pck_extras.txt）。
/// </summary>
public class SilentEnvenomBossEncounter : SilentBossEncounter
{
    public override string BossNodePath => "res://CharacterBosses/images/map/silent_poison";

    /// <summary>涂毒猎 = 第二幕档位（控制台 `fight SILENT_ENVENOM_BOSS_ENCOUNTER` 永远打这一套）。</summary>
    public override int? TierAct => 1;
}

/// <summary>
/// 第一幕「奇巧猎」（用户 2026-10-26 设计，静默猎手 · 奇巧/弃牌轴）。
/// 地图图标沿用崩坠里静默猎手小刀流那套（和刀猎同一批资源，别去新建贴图 —— 缺图会导致
/// 地图上 Boss 节点不显示，见 <see cref="CharBossEncounterBase{TMonster}.BossNodePath"/> 的注释）。
/// </summary>
public class SilentSlyBossEncounter : SilentBossEncounter
{
    public override string BossNodePath => "res://CharacterBosses/images/map/silent_shiv";

    /// <summary>奇巧猎 = 第一幕档位（控制台 `fight SILENT_SLY_BOSS_ENCOUNTER` 永远打这一套）。</summary>
    public override int? TierAct => 0;
}

/// <summary>
/// 第三幕「刀猎」：地图 Boss 图标用崩坠里静默猎手小刀流那套
/// （`downfallResources/images/ui/map/icon/silent_shiv.png` + `..._outline.png`）。
/// </summary>
public class SilentShivBossEncounter : SilentBossEncounter
{
    public override string BossNodePath => "res://CharacterBosses/images/map/silent_shiv";

    /// <summary>刀猎 = 第三幕档位（控制台 `fight SILENT_SHIV_BOSS_ENCOUNTER` 永远打这一套）。</summary>
    public override int? TierAct => 2;
}

public class DefectBossEncounter : CharBossEncounterBase<DefectBoss>
{
    /// <summary>
    /// 地图 Boss 节点图标：用塔 1 崩坠（Downfall）里「专注流 / 能力机」的地图图标
    /// （`defect_focus.png` + `defect_focus_outline.png`），从崩坠的 jar 里取出后随 mod 的 pck 打包。
    /// 名称成对：<c>X.png</c> 是图标、<c>X_outline.png</c> 是轮廓，游戏两样都会去加载。
    /// </summary>
    public override string BossNodePath => "res://CharacterBosses/images/map/defect_focus";
}

public class NecrobinderBossEncounter : CharBossEncounterBase<NecrobinderBoss>
{
}

public class RegentBossEncounter : CharBossEncounterBase<RegentBoss>
{
}

// ── 调试专用的「普通战斗」版本 ──
// 调试开关换进来的战斗如果仍是 RoomType.Boss，打赢后会走 Boss 结算（第一幕的 Boss 一死就直接进下一幕）。
// 这几个子类只把房间类型改成 Monster，于是它就是个普通战斗：给普通奖励、能继续走这一层的地图。

public sealed class IroncladBossNormalEncounter : IroncladBossEncounter
{
    public override RoomType RoomType => RoomType.Monster;
}

public sealed class SilentBossNormalEncounter : SilentBossEncounter
{
    public override RoomType RoomType => RoomType.Monster;
}

/// <summary>调试用的「普通战斗」版：第二幕涂毒猎（带自己的地图图标）。</summary>
public sealed class SilentEnvenomBossNormalEncounter : SilentEnvenomBossEncounter
{
    public override RoomType RoomType => RoomType.Monster;
}

/// <summary>调试用的「普通战斗」版：第一幕奇巧猎（带自己的地图图标）。</summary>
public sealed class SilentSlyBossNormalEncounter : SilentSlyBossEncounter
{
    public override RoomType RoomType => RoomType.Monster;
}

/// <summary>调试用的「普通战斗」版：第三幕刀猎（带自己的地图图标）。</summary>
public sealed class SilentShivBossNormalEncounter : SilentShivBossEncounter
{
    public override RoomType RoomType => RoomType.Monster;
}

public sealed class DefectBossNormalEncounter : DefectBossEncounter
{
    public override RoomType RoomType => RoomType.Monster;
}

public sealed class NecrobinderBossNormalEncounter : NecrobinderBossEncounter
{
    public override RoomType RoomType => RoomType.Monster;
}

public sealed class RegentBossNormalEncounter : RegentBossEncounter
{
    public override RoomType RoomType => RoomType.Monster;
}

/// <summary>「角色 Boss 遭遇」的公共实现：单一怪物、Boss 房、地图图标用占位图。</summary>
public abstract class CharBossEncounterBase<TMonster> : EncounterModel, Core.IBossTierPin
    where TMonster : MonsterModel
{
    /// <summary>
    /// 把强度档位钉死在第几幕（见 <see cref="Core.IBossTierPin"/>）。
    /// 默认 <c>null</c> = 不钉死，按当前幕数走 —— 也就是正常流程的行为，一个字都不改。
    /// 只有给控制台用的「一 Boss 一 id」遭遇才会覆盖它。
    /// </summary>
    public virtual int? TierAct => null;

    /// <summary>
    /// 战场缩放。<c>NCombatRoom</c> 会把整个<b>场景容器</b>（双方角色 + 地面）按这个值缩放，
    /// 所以 0.8 = 双方一起变小、战场看起来更大 —— 原版大怪（帝王蟹那类）就是用这个拉远的。
    /// 小于 1 时原版还会顺带把「可用宽度」按 <c>960 / scaling</c> 放宽（见 <c>PositionEnemies</c>）。
    /// 角色立绘是按玩家尺寸画的，直接当敌人会占满整个竖屏，所以这里统一拉远一点。
    /// </summary>
    public const float ArenaScale = 0.80f;

    /// <summary>
    /// 多人时的战场缩放 —— **和单人一样用 0.80**。
    /// 之前试过 0.62 想让角色显得更小，但原版背景图有尺寸上限，拉太远会露出背景之外的黑边（用户实测截图），
    /// 所以战场保持 0.80，改成把**我们自己的 UI（卡牌/遗物/能量/辉星）**按同比例缩小来腾空间。
    /// </summary>
    public const float MultiArenaScale = ArenaScale;

    /// <summary>整场战斗垂直微调（负 = 场景往上，给下方手牌留空间）。</summary>
    public const float ArenaOffsetY = -20f;

    public override RoomType RoomType => RoomType.Boss;

    public override float GetCameraScaling()
        => Core.MultiBossPlan.WantsSecondBoss(out _) ? MultiArenaScale : ArenaScale;

    public override Vector2 GetCameraOffset() => new Vector2(0f, ArenaOffsetY);

    /// <summary>
    /// 地图上的 Boss 节点图标：**复用原版 Boss 的占位图标**。
    /// 以前拼的是 <c>.../{自己的 id}_icon</c>，而我们的遭遇没有对应贴图，
    /// 于是地图上直接报「Failed to load resource」并且 Boss 节点不显示（用户实测：一层 boss 不见了）。
    /// </summary>
    public override string BossNodePath
        => ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.TheKinBoss>()?.BossNodePath
           ?? base.BossNodePath;

    public override IEnumerable<MonsterModel> AllPossibleMonsters =>
        new[] { ModelDb.Monster<TMonster>() };

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {
        // ★ v0.2.418（用户实测：`fight DEFECT_EVENT_BOSS_ENCOUNTER` 依然打成能力机）：
        //   变体判定（哪套卡组/遗物/剧本/特殊效果）是在**下面 `ModelDb.Monster<T>().ToMutable()`
        //   以及随后的影子 Player 建立时**就要用的，而那一刻 `CombatManager.state.Encounter`
        //   **还没赋值** ⇒ `CurrentEncounterEntry()` 读到 null ⇒ 退化成「按种子 50/50」，
        //   控制台点名也不管用。这里在**造怪物之前**先把本场遭遇记下来（一行，必定执行）。
        try { Core.BossLineups.NoteFightEncounter(Id?.Entry); } catch { /* 忽略 */ }

        string? myKey = null;
        try { myKey = BossKeys.KeyOfEncounter(this); } catch { myKey = null; }

        var list = new List<(MonsterModel, string?)>(1);
        var main = ModelDb.Monster<TMonster>().ToMutable();
        // ★ v0.2.333：把「这只是哪一套设计」写在怪物实例上（死亡提示 / 跟踪锁定的名字 / 地图徽章
        //   都靠它，不再依赖「按 (角色, 幕) 重算变体」—— 死亡那一刻 CombatState 已经变了，重算会落到主变体）。
        if (main is Monsters.CharBossMonster mainBoss && !string.IsNullOrEmpty(myKey))
            mainBoss.SpawnVariantKey = myKey;
        list.Add((main, (string?)null));

        // 多人：这一幕同时上**多只** Boss（数量 = 玩家数，见 Core/MultiBossPlan.PartnersFor）。
        // 单人时 PartnersFor 返回空列表，遭遇仍然只造一只 —— 与改动前完全一致。
        try
        {
            foreach (var (key, mate) in Core.MultiBossPlan.PartnersForWithKeys(myKey))
            {
                if (mate == null) continue;
                if (mate is Monsters.CharBossMonster mateBoss && !string.IsNullOrEmpty(key))
                    mateBoss.SpawnVariantKey = key;
                list.Add((mate, (string?)null));
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 伙伴 Boss 上场失败: " + e.Message);
        }

        // ★ 决定性诊断行（用户 2026-10-30 的不同步：房主 2 只、客人 1 只 ⇒ 校验和对不上）：
        //   这一行由**每台机器各自**打，列出本机为这个遭遇造出来的怪物。两端条数/顺序不一致，
        //   就是「多 Boss 生成」分叉，之后的所有差异（锁定图标数量、AI 的 RNG 计数…）都只是它的后果。
        try
        {
            Logger.Log($"[CharBoss] ※本机生成※ {Id.Entry} → 怪物 {list.Count} 只 " +
                       $"[{string.Join(",", list.Select(m => m.Item1?.Id.Entry ?? "?"))}]");
        }
        catch { /* 诊断失败不影响战斗 */ }

        // 行动顺序 = **这个列表的顺序**（原版按敌人列表次序逐个行动），
        // 而站位也恒等于这个顺序（`BossEnemySpreadPatch.PlacementRank` 取 `Enemies` 下标）
        // ⇒ 这里的排序**就是**玩家看到的左右顺序。
        //
        // ⚠ 用户 2026-10-29：「我记得 2 层行动顺序不是 猎 骨 机 战 储 吗？怎么现在战士在故障机器人前面？」
        //   原因是以前按**左右分组**排（`LeftRank`：静默/亡灵/铁甲 = 左、故障/储君 = 右）——
        //   铁甲被算进「左组」，于是无论剧本怎么排，**战士永远在故障机器人前面**。
        //   现在改成**按职业的固定次序**（用户口径）：
        //     一层 / 二层：猎(静默) → 骨(亡灵) → 机(故障) → 战(铁甲) → 储(储君)
        //     三层：猎 → 机 → 骨 → 储 → 战（保留「易伤战永远最右」这条老设计）
        if (list.Count >= 2)
        {
            try
            {
                int act = CurrentActIndex();
                var roster = BossKeys.RosterFor(act);
                // ★ 2026-11-05（用户：不再限制每层同种 Boss 数量 ⇒ 同场可能出现两只同角色，例如刀猎 + 炼药猎）：
                //   同角色并列时再按**阵容编号**定先后（编号小的在左、先行动），保证两端一致且可复现。
                list.Sort((a, b) =>
                {
                    int ra = StationRank(act, a.Item1);
                    int rb = StationRank(act, b.Item1);
                    if (ra != rb) return ra.CompareTo(rb);
                    return RosterNumberOf(roster, a.Item1).CompareTo(RosterNumberOf(roster, b.Item1));
                });
                Logger.Log($"[CharBoss] 多 Boss 行动顺序（按职业次序 + 同角色按编号，第 {act + 1} 幕）：" +
                           $"{string.Join(" → ", list.ConvertAll(x => x.Item1.Id.Entry))}");
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 多 Boss 行动顺序排序失败: " + e.Message);
            }
        }

        return list;
    }

    /// <summary>当前幕数（拿不到按 0 = 第一幕）。</summary>
    private static int CurrentActIndex()
    {
        try { return Core.MultiBossPlan.RunStateOrNull()?.CurrentActIndex ?? 0; }
        catch { return 0; }
    }

    /// <summary>
    /// 这只 Boss 在**本幕候补表里的编号**（0 起；拿不到返回 99 = 排最后）。
    /// 用途：同场出现两只同角色（刀猎 + 炼药猎、能力机 + 状态机…）时的先后次序。
    /// 键来自怪物实例上的 <c>SpawnVariantKey</c>（生成时就写好了，见 <c>GenerateMonsters</c>）。
    /// </summary>
    private static int RosterNumberOf(string[] roster, MonsterModel? monster)
    {
        try
        {
            if (roster == null || roster.Length == 0) return 99;
            string? key = (monster as Monsters.CharBossMonster)?.SpawnVariantKey;
            if (string.IsNullOrEmpty(key)) return 99;
            int i = Array.IndexOf(roster, key);
            return i >= 0 ? i : 99;
        }
        catch { return 99; }
    }

    /// <summary>
    /// 职业的固定站位/行动次序（越小越靠左、越先行动）。
    ///
    /// 用户 2026-10-29 口径：**猎 → 骨 → 机 → 战 → 储**
    /// （「我记得 2 层行动顺序不是猎骨机战储吗」）。
    /// 第三幕保留老设计「易伤战永远最右」⇒ 猎 → 机 → 骨 → 储 → 战。
    /// </summary>
    private static int StationRank(int act, MonsterModel? monster)
    {
        try
        {
            if (act >= 2)
            {
                return monster switch
                {
                    SilentBoss => 0,
                    DefectBoss => 1,
                    NecrobinderBoss => 2,
                    RegentBoss => 3,
                    IroncladBoss => 4,
                    _ => 5,
                };
            }
            return monster switch
            {
                SilentBoss => 0,
                NecrobinderBoss => 1,
                DefectBoss => 2,
                IroncladBoss => 3,
                RegentBoss => 4,
                _ => 5,
            };
        }
        catch { return 5; }
    }
}

/// <summary>
/// 「第 N 幕的 Boss 换谁」的登记处。★用户设计 Boss 时改这里。
///
/// 映射是**按原版 Boss 槽位**做的（不是随机）：每一幕原本有 3 个候选 Boss，
/// 第 j 个候选槽固定换成 <see cref="ActTable"/> 里给的角色。
/// 好处是无需随机种子就完全确定 —— 同一个存档里地图上显示的和实际打到的必然一致，
/// 而且同一幕的三个槽不会撞成同一个角色。
/// 强度档不用在这里写：怪物自己会读当前幕数（见 CharBossMonster.ActIndex）。
/// </summary>
public static class CharBossEncounters
{
    /// <summary>「第 4 幕及以后不覆盖」这条日志每幕只打一次。</summary>
    private static readonly HashSet<int> ActBeyondOurScopeLogged = new();

    /// <summary>「共用匹配池」抽取结果的日志每 (幕, 场) 只打一次。</summary>
    private static readonly HashSet<(int Act, int Fight)> SharePoolLogged = new();

    /// <summary>
    /// 换了一局（种子变了）就把「共用匹配池：这一场抽到…」的去重表清掉。
    /// v0.2.285：以前这张表**整个进程只记一次**，玩家在同一进程里开第二局就再也看不到抽取日志，
    /// 出问题时（用户 2026-10-31「共用匹配池好像不起作用」）根本没法确认到底抽了哪边。
    /// </summary>
    internal static void ResetSharePoolLog()
    {
        try { SharePoolLogged.Clear(); } catch { /* 忽略 */ }
    }


    /// <summary>「客人端同步槽位还是原版 ⇒ 先按确定性规则算」这条日志每 (幕, 场) 只打一次。</summary>
    private static readonly HashSet<(int Act, bool Second)> GuestPendingFollowLogged = new();

    /// <summary>客人端跟随房主时用来防重入（`SecondBossEncounter` 的 getter 是我们补过的）。</summary>
    [ThreadStatic] private static bool InGuestFollow;

    /// <summary>安全地读「第二槽」的原始值（避免客人分支里再触发 getter 补丁造成递归）。</summary>
    private static EncounterModel? SafeSecondBossEncounter(MegaCrit.Sts2.Core.Models.ActModel act)
    {
        try { return act.SecondBossEncounter; }
        catch { return null; }
    }

    /// <summary>角色下标：0 铁甲战士 / 1 静默猎手 / 2 故障机器人 / 3 亡灵契约师 / 4 储君。</summary>
    private static readonly Lazy<EncounterModel?[]> Encounters = new(() => new EncounterModel?[]
    {
        ModelDb.Encounter<IroncladBossEncounter>(),
        ModelDb.Encounter<SilentBossEncounter>(),
        ModelDb.Encounter<DefectBossEncounter>(),
        ModelDb.Encounter<NecrobinderBossEncounter>(),
        ModelDb.Encounter<RegentBossEncounter>(),
    });

    /// <summary>调试开关用的「普通战斗」版本，下标与 <see cref="Encounters"/> 一一对应。</summary>
    private static readonly Lazy<EncounterModel?[]> DebugEncounters = new(() => new EncounterModel?[]
    {
        ModelDb.Encounter<IroncladBossNormalEncounter>(),
        ModelDb.Encounter<SilentBossNormalEncounter>(),
        ModelDb.Encounter<DefectBossNormalEncounter>(),
        ModelDb.Encounter<NecrobinderBossNormalEncounter>(),
        ModelDb.Encounter<RegentBossNormalEncounter>(),
    });

    /// <summary>
    /// 「一只 Boss 一个 id」的遭遇（档位钉死，见 <see cref="Core.IBossTierPin"/>）。
    /// **正常流程与控制台用的是同一批**（用户 2026-10-23：「以控制台为准」）：
    /// `ForVanillaBoss` 直接返回这里对应 (角色, 幕) 的那个，于是地图上的名字、
    /// 顶栏图标、死亡提示全都是设计名（愤怒战 / 灵魂骨 / 无色储君…），
    /// `fight &lt;id&gt;` 打到的也是同一个遭遇。
    /// </summary>
    private static readonly Lazy<EncounterModel?[]> TierEncounters = new(() => new EncounterModel?[]
    {
        ModelDb.Encounter<IroncladAngerBossEncounter>(),
        ModelDb.Encounter<IroncladStrikeBossEncounter>(),    // 一层铁甲第二套设计「打击战」（用户 2026-11-07）
        ModelDb.Encounter<IroncladVulnBossEncounter>(),
  ModelDb.Encounter<IroncladBoulderBossEncounter>(),
        ModelDb.Encounter<SilentEnvenomBossEncounter>(),
        // 二层静默猎手的第二套设计（涂毒猎 / 蛇眼猎，用户 2026-11-03）
        ModelDb.Encounter<SilentSneckoBossEncounter>(),
        ModelDb.Encounter<SilentSlyBossEncounter>(),
        ModelDb.Encounter<DefectDarkBossEncounter>(),
        ModelDb.Encounter<IroncladBloodBossEncounter>(),
        // 二层铁甲战士的第二套设计（放血战 / 杂耍战，用户 2026-11-02）
        ModelDb.Encounter<IroncladJugglerBossEncounter>(),
        ModelDb.Encounter<SilentShivBossEncounter>(),
        ModelDb.Encounter<DefectAiBossEncounter>(),
        // 三层故障机器人的第二套设计（能力机 / 事件机，用户 2026-11-03）
        ModelDb.Encounter<DefectEventBossEncounter>(),
        ModelDb.Encounter<DefectClawBossEncounter>(),
        ModelDb.Encounter<DefectStatusBossEncounter>(),      // 三层故障机器人第三套设计「状态机」        ModelDb.Encounter<DefectThunderBossEncounter>(),     // 一层故障机器人第二套设计「电球机」（用户 2026-11-05）
        ModelDb.Encounter<DefectRainbowBossEncounter>(),     // 二层故障机器人第二套设计「彩虹机」（用户 2026-11-07）
        ModelDb.Encounter<RegentMinionBossEncounter>(),      // 二层储君第三套设计「仆从储」（用户 2026-11-05）
        ModelDb.Encounter<NecrobinderDoomBossEncounter>(),
        ModelDb.Encounter<NecrobinderSoulBossEncounter>(),
        ModelDb.Encounter<NecrobinderEradicateBossEncounter>(),
        // 三层亡灵契约师的第二套设计（根除骨 / 养手骨，用户 2026-11-03）
        ModelDb.Encounter<NecrobinderOstyBossEncounter>(),
        ModelDb.Encounter<RegentDebrisBossEncounter>(),
        ModelDb.Encounter<RegentForgeBossEncounter>(),
        // 二层储君的第二套设计（铸剑储君 / 征服者储君，用户 2026-11-02）
        ModelDb.Encounter<RegentKingBossEncounter>(),
        ModelDb.Encounter<RegentColorlessBossEncounter>(),
        // 三层静默猎手的第二套设计（刀猎 / 炼药猎 50/50）
        ModelDb.Encounter<SilentBrewerBossEncounter>(),
        // 三层储君的第二套设计（无色储君 / 辉星储 50/50，用户 2026-10-28）
        ModelDb.Encounter<RegentStarBossEncounter>(),
        // 一层储君的第二套设计（碎屑储君 / **轰击储**，用户 2026-11-04）
        ModelDb.Encounter<RegentBombardBossEncounter>(),
    });

    /// <summary>
    /// **(角色下标, 幕数 0 起) → 那一档的专属遭遇**。有设计的档位才在里面；
    /// 没设计的那几档（例如铁甲战士一层以外）会退回按角色的基础遭遇（用起始卡组占位）。
    ///
    /// 角色下标：0 铁甲战士 / 1 静默猎手 / 2 故障机器人 / 3 亡灵契约师 / 4 储君。
    /// </summary>
    private static readonly Lazy<Dictionary<(int Character, int Act), EncounterModel?>>
        TierByCharacterAct = new(() =>
        {
            var map = new Dictionary<(int, int), EncounterModel?>();
            void Add(int character, int act, EncounterModel? encounter)
            {
                if (encounter != null) map[(character, act)] = encounter;
            }

            Add(0, 0, ModelDb.Encounter<IroncladAngerBossEncounter>());          // 一层 愤怒战
            Add(0, 2, ModelDb.Encounter<IroncladVulnBossEncounter>());            // 三层 易伤战
            Add(1, 0, ModelDb.Encounter<SilentSlyBossEncounter>());              // 一层 奇巧猎（用户 2026-10-26）
            Add(1, 1, ModelDb.Encounter<SilentEnvenomBossEncounter>());          // 二层 涂毒猎
            Add(1, 2, ModelDb.Encounter<SilentShivBossEncounter>());             // 三层 刀猎
            Add(2, 1, ModelDb.Encounter<DefectClawBossEncounter>());             // 二层 物理机
            Add(2, 0, ModelDb.Encounter<DefectDarkBossEncounter>());            // 一层 黑球机（暗黑充能球轴）
            // 二层铁甲战士 = 「放血战」（用户 2026-10-27 设计）。以前那两行是「还没设计、暂时下线」的注释，
            // 现在已设计完 ⇒ 恢复登记，正常流程与自己开控制台都会用到它。
            Add(0, 1, ModelDb.Encounter<IroncladBloodBossEncounter>());          // 二层 放血战（用户 2026-10-27 设计）
            Add(2, 2, ModelDb.Encounter<DefectAiBossEncounter>());               // 三层 能力机
            Add(3, 0, ModelDb.Encounter<NecrobinderDoomBossEncounter>());        // 一层 灾厄骨
            Add(3, 1, ModelDb.Encounter<NecrobinderSoulBossEncounter>());        // 二层 灵魂骨
            Add(3, 2, ModelDb.Encounter<NecrobinderEradicateBossEncounter>());   // 三层 根除骨
            Add(4, 0, ModelDb.Encounter<RegentDebrisBossEncounter>());           // 一层 碎屑储君
            Add(4, 1, ModelDb.Encounter<RegentForgeBossEncounter>());            // 二层 铸剑储君
            Add(4, 2, ModelDb.Encounter<RegentColorlessBossEncounter>());        // 三层 无色储君
            return map;
        });

    /// <summary>某一幕里这个角色的专属遭遇（没有设计就返回 null）。</summary>
    public static EncounterModel? TierEncounterFor(int character, int actIndex)
    {
        try
        {
            // 三层静默猎手有**两套设计**：刀猎 / 炼药猎，按种子 50/50
            // （用户 2026-10-25；挑法见 Core/BossLineups.SilentBrewerAt3，两端一致）。
            // 三层储君也有两套设计：无色储君 / **辉星储**（用户 2026-10-28，种子 50/50）
            if (character == 4 && actIndex == 2 && Core.BossLineups.RegentStarAt3())
                return ModelDb.Encounter<RegentStarBossEncounter>();

            if (character == SilentCharacterIndex && actIndex == 2 && Core.BossLineups.SilentBrewerAt3())
                return ModelDb.Encounter<SilentBrewerBossEncounter>();

            // 一层储君也有两套设计：碎屑储君 / **轰击储**（用户 2026-11-04，种子 50/50；
            // 控制台点名 `fight REGENT_BOMBARD_BOSS_ENCOUNTER` 时以房间遭遇为准）。
            if (character == RegentCharacterIndex && actIndex == 0 && Core.BossLineups.BombardActive())
                return ModelDb.Encounter<RegentBombardBossEncounter>();

            // 三层铁甲战士同样有两套设计：易伤战 / **巨石战**（用户 2026-10-27），
            // 按种子 50/50（Core.BossLineups.BoulderAt3；控制台点名时以房间遭遇为准）。
            if (character == IroncladCharacterIndex && actIndex == 2 && Core.BossLineups.BoulderAt3())
                return ModelDb.Encounter<IroncladBoulderBossEncounter>();

            // 二层储君也有两套设计：铸剑储君 / **征服者储君**（用户 2026-11-02）。
            // 正常流程走「阵容 → Key → 遭遇」，这里主要给「只按角色 + 幕数取一个 id」的地方用
            // （死亡提示 `EncounterForBoss`、地图徽章之类）。
            // 二层静默猎手也有两套设计：涂毒猎 / **蛇眼猎**（用户 2026-11-03）。
            if (character == SilentCharacterIndex && actIndex == 1 && Core.BossLineups.SneckoActive())
                return ModelDb.Encounter<SilentSneckoBossEncounter>();

            // 三层亡灵契约师也有两套设计：根除骨 / **养手骨**（用户 2026-11-03）。
            if (character == NecrobinderCharacterIndex && actIndex == 2 && Core.BossLineups.OstyActive())
                return ModelDb.Encounter<NecrobinderOstyBossEncounter>();

            // ★ 2026-11-05（用户：Boss 改成「只按层数分 + 编号」，不再区分主设计/变体）：
            //   同层同角色可能有 2～3 套设计（三层故障 = 能力机 1 / 事件机 2 / 状态机 3），
            //   **具体是哪一套由阵容 Key 决定**（走 `BossKeys.EncounterOf(key)`，见下面的 ② 之后那条路）。
            //   这个「按 (角色, 幕) 取一个遭遇」的函数只剩兜底用途（死亡提示 / 地图徽章拿不到 Key 时），
            //   所以这里不再按种子 50/50 挑变体，统一退到**第 1 号设计**（各幕的主设计）。

            if (character == RegentCharacterIndex && actIndex == 1 && Core.BossLineups.KingActive())
                return ModelDb.Encounter<RegentKingBossEncounter>();

            return TierByCharacterAct.Value.TryGetValue((character, actIndex), out var encounter)
                ? encounter
                : null;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 取档位遭遇失败: " + e.Message);
            return null;
        }
    }

    /// <summary>所有「一只 Boss 一个 id」的遭遇，已注册的才在里面。</summary>
    public static IEnumerable<EncounterModel?> AllTierEncounters() => TierEncounters.Value;

    /// <summary>按角色的基础遭遇（没设计的那几档用它占位）。给死亡提示这类「只要一个 id」的地方用。</summary>
    public static EncounterModel? BaseEncounterFor(int character)
    {
        try
        {
            var all = Encounters.Value;
            return character >= 0 && character < all.Length ? all[character] : null;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 取角色基础遭遇失败: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// 我们的遭遇 id → 角色下标（按 id 前缀认：`NECROBINDER_SOUL_BOSS_ENCOUNTER` → 3）。
    /// 不是我们的遭遇返回 -1。用途：多人时「另一只该是谁」在阵容种子拿不到时，
    /// 改用**地图上存着的两个槽**兜底（见 <c>MultiBossPlan.PartnerFor</c>）。
    /// </summary>
    public static int CharacterOfEncounter(EncounterModel? encounter)
    {
        try
        {
            string? entry = encounter?.Id.Entry;
            if (string.IsNullOrEmpty(entry)) return -1;

            if (entry!.StartsWith("IRONCLAD", StringComparison.Ordinal)) return 0;
            if (entry.StartsWith("SILENT", StringComparison.Ordinal)) return 1;
            if (entry.StartsWith("DEFECT", StringComparison.Ordinal)) return 2;
            if (entry.StartsWith("NECROBINDER", StringComparison.Ordinal)) return 3;
            if (entry.StartsWith("REGENT", StringComparison.Ordinal)) return 4;
        }
        catch { /* 忽略 */ }
        return -1;
    }

    /// <summary>Boss 定义 Key（如 <c>NECROBINDER_BOSS</c>）→ 角色下标；认不出来返回 -1。</summary>
    public static int CharacterIndexOfKey(string? defKey) => defKey switch
    {
        "IRONCLAD_BOSS" => 0,
        "SILENT_BOSS" => 1,
        "DEFECT_BOSS" => 2,
        "NECROBINDER_BOSS" => 3,
        "REGENT_BOSS" => 4,
        _ => -1,
    };

    /// <summary>
    /// 某只 Boss 实例**自己的**遭遇（优先它这一档的专属 id，没有就用按角色的基础遭遇）。
    /// 用途：多人时按「凶手」重造死亡提示（见 <c>Patches/BossLossMessagePatches.cs</c>）。
    /// </summary>
    public static EncounterModel? EncounterForBoss(Monsters.CharBossMonster? boss)
    {
        try
        {
            if (boss?.Def == null) return null;

            // ① **优先用生成它的那个遭遇**（v0.2.333）：这只 Boss 出生时就被标好了是哪一套设计
            //    （`CharBossMonster.SpawnVariantKey`）。下面的 ②③ 都是「按 (角色, 幕) 重算变体」，
            //    重算依赖 `BossLineups` 的**本场锁定 / 当前房间遭遇** —— 玩家死亡那一刻战斗正在结束，
            //    CombatState 已经换了 ⇒ 锁定清空、重算落到同档的主变体上
            //    （用户 2026-11-03 实测：炼药猎的死亡提示显示成刀猎的）。
            try
            {
                var bySpawn = BossKeys.EncounterOf(boss.SpawnVariantKey);
                if (bySpawn != null) return bySpawn;
            }
            catch { /* 继续走老路 */ }

            int character = CharacterIndexOfKey(boss.Def.Key);
            if (character < 0) return null;

            int act = 0;
            try { act = Math.Clamp(boss.ActIndex, 0, 2); } catch { act = 0; }

            return TierEncounterFor(character, act) ?? BaseEncounterFor(character);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 取 Boss 自己的遭遇失败: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// 每一幕的 3 个原版 Boss 槽分别换成谁。行号 = <c>ActModel.Index</c>（幕数档位）。
    /// 注意 Index 是「第几幕」而不是幕的身份：密林与暗港都是第一幕，Index 都是 0；巢穴 1；荣耀 2。
    /// 走完三幕 5 个角色都会出场（第 1 幕 3 个 + 第 2 幕 3 个 + 第 3 幕 3 个）。
    /// </summary>
    private static readonly int[][] ActTable =
    {
        new[] { 0, 0, 0 },   // 第一幕（密林 / 暗港）：三个槽全是铁甲战士「**愤怒战**」（用户 2026-10-23：暂时固定成他）
        new[] { 3, 3, 3 },   // 第二幕（巢穴）：三个槽全是亡灵契约师「**灵魂骨**」（用户 2026-10-22：暂时把 2 层 Boss 固定为她）
        new[] { 3, 4, 2 },   // 第三幕（荣耀）：**先根除骨（亡灵契约师）、后无色储君（储君）**（用户要求）
                             // 第二只靠「本幕第几只 Boss」的记账来取第 2 列，见 BossReplacementPatches。
        new[] { 4, 0, 1 },   // 备用（将来若出现第四幕）
    };

    /// <summary>某一幕的「职业分配」一行（多人时决定这一幕该上哪两只 Boss，见 <c>Core/MultiBossPlan</c>）。</summary>
    public static int[] ActRowFor(int actIndex)
    {
        try
        {
            if (ActTable.Length == 0) return Array.Empty<int>();
            int i = Math.Min(Math.Max(actIndex, 0), ActTable.Length - 1);
            return ActTable[i];
        }
        catch { return Array.Empty<int>(); }
    }

    /// <summary>把一幕里的某个原版 Boss 槽换成对应的角色 Boss（返回 canonical 实例）。</summary>
    /// <param name="secondBoss">
    /// 这一槽是「第二个 Boss」（进阶 10 的第三幕要连打两个）。
    /// **必须靠它区分**：原版第三幕的两个 Boss 在 <c>BossDiscoveryOrder</c> 里会解析到同一个下标，
    /// 于是 <see cref="SlotOf"/> 对两槽都返回同一个数 ⇒ 两个 Boss 变成同一个角色
    /// （用户实测：第三幕两个都是刀猎）。改成「第 1 槽用 0、第 2 槽用 1」就稳定了；
    /// 第一/第二幕的 <see cref="ActTable"/> 那一行本来就是同一个角色，所以对它们没有影响。
    /// </param>
    public static EncounterModel? ForVanillaBoss(ActModel act, EncounterModel? vanilla, bool secondBoss = false)
    {
        // ★ 用户 2026-10-29（装了「第四幕心脏」模组）：**第 4 幕及以后一律不碰** ——
        //   我们的设计只做到前三幕；第四幕（心脏）以及以后任何模组加出来的幕，
        //   它们的 Boss 必须原样保留，不能被我们换成角色 Boss。
        if (act == null) return null;
        try
        {
            if (act.Index >= 3)
            {
                if (ActBeyondOurScopeLogged.Add(act.Index))
                {
                    Logger.Log($"[CharBoss] 第 {act.Index + 1} 幕不在本模组覆盖范围内（只做前 3 幕）⇒ " +
                               "该幕 Boss 保持原版，不做任何替换");
                }
                return null;
            }
        }
        catch { return null; }

        // ★ 用户 2026-10-29：**多人非房主（客人）不用本机配置** ——
        //   一切「游戏内会用到的设置」（地图开关 / 共用匹配池…）以**房主**为准。
        //   房主的结论已经写进同步状态（`act.BossEncounter` / `act.SecondBossEncounter`），
        //   所以客人这边只要：同步状态里是我们的遭遇 → 跟随它。
        //
        // ★★ v0.2.237（联机不同步修复，`mod_log (14).txt` 20:17:35 那一串）：
        //   以前同步槽位**不是我们的**就直接 `return null`（客人打原版 Boss）。
        //   可是「槽位还是原版」有两种完全不同的情况：
        //     ① 房主真的没换（我们要跟着不换）——好；
        //     ② **房主的数据还没同步到本机**（刚读档 / 刚进房间 / 刚恢复不同步）——
        //        这时客人打的是原版 Boss，房主打的是角色 Boss ⇒ 进 Boss 房立刻 divergence，
        //        而且客户端会一直回滚重来（那次日志里 20:17:39、20:18:01、20:18:41 三次，全在轮次 1）。
        //   现在把 ② 拆出来：**先按与房主完全相同的确定性规则算一个**
        //   （种子 `RunState.Rng.Seed` / 幕 / 场 / 人数 / 进阶 全部来自同步数据，
        //    见 `BossLineups`；只有「本机配置」这一项是各机器自己的）。
        //   于是「配置相同」的两端算出来的 Boss 一样 ⇒ 不再分叉；
        //   万一本机配置与房主不同，日志里会多一行，方便排查。
        try
        {
            if (Core.NetRole.IsGuest && !InGuestFollow)
            {
                InGuestFollow = true;
                try
                {
                    var stored = secondBoss ? SafeSecondBossEncounter(act) : act.BossEncounter;
                    if (stored != null && IsOurs(stored))
                    {
                        Logger.Log($"[CharBoss] 客人端：第 {act.Index + 1} 幕{(secondBoss ? "第二场" : "")}Boss 跟随房主 ⇒ {stored.Id.Entry}");
                        return stored;
                    }

                    if (GuestPendingFollowLogged.Add((act.Index, secondBoss)))
                    {
                        Logger.Log($"[CharBoss] 客人端：第 {act.Index + 1} 幕{(secondBoss ? "第二场" : "")}Boss " +
                                   $"同步槽位还是原版（{(stored == null ? "<空>" : stored.Id.ToString())}）" +
                                   "⇒ 房主数据没到 / 房主没换，两种情况分不出来；" +
                                   "**先按与房主相同的确定性规则算一个**（错的话会多一行「本机算得」日志，随日志发我）");
                    }
                    // 故意不 return：下面就是房主用的那套确定性规则（种子来自同步的 RunState）。
                }
                finally { InGuestFollow = false; }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 客人端跟随房主失败（改按确定性规则继续算）: " + e.Message);
        }

        // 配置项 A1（用户 2026-10-28）：这张地图没开「替换」就整个不动，原版 Boss 照常。
        // 放在最前面 = 四个切点（发现顺序 / 生成房间 / 读档校验 / 第二槽读取）全部一起生效。
        // ⚠ 用户 2026-10-29（装了「往昔之章」等地图模组）：**非原版地图默认关闭**，
        //   只有玩家在「模组配置」里手动打开才会替换（见 BossConfig.IsMapEnabledById）。
        // ★ v0.2.468（用户 2026-11-07）：改用 **IsMapEnabledForGameplay** ——
        //   客人端不再读本机开关（房主开着、客人关着本来会让两端分叉），见那边的注释。
        try
        {
            if (!Core.BossConfig.IsMapEnabledForGameplay(act)) return null;
        }
        catch (Exception e) { Logger.Log("[CharBoss] 读地图开关失败（按关闭处理）: " + e.Message); return null; }

        // 配置项 A4「与原本 Boss 共用匹配池」（用户 2026-10-29）：
        //   开了替换的地图不再「单纯替换」，而是从「这张地图原本的 Boss」和「本模组的角色 Boss」里按种子抽一个。
        //   抽取只用「本局种子 + 幕 + 场」⇒ 一幕开始（生成房间）时就定下来，地图预告 = 实际 Boss，联机两端一致。
        // ★ v0.2.468：客人端用 **SharePoolForGameplay**（恒 false）—— 房主已经抽过了，结论就在同步槽位里，
        //   客人再按本机配置抽一次只会抽歪（用户 2026-11-07 的要求：这两项都以房主为准）。
        try
        {
            if (Core.BossConfig.SharePoolForGameplay)
            {
                int fight = secondBoss ? 1 : 0;

                // 子项（用户 2026-10-29）：进阶 10 第三幕**固定一场原本 + 一场角色**。
                // 满足条件时整幕只抽一次、两场互补，不再「各抽各的」。
                bool pairFixed = false;
                try
                {
                    pairFixed = Core.BossConfig.Asc10Act3OneEachForGameplay && act.Index >= 2;
                    if (pairFixed)
                    {
                        int asc = Core.BossLineups.Ascension();
                        pairFixed = asc >= 10;
                    }
                }
                catch { pairFixed = false; }

                bool keepVanilla = pairFixed
                    ? Core.BossLineups.Asc10Act3PicksVanilla(fight)
                    : Core.BossLineups.SharePoolPicksVanilla(act.Index, fight);

                if (keepVanilla)
                {
                    if (SharePoolLogged.Add((act.Index, fight)))
                    {
                        Logger.Log($"[CharBoss] 共用匹配池：第 {act.Index + 1} 幕第 {fight + 1} 场抽到**原本的 Boss** ⇒ " +
                                   "这一场不替换" +
                                   (pairFixed ? "（进阶 10 第三幕固定「一场原本 + 一场角色」）" : string.Empty));
                    }
                    return null;
                }
                if (SharePoolLogged.Add((act.Index, fight)))
                {
                    Logger.Log($"[CharBoss] 共用匹配池：第 {act.Index + 1} 幕第 {fight + 1} 场抽到**本模组的角色 Boss** ⇒ " +
                               "按完全替换处理（主 Boss 是角色 Boss，Boss 数 = 玩家数）" +
                               (pairFixed ? "（进阶 10 第三幕固定「一场原本 + 一场角色」）" : string.Empty));
                }
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 共用匹配池抽取失败（按替换处理）: " + e.Message); }

        // 用户 2026-10-26：阵容的**单位变成「Boss 标识」**（角色 + 变体，见 Core/BossKeys.cs）——
        // 因为刀猎与炼药猎都是静默猎手、却是两个不同的 Boss（分别属于第三幕的两个变体）。
        // 主 Boss = 阵容第 0 位；其余同时上场的那几只由 MultiBossPlan.PartnersFor 按同一份阵容补上。
        string? bossKey = null;
        int actIndex = 0;
        try
        {
            actIndex = Math.Clamp(act.Index, 0, ActTable.Length - 1);
            int fight = secondBoss ? 1 : 0;
            Core.BossLineups.NoteCurrentFight(actIndex, fight);      // 供「静默用哪套设计」判断
            var lineup = Core.BossLineups.For(actIndex, fight);
            if (lineup.Length > 0) bossKey = lineup[0];
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 取阵容失败，退回固定表: " + e.Message);
        }
        // ★ 2026-11-05（用户：「主 boss 仍然整局不重复」）：
        //   第二场再兜一次「主 Boss 不能和**存档里真实的第一场**同角色」。
        //   为什么要用存档而不是重算：第一场的 Boss 早就写进存档了，而 `SecondBossEncounterGetPatch`
        //   每次读取都用**当前版本**重算第二场 ⇒ 重算结果可能又落回第一个角色，于是两场主 Boss 同角色。
        if (secondBoss && bossKey != null)
        {
            try
            {
                int firstChar = CharacterOfEncounter(act.BossEncounter);
                if (firstChar >= 0 && BossKeys.CharacterOf(bossKey) == firstChar)
                {
                    var lineupNow = Core.BossLineups.For(actIndex, 1);
                    var alt = Core.BossLineups.AlternativeMainKey(actIndex, 1, firstChar, lineupNow);
                    if (alt != null)
                    {
                        Logger.Log($"[CharBoss] 第 {actIndex + 1} 层第 2 场：主 Boss {BossKeys.LabelOf(bossKey)}" +
                                   $" 与**存档里的第一场**同角色（角色下标 {firstChar}）→ 换成 {BossKeys.LabelOf(alt)}");
                        bossKey = alt;
                    }
                    else
                    {
                        Logger.Log($"[CharBoss] 第 {actIndex + 1} 层第 2 场：主 Boss {bossKey} 与第一场同角色，" +
                                   "但候补里没有别的角色可选（人太多时会这样）");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 第二场主 Boss 查重失败: " + e.Message);
            }
        }
        // ①b ★ v0.2.242（用户 2026-10-30：「进阶 10 三层两场战斗里的相同角色 Boss 都相同
        //     —— 两场都用刀猎、辉星储、易伤战」）：
        //     第二场绝不能和第一场用**同一个变体**。上面那段只管「主 Boss 的**角色**别重复」，
        //     而这里要管「同角色的**另一个变体**」（刀猎 vs 炼药猎 之类）：
        //     万一第二场算出来的 Key 就在第一场阵容里，就换成同角色的互补变体。
        if (secondBoss && bossKey != null)
        {
            try
            {
                var firstLineup = Core.BossLineups.For(actIndex, 0);
                if (Array.IndexOf(firstLineup, bossKey) >= 0)
                {
                    var complement = BossKeys.ComplementVariant(bossKey);
                    if (complement != null)
                    {
                        Logger.Log($"[CharBoss] 第 {actIndex + 1} 层第 2 场：主 Boss {bossKey} 与**第一场同一个变体**" +
                                   $"（第一场阵容 [{string.Join(",", firstLineup)}]）→ 换成同角色的另一个变体 {complement}");
                        bossKey = complement;
                    }
                    else
                    {
                        Logger.Log($"[CharBoss] 第 {actIndex + 1} 层第 2 场：主 Boss {bossKey} 与第一场重复，" +
                                   "但它没有成对变体（能力机 / 根除骨这类）⇒ 保留");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 第二场变体查重失败: " + e.Message);
            }
        }

        // ① 阵容给了 Boss 标识 ⇒ 直接换成它的专属遭遇（控制台 / 地图 / 死亡提示 / 实际打到的都是同一只）
        var byKey = BossKeys.EncounterOf(bossKey);
        if (byKey != null) return byKey;

        // ② 兜底：退回旧的「角色下标 → 档位遭遇」
        int character = 4;
        try
        {
            var table = ActTable[Math.Clamp(act.Index, 0, ActTable.Length - 1)];
            character = table[Math.Clamp(secondBoss ? 1 : 0, 0, table.Length - 1)];
        }
        catch { /* 用默认值 */ }

        var tier = TierEncounterFor(character, actIndex);
        if (tier != null) return tier;

        // ③ 这一档没有设计（例如铁甲战士一层以外 / 故障机器人一二层 / 猎手一层）
        //    → 退回按角色的基础遭遇，用起始卡组占位。
        if (character == SilentCharacterIndex)
        {
            var byAct = SilentEncounterForAct(act.Index);
            if (byAct != null) return byAct;
        }
        return Encounters.Value[character];
    }

    /// <summary>多人第三幕第一场的主 Boss 下标：2 = 故障机器人（能力机）。（-1 = 不改，沿用 ActTable）</summary>
    private const int Act3FirstBossInMultiplayer = 2;

    /// <summary>铁甲战士在角色表里的下标。</summary>
    private const int IroncladCharacterIndex = 0;

    /// <summary>静默猎手在角色表里的下标。</summary>
    private const int SilentCharacterIndex = 1;

    /// <summary>故障机器人在角色表里的下标。</summary>
    private const int DefectCharacterIndex = 2;

    /// <summary>亡灵契约师在角色表里的下标。</summary>
    private const int NecrobinderCharacterIndex = 3;

    /// <summary>储君在角色表里的下标。</summary>
    private const int RegentCharacterIndex = 4;

    /// <summary>静默猎手按幕分的遭遇（图标不同）；没设计的幕 → 用基础遭遇（占位图标）。</summary>
    private static EncounterModel? SilentEncounterForAct(int actIndex)
    {
        try
        {
            return actIndex switch
            {
                0 => ModelDb.Encounter<SilentSlyBossEncounter>(),       // 第一幕：奇巧猎
                1 => ModelDb.Encounter<SilentEnvenomBossEncounter>(),   // 第二幕：涂毒猎
                2 => ModelDb.Encounter<SilentShivBossEncounter>(),     // 第三幕：刀猎
                _ => null,
            };
        }
        catch (Exception e)
        {
            // 模型没注册成功时退回基础遭遇（图标退回占位），不要让整条替换链断掉
            Logger.Log("[CharBoss] 按幕的静默猎手遭遇取不到，退回基础遭遇: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// 调试开关用：默认给铁甲战士（样板卡组最完整）；
    /// 在 <c>charbosses_debug.txt</c> 里写角色名可指定，写 <c>cycle</c> 则每次轮换。
    ///
    /// 返回的是 `*NormalEncounter`（RoomType = Monster），所以这场调试战斗是**普通战斗**：
    /// 打赢给普通奖励、继续走当前幕的地图，不会因为「Boss 被打死」直接跳到下一幕。
    /// </summary>
    public static EncounterModel? ForDebug()
    {
        var all = DebugEncounters.Value;

        if (DebugMode.IsCycle) return all[DebugMode.NextCycleIndex(all.Length)];

        var requested = DebugMode.RequestedCharacter;
        return all[requested >= 0 ? requested : 0];
    }

    /// <summary>这个遭遇是不是我们自己的角色 Boss（用于避免二次映射）。</summary>
    public static bool IsOurs(EncounterModel? encounter)
    {
        if (encounter == null) return false;
        foreach (var ours in Encounters.Value)
        {
            if (ours != null && ours.Id == encounter.Id) return true;
        }
        // 调试用的「普通战斗版」也算我们的：读档/重进房间时不该被再次轮换掉
        foreach (var ours in DebugEncounters.Value)
        {
            if (ours != null && ours.Id == encounter.Id) return true;
        }
        // 按幕分的静默猎手遭遇（涂毒猎 / 刀猎，图标不同）
        foreach (var extra in SilentEncountersByAct())
        {
            if (extra != null && extra.Id == encounter.Id) return true;
        }
        // 「一只 Boss 一个 id」的控制台遭遇（含静默猎手那两个，重复判断无害）
        foreach (var tier in TierEncounters.Value)
        {
            if (tier != null && tier.Id == encounter.Id) return true;
        }
        return false;
    }

    /// <summary>按幕分的静默猎手遭遇（也用于 <see cref="IsOurs"/>，避免被二次映射）。</summary>
    private static IEnumerable<EncounterModel?> SilentEncountersByAct()
    {
        yield return SilentEncounterForAct(0);
        yield return SilentEncounterForAct(1);
        yield return SilentEncounterForAct(2);
    }

    /// <summary>该原版 Boss 在这一幕的候选列表里排第几位。</summary>
    private static int SlotOf(ActModel act, EncounterModel? vanilla)
    {
        if (vanilla == null) return 0;

        var index = 0;
        try
        {
            foreach (var candidate in act.BossDiscoveryOrder)
            {
                if (candidate.Id == vanilla.Id) return index;
                index++;
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 读 BossDiscoveryOrder 失败: " + e.Message);
        }
        return 0;
    }
}
