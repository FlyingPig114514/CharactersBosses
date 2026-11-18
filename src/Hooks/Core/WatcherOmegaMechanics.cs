#if STS2_111   // 观者 Boss 只在 0.113 构建里（用户：107 不加这个 Boss）
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Powers;

namespace CharacterBosses.Core;

/// <summary>
/// 三层 2 号「**Ω观**」的机制接管（用户 2026-11-16）。全部是**运行期** Harmony
/// （目标类型在别的程序集里，铁律：不编译期引用）。
///
/// | 需求 | 反编译结论 | 做法 |
/// |---|---|---|
/// | 欧米伽隔一回合触发一次 | `OmegaPower.AfterSideTurnEnd` 每回合结束对所有敌人造成 `MagicNumber` 点伤害 | 压掉原版，只在她**奇数回合**结束时代为结算 |
/// | 点穴伤害可被格挡 | `WatcherPressurePoints.OnPlay` 对每个有印记的敌人 `CreatureCmd.Damage(…, ValueProp)6, …)`（**含 Unblockable**） | 前缀整条接管：上印记照旧，伤害换成可格挡的 `ValueProp.Unpowered` |
/// | 不惧妖邪按「被锁玩家这回合有没有打攻击牌」进平静 | `FearNoEvil.OnPlay` 判的是「**目标敌人的意图**是不是攻击」，对我们的影子玩家（目标其实是玩家）恒为 false | 后缀：buff 记到「玩家打过攻击牌」且她还没在平静 ⇒ 补一次 `EnterCalm` |
/// | 阿尔法/贝塔生成的牌要展示 | `Alpha/Beta` 把贝塔/欧米伽放进**抽牌堆**（`(PileType)1`） | 打完从她的牌堆里找出来，用原版预览界面亮一下（同「羽化」） |
/// </summary>
internal static class WatcherOmegaMechanics
{
    private static bool _tried;
    private static int _logs;

    // ── 安装 ─────────────────────────────────────────────────────────────
    internal static void TryInstall()
    {
        if (_tried) return;
        try
        {
            if (WatcherCards.ModAssembly() == null) return;
            bool any = InstallOmegaGate();
            any |= InstallPressurePointsBlockable();
            any |= InstallFearNoEvilCalm();
            if (any) _tried = true;
        }
        catch (Exception e) { Logger.Log("[CharBoss] Ω观机制安装失败: " + e.Message); }
    }

    /// <summary>只有**我们的 Ω观**（身上挂着「斋戒」）才接管；别的观者（真人玩家、其它 Boss）一律原版。</summary>
    private static Monsters.CharBossMonster? OurOmegaBoss(Creature? creature)
    {
        try
        {
            if (creature?.Monster is not Monsters.CharBossMonster boss) return null;
            if (boss.Creature?.GetPower<WatcherFastingPower>() == null) return null;
            return boss;
        }
        catch { return null; }
    }

    /// <summary>
    /// 「**会用不惧妖邪进平静**」的观者 Boss：Ω观（斋戒）**或** 三层 3 号「红蓝观」（节制）。
    /// 两份设计都要「被锁玩家本回合打过攻击牌 ⇒ 她那记不惧妖邪让她进平静」，所以闸门从
    /// 「只认 Ω观」放宽成这两只（用户 2026-11-18）。
    ///
    /// ⚠ 只放宽**这一条**（不惧妖邪 → 平静）。Ω观 专属的欧米伽 / 点穴那套仍然只认 `OurOmegaBoss`，
    /// 别把红蓝观也算成 Ω观（否则她的意图里会凭空多出欧米伽伤害）。
    /// </summary>
    internal static Monsters.CharBossMonster? FearNoEvilBoss(Creature? creature)
    {
        try
        {
            if (creature?.Monster is not Monsters.CharBossMonster boss) return null;
            if (OurOmegaBoss(creature) != null) return boss;
            if (boss.Creature?.GetPower<Powers.BossTemperancePower>() != null) return boss;
        }
        catch { /* 忽略 */ }
        return null;
    }
    /// <summary>这个 power 挂在哪只 Ω观身上（没有就 null）。给 `WatcherFearNoEvilAimPower` 用。</summary>
    internal static Monsters.CharBossMonster? OmegaOwnerOf(PowerModel? power) => OurOmegaBossOfPower(power);

    private static Monsters.CharBossMonster? OurOmegaBossOfPower(PowerModel? power)
    {
        try { return OurOmegaBoss(power?.Owner); } catch { return null; }
    }

    // ── ① 欧米伽：隔一回合触发一次 ────────────────────────────────────────
    private static bool InstallOmegaGate()
    {
        try
        {
            var type = WatcherCards.T("OmegaPower");
            if (type == null)
            {
                Logger.Log("[CharBoss] 斋戒：找不到观者的 OmegaPower（B 版可能不叫这个名）⇒ 欧米伽每回合都会触发");
                return true;
            }
            var method = type.GetMethod("AfterSideTurnEnd",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
            {
                Logger.Log("[CharBoss] 斋戒：OmegaPower 上没有 AfterSideTurnEnd ⇒ 跳过");
                return true;
            }
            var prefix = new HarmonyMethod(typeof(WatcherOmegaMechanics)
                .GetMethod(nameof(OmegaGatePrefix), BindingFlags.Static | BindingFlags.NonPublic)!);
            new Harmony("CharacterBosses.watcher.omegagate").Patch(method, prefix: prefix);
            Logger.Log("[CharBoss] 斋戒：已挂钩 OmegaPower.AfterSideTurnEnd（只在她奇数回合结算）");
            return true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 斋戒：欧米伽挂钩失败: " + e.Message);
            return true;
        }
    }

    /// <summary>
    /// 「下一次她回合结束时，欧米伽要不要结算」这一位（严格交替）。
    ///
    /// ★ v0.2.698（用户 2026-11-16：「欧米伽的预览怎么每回合都有？」＋「7/9/11 反而没了」）：
    ///   以前按 `boss.TurnNumber % 2` 判奇偶，但那个数**不可靠**：
    ///   ① 日志实证「她第 5 回合结束」那一刻 `TurnNumber` 已经是 6（`PrepareTurn` 给下一回合做的手牌
    ///      先跑完了）⇒ 该触发的第 5 回合被判成「偶数 ⇒ 跳过」，实际变成了 6/8/10 触发（正好反了）；
    ///   ② 原版 `AfterSideTurnEnd` 对**两侧**都会派发，前缀没有过滤阵营 ⇒ 一回合里可能翻两次。
    ///   现在不用回合号：在**她自己那一侧**的回合结束钩子里读一个布尔位、然后取反。
    ///   初始值 true ⇒ 她打出欧米伽的那一回合（第 5 回合）结束就结算，之后 6 跳过、7 结算、8 跳过……
    /// </summary>
    private static readonly Dictionary<Monsters.CharBossMonster, bool> OmegaFireNext = new();

    private static bool OmegaFireFlag(Monsters.CharBossMonster boss)
    {
        if (!OmegaFireNext.TryGetValue(boss, out bool v))
        {
            if (OmegaFireNext.Count > 32) OmegaFireNext.Clear();   // 不该发生；防御性
            v = true;
            OmegaFireNext[boss] = v;
        }
        return v;
    }

    private static bool OmegaGatePrefix(object __instance, object[] __args, ref Task __result)
    {
        try
        {
            var power = __instance as PowerModel;
            var boss = OurOmegaBossOfPower(power);
            if (boss == null) return true;

            // 只处理「她自己那一侧」的回合结束（原版对玩家侧也会派发）
            try
            {
                MegaCrit.Sts2.Core.Combat.CombatSide? side = null;
                if (__args != null)
                {
                    foreach (var a in __args)
                    {
                        if (a is MegaCrit.Sts2.Core.Combat.CombatSide s) { side = s; break; }
                    }
                }
                if (side.HasValue && power!.Owner.Side != side.Value) return true;
            }
            catch { /* 拿不到阵营就照常处理 */ }

            // ★ v0.2.733（用户 2026-11-17）：**恐惧版斋戒取消了「隔一回合触发一次」** ⇒ 每回合都结算。
            //   这里直接放行、并且**不翻那一位**（万一中途关了恐惧模式，隔回合的节奏还接得上）。
            if (Core.DreadMode.IsActive)
            {
                if (_logs++ < 8)
                    Logger.Log("[CharBoss] 斋戒（恐惧版）：每回合都结算欧米伽（去掉隔回合限制）");
                return true;
            }

            bool fire = OmegaFireFlag(boss);
            OmegaFireNext[boss] = !fire;      // 下一回合反过来

            if (fire) return true;            // 原版照常结算

            __result = Task.CompletedTask;
            if (_logs++ < 8)
                Logger.Log($"[CharBoss] 斋戒：这一回合结束时欧米伽不结算（隔一回合；她 TurnNumber={boss.TurnNumber}）");
            return false;
        }
        catch { return true; }
    }

    /// <summary>
    /// 欧米伽**这一回合结束**会不会结算（给意图用）——
    /// 就是上面那一位（她自己的回合里 = 本回合；玩家回合里看的是她下一回合）。
    ///
    /// ★ v0.2.733（用户 2026-11-17）：**恐惧模式下每回合都触发**（不受「隔一回合」限制）。
    /// </summary>
    internal static bool OmegaTriggersOn(Monsters.CharBossMonster? boss)
    {
        try
        {
            if (boss == null) return false;
            if (Core.DreadMode.IsActive) return true;
            return OmegaFireFlag(boss);
        }
        catch { return false; }
    }

    // ── ② 点穴：伤害可以被格挡 ────────────────────────────────────────────
    /// <summary>
    /// **玩家这一场是不是被「点穴」打死的**（用户 2026-11-17：Ω观的死亡提示要按死因特判）。
    /// `null` = 不是被点穴打死的；否则 = 被点穴打死的那位玩家的角色模型
    /// （死亡提示的 `GetLossMessageFor(character)` 拿这个做引用比对，多人时不会张冠李戴）。
    /// </summary>
    internal static MegaCrit.Sts2.Core.Models.CharacterModel? PressurePointsKillCharacter;

    private static bool InstallPressurePointsBlockable()
    {
        try
        {
            var type = WatcherCards.PressurePoints;
            if (type == null)
            {
                Logger.Log("[CharBoss] 斋戒：找不到观者的点穴 ⇒ 点穴伤害仍是无视格挡");
                return true;
            }
            var method = type.GetMethod("OnPlay",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
            {
                Logger.Log("[CharBoss] 斋戒：点穴上没有 OnPlay ⇒ 跳过");
                return true;
            }
            var prefix = new HarmonyMethod(typeof(WatcherOmegaMechanics)
                .GetMethod(nameof(PressurePointsPrefix), BindingFlags.Static | BindingFlags.NonPublic)!);
            new Harmony("CharacterBosses.watcher.pressurepoints").Patch(method, prefix: prefix);
            Logger.Log("[CharBoss] 斋戒：已接管点穴 OnPlay（印记照旧，伤害改成可被格挡）");
            return true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 斋戒：点穴挂钩失败: " + e.Message);
            return true;
        }
    }

    /// <summary>
    /// 观者「点穴」的整条接管。原版（反编译 `WatcherPressurePoints.OnPlay`）：
    /// <code>
    /// await PowerCmd.Apply&lt;MarkPower&gt;(cardPlay.Target, MagicNumber, Owner.Creature, this);
    /// foreach (var c in CombatState.HittableEnemies) {
    ///     int marks = c.GetPowerAmount&lt;MarkPower&gt;();
    ///     if (marks &gt; 0) await CreatureCmd.Damage(ctx, c, marks, (ValueProp)6, Owner.Creature, this, cardPlay);
    /// }
    /// </code>
    /// `(ValueProp)6` 里带 **Unblockable** ⇒ 我们的「斋戒」把它换成 `ValueProp.Unpowered`
    /// （仍然不吃力量/虚弱，但**吃格挡**，正是用户要的「点穴造成的伤害可以被你格挡」）。
    /// </summary>
    private static bool PressurePointsPrefix(CardModel __instance, PlayerChoiceContext choiceContext,
                                             CardPlay cardPlay, ref Task __result)
    {
        try
        {
            var owner = __instance?.Owner;
            var boss = OurOmegaBoss(owner?.Creature);
            if (boss == null) return true;                 // 不是我们的 Ω观 ⇒ 原版

            var target = cardPlay?.Target;
            if (target == null) return true;
            __result = ApplyBlockablePressurePointsAsync(__instance!, owner!, boss, choiceContext, cardPlay!, target);
            return false;
        }
        catch { return true; }
    }

    private static async Task ApplyBlockablePressurePointsAsync(CardModel card, Player owner,
                                                                Monsters.CharBossMonster boss,
                                                                PlayerChoiceContext ctx, CardPlay play,
                                                                Creature target)
    {
        decimal marks = 0m;
        try { marks = card.DynamicVars["MagicNumber"].BaseValue; } catch { marks = 0m; }

        // ① 给目标上「印记」（原版 `MarkPower`，A 版 `Mark`）
        var markType = WatcherCards.T("MarkPower") ?? WatcherCards.T("Mark");
        if (markType != null && marks > 0m)
        {
            try { await WatcherCards.ApplyPowerAsync(markType, target, marks, owner.Creature, card); }
            catch (Exception e) { Logger.Log("[CharBoss] 点穴：上印记失败: " + e.Message); }
        }

        // ② 所有**有印记**的敌人失去等同于层数的生命 —— 但**可被格挡**（用户口径）
        int hitTargets = 0;
        try
        {
            var enemies = card.CombatState?.HittableEnemies;
            if (enemies != null)
            {
                foreach (var c in new List<Creature>(enemies))
                {
                    if (c == null || !c.IsAlive) continue;
                    int stacks = MarkAmount(c);
                    if (stacks <= 0) continue;
                    await CreatureCmd.Damage(ctx, c, stacks, ValueProp.Unpowered, owner.Creature, card, play);
                    hitTargets++;

                    // ★ v0.2.736（用户 2026-11-17）：如果是玩家、而且这一下把他打死了 ⇒ 记下死因
                    //   （死亡提示 `WATCHER_OMEGA_BOSS_ENCOUNTER.loss_pressure` 会因此替换掉默认那句）
                    try
                    {
                        if (c.Player != null && (!c.IsAlive || c.CurrentHp <= 0))
                        {
                            PressurePointsKillCharacter = c.Player.Character;
                            Logger.Log($"[CharBoss] Ω观：{c.Name} 被点穴打死 ⇒ 死亡提示改用点穴专属文案");
                        }
                    }
                    catch { /* 忽略 */ }
                }
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 点穴：结算伤害失败: " + e.Message); }

        if (_logs++ < 12)
            Logger.Log($"[CharBoss] 斋戒：点穴 给她上 {marks} 层印记 ⇒ 对 {hitTargets} 个有印记的敌人结算" +
                       "（这一下**可以被格挡**）");
    }

    /// <summary>她给某个敌人叠了多少层「印记」（A: `MarkPower`，B 可能是别的名字）。</summary>
    internal static int MarkAmount(Creature? creature)
    {
        try
        {
            var powers = creature?.Powers;
            if (powers == null) return 0;
            for (int i = 0; i < powers.Count; i++)
            {
                var p = powers[i];
                if (p == null) continue;
                string name;
                try { name = p.GetType().Name; } catch { continue; }
                if (name != "MarkPower" && name != "Mark") continue;
                return Math.Max(0, (int)Math.Round((double)p.Amount));
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    // ── ③ 不惧妖邪：被锁玩家打过攻击牌 ⇒ 进平静 ──────────────────────────
    private static bool InstallFearNoEvilCalm()
    {
        try
        {
            var type = WatcherCards.FearNoEvil;
            if (type == null)
            {
                Logger.Log("[CharBoss] 不惧妖邪：找不到这张牌 ⇒ 跳过");
                return true;
            }
            var method = type.GetMethod("OnPlay",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
            {
                Logger.Log("[CharBoss] 不惧妖邪：没有 OnPlay ⇒ 跳过");
                return true;
            }
            var postfix = new HarmonyMethod(typeof(WatcherOmegaMechanics)
                .GetMethod(nameof(FearNoEvilPostfix), BindingFlags.Static | BindingFlags.NonPublic)!);
            new Harmony("CharacterBosses.watcher.fearnoevil").Patch(method, postfix: postfix);
            Logger.Log("[CharBoss] 不惧妖邪：已挂钩 OnPlay（被锁玩家这回合打过攻击牌 ⇒ 进平静）");
            return true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 不惧妖邪挂钩失败: " + e.Message);
            return true;
        }
    }

    /// <summary>
    /// 原版判据是「**目标的意图**是不是攻击」（`WatcherSimpleAHelper.IsTargetAttacking(cardPlay.Target)`），
    /// 而她的目标是**玩家**（没有意图）⇒ 原版永远不会进平静。
    /// 用户口径：「如果本回合被她锁定的玩家打出攻击牌，观者就在本回合打出不惧妖邪时进入平静，
    /// 否则打出不惧妖邪时不改变姿态」⇒ 由 <see cref="WatcherFearNoEvilAimPower"/> 记这件事，这里补结算。
    /// </summary>
    private static void FearNoEvilPostfix(CardModel __instance)
    {
        try
        {
            var owner = __instance?.Owner;
            // ★ v0.2.759（用户 2026-11-18：「即使打出攻击牌，不惧妖邪也没有使她进入平静」）：
            //   原来只认 Ω观（`OurOmegaBoss` = 身上有「斋戒」那只）⇒ 三层 3 号「红蓝观」永远走不进这里。
            //   现在改成「**会用不惧妖邪进平静的观者 Boss**」：Ω观 或 红蓝观（身上有「节制」）。
            var boss = FearNoEvilBoss(owner?.Creature);
            if (boss == null) return;

            var aim = boss.Creature?.GetPower<WatcherFearNoEvilAimPower>();
            if (aim == null || !aim.PlayerAttacked) return;
            if (WatcherCards.InCalm(owner)) return;      // 原版已经进了就不重复

            _ = WatcherCards.EnterCalmAsync(owner);
            Logger.Log("[CharBoss] 不惧妖邪：被锁定的玩家这回合打过攻击牌 ⇒ 她进入「平静」");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 不惧妖邪结算失败: " + e.Message); }
    }

    // ── ④ 阿尔法/贝塔：把生成的那张亮给玩家看 ────────────────────────────
    /// <summary>
    /// 她打完「阿尔法」⇒ 抽牌堆里会多一张「贝塔」；打完「贝塔」⇒ 多一张「欧米伽」。
    /// 与「聚能成刃 → 灭除之刃」同一套做法：从她所有牌堆里找出来，用原版预览界面展示。
    /// </summary>
    internal static void NoteGeneratedAsync(Monsters.CharBossMonster boss, CardModel? playedCard)
    {
        try
        {
            if (boss == null || playedCard == null) return;
            string? wantEntry = null;
            string? what = null;
            if (Monsters.CharBossMonster.WatcherIdIs(playedCard.Id.Entry, "ALPHA")) { wantEntry = "BETA"; what = "贝塔"; }
            else if (Monsters.CharBossMonster.WatcherIdIs(playedCard.Id.Entry, "BETA")) { wantEntry = "OMEGA"; what = "欧米伽"; }
            if (wantEntry == null) return;

            var found = new List<CardModel>();
            string pileDesc = "无";
            try
            {
                var pcs = boss.Shadow?.PlayerCombatState;
                if (pcs?.AllPiles != null)
                {
                    foreach (var pile in pcs.AllPiles)
                    {
                        if (pile == null) continue;
                        foreach (var c in pile.Cards)
                        {
                            if (c == null) continue;
                            if (!Monsters.CharBossMonster.WatcherIdIs(c.Id.Entry, wantEntry)) continue;
                            found.Add(c);
                            if (pileDesc == "无") { try { pileDesc = pile.Type.ToString(); } catch { pileDesc = "?"; } }
                        }
                    }
                }
            }
            catch { /* 忽略 */ }

            if (found.Count == 0)
            {
                Logger.Log($"[CharBoss] {what}：打完了，但所有牌堆里都没找到生成的「{what}」");
                return;
            }

            Logger.Log($"[CharBoss] {what}：生成的「{what}」在「{pileDesc}」（{found.Count} 张）⇒ 用原版预览界面展示");
            MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(Core.BossCardPreview.ShowAsync(boss, found, 1.8f));
        }
        catch (Exception e) { Logger.Log("[CharBoss] 生成牌展示失败: " + e.Message); }
    }

    /// <summary>
    /// 欧米伽这一下会打多少。
    ///
    /// ★ v0.2.696（用户：「欧米伽的伤害预览没有再出现，应该在 7、9、11 等回合也出现」）：
    ///   反编译 `Watcher.Code.Powers.OmegaPower.AfterSideTurnEnd` —— 伤害直接取 **`Amount`**：
    ///   <code>
    ///   if (Owner.Side == side &amp;&amp; Amount &gt; 0)
    ///       await CreatureCmd.Damage(ctx, CombatState.GetOpponentsOf(Owner).Where(alive), Amount, (ValueProp)4, Owner);
    ///   </code>
    ///   而「欧米伽」这张牌是 `WithPower&lt;OmegaPower&gt;(50, 10)`（升级 +10）⇒ power 的 `Amount` 就是伤害。
    ///   上一版读的是 `DynamicVars["MagicNumber"]`（那是**点穴**的变量名），power 上根本没有 ⇒ 抛异常 ⇒
    ///   兜底又只在「她这回合要打欧米伽」时才成立 ⇒ 第 7/9/11 回合整条消失。
    ///
    /// 现在按顺序取：① power 的 `Amount`；② power 上万一有 `MagicNumber`；③ 本回合计划里那张欧米伽的
    /// `PowerVar&lt;OmegaPower&gt;` 牌面（第 5 回合她还没把它打出去时用）。
    /// </summary>
    internal static int OmegaDamage(Monsters.CharBossMonster? boss)
    {
        try
        {
            var powers = boss?.Creature?.Powers;
            if (powers != null)
            {
                for (int i = 0; i < powers.Count; i++)
                {
                    var p = powers[i];
                    if (p == null) continue;
                    string name;
                    try { name = p.GetType().Name; } catch { continue; }
                    if (name != "OmegaPower") continue;
                    try
                    {
                        int amount = (int)Math.Round((double)p.Amount);
                        if (amount > 0) return amount;      // ← 真实伤害就是这个
                    }
                    catch { /* 读不到就看变量 */ }
                    try
                    {
                        int magic = (int)Math.Round((double)p.DynamicVars["MagicNumber"].BaseValue);
                        if (magic > 0) return magic;
                    }
                    catch { /* 没有这个变量 */ }
                    break;
                }
            }

            // power 还没上身 ⇒ 用她本回合计划里那张「欧米伽」的牌面
            try
            {
                foreach (var play in boss!.Plan)
                {
                    var card = play.Card;
                    if (card == null) continue;
                    if (!Monsters.CharBossMonster.WatcherIdIs(card.Id.Entry, "OMEGA")) continue;
                    int face = BossDamagePredictor.VarPreviewInt(card, "OmegaPower", 0);
                    if (face <= 0) face = BossDamagePredictor.VarPreviewInt(card, "MagicNumber", 0);
                    if (face > 0) return face;
                }
            }
            catch { /* 忽略 */ }
        }
        catch { /* 忽略 */ }
        return 0;
    }
}
#endif
