using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace CharacterBosses.Patches;

/// <summary>
/// 让 <c>CombatState.HittableEnemies</c> 变成**相对出手方**的语义。
///
/// 原版实现是 <c>Enemies.Where(e =&gt; e.IsHittable)</c> —— 永远是「敌方阵营」，
/// 因为原版里出牌的永远是玩家。但角色 Boss 在敌方阵营、用玩家卡牌出牌，
/// 于是所有走 <c>HittableEnemies</c> 的「对所有敌人/随机敌人」效果都会打到 **Boss 自己**：
///   · 弹跳药瓶 → 给自己上毒
///   · 下砸    → 给自己减力量
///   · 回响斩击 → 打自己
/// 修法：敌方回合（也就是 Boss 出牌的时段）把「可命中的敌人」解释成**玩家侧**；
/// 玩家回合保持原样，所以玩家侧一切行为不受影响。
/// </summary>
[HarmonyPatch(typeof(CombatState), "get_HittableEnemies")]
public static class HittableEnemiesSidePatch
{
    /// <summary>
    /// 手动替影子 Player 触发遗物钩子时用的强制开关。
    /// 那种时候当前阵营还是「玩家回合」，但语义上应该是「Boss 自己出手」，
    /// 所以要把「可命中敌人」解释成玩家侧（例如弹珠袋应该给玩家上易伤，而不是给 Boss 自己）。
    /// </summary>
    public static bool ForceRelativeSide;

    /// <summary>
    /// 「她正在出牌」的嵌套深度（&gt; 0 = 这次读取 `HittableEnemies` 是她打出的牌在找目标）。
    ///
    /// 为什么不能再用「当前阵营 == 敌方」当判据（用户 2026-10-24 实测的两个 bug）：
    ///   · 玩家（亡灵契约师）打出「亡灵精通」后，**自己**的奥斯提掉血 → 那张 power 是
    ///     `CreatureCmd.Damage(ctx, CombatState.HittableEnemies, …)`，在她的回合结算 ⇒ 被翻成玩家侧
    ///     ⇒ 伤害打在玩家自己身上；
    ///   · 玩家（六火亡魂）引爆「灼魂」：崩坠 `SoulBurnPower.AfterSideTurnStart` 同样是
    ///     `Damage(ctx, CombatState.HittableEnemies, …)`，在**敌方回合开始**结算 ⇒ 同样炸到自己。
    /// 这两者的共同点：**效果属于玩家那一侧**，只是碰巧在她的回合结算。原版语义里
    /// `HittableEnemies` 就是「敌方阵营」（= 她），对它们是正确的。
    /// 所以只有「她自己在出手」时才需要翻：出牌期间（本计数）、她的遗物钩子（<see cref="ForceRelativeSide"/>）、
    /// 以及少数她自己的回合边界效果（用 <see cref="RequestRelativeSideOnce"/> 一次性标记）。
    /// </summary>
    internal static int BossActingDepth;

    /// <summary>
    /// 「正在出牌的 Boss **本身就在玩家侧**」的嵌套深度（斗蛐蛐红队：2026-11-08 用户实测
    /// 「刀猎的伤害打在自己身上」）。
    ///
    /// 斗蛐蛐里红队战士是玩家侧的宠物，于是 <c>HittableEnemies</c>（= `Enemies`）**本来就是对面的
    /// 蓝队 Boss** —— 这时再按「翻成玩家侧」去解析，就变成「打自己这一边」：
    /// 群攻牌 <c>FAN_OF_KNIVES</c> 的目标直接选中了她自己。
    /// 判据很简单：<c>BossActingDepth &gt; 0</c> 且出手的这只 Boss 的 <c>Side == Player</c> ⇒ 走原版。
    /// </summary>
    internal static int PlayerSideBossDepth;

    /// <summary>
    /// 「**接下来这一段（可能跨 await/多帧）都按玩家侧解析 `HittableEnemies`**」的计数。
    ///
    /// ★ v0.2.308（玩家反馈：「倾泻打出巨石会打自己」）：
    /// 「倾泻」（`Cascade`）的实现是
    /// <c>CardPileCmd.AutoPlayFromDrawPile(ctx, owner, n, Top, forceExhaust:false)</c> ——
    /// 从**摸牌堆顶**把牌自动打出去（巨石 `GiantRock` 就是这种 `TargetType.AnyEnemy` 的 token）。
    /// 这条路径可能在**她自己回合的边界之后**才真正结算（而且是一条自己 await 的链），
    /// 那时 `CurrentSide` 已经翻成 Player ⇒ 我们的「敌方回合才翻侧」判据不成立
    /// ⇒ 巨石的选靶按**敌方阵营**解析 = **打她自己**（和当年「红面具的虚弱加到自己身上」同一类）。
    ///
    /// 所以这里给 `AutoPlayFromDrawPile` 单独开一个**计数**（计数而不是 bool：可能嵌套/并发），
    /// 整条任务跑完再减回去（见 `Patches/AutoPlayFromDrawPileSideHoldPatch`）。
    /// </summary>
    internal static int ForceRelativeHold;

    private static int _vanillaOnceFrame = -1;

    /// <summary>「这一场没有角色 Boss ⇒ 走原版」这条日志最多打几次（免得普通战斗刷屏）。</summary>
    private static int _noBossSkipLogged;

    /// <summary>
    /// **豁免**：接下来那一两次读取 `HittableEnemies` 强制走原版（敌方阵营 = 她），只在这两帧内有效。
    ///
    /// ⚠ 这个「时间窗」版本对**异步结算**不可靠（伤害常在 `Cmd.Wait`/动画之后，早就过了两帧），
    /// 现在真正的豁免走 <see cref="PlayerEffectExemption"/> 的 `PushPlayerEffect`/`PopPlayerEffect`
    /// （挂在状态机 MoveNext 上，覆盖整个方法体）。这个 API 留着给「同步、立刻读」的场合。
    /// </summary>
    internal static void RequestVanillaOnce()
    {
        try { _vanillaOnceFrame = (int)Godot.Engine.GetFramesDrawn(); }
        catch { _vanillaOnceFrame = 0; }
    }

    private static bool ConsumeVanillaOnce()
    {
        if (_vanillaOnceFrame < 0) return false;

        int now;
        try { now = (int)Godot.Engine.GetFramesDrawn(); }
        catch { now = _vanillaOnceFrame; }

        int requested = _vanillaOnceFrame;
        _vanillaOnceFrame = -1;                 // 一次性：用过就清
        return now - requested <= 2;            // 超过两帧还没被用到就作废，免得漏到后面的读取
    }

    /// <summary>
    /// 「玩家施加的效果正在结算」的嵌套深度（&gt; 0 时 `HittableEnemies` 走原版）。
    /// 由 <see cref="PlayerEffectExemption"/> 挂在那些 power 的 async 状态机上，进出成对。
    /// </summary>
    internal static int PlayerEffectDepth;

    internal static void PushPlayerEffect()
    {
        // 兜底：万一某次 postfix 没跑到（异常），不让深度永久卡住把翻侧整场关掉
        if (PlayerEffectDepth > 16) PlayerEffectDepth = 0;
        PlayerEffectDepth++;
    }

    internal static void PopPlayerEffect()
    {
        if (PlayerEffectDepth > 0) PlayerEffectDepth--;
    }

    /// <summary>
    /// 「**真人玩家自己的牌正在结算**」的嵌套深度（&gt; 0 时 `HittableEnemies` 走原版）。
    ///
    /// ★ v0.2.293（玩家 2026-11-02 反馈：「玩家战士打了地狱狂徒，被 boss 击中触发遗物百年积木的效果，
    ///   积木抽上来的打击牌会**自动对玩家打出**而非对 boss 打出」）——**这是真 bug，我们这边**。
    ///
    /// 链路（全部反编译核对过）：
    ///   1. 玩家身上「地狱狂徒」`HellraiserPower`：`AfterCardDrawnEarly` 里
    ///      `CardCmd.AutoPlay(choiceContext, card, target: null)` —— **不给目标**；
    ///   2. 原版帮它选目标的那段（`CardCmd.AutoPlay` 里的过滤委托）是
    ///      `HittableEnemies` 里挑 `c.IsPlayer &amp;&amp; c != card.Owner.Creature`；
    ///   3. 玩家被打 ⇒ 遗物「百年积木」`CentennialPuzzle` 在**敌方回合**里抽 3 张 ⇒ 抽到「打击」⇒ 走 1；
    ///   4. 而我们在**敌方回合**把 `HittableEnemies` 翻成了「玩家侧」⇒ 第 2 步只能在**玩家**里挑
    ///      ⇒ 玩家自己的打击打到了玩家自己（单人时就是他自己）。
    ///
    /// 判据：这张牌是**真人玩家**的（`cardPlay.Player.Creature.Monster == null`）⇒ 那一刻的
    /// `HittableEnemies` 必须保持原版语义（对手 = Boss 侧），跟原版单机一模一样。
    /// 她自己（影子玩家）的牌不算，照旧翻成玩家侧。
    ///
    /// ⚠ **登记点必须在 `CardCmd.AutoPlay` 的入口**（用户 2026-11-02 本机实测：
    ///   「第一张剑柄打击还是会打自己」）——说明**目标是在 `AutoPlay` 内部、`Hook.BeforeCardPlayed`
    ///   **之前**就选好的 ⇒ 只挂 BeforeCardPlayed 会漏掉每一串自动出牌的**第一张**
    ///   （后面几张靠前一张还没注销的登记侥幸生效）。所以现在两根钩子都挂：
    ///     · `CardCmd.AutoPlay` 前缀（按**卡牌实例**登记）—— 覆盖目标选择那一段；
    ///     · `Hook.BeforeCardPlayed` 前缀（按 `CardPlay` 实例登记）—— 覆盖结算过程中的嵌套读取。
    ///   注销都在 `Hook.AfterCardPlayed` 后缀（两种键各摘一次）。
    ///
    /// 为什么用「记实例」而不是简单计数：钩子是配对的，但中间抛异常就可能只进不出；
    /// 记实例能在 After 里精确摘掉那一个，再加一条「超过 15 秒还没等到 After 就整段作废」的兜底，
    /// 确保不会把翻侧永久关掉（翻侧关掉的后果是她自己的 AOE 打自己）。
    /// </summary>
    internal static int RealPlayerCardDepth => _realPlayerPlays.Count + _realPlayerAutoPlays.Count;

    /// <summary>`Hook.BeforeCardPlayed` 登记的 `CardPlay` 实例。</summary>
    private static readonly HashSet<object> _realPlayerPlays = new(ReferenceEqualityComparer.Instance);

    /// <summary>`CardCmd.AutoPlay` 入口登记的**卡牌**实例（目标就是在那之后选的）。</summary>
    private static readonly HashSet<object> _realPlayerAutoPlays = new(ReferenceEqualityComparer.Instance);

    private static double _realPlayerPlayAt;          // 最近一次登记的秒数（含兜底过期）

    /// <summary>这张牌是不是**真人玩家**打出的（不是她/影子玩家的）。</summary>
    internal static bool IsRealPlayerCard(MegaCrit.Sts2.Core.Entities.Cards.CardPlay? cardPlay)
    {
        try
        {
            var creature = CharacterBosses.Core.GameCompat.ActorOf(cardPlay)?.Creature;
            if (creature == null) return false;
            if (creature.Monster != null) return false;        // 怪物 / 我们的影子玩家
            return creature.Player != null;
        }
        catch { return false; }
    }

    /// <summary>这张**卡牌**是不是真人玩家名下的（`CardCmd.AutoPlay` 入口用的判据）。</summary>
    internal static bool IsRealPlayerCardModel(MegaCrit.Sts2.Core.Models.CardModel? card)
    {
        try
        {
            var creature = card?.Owner?.Creature;
            if (creature == null) return false;
            if (creature.Monster != null) return false;
            return creature.Player != null;
        }
        catch { return false; }
    }

    /// <summary>
    /// <c>CardCmd.AutoPlay</c> 前缀里调：登记「真人玩家的这张牌要自动打出」。
    /// **必须在目标选择之前**——原版 `AutoPlay` 是先挑目标、再走 `BeforeCardPlayed`。
    /// </summary>
    internal static void PushRealPlayerAutoPlay(object? card, MegaCrit.Sts2.Core.Combat.ICombatState? combat)
    {
        try
        {
            if (card == null) return;
            if (_realPlayerAutoPlays.Count > 32) _realPlayerAutoPlays.Clear();   // 兜底
            _realPlayerAutoPlays.Add(card);
            _realPlayerPlayAt = NowSeconds();
            LogRealPlayerCardInEnemyTurn(combat, SafeEntry(card));
        }
        catch { /* 忽略 */ }
    }

    /// <summary><c>Hook.AfterCardPlayed</c> 后缀里调：这张牌结算完了，把两种登记都摘掉。</summary>
    internal static void PopRealPlayerCard(MegaCrit.Sts2.Core.Entities.Cards.CardPlay? cardPlay)
    {
        try
        {
            if (cardPlay == null) return;
            _realPlayerPlays.Remove(cardPlay);
            var card = cardPlay.Card;
            if (card != null) _realPlayerAutoPlays.Remove(card);
        }
        catch { /* 忽略 */ }
    }

    /// <summary><c>Hook.BeforeCardPlayed</c> 里调：登记「真人玩家的这张牌开始结算」。</summary>
    internal static void PushRealPlayerCard(MegaCrit.Sts2.Core.Combat.ICombatState? combat,
                                            MegaCrit.Sts2.Core.Entities.Cards.CardPlay? cardPlay)
    {
        try
        {
            if (!IsRealPlayerCard(cardPlay)) return;
            if (_realPlayerPlays.Count > 32) _realPlayerPlays.Clear();     // 兜底：明显卡住了就重来
            _realPlayerPlays.Add(cardPlay!);
            _realPlayerPlayAt = NowSeconds();

            string entry = "?";
            try { entry = cardPlay!.Card?.Id.Entry ?? "?"; } catch { }
            LogRealPlayerCardInEnemyTurn(combat, entry);
        }
        catch { /* 忽略 */ }
    }

    private static int _realPlayerCardLogs;

    /// <summary>只有在**敌方回合**里才有意义（玩家回合本来就不翻侧）—— 记几行方便玩家复验。</summary>
    private static void LogRealPlayerCardInEnemyTurn(MegaCrit.Sts2.Core.Combat.ICombatState? combat, string entry)
    {
        try
        {
            bool enemyTurn;
            try { enemyTurn = combat != null && combat.CurrentSide == MegaCrit.Sts2.Core.Combat.CombatSide.Enemy; }
            catch { enemyTurn = false; }
            if (!enemyTurn || _realPlayerCardLogs++ >= 5) return;
            Logger.Log($"[CharBoss] 真人玩家的牌在敌方回合里结算：{entry} ⇒ 这一段「可命中敌人」走原版" +
                       "（否则原版会在这份列表里挑玩家当目标，玩家自己的牌就打到自己身上）");
        }
        catch { /* 忽略 */ }
    }

    private static string SafeEntry(object? card)
    {
        try
        {
            if (card is MegaCrit.Sts2.Core.Models.CardModel model) return model.Id.Entry;
        }
        catch { /* 忽略 */ }
        return "?";
    }

    /// <summary>登记表是否已经过期（超过 15 秒没动静 ⇒ 一定是有 After 没跑到）。</summary>
    internal static bool RealPlayerCardStale()    {
        if (_realPlayerPlays.Count == 0 && _realPlayerAutoPlays.Count == 0) return false;
        if (NowSeconds() - _realPlayerPlayAt < 15.0) return false;
        _realPlayerPlays.Clear();
        _realPlayerAutoPlays.Clear();
        Logger.Log("[CharBoss] 真人玩家出牌登记超时（15s）⇒ 已清空，避免「可命中敌人」整场不翻侧");
        return false;
    }

    private static double NowSeconds()
    {
        try { return Godot.Time.GetTicksUsec() / 1_000_000.0; }
        catch { return 0.0; }
    }

    /// <summary>
    /// **「可命中敌人」翻侧决策的全部输入**（v0.2.450，联机不同步取证用，只读）。
    ///
    /// 为什么值得单独打一行：这套翻侧是**纯本机记账**（深度计数 + 登记表 + 15 秒过期兜底），
    /// 而它决定 `HittableEnemies` 把「敌人」解释成哪一边 —— 一旦两端记账不同，
    /// 同一张群攻牌就会在**一端打玩家、另一端打 Boss**，表现正好是「玩家血没动、Boss 掉血」。
    /// 两端各一份 `mod_log` 把这一行逐字一比，就能确认是不是这个原因。
    /// 日志里那条 `真人玩家出牌登记超时（15s）` 就是这套记账存在时间敏感路径的直接证据。
    /// </summary>
    internal static string SideFlipState()
    {
        try
        {
            int plays = 0, autos = 0;
            double age = 0;
            try
            {
                plays = _realPlayerPlays.Count;
                autos = _realPlayerAutoPlays.Count;
                age = _realPlayerPlayAt > 0 ? Math.Max(0, NowSeconds() - _realPlayerPlayAt) : -1;
            }
            catch { /* 忽略 */ }
            return $"BossActingDepth={BossActingDepth} MultiTargetCardDepth={MultiTargetCardDepth} " +
                   $"NarrowToTargetDepth={NarrowToTargetDepth} 真人出牌登记={plays}/自动={autos} 最近登记={age:0.0}s前";
        }
        catch { return "?"; }
    }

    /// <summary>
    /// 「随机一名敌人」收窄成**一个具体目标**的嵌套深度（&gt; 0 时 `HittableEnemies` 只返回
    /// <see cref="NarrowTarget"/>）。
    ///
    /// 用途（用户 2026-10-26）：「多人时，让群蛇形态的伤害也受跟踪锁定影响，只攻击被锁定目标」。
    /// `SerpentFormPower.AfterCardPlayed` 的原版写法是
    /// <c>Rng.CombatTargets.NextItem(CombatState.HittableEnemies)</c> —— 多人时会在**所有真人玩家**里随机，
    /// 那就等于无视了「跟踪锁定」。这里由 <c>SerpentFormLockOnPatch</c> 挂在那条状态机上，
    /// 进出成对设置「只打这位」。
    /// </summary>
    internal static int NarrowToTargetDepth;

    internal static Creature? NarrowTarget;

    /// <summary>
    /// &gt; 0 表示**正在打一张「对所有敌人 / 所有友方」的牌**，这段里**禁止**收窄选靶。
    ///
    /// 为什么（用户 2026-10-29 第三次实测：播种依旧只打一个人）：
    /// `Sow.OnPlay` 是 `DamageCmd.Attack(...).FromCard(sow, cardPlay).TargetingAllOpponents(combat)`，
    /// 而 `AttackCommand.GetPossibleTargets()` 在「多目标」时返回的就是
    /// <c>_combatState.GetOpponentsOf(Attacker)</c> ——
    /// **群攻牌的打击列表和随机单体牌用的是同一个来源**。所以对 `GetOpponentsOf` 做收窄，
    /// 必然把群攻牌一起变成单体（`NarrowToLockedPlayer` 干的就是这件事）。
    ///
    /// 结论：**只在「单目标 / 随机单体」的时候收窄**，群攻牌整段豁免。
    /// 由 `CharBossMonster` 在 `CardCmd.AutoPlay` 前后成对加减（try/finally）。
    /// </summary>
    internal static int MultiTargetCardDepth;

    /// <summary>
    /// &gt; 0 表示**我们自己正在取一次「不该被收窄」的对手列表**，`NarrowToLockedPlayer` 直接返回原结果。
    ///
    /// 与 <see cref="MultiTargetCardDepth"/> 的分工：
    ///   · 那个是「**我们**正在打一张群攻牌」（在 `CharBossMonster` 的 `CardCmd.AutoPlay` 前后成对加减）；
    ///   · 这个是「第三方替我们**重算**对手列表」时的临时豁免（见
    ///     <see cref="AoeAttackNotNarrowedPatch"/>），只在一次同步调用内有效。
    /// </summary>
    internal static int SkipNarrowDepth;

    internal static void PushNarrowTo(Creature? target)
    {
        if (NarrowToTargetDepth > 16) { NarrowToTargetDepth = 0; NarrowTarget = null; }   // 兜底防卡死
        NarrowToTargetDepth++;
        NarrowTarget = target;
    }

    /// <summary>
    /// **一次性**收窄：只对**紧接着的那一次** `HittableEnemies` 读取生效（两帧内有效）。
    ///
    /// 为什么要有这个（用户 2026-10-29 四人局实测）：`PushNarrowTo/PopNarrowTo` 用的是**作用域**，
    /// 而群蛇形态的收窄是**全局**的 —— 多人同场时它串到了别的牌身上：
    /// 日志里 `PLAY SOW`（对所有敌人）之后紧跟着
    /// `选靶收窄：NECROBINDER_BOSS 的对手列表只留锁定玩家` ⇒ 播种只打了一个人。
    /// 群蛇形态那条状态机读完 `NextItem(HittableEnemies)` 就不再需要收窄了，
    /// 所以改成「读一次就失效」最安全。
    /// </summary>
    internal static void RequestNarrowOnce(Creature? target)
    {
        NarrowOnceTarget = target;
        try { _narrowOnceFrame = (int)Godot.Engine.GetFramesDrawn(); }
        catch { _narrowOnceFrame = 0; }
    }

    private static Creature? NarrowOnceTarget;
    private static int _narrowOnceFrame = -1;

    /// <summary>吃掉「一次性收窄」（两帧内有效，过期作废）。</summary>
    internal static bool ConsumeNarrowOnce(out Creature? target)
    {
        target = null;
        if (_narrowOnceFrame < 0 || NarrowOnceTarget == null) return false;

        int now;
        try { now = (int)Godot.Engine.GetFramesDrawn(); }
        catch { now = _narrowOnceFrame; }
        int requested = _narrowOnceFrame;
        _narrowOnceFrame = -1;
        var t = NarrowOnceTarget;
        NarrowOnceTarget = null;
        if (now - requested > 2) return false;      // 超过两帧还没被用到 ⇒ 作废
        target = t;
        return true;
    }

    /// <summary>把收窄状态整个清掉（每次出牌前调，防止任何泄漏串到下一张牌）。</summary>
    internal static void ResetNarrowing()
    {
        NarrowToTargetDepth = 0;
        NarrowTarget = null;
        NarrowOnceTarget = null;
        _narrowOnceFrame = -1;
    }

    internal static void PopNarrowTo()
    {
        if (NarrowToTargetDepth > 0) NarrowToTargetDepth--;
        if (NarrowToTargetDepth == 0) NarrowTarget = null;
    }

    [HarmonyPrefix]
    public static bool Prefix(CombatState __instance, ref IReadOnlyList<Creature> __result)
    {
        try
        {
            // ⓪ 「只打被锁定的那位」（群蛇形态的随机敌人）：优先级最高，直接给那一个。
            //    作用域版（Push/Pop）现在只剩兼容用；真正生效的是**一次性**版，
            //    这样它不会串到别人的牌上（播种只打一个人那次事故）。
            if (NarrowToTargetDepth > 0)
            {
                var only = NarrowTarget;
                if (only != null && only.IsAlive)
                {
                    __result = new List<Creature> { only };
                    return false;
                }
            }

            if (ConsumeNarrowOnce(out var once) && once != null && once.IsAlive)
            {
                __result = new List<Creature> { once };
                return false;
            }

            // ① 豁免优先：「玩家施加、在她回合结算」的效果要打她，不能翻
            if (PlayerEffectDepth > 0) return true;
            if (ConsumeVanillaOnce()) return true;

            // ①b ★ v0.2.293：**真人玩家自己的牌正在结算** ⇒ 一律走原版，不翻。
            //     （玩家被打 →「百年积木」在敌方回合抽牌 →「地狱狂徒」自动打出那几张打击，
            //      若不豁免就会在「玩家侧」里选靶 ⇒ 玩家自己的打击打自己；
            //      详见 RealPlayerCardDepth 的注释）
            if (RealPlayerCardDepth > 0 && !RealPlayerCardStale()) return true;

            // ①c ★ 2026-11-08（斗蛐蛐红队实测「打自己」）：出手的 Boss 本身在**玩家侧**时，
            //     `HittableEnemies` 的原版语义（`Enemies` = 对面蓝队）已经是正确的，
            //     再翻成「玩家侧」就等于让她打自己和队友。⇒ 这段窗口里一律走原版。
            if (PlayerSideBossDepth > 0) return true;

            // ② 敌方回合（她出牌的时段）才翻；玩家回合一律走原版
            // ★ v0.2.308：除了「敌方回合」，**她在出手的窗口内**（`BossActingDepth > 0`，覆盖
            //   AutoPlay 整条 await 链）和「摸牌堆自动打出」窗口（`ForceRelativeHold > 0`）也要翻
            //   —— 否则「倾泻」这类延迟结算的自动出牌会在阵营已经翻回玩家之后才选靶，
            //   于是按敌方阵营解析 ⇒ 打她自己。
            if (!ForceRelativeSide && ForceRelativeHold <= 0
                && BossActingDepth <= 0
                && __instance.CurrentSide != CombatSide.Enemy) return true;

            // ②b ★ v0.2.238（玩家 2026-10-30 反馈「小怪关不同步」时查出来的）：**只有我们的角色 Boss
            //    在场时**才允许翻。以前这里没有任何闸门 ⇒ **普通战斗**的敌方回合也会把「可命中敌人」
            //    翻成玩家侧，等于替所有怪物 / 别的模组改了选靶语义：
            //      · 单机时只是「行为与原版不同」（还顺手盖掉了一些别的模组的选靶 bug，所以没人发现）；
            //      · **联机时它会变成不同步的源头** —— 只要有一台机器没装本模组 / 版本不同 /
            //        本模组没加载成功，两端在敌方回合算出来的目标就不是同一批 ⇒ 战斗状态必然分叉。
            //    正常 Boss 战不受影响（她一定在 `Enemies` 里）；普通战斗现在 100% 走原版。
            if (!ForceRelativeSide && ForceRelativeHold <= 0 && BossActingDepth <= 0
                && !CharacterBosses.Core.BossShadowPets.BossFightActive(__instance))
            {
                if (_noBossSkipLogged++ < 3)
                {
                    Logger.Log("[CharBoss] 这一场没有角色 Boss（普通战斗 / 别的模组的战斗）⇒ " +
                               "HittableEnemies 走原版，不做「翻成玩家侧」的改动");
                }
                return true;
            }

            // ③ 翻成**真人玩家**（细节见下面注释）
            //
            // ⚠ 只挑真人玩家，**不带上玩家侧的召唤物/宠物**（用户 2026-10-24 实测：
            //   玩家玩「猫坠」的史莱姆老大，分裂出来的小史莱姆被她的毒牌一起上了毒）。
            //   `PlayerCreatures` 是「玩家侧的所有生物」（真人 + 他们的宠物），
            //   直接用它当靶子就等于「她的 AOE/随机选靶会波及玩家的召唤物」——
            //   而设计上她的牌只该打玩家本人。判据：`Player != null && Monster == null`。
            var players = PlayerCharacters(__instance);

            // 「可命中」在**战斗刚开始**时可能对玩家也是 false（那时还没有真正进入出牌阶段），
            // 于是原版分支会把「敌人」解释回 Boss 自己这边 → 红面具的虚弱加到了她自己身上、
            // 玩家身上一层都没有（用户实测：战斗第一回合 weak=0）。
            // 兜底：只要玩家侧有活着的真人玩家，就用它们 —— 反正语义上这里就是要打玩家。
            if (players.Count == 0)
                players = PlayerCharacters(__instance, requireHittable: false);

            // 玩家侧一个都不可命中（全在无敌/隐身）时也别拦：交给原版，免得出现「谁都打不到」的死结
            if (players.Count == 0) return true;

            __result = players;
            return false;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>玩家侧的**真人玩家**生物（排除宠物/召唤物）。</summary>
    private static List<Creature> PlayerCharacters(CombatState state, bool requireHittable = true)
    {
        var list = new List<Creature>();
        try
        {
            // ★ v0.2.529（玩家 2026-11-09 反馈「炼药猎的部分药水效果会翻倍，比如镣铐药水」）：
            //   「对所有敌方生效」的药水（镣铐药水 / 爆炸安瓿…）在反编译里都是
            //   `PowerCmd.Apply<X>(ctx, combatState.HittableEnemies, …)` / `Damage(… HittableEnemies …)`，
            //   也就是**这份列表里有几只就结算几次**。以前这里没有任何去重 ⇒ 只要 `PlayerCreatures`
            //   里同一个生物出现两次（或某个宠物没被 `Monster != null` / `IsPet` 两条判据挡住），
            //   那位玩家身上就会吃到**两份**药水效果（镣铐 = 力量 -7 变 -14，正是玩家说的「翻倍」）。
            //   这里按**引用**去重：同一只生物在一份「敌人列表」里出现两次永远是 bug。
            var seen = new HashSet<Creature>();
            foreach (var creature in state.PlayerCreatures)
            {
                if (creature == null || creature.Monster != null || creature.Player == null) continue;  // 宠物/召唤物跳过
                // 再明确排一次「宠物」（玩家侧的奥斯提、召唤物…）。`Creature.IsPet` 看的是 `PetOwner`，
                // 比 `Player == null` 更准（有些宠物身上也带着 Player 引用）——
                // 用户 2026-10-27 让核对「群蛇形态会不会打到玩家的奥斯提/火炬头」时补的这一道。
                try { if (creature.IsPet) continue; } catch { /* 忽略 */ }
                if (requireHittable ? !creature.IsHittable : (!creature.IsAlive || creature.IsDead)) continue;
                if (!seen.Add(creature)) continue;     // ★ 同一只只算一次
                list.Add(creature);
            }
        }
        catch { /* 忽略 */ }
        return list;
    }
}

/// <summary>
/// 让 Boss 名下的**装饰性随从**（佩尔的士兵）不再是「可命中敌人」。
///
/// 用户实测反馈原话：「我不是让你把佩尔的士兵单独搞成一个敌人……它不应该是能承伤的」。
/// 它放在 Boss 这一侧（＝真实玩家眼里的敌方侧），所以默认会进 <c>HittableEnemies</c>：
/// 玩家能点它、AOE 会扫到它、它头上还挂着血条 —— 看起来就是个独立敌人。
/// 这里把「已登记为影子随从」的生物从结果里摘掉（登记见 <c>BossShadowPets</c>），
/// 于是它打不到、也不再是合法目标，只剩形象。
///
/// 注意：**奥斯提不算**（它是真随从，该承伤），见 <c>BossShadowPets.Update</c> 里的跳过分支。
/// </summary>
[HarmonyPatch(typeof(CombatState), "get_HittableEnemies")]
public static class ShadowPetNotHittablePatch
{
    [HarmonyPostfix]
    public static void Postfix(CombatState __instance, ref IReadOnlyList<Creature> __result)
    {
        try
        {
            if (__result == null || __result.Count == 0) return;
            // 零开销闸门：既没有登记过的装饰性随从、这间房里也没有我们的角色 Boss → 直接放行。
            // ⚠ 以前只看 `HasKnownPets`：亡灵契约师**没有**装饰性随从（她只有奥斯提，不登记进 Known），
            //   于是这里一直放行 ⇒ 她的奥斯提留在「可命中敌人」里，被崩坠「转嫁疼痛」
            //   （随机取一只 HittableEnemies）选中并掉血（测试玩家 2026-10-23 实测）。
            if (!CharacterBosses.Core.BossShadowPets.HasKnownPets &&
                !CharacterBosses.Core.BossShadowPets.BossFightActive(__instance)) return;

            List<Creature>? kept = null;
            for (int i = 0; i < __result.Count; i++)
            {
                var creature = __result[i];
                // 奥斯提（她真正的随从）也不能被点到/被群攻波及 ——
                // 用户设计：只有「替亡灵契约师挡下未被格挡的攻击伤害」时它才掉血。
                bool isOsty = CharacterBosses.Core.BossOsty.IsBossOsty(creature);
                bool drop = creature == null || !creature.IsAlive
                            || CharacterBosses.Core.BossShadowPets.IsShadowPet(creature)
                            || isOsty;
                if (isOsty && NoteOstyDropped())
                {
                    Logger.Log("[CharBoss] 奥斯提已从「可命中敌人」里摘掉" +
                               "（选靶不会再挑它；本场只报一次）");
                }
                if (drop && kept == null)
                {
                    kept = new List<Creature>(__result.Count);
                    for (int j = 0; j < i; j++) kept.Add(__result[j]);
                }
                else if (!drop && kept != null)
                {
                    kept.Add(creature);
                }
            }

            if (kept == null) return;          // 一个都没摘掉 → 保持原结果
            if (kept.Count == 0) return;       // 摘空了就别摘（免得出现「谁都打不到」的死结）
            __result = kept;
        }
        catch
        {
            /* 出错就用原结果，绝不影响战斗 */
        }
    }

    /// <summary>「奥斯提被摘掉」的日志只报一次（每场战斗重置见 <c>ResetDroppedLog</c>）。</summary>
    private static bool _ostyDroppedLogged;

    private static bool NoteOstyDropped()
    {
        if (_ostyDroppedLogged) return false;
        _ostyDroppedLogged = true;
        return true;
    }

    /// <summary>每场战斗重新允许报一次（由 <c>BossShadowPets.ResetDiagnostics</c> 调）。</summary>
    internal static void ResetDroppedLog() => _ostyDroppedLogged = false;
}

/// <summary>
/// 更根本的一刀：让装饰性随从（佩尔的士兵）在**任何**判定里都「不可被命中」。
///
/// 为什么光过滤 <c>HittableEnemies</c> 不够（用户实测：「随机攻击牌（如连续反弹）有可能攻击到
/// 储君的佩尔的士兵」）：那张牌的选靶是
/// <code>
/// enemy = Owner.RunState.Rng.CombatTargets.NextItem(CombatState.HittableEnemies);
/// </code>
/// 一旦有任何一条路径绕过那个属性（或者取到的是缓存的旧列表），我们就拦不住。
/// 而 <c>Creature.IsHittable</c> 的定义是
/// <code>
/// get { if (IsDead) return false; if (!Hook.ShouldAllowHitting(CombatState, this)) return false; return true; }
/// </code>
/// 所以在这里把它按成 false，等于**所有**选靶路径（手动点选、随机选靶、群体扫射）都拿不到它。
/// 判据仍是「已登记的影子随从」，奥斯提不登记，不受影响。
/// </summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), nameof(MegaCrit.Sts2.Core.Hooks.Hook.ShouldAllowHitting))]
public static class ShadowPetNeverHittablePatch
{
    [HarmonyPostfix]
    public static void Postfix(Creature creature, ref bool __result)
    {
        try
        {
            if (!__result) return;                       // 本来就不让打 → 不用管
            if (!CharacterBosses.Core.BossShadowPets.IsShadowPet(creature)) return;

            __result = false;
            if (CharacterBosses.Core.BossShadowPets.NoteBlockedHit(creature))
            {
                Logger.Log($"[CharBoss] {creature.Name} 被判定为不可命中（装饰性随从，本场只报一次）");
            }
        }
        catch
        {
            /* 出错就保持原结果 */
        }
    }
}

/// <summary>
/// 「给它加个爪牙」——直接在**判定层**把装饰性随从算成次要敌人，不依赖那层 MinionPower。
///
/// 反编译：
/// <code>
/// public bool IsSecondaryEnemy => Side == Enemy &amp;&amp; Powers.Any(p => p.OwnerIsSecondaryEnemy);
/// public bool IsPrimaryEnemy   => Side != Enemy ? false : !IsSecondaryEnemy;
///
/// // CombatManager.IsCombatEnding
/// if (State.Enemies.Any(e => e != null &amp;&amp; e.IsAlive &amp;&amp; e.IsPrimaryEnemy)) return false;
/// </code>
/// 原来我们是靠 <c>PowerCmd.Apply&lt;MinionPower&gt;</c>（异步 fire-and-forget）来达成同一条判定，
/// 但它没落地时士兵就还是 primary enemy ⇒ 储君死了战斗也不结束（用户实测两次）。
/// 这里直接把 <c>IsSecondaryEnemy</c> 按成 true：等价于「官方爪牙」，
/// 不改任何其它行为（不加攻击、不加血、不影响承伤逻辑）。
/// </summary>
[HarmonyPatch(typeof(Creature), "get_IsSecondaryEnemy")]
public static class ShadowPetIsSecondaryEnemyPatch
{
    [HarmonyPostfix]
    public static void Postfix(Creature __instance, ref bool __result)
    {
        try
        {
            if (__result) return;                                    // 本来就是次要敌人 → 不用管
            if (CharacterBosses.Core.BossShadowPets.IsShadowPet(__instance)) __result = true;
        }
        catch
        {
            /* 保持原结果 */
        }
    }
}

/// <summary>
/// 同一件事再堵一层，直接堵 <c>IsPrimaryEnemy</c> 本身。
///
/// 为什么要重复：<c>IsPrimaryEnemy => Side==Enemy &amp;&amp; !IsSecondaryEnemy</c>，
/// 而 JIT 很可能把那个属性 getter **内联**进 <c>IsPrimaryEnemy</c> 里 ——
/// Harmony 补的是方法入口，内联掉的调用就绕过了补丁。直接补「被读的那一个」最稳。
/// </summary>
[HarmonyPatch(typeof(Creature), "get_IsPrimaryEnemy")]
public static class ShadowPetNotPrimaryEnemyPatch
{
    [HarmonyPostfix]
    public static void Postfix(Creature __instance, ref bool __result)
    {
        try
        {
            if (!__result) return;
            if (CharacterBosses.Core.BossShadowPets.IsShadowPet(__instance)) __result = false;
        }
        catch
        {
            /* 保持原结果 */
        }
    }
}

/// <summary>
/// 装饰性随从（佩尔的士兵）不属于任何人的「对手」—— 这是随机/弹跳类攻击的真实选靶来源。
///
/// 探针实测（用户反馈「连续反弹还是打得到佩尔的士兵」，日志抓到的其实是另一张牌）：
/// <code>
/// [Hook] BeforeCardPlayed card=RICOCHET … perHit=2.25 hits=4 targets=2
/// [CharBoss] ⚠ 佩尔的士兵 受到了伤害 2.25（未格挡=2，props=Move）
/// </code>
/// 反编译 <c>AttackCommand.Execute</c>：随机选靶用的是
/// <code>
/// validTargets = _combatState.GetOpponentsOf(Attacker);   // ← 不是 HittableEnemies！
/// singleTarget = combatTargets.NextItem(validTargets);
/// </code>
/// 所以只拦 <c>HittableEnemies</c> / <c>IsHittable</c> 都拦不住弹跳类牌 ——
/// 必须把「谁的对手列表里」这一层也摘掉。
/// </summary>
[HarmonyPatch(typeof(CombatState), nameof(CombatState.GetOpponentsOf))]
public static class ShadowPetNotAnOpponentPatch
{
    private static int _narrowed;

    [HarmonyPostfix]
    public static void Postfix(Creature creature, ref IReadOnlyList<Creature> __result)
    {
        try
        {
            // ① 摘掉装饰性随从 / 奥斯提（原逻辑）
            //    闸门同时认「有我们的 Boss」：亡灵契约师没有装饰性随从，只看 HasKnownPets
            //    会让这段整段跳过 ⇒ 她的奥斯提进「对手列表」被随机攻击/转嫁疼痛选中。
            bool bossFight = CharacterBosses.Core.BossShadowPets.BossFightActive(creature?.CombatState);
            if (__result != null && __result.Count > 0
                && (CharacterBosses.Core.BossShadowPets.HasKnownPets || bossFight))
            {
                List<Creature>? kept = null;
                for (int i = 0; i < __result.Count; i++)
                {
                    var each = __result[i];
                    if (each != null && each.IsAlive
                        && !CharacterBosses.Core.BossShadowPets.IsShadowPet(each)
                        && !CharacterBosses.Core.BossOsty.IsBossOsty(each))   // 奥斯提同样不算「可打的对手」
                    {
                        kept?.Add(each);
                        continue;
                    }
                    if (kept == null)
                    {
                        kept = new List<Creature>(__result.Count);
                        for (int j = 0; j < i; j++) kept.Add(__result[j]);
                    }
                }
                if (kept != null && kept.Count > 0) __result = kept;   // 一个都没摘 / 摘空了 → 保持原结果
            }

            // ② 多人「跟踪锁定」：我们 Boss 的**对手列表只保留它锁定的那名玩家**。
            //
            // 为什么在这里拦（反编译确认过的两条路径都走它）：
            //   · `AttackCommand.Execute` 的**随机**选靶：`validTargets = GetOpponentsOf(Attacker)`；
            //   · 充能球（闪电 / 黑暗）的选靶：`GetOpponentsOf(orb.Owner.Creature).Where(IsHittable)` 再随机取一个。
            // 以前这两处会从「所有玩家」里随机挑 ⇒ 没被锁定的玩家也会被充能球打（用户实测）。
            //
            // ⚠ **群攻不受影响**：已核对 CrushUnder / Thunderclap / DaggerSpray / EchoingSlash 四张，
            //   它们走的是 `HittableEnemies` + `TargetingAllOpponents`，根本不经过这条。
            NarrowToLockedPlayer(creature, ref __result);
        }
        catch
        {
            /* 出错就用原结果 */
        }
    }

    /// <summary>把「我们 Boss 的对手列表」收窄成它锁定的那名玩家（单人局不做任何事）。</summary>
    private static void NarrowToLockedPlayer(Creature creature, ref IReadOnlyList<Creature> result)
    {
        try
        {
            if (result == null || result.Count <= 1) return;
            if (creature?.Monster is not CharacterBosses.Monsters.CharBossMonster boss) return;
            if (CharacterBosses.Core.BossLockOn.RealPlayerCount(creature.CombatState) < 2) return;   // 单人：原样

            // ⚠ v0.2.162：**这里不再看 `MultiTargetCardDepth`**。
            //
            // 以前「正在打一张群攻牌时，整段不许收窄」（v0.2.141）—— 那会的豁免是**按时间窗**给的，
            // 于是那张群攻牌结算过程中**嵌套触发**的任何随机伤害（充能球被动、别的随机单体）
            // 也一起被放宽了，等于让随机伤害打到了没被锁的人。
            // 现在豁免改在**攻击本身**上判（`AoeAttackNotNarrowedPatch`：只有真正的
            // `TargetingAllOpponents` 才把结果恢复成全部对手；`IsRandomlyTargeted` 一律不放宽），
            // 所以收窄这里是**唯一口径**：单体、随机单体、随机多段统统只留锁定玩家。
            // `MultiTargetCardDepth` 仍由 `CharBossMonster` 递增/递减，但**不再参与判定**（留作诊断）。
            if (HittableEnemiesSidePatch.SkipNarrowDepth > 0) return;

            var victim = CharacterBosses.Core.BossLockOn.TargetFor(boss)?.Creature;
            if (victim == null || !victim.IsAlive) return;             // 锁的人没了：交给原版
            if (!result.Contains(victim)) return;                      // 他本来就不在候选里：别硬塞

            bool hasOther = false;
            foreach (var c in result)
            {
                if (ReferenceEquals(c, victim)) continue;
                hasOther = true;
                break;
            }
            if (!hasOther) return;

            int before = result.Count;
            result = new List<Creature> { victim };
            if (_narrowed++ < 40)
            {
                Logger.Log($"[CharBoss] 选靶收窄：{boss.Id.Entry} 的对手列表只留锁定玩家" +
                           $"（原来 {before} 项 → 1）");
            }
        }
        catch { /* 忽略，保持原结果 */ }
    }
}

/// <summary>
/// **群攻攻击一律不被「跟踪锁定」收窄** —— 判据是**这次攻击本身是不是多目标**，
/// 而不是「这张牌是不是我们自己的代码打出去的」。
///
/// 为什么必须有这一层（用户 2026-10-29 追问「翻越撑击不是群攻」暴露出来的漏洞）：
///   v0.2.141 的豁免只写在 `CharBossMonster` 的 `CardCmd.AutoPlay` 前后
///   （`HittableEnemiesSidePatch.MultiTargetCardDepth`），也就是**只覆盖我们主动打出的牌**。
///   而「奇巧」（`CardKeyword.Sly`）牌是**被丢弃后由原版自己打出去的** ——
///   反编译 `CardCmd.&lt;DiscardAndDraw&gt;d__4`：
///   <code>
///   foreach (CardModel sly in slyCards)
///       await AutoPlay(choiceContext, sly, null, AutoPlayType.SlyDiscard);
///   </code>
///   这条路径完全在我们的 `AutoPlay` 包装之外 ⇒ 那一刻 `MultiTargetCardDepth == 0`
///   ⇒ 翻越撑击（`FlickFlack`：`TargetType.AllEnemies`、「对所有敌人造成 7 点伤害」、
///   带 `CardKeyword.Sly`，由「早有准备」丢掉后免费打出）被收窄成**只打被锁定的那名玩家**。
///   同一个漏洞还覆盖：爆发/复制药水的重放、瓶中精灵、任何别的 mod 代打、以及将来新增的
///   自动打出机制 —— 所以豁免必须挂在**攻击经过的那个唯一汇合点**上。
///
/// 汇合点（反编译 `AttackCommand.GetPossibleTargets`）：
/// <code>
/// if (IsSingleTargeted) return new ReadOnlySingleElementList&lt;Creature&gt;(_singleTarget);
/// if (IsMultiTargeted) {
///     if (_sourceType == SourceType.Monster) return _combatState.PlayerCreatures;
///     return _combatState.GetOpponentsOf(Attacker);      // ← 群攻牌走这条，被我们收窄了
/// }
/// </code>
/// 这里 Postfix：**多目标攻击**的结果如果退化成了 1 个对手，就用「暂停收窄」的方式重算一次完整列表。
/// 只认 `IsMultiTargeted`，所以单目标 / 随机单体攻击照旧跟着锁定走（那是设计要的行为）。
/// </summary>
[HarmonyPatch(typeof(AttackCommand), "GetPossibleTargets")]
public static class AoeAttackNotNarrowedPatch
{
    private static readonly System.Reflection.FieldInfo? CombatStateField =
        typeof(AttackCommand).GetField("_combatState",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    private static int _fixed;

    [HarmonyPostfix]
    public static void Postfix(AttackCommand __instance, ref IReadOnlyList<Creature> __result)
    {
        try
        {
            // 只处理「多目标」攻击：`TargetingAllOpponents` 之后 `IsMultiTargeted == true`。
            if (__instance == null || !__instance.IsMultiTargeted) return;

            // ⚠ **随机目标**不算群攻！`TargetingRandomOpponents` 也是把 `_combatState` 填上
            //   ⇒ `IsMultiTargeted` 同样为 true，但它其实每次只打**一个**随机对手
            //   （连续反弹 Ricochet 就是这种）。收窄成「只打被锁定的那位」是**设计要的**行为，
            //   绝不能在这里被「恢复成全部对手」——用户 2026-10-29 实测：连续反弹打到了没被锁的玩家。
            //   原版给了现成的判据 `IsRandomlyTargeted`（反编译 `TargetingRandomOpponents` 里置 true）。
            try { if (__instance.IsRandomlyTargeted) return; } catch { /* 读不到就按群攻处理 */ }

            // 单人局没有任何收窄，不用管。
            var attacker = __instance.Attacker;
            if (attacker?.Monster is not CharacterBosses.Monsters.CharBossMonster boss) return;

            var combat = CombatStateField?.GetValue(__instance) as ICombatState;
            if (combat == null) return;
            if (CharacterBosses.Core.BossLockOn.RealPlayerCount(combat) < 2) return;

            // 只在「确实被收窄了」时动手：原结果只剩 1 个目标，而完整对手列表更多。
            int have = __result?.Count ?? 0;
            if (have > 1) return;        // 没被收窄（或本来就多于 1 个）⇒ 原样

            IReadOnlyList<Creature>? full = null;
            HittableEnemiesSidePatch.SkipNarrowDepth++;
            try { full = combat.GetOpponentsOf(attacker); }
            finally
            {
                if (HittableEnemiesSidePatch.SkipNarrowDepth > 0) HittableEnemiesSidePatch.SkipNarrowDepth--;
            }
            if (full == null || full.Count <= have) return;

            __result = full;
            if (_fixed++ < 30)
            {
                Logger.Log($"[CharBoss] 群攻豁免：{boss.Id.Entry} 的多目标攻击被收窄成 {have} 个目标" +
                           $"⇒ 恢复成全部 {full.Count} 名对手（原来的豁免只覆盖我们自己打出的牌）");
            }
        }
        catch { /* 出错就用原结果 */ }
    }
}

/// <summary>
/// 探针：如果这只装饰性随从**仍然**掉了血，立刻把「掉了多少 / 什么属性」打出来。///
/// 用来定位「到底哪条路径绕过了不可命中」（用户反馈：随机攻击牌仍可能打到佩尔的士兵）。
/// 有了这行日志，再对上时间点前面那行 <c>[Hook] BeforeCardPlayed card=...</c> 就能认出是哪张牌。
/// 正常情况下它**一次都不应该出现**。
/// </summary>
[HarmonyPatch(typeof(Creature), nameof(Creature.LoseHpInternal))]
public static class ShadowPetDamageProbePatch
{
    [HarmonyPostfix]
    public static void Postfix(Creature __instance, decimal amount, DamageResult __result)
    {
        try
        {
            if (!CharacterBosses.Core.BossShadowPets.IsShadowPet(__instance)) return;
            Logger.Log($"[CharBoss] ⚠ {__instance.Name} 受到了伤害 {amount}" +
                       $"（未格挡={__result?.UnblockedDamage}，props={__result?.Props}）" +
                       " —— 有路径绕过了「不可命中」，请对照上一行的 BeforeCardPlayed");
        }
        catch { /* 忽略 */ }
    }
}

/// <summary>
/// **最后一层硬闸**：装饰性随从（佩尔的士兵）掉血时把伤害按成 0。
///
/// ★ v0.2.376（玩家反馈，经飞书 diag 定位：`props=Move` 的牌伤 15.75 仍然落在了佩尔的士兵身上）：
///   前四层（`Hook.ShouldAllowHitting` / `CombatState.GetOpponentsOf` / `IsSecondaryEnemy` /
///   `IsPrimaryEnemy`）拦的都是**选靶阶段**。而**第三方模组**的牌完全可以自己算出目标列表
///   （例如直接读 `CombatState.Enemies`、或把目标列表缓存下来再调 `CreatureCmd.Damage(list, …)`），
///   这样一条都不经过 ⇒ 照样能打到它。玩家那局带的是「Hikari - 光 (Arcaea)」角色模组。
///
/// 所以这里不再试图猜路径，直接**在掉血那一刻**把它按成 0：
///   `Creature.LoseHpInternal(decimal amount, ValueProp props)` 是**所有**掉血的唯一终点，
///   前缀把 `amount` 改成 0 ⇒ 原版照常结算出一个「0 点、无格挡」的 DamageResult，
///   不扣血、不破格挡、也不会给调用方留下一个假的/空的返回值（比 return false 安全得多）。
/// 只对**已登记的影子随从**生效（`BossShadowPets.IsShadowPet`）；奥斯提、真人玩家、其它怪物都不受影响。
/// </summary>
[HarmonyPatch(typeof(Creature), nameof(Creature.LoseHpInternal))]
public static class ShadowPetNoDamagePatch
{
    private static int _zeroed;

    [HarmonyPrefix]
    public static void Prefix(Creature __instance, ref decimal amount)
    {
        try
        {
            if (amount <= 0m) return;
            if (!CharacterBosses.Core.BossShadowPets.IsShadowPet(__instance)) return;
            amount = 0m;
            if (_zeroed++ < 10)
            {
                Logger.Log($"[CharBoss] {__instance.Name} 是装饰性随从 ⇒ 这次伤害按 0 处理" +
                           "（第三方模组的牌绕过了选靶拦截，这里兜底）");
            }
        }
        catch { /* 出错就按原样扣血 */ }
    }
}

/// <summary>
/// 冰雹风暴（<c>HailstormPower</c>）在**她的回合结束**结算，走的是
/// `CreatureCmd.Damage(ctx, CombatState.HittableEnemies, Amount, Unpowered, Owner)`。
/// 敌方回合的默认规则（见 <see cref="HittableEnemiesSidePatch"/>）本来就会把它翻成玩家侧，
/// **不需要**额外补丁 —— 之前那版「只在她出牌时翻」时才要一次性开关，已随那次回退一起删掉。
/// </summary>
internal static class BossHailstormRelativeSideNote
{
    // 占位：留个说明免得以后又有人加一次多余的一次性开关。
}
