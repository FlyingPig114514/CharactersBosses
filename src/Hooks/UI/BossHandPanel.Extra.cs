using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using CharacterBosses.Core;
using CharacterBosses.Monsters;

namespace CharacterBosses.UI;

/// <summary>
/// 手牌栏里「不属于某张牌」的独立意图 —— <see cref="BossHandPanel"/> 的分部文件。
///
/// 目前只有一个：**冰雹风暴**（<c>HailstormPower</c>）。它在 `BeforeSideTurnEnd` 里结算：
/// 冰球数量够的时候，对敌方造成**层数**点伤害（`ValueProp.Unpowered`，不吃力量/集中）。
/// 因为它的触发点不在任何一张牌上，手牌栏原来完全没有它的位置 —— 用户要求把它画在
/// **能量栏正下方、和其他意图同一高度**。
///
/// 数字用 <see cref="RawAttackIntent"/> 原样显示（不受加成），节点仍是原版 <c>NIntent</c>。
/// </summary>
public partial class BossHandPanel
{
    private const string ExtraIntentsName = "ExtraIntents";

    /// <summary>「画在**所有卡牌左边**」的那一颗独立意图（目前只有「抱抱先生」）。</summary>
    private const string LeftIntentName = "ExtraIntentLeft";

    private Control? _extraIntents;

    /// <summary>抱抱先生那颗：用户要求「将该伤害在他意图的所有卡牌左边显示」⇒ 单独一颗，摆在整排卡的左外侧。</summary>
    private Control? _leftIntent;

    /// <summary>
    /// 重建独立意图（每次刷新手牌栏时调）。没有就清空。
    ///
    /// 现在有**四条**来源，画在两处：
    ///   · **行内那一排**（能量栏正下方，同一个 HBox 横向排开 ⇒ 不会互相重叠）：
    ///     1. **冰雹风暴**（能力机）：`HailstormPower.Amount` 在本回合结束结算成伤害；
    ///     2. **炸弹**（储君）：<c>TheBombPower</c> 剩 1 回合就爆；
    ///     3. **充能球被动伤害**（所有会玩球的 Boss —— 故障机器人两套；用户 2026-10-24 要求）：
    ///        本回合**回合结束时**队列里会打伤害的球（闪电 / 玻璃）各触发一次被动，
    ///        **镀金缆线**（`GOLD_PLATED_CABLES`：最右侧那颗球额外触发一次被动）要算进去。
    ///   · **所有卡牌的左边**（用户 2026-11-02 明确要求）：
    ///     4. **抱抱先生**（`MrStruggles`，二层「王储」进阶 9 的遗物）：她**回合开始**对所有玩家
    ///        造成「她的回合数」点无来源伤害。它发生在**任何一张牌之前** ⇒ 画在最左边
    ///        （见 <see cref="PlaceLeftIntent"/>）。
    private void BuildExtraIntents(CharBossMonster boss)
    {
        if (_extraIntents != null && IsInstanceValid(_extraIntents)) _extraIntents.QueueFree();
        _extraIntents = null;
        if (_leftIntent != null && IsInstanceValid(_leftIntent)) _leftIntent.QueueFree();
        _leftIntent = null;

        // 被击晕 ⇒ 这回合她什么都不做，独立意图（冰雹风暴 / 炸弹 / 充能球被动 / 抱抱先生）全部撤掉。
        // 用户 2026-11-04 的口径是「撤掉**所有**意图」，这几条也属于「本回合会造成多少伤害」的预告，
        // 留着就等于骗人（球被动、炸弹、抱抱先生都挂在她自己的回合流程上，被晕时不会发生）。
        if (boss == null || boss.Stunned) return;

        // 左边那一列 = **她回合开始、任何一张牌之前**就会发生的事，按发生顺序排：
        //   ①「抱抱先生」（`MrStruggles`：她回合开始时对所有玩家造成「她的回合数」点无来源伤害）；
        //   ②「黑洞」的**下回合辉星到账**那一下（`StarNextTurnPower` 结算 → `BlackHolePower.Amount` 点无来源伤害）。
        // ★ 2026-11-04（用户：「我记得之前好像加过这个。你再检查一下，如果真没有就加一下」）：
        //   真没有 —— `BossIntentBuilder.BlackHoleDamageOf` 只给「会花/得辉星的牌」挂黑洞数，
        //   而「下回合到账」那一笔当时**任何意图里都没有**（只有一行日志）。
        //   代价是玩家按意图总数算血量时会**凭空少算这一笔**：她回合开始先挨 `黑洞层数` 点，
        //   而且它是 `ValueProp.Unpowered` ⇒ 连奥斯提都不会替你挡。
        var leftDamages = new List<(int Damage, string Kind)>();
        try
        {
            int cuddles = MrStrugglesDamage(boss);
            if (cuddles > 0) leftDamages.Add((cuddles, "抱抱先生"));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 抱抱先生意图绘制失败: " + e);
        }
        try
        {
            int blackHole = BlackHoleAtTurnStart(boss);
            if (blackHole > 0) leftDamages.Add((blackHole, "黑洞（下回合辉星到账）"));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 黑洞（回合开始）意图绘制失败: " + e);
        }

        // ★ 2026-11-05（用户）：「大抱抱」在她**洗牌的那一回合**（第 6 回合及之后每个循环）会生成煤灰，
        //   而每张煤灰都会触发「烟囱」的全体伤害。这一笔发生在**任何一张牌之前**，
        //   所以像抱抱先生那样放在**最左列**，而不是挂在某张牌底下。
        try
        {
            int soot = Core.StatusBossMath.BigHugSootDamage(boss);
            if (soot > 0) leftDamages.Add((soot, "大抱抱（煤灰，全体）"));
        }
        catch (Exception e) { Logger.Log("[CharBoss] 大抱抱意图绘制失败: " + e.Message); }

        // ★ 2026-11-05（用户：「水银沙漏搞个跟抱抱先生一样的意图，并确保他会攻击所有玩家」）：
        //   「水银沙漏」（`MercuryHourglass`）原版是 `AfterPlayerTurnStart` ⇒ 她**回合开始**对
        //   `HittableEnemies`（从她视角 = **所有玩家**）造成无来源伤害。它不在任何一张牌的意图里，
        //   所以和抱抱先生一样放在**最左列**。
        try
        {
            int hourglass = MercuryHourglassDamage(boss);
            if (hourglass > 0) leftDamages.Add((hourglass, "水银沙漏（全体）"));
        }
        catch (Exception e) { Logger.Log("[CharBoss] 水银沙漏意图绘制失败: " + e.Message); }

        // ★ 2026-11-05（用户：「在他打出轰击后晕的那几个回合要把轰击的伤害显示出来
        //   （类似抱抱先生那个位置，但轰击算**攻击伤害**、受力量等加成）」）：
        //   轰击（`Bombardment`）躺在她的**消耗牌堆**里，每个回合开始自动复燃一次 18 点。
        //   它既不是某张计划牌、也不在 `Plan` 里 ⇒ 手牌栏原来什么都不显示。
        //   这里把它画进**左边那一列**（和她回合开始发生的抱抱先生/黑洞同列），
        //   并且**走原版攻击伤害管道**（`LiveSingleAttackIntent` = 每次渲染重算）⇒
        //   她的力量/虚弱、你的易伤都会实时反映。
        //   ⚠ 唤醒之后轰击改为打她自己 ⇒ 对玩家不再有威胁，这一颗就不画了。
        var liveIntents = new List<MegaCrit.Sts2.Core.MonsterMoves.Intents.AbstractIntent>();
        try
        {
            if (!Powers.BossOrbitalBombardPower.Awake)
            {
                var pcs = boss?.Shadow?.PlayerCombatState;
                var bomb = pcs == null ? null : Core.BossScriptRunner.FindInExhaust(pcs, "BOMBARDMENT");
                if (bomb != null)
                {
                    int baseDamage = 18;
                    try { baseDamage = (int)Math.Round((double)bomb.DynamicVars.Damage.BaseValue); }
                    catch { /* 取不到就用 18（卡面基础值） */ }
                    liveIntents.Add(new Core.LiveSingleAttackIntent(bomb, baseDamage));
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 轰击复燃意图绘制失败: " + e.Message);
        }

        if (leftDamages.Count > 0 || liveIntents.Count > 0)
            _leftIntent = BuildIntentRow(LeftIntentName, leftDamages, boss, liveIntents);

        var damages = new List<(int Damage, string Kind)>();
        try
        {
            int hailstorm = HailstormDamage(boss);
            if (hailstorm > 0) damages.Add((hailstorm, "冰雹风暴"));

            int bomb = BombDamage(boss);
            if (bomb > 0) damages.Add((bomb, "炸弹"));

            int orbs = OrbPassiveDamage(boss, quiet: false, out int orbsAoe);
            // ⚠ 球被动是**单体随机**伤害（用户 2026-10-29）：
            //   闪电/玻璃球的被动每次只打 `HittableEnemies` 里随机一个，而她在出手时那份列表被收窄成
            //   被锁定的那位 ⇒ **只有那名玩家会挨**。所以本地玩家不是被锁的人时，这条要显示 0。
            //   （冰雹风暴 / 炸弹是全体伤害，不做这一步 —— 它们对每个人都该显示真实数字。）
            if (orbs > 0 && BossIntentBuilder.RandomAimIsElsewhere(boss))
            {
                Logger.Log($"[CharBoss] 独立意图（充能球被动）{orbs} 点 → 对本地玩家显示 0" +
                           "（球被动是单体随机，她锁的不是本机玩家）");
                orbs = 0;
            }
            if (orbs > 0) damages.Add((orbs, "充能球被动（单体）"));
            // 玻璃球被动是**群体**：另起一条，不和单体那个数字混在一起
            if (orbsAoe > 0) damages.Add((orbsAoe, "充能球被动（群体）"));

            // ★ 2026-11-10（用户「血肉骨」需求）：「如果回合结束时她的格挡会不小于 10 点，
            //   在显示电球、炸弹等的位置显示招架盾的 6 点伤害，记得考虑她特殊效果和骇人头盔
            //   为她带来的格挡」。
            //   反编译 `ParryingShield`：`AfterSideTurnEnd` 里若 `Block >= 10` ⇒ 对 `HittableEnemies`
            //   **随机一个**造成 6 点 `ValueProp.Unpowered` 伤害。因为是无来源伤害，用 Raw 意图
            //   （不吃她的力量、也不吃目标的易伤）正合适。
            //   格挡预测见 `Core.FleshBone.EndOfTurnBlock`（plan 里的 Block 变量 + 骇人头盔 + 吞噬生命）。
            int parry = Core.FleshBone.ParryDamage(boss);
            if (parry > 0) damages.Add((parry, "招架盾"));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 独立意图计算失败: " + e);
        }

        if (damages.Count == 0) return;

        _extraIntents = BuildIntentRow(ExtraIntentsName, damages, boss);
        // ★ v0.2.473（用户 2026-11-07：「第 7、12 回合最右侧显示充能球被动和寒冰风暴的地方还是看不见」）：
        //   这一行是**独立**的（`_extraIntents`），不和卡面下方那排意图一起挪 ⇒ 单独再左移一次。
        try
        {
            int shift = Core.RainbowMimic.IntentShiftFor(boss);
            if (shift > 0 && _extraIntents != null)
                _extraIntents.Position = new Vector2(_extraIntents.Position.X - shift, _extraIntents.Position.Y);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 用一颗（或几颗）原版 <see cref="NIntent"/> 造一个横向排开的意图行。
    /// 数字走 <see cref="RawAttackIntent"/>：原样显示，不吃任何加成（和实际结算的「无来源伤害」一致）。
    /// <paramref name="liveIntents"/> 是**例外**：那些意图自己已经在渲染时走原版伤害管道
    /// （例如「轰击」的复燃，它是真正的攻击伤害、要吃她的力量与你的易伤）。
    /// </summary>
    private Control? BuildIntentRow(string name, List<(int Damage, string Kind)> damages,
                                    CharBossMonster boss, List<MegaCrit.Sts2.Core.MonsterMoves.Intents.AbstractIntent>? liveIntents = null)
    {
        try
        {
            var row = new HBoxContainer
            {
                Name = name,
                MouseFilter = MouseFilterEnum.Ignore,
                Alignment = BoxContainer.AlignmentMode.Center,
            };
            AddChild(row);

            foreach (var (damage, kind) in damages)
            {
                var node = NIntent.Create(0f);
                if (node == null) continue;
                // 必须先入树（_Ready 建好内部节点）再 UpdateIntent，否则空引用
                row.AddChild(node);
                node.UpdateIntent(new RawAttackIntent(damage), TargetsOf(boss), boss.Creature);
                Logger.Log($"[CharBoss] 独立意图（{kind}）: {damage} 点");
            }

            // 走原版伤害管道的那些（每次渲染重算，所以数字会随力量/易伤变化）
            if (liveIntents != null)
            {
                foreach (var intent in liveIntents)
                {
                    if (intent == null) continue;
                    var node = NIntent.Create(0f);
                    if (node == null) continue;
                    row.AddChild(node);
                    node.UpdateIntent(intent, TargetsOf(boss), boss.Creature);
                    Logger.Log($"[CharBoss] 独立意图（轰击复燃）: 走攻击伤害管道（{intent}）");
                }
            }

            if (row.GetChildCount() == 0)
            {
                row.QueueFree();
                return null;
            }
            return row;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 独立意图绘制失败: " + e);
            return null;
        }
    }

    /// <summary>
    /// 「抱抱先生」本回合会打出的伤害（= 她下一次回合开始时的**回合数**）。
    /// 与 <see cref="Core.MrStrugglesPlays.UpcomingDamage"/> 同一套算法 ——
    /// <see cref="Core.MintySpireDamagePreviewCompat"/> 也复用这一个，保证两个数字不打架。
    /// </summary>
    internal static int MrStrugglesDamage(CharBossMonster boss)
        => Core.MrStrugglesPlays.UpcomingDamage(boss);

    /// <summary>
    /// 「**水银沙漏**」（`MercuryHourglass`）：她**回合开始**对所有玩家造成 `Damage`（默认 3）点无来源伤害。
    /// 反编译：`AfterPlayerTurnStart(player)` → 若 `player` 是持有者本人，则对
    /// `player.Creature.CombatState.HittableEnemies`（= 玩家侧全体）造成 `DynamicVars.Damage`（`Unpowered`）。
    /// 这里只**显示**，真实伤害仍由原版遗物钩子结算（不会重复）。
    /// </summary>
    internal static int MercuryHourglassDamage(CharBossMonster boss)
    {
        try
        {
            var relics = boss?.Shadow?.Relics;
            if (relics == null) return 0;
            foreach (var relic in relics)
            {
                if (relic == null) continue;
                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry != "MERCURY_HOURGLASS") continue;
                try { return Math.Max(0, (int)Math.Round(relic.DynamicVars.Damage.BaseValue)); }
                catch { return 3; }
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    /// <summary>
    /// 「黑洞」在**她回合开始那一刻**会打多少：只要有「下回合辉星」待结算（<c>StarNextTurnPower</c>），
    /// 到账时 `<c>BlackHolePower</c>` 就会对每名真人玩家各打 `Amount` 点**无来源**伤害。
    ///
    /// 反编译依据：
    /// <code>
    /// // BlackHolePower.AfterStarsGained(amount, gainer)
    /// if (amount &gt; 0 &amp;&amp; gainer == Owner.Player)
    ///     await CreatureCmd.Damage(ctx, CombatState.HittableEnemies, Amount, ValueProp.Unpowered, Owner);
    /// </code>
    /// ⚠ 我们的影子玩家拿不到那条原版广播 ⇒ 由 `CharBossMonster.SettleStarNextTurnPower` 手动结算
    /// （日志行「黑洞：下回合辉星 +N 到账 → …」）。这里只是把同一个数字**提前画出来**。
    ///
    /// **一次到账只触发一次**（`amount` 只用来判 &gt; 0），所以显示的是层数、不是「层数 × 星数」。
    /// </summary>
    internal static int BlackHoleAtTurnStart(CharBossMonster boss)
    {
        try
        {
            var creature = boss?.Creature;
            if (creature == null) return 0;
            int pendingStars = creature.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.StarNextTurnPower>();
            if (pendingStars <= 0) return 0;
            int amount = creature.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.BlackHolePower>();
            return Math.Max(0, amount);
        }
        catch { return 0; }
    }

    /// <summary>冰雹风暴在本回合结束时会打出多少（身上层数 + 本回合计划要打的那几张），没有冰球就是 0。
    /// <para>⚠ <c>internal</c>（原来 private）：<see cref="Core.MintySpireDamagePreviewCompat"/> 要复用同一个数
    /// 去补「薄荷尖塔」的伤害预测，两边**必须是同一个算法**，否则屏幕上两个数字会打架。</para></summary>
    internal static int HailstormDamage(CharBossMonster boss)
    {
        int current = (int)Math.Round((double)(HailstormOf(boss)?.Amount ?? 0));
        int planned = PlannedHailstormStacks(boss);
        int damage = current + planned;
        if (damage <= 0) return 0;
        if (FrostOrbsAtTurnEnd(boss) <= 0) return 0;
        return damage;
    }

    /// <summary>
    /// 炸弹（<c>TheBomb</c>）：<c>TheBombPower.Amount</c> 是剩余回合数，**剩 1 回合 = 这回合结束就炸**。
    /// 伤害按用户设计：进阶 0～8 → 40，进阶 9～10 → 50（进阶 9 起剧本把炸弹升了级，卡面本来就是 50）。
    ///
    /// ⚠ <c>internal</c>：见 <see cref="HailstormDamage"/> 的说明（薄荷尖塔兼容复用同一个数）。
    /// </summary>
    internal static int BombDamage(CharBossMonster boss)
    {
        try
        {
            var power = BombOf(boss);
            if (power == null) return 0;
            if (power.Amount > 1) return 0;                    // 还没到"要炸"的那一回合
            return boss.AscensionLevel >= 9 ? 50 : 40;
        }
        catch { return 0; }
    }

    /// <summary>
    /// **本回合充能球被动会造成的伤害**（用户 2026-10-24：所有故障机器人都要显示，位置在能量栏下方，
    /// 和冰雹风暴 / 炸弹同一行但不要重叠）。
    ///
    /// 算法：
    ///   · 用 <see cref="BossOrbSim.OrbsAfter"/> 把本回合计划模拟一遍，取**回合结束时**队列里的球；
    ///   · 只会打伤害的球算进来（反编译：`LightningOrb.Passive` 对随机敌人造成 `PassiveVal`、
    ///     `GlassOrb.Passive` 同理；冰球是格挡、等离子是能量、黑暗是涨层数 ⇒ 不算）；
    ///   · `PassiveVal` 已经含集中（`ModifyOrbValue`）；
    ///   · **镀金缆线**：反编译 `GoldPlatedCables.ModifyOrbPassiveTriggerCounts`
    ///     —— 只对**队列最右侧那颗**（`OrbQueue.Orbs[0]`）额外 +1 次触发 ⇒ 那颗球的伤害算两遍。
    /// 没有球位的 Boss（铁甲 / 猎手 / 储君 / 亡灵）恒返回 0，等于没有这一条。
    ///
    /// ⚠ <c>internal</c> + <paramref name="quiet"/>：见 <see cref="HailstormDamage"/> 的说明。
    /// 薄荷尖塔每刷一次血条就会问一次，那边传 <c>quiet: true</c> 避免日志被这条重复刷屏。

    internal static int OrbPassiveDamage(CharBossMonster boss, bool quiet = false)
    => OrbPassiveDamage(boss, quiet, out _);

/// <summary>
/// 同 <see cref="OrbPassiveDamage(CharBossMonster, bool)"/>，另外把**群体**那部分单独返回。
///
/// ★ v0.2.472（用户 2026-11-07：「充能球被动似乎没有计算将要生成的玻璃球被动
///   （注意玻璃球是群伤，最好分开显示）」）：
///   · 闪电球被动 = **单体随机**，玻璃球被动 = **群体**（`GlassOrb.Passive` 打 `HittableEnemies` 全体）
///     ⇒ 两笔性质完全不同，混成一个数字会让「只有你会挨」和「所有人都挨」看起来一样；
///   · 所以这里把玻璃球那部分单独算出来（`aoe`），调用方决定怎么显示。
///   · 顺带确认：`OrbsAfter` 已经把**本回合剧本里还没打出的牌**将要生成的球算进队列了
///     （玻璃工艺 / 聚变 / 彩虹 都在 `BossOrbMath.TryChannels` 里），所以「将要生成的玻璃球」本来就在循环里，
///     以前只是被合并进单体那一个数字。
/// </summary>
internal static int OrbPassiveDamage(CharBossMonster boss, bool quiet, out int aoe)
{
    aoe = 0;
    return OrbPassiveDamageCore(boss, quiet, ref aoe);
}

private static int OrbPassiveDamageCore(CharBossMonster boss, bool quiet, ref int aoe)
    {
        try
        {
            var pcs = boss.Shadow?.PlayerCombatState;
            var queue = pcs?.OrbQueue;
            if (queue == null || queue.Capacity <= 0) return 0;

            // ★ 2026-11-05：把「每张牌出手时的剩余能量」一起传下去 ——
            //   「暴风雨」是 X 费牌，生成几个闪电球要看那个数（用户要求提前计算），
            //   否则它在本条被动汇总里被当成 0 颗球。
            var cards = new List<(CardModel Card, int X)>();
            foreach (var play in boss.Plan)
            {
                if (play?.Card == null) continue;
                int x = 0;
                try
                {
                    if (string.Equals(play.Card.Id.Entry, "TEMPEST", StringComparison.Ordinal))
                    {
                        x = Math.Max(1, play.Cost);
                        try { if (play.Card.IsUpgraded) x++; } catch { }   // 2026-11-13 (dread): Tempest+ 多 1 颗
                    }
                }
                catch { x = 0; }
                cards.Add((play.Card, x));
            }
            var orbs = Core.BossOrbSim.OrbsAfter(boss.Creature, queue, cards);
            if (orbs.Count == 0) return 0;

            // 本回合**还没打出去**的牌会让集中变化 ⇒ 回合结束那一刻的被动伤害跟着变
            //（用户 2026-10-24：第 3 回合的「热修复」+2 集中没算进去 → 预告 8、实际 12）。
            //
            // ⚠ **人工制品（Artifact）要算**：她出生就带 3 层（人工合成），而玩家可能在她的回合之前
            // 打掉一部分 ⇒ 这里按**实时**层数走一遍计划：
            //   · 「超能光束」的 −3 集中是**减益**（`HyperbeamFocusDownPower`）⇒ 有人工制品就被整条挡掉
            //     （消耗 1 层），球的被动伤害按**没减**显示（用户 2026-10-24：「如果有人工制品的话，
            //     超能光束的-3集中的buff就会被削掉，球的伤害应当正常显示」）；
            //   · 「热修复」的 +2 是**增益**，人工制品不挡 ⇒ 照常加上。
            int focusDelta = 0;
            int artifact = ArtifactStacks(boss);
            try
            {
                foreach (var play in boss.Plan)
                {
                    var card = play?.Card;
                    if (card == null) continue;
                    if (pcs != null && !pcs.Hand.Cards.Contains(card)) continue;   // 已经打出去的别再算一次

                    int delta = Core.BossDamagePredictor.FocusDeltaOf(card, boss?.Shadow?.PlayerCombatState);
                    if (delta == 0) continue;
                    if (delta < 0 && artifact > 0)
                    {
                        artifact--;
                        if (!quiet)
                        {
                            Logger.Log($"[CharBoss] 球被动预告：{card.Id.Entry} 的 {delta} 集中被人工制品挡掉" +
                                       $"（剩余人工 {artifact}）");
                        }
                        continue;
                    }
                    focusDelta += delta;
                }
            }
            catch { /* 忽略：算不出来就当没有 */ }

            // ★ v0.2.474（用户 2026-11-07：「还是没有考虑特殊效果『彩虹』带来的集中，或者干脆在第 3、8 回合
            //   提前多算 1 点集中得了」）：就用这条简单口径 —— 彩虹机这两回合各会生成够 5 颗球，
            //   于是回合结束时的球被动按 **+1 集中** 算。别的故障机器人没有这层 Power ⇒ 一点不影响。
            try
            {
                if (boss.Creature?.GetPower<CharacterBosses.Powers.BossRainbowPower>() != null
                    && (boss.TurnNumber == 3 || boss.TurnNumber == 8))
                {
                    focusDelta += 1;
                    if (!quiet) Logger.Log("[CharBoss] 球被动预告：特殊效果「彩虹」（第 " + boss.TurnNumber + " 回合）⇒ +1 集中");
                }
            }
            catch { /* 忽略 */ }
            bool cables = HasRelic(boss, "GOLD_PLATED_CABLES");
            // 诊断（用户 2026-11-05：被动总伤显示 30/35，对一颗闪电球明显不对）：
            // 把「每颗球各自多少、集中补了多少、缆线翻倍没」都打出来，下次一跑就知道数从哪来。
            if (!quiet)
            {
                try
                {
                    var parts = new List<string>();
                    for (int i = 0; i < orbs.Count; i++) parts.Add($"{orbs[i]?.GetType().Name}={PassiveDamageOf(orbs[i])}");
                    Logger.Log($"[CharBoss] 球被动明细：{orbs.Count} 颗 [{string.Join(", ", parts)}]" +
                               $" 集中补 {focusDelta} 缆线={cables} 人工={artifact}（本回合计划内）");
                }
                catch { /* 忽略 */ }
            }
            int total = 0;
            // 模拟器里新建的球 `PassiveVal` 是 0（没有 owner、没有烘进集中）⇒ 用「基础被动 + 当前集中」兜底。
            int focusNow = 0;
            try { focusNow = Core.BossOrbMath.FocusOf(boss.Creature); } catch { focusNow = 0; }
            for (int i = 0; i < orbs.Count; i++)
            {
                int per = PassiveDamageOf(orbs[i]);
                // ★ 2026-11-06（用户：「充能球被动虚高 20 —— 你把冰球也算成电球了？」）：
                //   兜底**只给会打伤害的球**（闪电 / 玻璃）；冰球被动是格挡，绝不能算成 3 点伤害。
                bool isGlass = orbs[i] is GlassOrb;
                bool damagerOrb = orbs[i] is LightningOrb || isGlass;
                // ★ v0.2.473（用户 2026-11-07：「将要生成的玻璃球的被动的伤害预览好像没考虑集中」）：
                //   反编译 `GlassOrb`：`_passiveVal = 4`、`EvokeVal = PassiveVal × 2`，
                //   而 `PassiveVal => ModifyOrbValue(_passiveVal)` ⇒ 玻璃球被动 = **4 + 集中**（不是闪电球的 3）。
                if (per <= 0 && damagerOrb) per = (isGlass ? 4 : 3) + focusNow;
                if (per <= 0) continue;
                per = Math.Max(0, per + focusDelta);                 // 集中是加法（加在每颗球上）
                int triggers = 1 + ((cables && i == 0) ? 1 : 0);     // Orbs[0] = 最右侧那颗
                // ★ v0.2.472：玻璃球是**群伤**，单独记一笔（用户要求分开显示）
                if (orbs[i] is GlassOrb) aoe += per * triggers;
                else total += per * triggers;
            }
            // ★ v0.2.358（用户 2026-11-04：「第二回合对他造成伤害使他的情感芯片生效的时候，
            //   没有标注开始时闪电充能球被动会造成的伤害」）：
            //   「情感芯片」触发在**她回合开始**那一刻 —— 那时队列里的球是**玩家回合结束时**的那批，
            //   而这条意图原来算的是「她**整个回合打完之后**还剩的球」（`OrbsAfter` 模拟结果），
            //   两者常常不是一个集合（第 2 回合她打完只剩冰球，可回合开始时那颗**闪电球**照样触发一次被动）
            //   ⇒ 原来那条额外伤害要么显示 0、要么按错集合翻倍（用户看到的「没有标注」）。
            //   现在改成：**按回合开始那颗集合（= 现在队列里的球）单独算一次**，加上去，不再拿总数翻倍。
            bool chipTriggers = false;
            try { chipTriggers = Core.EmotionChipPlays.WillTriggerAtOwnTurnStart(boss); }
            catch { chipTriggers = false; }
            if (chipTriggers)
            {
                int chipDamage = 0;
                try
                {
                    var queueNow = pcs?.OrbQueue;
                    if (queueNow != null)
                    {
                        // ★ v0.2.392（用户 2026-11-05：「事件机第一回合伤害预览写的是 12，而不是 16」）：
                        //   这一支原来**漏了镀金缆线**（上面回合结束那支有 `triggers`，这里只 `+ per` 一遍）。
                        //   实测口径：她回合开始情感芯片触发最右侧那颗闪电球被动 4×2 = 8，
                        //   回合结束再 4×2 = 8 ⇒ 一共 16，而预览只给 4 + 8 = 12。
                        //   反编译核对：情感芯片是 `OrbCmd.Passive(ctx, orb, null, countAffectedByHooks: true)`
                        //   ⇒ `GoldPlatedCables.ModifyOrbPassiveTriggerCounts` 照样生效（只对 `Orbs[0]`），
                        //   所以这里必须和回合结束那一支用**同一把尺子**。
                        var orbsNow = queueNow.Orbs;
                        for (int i = 0; i < orbsNow.Count; i++)
                        {
                            int per = PassiveDamageOf(orbsNow[i]);
                            if (per <= 0) continue;
                            // 回合开始那一刻她还没打牌 ⇒ 集中变化（focusDelta）尚未发生，不加
                            int triggers = 1 + ((cables && i == 0) ? 1 : 0);   // Orbs[0] = 最右侧那颗
                            chipDamage += per * triggers;
                        }
                    }
                }
                catch { chipDamage = 0; }

                if (chipDamage > 0)
                {
                    if (!quiet)
                    {
                        Logger.Log($"[CharBoss] 球被动预告：情感芯片她回合开始会再触发一次" +
                                   $"（按回合开始那批球算、镀金缆线={cables}）+{chipDamage} ⇒ {total} → {total + chipDamage}");
                    }
                    total += chipDamage;
                }
                else if (!quiet)
                {
                    Logger.Log("[CharBoss] 球被动预告：情感芯片会触发，但回合开始那批球里没有会打伤害的（0 点）");
                }
            }

                // ★ v0.2.357（用户 2026-11-04：「伤害预览没有计算吞噬暗影生成的黑球」）：
                //   「吞噬暗影」（`ConsumingShadowPower.AfterSideTurnEnd`）在她的**回合结束**会
                //   **激发最左侧的 N 颗充能球**，而黑暗球的伤害**只在激发时**结算
                //   （`DarkOrb.Passive` 只是把层数涨进 `_evokeVal`，本身不造成伤害）
                //   ⇒ 原来那排只数闪电/玻璃球的**被动**，黑暗球整颗被漏掉。
                //   这里按「回合结束时最左侧那几颗会被激发」把它们的**激发伤害**补进来。
                //   ⚠ 队列顺序：`Orbs[0]` 是**最右侧**那颗（`GOLD_PLATED_CABLES` 只对 `Orbs[0]` 生效那条注释），
                //     所以「最左侧」= 索引 `Count-1`，往左依次是 `Count-2` …
                int consumes = ConsumingShadowEvokesAtTurnEnd(boss);
                if (consumes > 0)
                {
                    int evokeTotal = 0;
                    for (int k = 0; k < consumes && k < orbs.Count; k++)
                    {
                        var leftmost = orbs[orbs.Count - 1 - k];
                        if (leftmost == null) continue;
                        int per = BossOrbMath.EvokeDamageOf(leftmost, BossOrbMath.FocusOf(boss.Creature));
                        if (per <= 0) continue;
                        per = Math.Max(0, per + focusDelta);
                        evokeTotal += per;
                        if (!quiet)
                        {
                            Logger.Log($"[CharBoss] 球被动预告：第 {k + 1} 颗（最左侧）{leftmost.GetType().Name} " +
                                       "回合结束会被吞噬暗影激发 ⇒ 伤害 +" + per);
                        }
                    }
                    if (evokeTotal > 0)
                    {
                        if (!quiet) Logger.Log($"[CharBoss] 球被动预告：吞噬暗影的回合结束激发 +{evokeTotal} ⇒ {total} → {total + evokeTotal}");
                        total += evokeTotal;
                    }
                }

                return total;
        }
        catch (Exception e)
        {
            if (!quiet) Logger.Log("[CharBoss] 充能球被动伤害估算失败: " + e.Message);
            return 0;
        }
    }

    /// <summary>她身上的人工制品层数（没有 = 0）。球被动预告要靠它判断「减集中的 debuff 会不会被挡」。 </summary>
    private static int ArtifactStacks(CharBossMonster boss)
    {
        try
        {
            return Math.Max(0,
                boss.Creature?.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.ArtifactPower>() ?? 0);
        }
        catch { return 0; }
    }

    /// <summary>
    /// 本回合**结束时会因「吞噬暗影」被激发的充能球数量**（0 = 没有这层能力 / 没球）。
    ///
    /// 反编译 `ConsumingShadowPower.AfterSideTurnEnd`：她的阵营回合结束时，
    /// 对队列**最左侧**的 `Amount` 颗球逐个 `OrbCmd.Evoke`。
    /// 层数来源两处，取大的那个：
    ///   · 她**身上**已有的 `ConsumingShadowPower.Amount`；
    ///   · 本回合计划里、**还没打出去**的那张「吞噬暗影」（卡面 `Repeat`…实际是给 1 层能力，
    ///     所以按 1 算；它是 `Play<ConsumingShadow>`/`Free<ConsumingShadow>`，剧本里最多一张）。
    /// </summary>
    private static int ConsumingShadowEvokesAtTurnEnd(CharBossMonster boss)
    {
        int stacks = 0;
        try
        {
            stacks = Math.Max(0, boss.Creature?
                .GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.ConsumingShadowPower>() ?? 0);
        }
        catch { stacks = 0; }

        try
        {
            var pcs = boss.Shadow?.PlayerCombatState;
            foreach (var play in boss.Plan)
            {
                var card = play?.Card;
                if (card == null) continue;
                string entry;
                try { entry = card.Id.Entry; } catch { continue; }
                if (!string.Equals(entry, "CONSUMING_SHADOW", StringComparison.Ordinal)) continue;
                // 已经打出去的别再算一次（打在手里 = 还没结算）
                if (pcs != null && !pcs.Hand.Cards.Contains(card)) continue;
                stacks = Math.Max(stacks, 1);
            }
        }
        catch { /* 忽略 */ }

        return stacks;
    }

    /// <summary>这颗球的**被动**会打多少伤害（不造成伤害的球返回 0）。</summary>
    private static int PassiveDamageOf(OrbModel? orb)
    {
        if (orb == null) return 0;
        try
        {
            bool damager = orb is LightningOrb || orb is GlassOrb;
            if (!damager) return 0;
            int per = Math.Max(0, (int)Math.Round(orb.PassiveVal));
            if (per > 0) return per;

            // ★ 2026-11-05（用户「我充能球被动呢？」；日志 `3 颗 [LightningOrb=0, LightningOrb=0, 0]`）：
            //   **模拟器里新建的球**没初始化（没进队列、没烘集中）⇒ `PassiveVal` 是 0，
            //   于是本回合新生成的球全被当成「被动 0」，整条被动意图消失。
            //   这里退回**同类球的原型实例**读基础被动，读不到再按闪电球 3 兜底。
            try
            {
                var canonical = MegaCrit.Sts2.Core.Models.ModelDb.GetById<MegaCrit.Sts2.Core.Models.OrbModel>(
                    MegaCrit.Sts2.Core.Models.ModelDb.GetId(orb.GetType()));
                if (canonical != null) per = Math.Max(0, (int)Math.Round(canonical.PassiveVal));
            }
            catch { /* 忽略 */ }
            if (per <= 0 && orb is LightningOrb) per = 3;
            return per;
        }
        catch { return 0; }
    }

    /// <summary>她身上有没有某件遗物（按模型 id 的 entry 判）。</summary>
    private static bool HasRelic(CharBossMonster boss, string entry)
    {
        try
        {
            var relics = boss.Shadow?.Relics;
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

    private static MegaCrit.Sts2.Core.Models.Powers.TheBombPower? BombOf(CharBossMonster boss)
    {
        try { return boss.Creature?.GetPower<MegaCrit.Sts2.Core.Models.Powers.TheBombPower>(); }
        catch { return null; }
    }

    /// <summary>把独立意图摆到「能量栏正下方 + 与其他意图同高」；由 <c>LayoutCells</c> 调用。</summary>
    private void PlaceExtraIntents(float intentTop, float cardW)
    {
        if (_extraIntents != null && IsInstanceValid(_extraIntents))
        {
            var min = _extraIntents.GetCombinedMinimumSize();
            float w = Math.Max(min.X, 1f);
            float h = min.Y > 1f ? min.Y : 32f;
            float centerX = BossStatusRow.EnergyCenterX(_statusRow);

            // ★ 2026-11-10（用户：「第二回合电球被动过于偏左，都和扫荡射线的叠在一起了」）：
            //   原来是 `centerX − w/2` 一算了事 —— 这一行和卡面下方那排意图是**同一高度**，
            //   只要横向区间相交就会压在一起。能量栏中心随「本回合最大手牌数」和手牌张数变化，
            //   牌少/这一行偏宽时左缘就会滑进卡牌的意图区。
            //   ⇒ 现在先算出**所有卡牌意图行的真实右缘**，再让这一行的左缘至少落在它右边 Gap 处。
            //     能量栏中心本身更靠右时仍用它（保持原来的观感）。
            float intentsRight = CardIntentRowsRightEdge(cardW);
            float shift = Core.RainbowMimic.IntentShiftFor(_boss);
            float natural = centerX - w * 0.5f - shift;
            // ⚠ 彩虹机那笔整体左移要算在 max 之前，否则左移又会把它压回卡牌的意图上
            float x = Math.Max(natural, intentsRight + Gap);

            _extraIntents.Size = new Vector2(w, h);
            _extraIntents.Position = new Vector2(x, intentTop + IntentShiftY * cardW);
            if (_extraLayoutLogs++ < 10)
            {
                Logger.Log($"[CharBoss] 独立意图定位：能量中心 x={centerX:0.#} / 行宽 {w:0.#} ⇒ 自然 x={natural:0.#}；" +
                           $"卡牌意图右缘 {intentsRight:0.#} ⇒ 实际 x={x:0.#}（面板宽 {Size.X:0.#}）");
            }
        }

        PlaceLeftIntent(intentTop, cardW);
    }

    /// <summary>独立意图行定位日志限流。</summary>
    private int _extraLayoutLogs;

    /// <summary>
    /// **所有卡牌下方意图行的最右缘**（面板局部坐标）。
    ///
    /// 意图行是在**卡片节点内部**、并且整体左移了 <c>IntentShiftX × 卡宽</c>，内容还可能比卡面宽
    /// （一张牌挂两条意图时就会溢出），所以不能拿「卡牌右缘」当边界 —— 必须逐格读真实的
    /// 位置 + 内容宽度。读不到就退回「面板宽度」（= 本回合最大牌数的右缘）。
    /// </summary>
    private float CardIntentRowsRightEdge(float cardW)
    {
        float right = 0f;
        try
        {
            foreach (var cell in _cells)
            {
                var node = cell?.Node;
                if (node == null || !IsInstanceValid(node)) continue;
                foreach (var child in node.GetChildren())
                {
                    if (child is not Control row) continue;
                    if (row.Name != BossHandCard.IntentsRowName) continue;
                    float rowW = Math.Max(row.Size.X, row.GetCombinedMinimumSize().X);
                    float edge = node.Position.X + row.Position.X + rowW;
                    if (edge > right) right = edge;
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 意图右缘测量失败: " + e.Message);
        }
        if (right <= 0f)
        {
            try { right = Math.Max(IntentShiftX * cardW, Size.X); } catch { right = 0f; }
        }
        return right;
    }

    /// <summary>
    /// 「抱抱先生」那颗：**摆到整排卡牌的左外侧**、与其它意图同高（用户 2026-11-02：
    /// 「将该伤害在他意图的所有卡牌左边显示」）。
    ///
    /// 位置 = `第一张卡的意图行左缘 − (宽 + 间距)`：
    /// 意图行本身就整体左移了 `IntentShiftX × 卡宽`（默认 −0.58 卡宽，让最左意图的左缘对齐卡面左缘），
    /// 所以只写 `-(宽+间距)` 会**压到第一张卡自己的意图**上（用户实测「再往左一点」）。
    /// 面板不裁剪子节点（`ClipContents` 默认 false），x 取负也能正常画出来。
    /// </summary>
    private void PlaceLeftIntent(float intentTop, float cardW)
    {
        if (_leftIntent == null || !IsInstanceValid(_leftIntent)) return;
        try
        {
            var min = _leftIntent.GetCombinedMinimumSize();
            float w = Math.Max(min.X, 1f);
            float h = min.Y > 1f ? min.Y : 32f;

            float cardsIntentLeft = IntentShiftX * cardW;   // 第一张卡意图行的左缘（负数）

            _leftIntent.Size = new Vector2(w, h);
            _leftIntent.Position = new Vector2(cardsIntentLeft - (w + Gap), intentTop + IntentShiftY * cardW);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 抱抱先生意图定位失败: " + e.Message);
        }
    }

    private static HailstormPower? HailstormOf(CharBossMonster boss)
    {
        try { return boss.Creature?.GetPower<HailstormPower>(); }
        catch { return null; }
    }

    /// <summary>本回合计划里还要再打几张冰雹风暴、每张加多少层（卡面 `PowerVar&lt;HailstormPower&gt;` = 6）。</summary>
    private static int PlannedHailstormStacks(CharBossMonster boss)
    {
        int stacks = 0;
        try
        {
            foreach (var play in boss.Plan)
            {
                if (play.Card?.Id.Entry != "HAILSTORM") continue;
                decimal amount = 6m;
                try { amount = play.Card.DynamicVars["HailstormPower"].BaseValue; }
                catch { /* 读不到就用基础值 6 */ }
                stacks += (int)Math.Round((double)amount);
            }
        }
        catch { /* 忽略 */ }
        return stacks;
    }

    private static int FrostOrbCount(CharBossMonster boss)
    {
        try
        {
            var orbs = boss.Shadow?.PlayerCombatState?.OrbQueue?.Orbs;
            if (orbs == null) return 0;
            int n = 0;
            foreach (var orb in orbs) if (orb is FrostOrb) n++;
            return n;
        }
        catch { return 0; }
    }

    /// <summary>
    /// 本回合打完之后队列里会有几个冰球（冰雹风暴的触发条件看的是**回合结束**那一瞬）。
    /// 现在没有冰球、但本回合要打冰川/寒流时，这条也要算成立。
    /// </summary>
    private static int FrostOrbsAtTurnEnd(CharBossMonster boss)
    {
        int now = FrostOrbCount(boss);
        try
        {
            var pcs = boss.Shadow?.PlayerCombatState;
            if (pcs == null || boss.Plan.Count == 0) return now;
            var cards = new List<CardModel>();
            foreach (var play in boss.Plan) cards.Add(play.Card);
            return BossOrbSim.FrostOrbsAfter(boss.Creature, pcs.OrbQueue, cards);
        }
        catch { return now; }
    }

    /// <summary>冰雹风暴打的是「可命中敌人」——Boss 视角下就是玩家侧。</summary>
    private static List<Creature> TargetsOf(CharBossMonster boss)
    {
        var targets = new List<Creature>();
        try
        {
            // 这里只是**读一次**做展示：敌方回合的默认规则（`HittableEnemiesSidePatch`）本来就会把
            // 「可命中敌人」解释成玩家侧，不需要额外开关。
            var hittable = boss.CombatState?.HittableEnemies;
            if (hittable != null)
            {
                foreach (var creature in hittable)
                {
                    if (creature != null && creature.IsAlive) targets.Add(creature);
                }
            }
        }
        catch { /* 忽略：拿不到就用空列表，数字照常显示 */ }
        return targets;
    }
}
