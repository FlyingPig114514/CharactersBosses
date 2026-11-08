using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace CharacterBosses.Core;

/// <summary>
/// 按「本回合的出牌顺序」模拟充能球队列，算出每张牌会**激发**出什么，从而补上意图。
///
/// 为什么需要模拟器（而不是只看牌自己的 <c>OrbEvokeType</c>）：
///   1. <c>OrbCmd.Channel</c> 原版是「**队列满了就先激发队首**再入队」，
///      所以电击 / 寒流 / 冰川这些**生成球**的牌在球位满时也会激发球；
///   2. 雷暴（<c>StormPower.AfterCardPlayed</c>）让**每张能力牌**都生成 1 个电球，同样可能激发；
///   3. 扩容（<c>Capacitor</c>）会加球位，改变后面还能不能激发。
/// → 「会不会激发、激发什么球」取决于前面的牌，必须顺序推进。
///
/// 激发类型 ↔ 意图（用户定的规则）：
///   · 电球 / 黑暗球 / 玻璃球 → **攻势**（伤害走 <see cref="BossOrbMath.EvokeDamageOf"/>，已剔除集中）；
///   · 冰球 → **守势**（原版 <c>DefendIntent</c>，不带数字）；
///   · 等离子球 → 只给能量，没有意图。
/// </summary>
internal sealed class BossOrbSim
{
    /// <summary>一张牌造成的激发结果（伤害按「每次激发的伤害」分组，方便显示成 `伤害×次数`）。</summary>
    internal sealed class Result
    {
        public Dictionary<int, int> DamageHits { get; } = new();   // 单次伤害 → 次数
        public int BlockHits { get; private set; }
        public int TotalDamage { get; private set; }

        internal void AddDamage(int perHit, int times)
        {
            if (perHit <= 0 || times <= 0) return;
            DamageHits[perHit] = DamageHits.TryGetValue(perHit, out var n) ? n + times : times;
            TotalDamage += perHit * times;
        }

        internal void AddBlock(int times)
        {
            if (times > 0) BlockHits += times;
        }
    }

    private readonly List<OrbModel> _orbs = new();
    /// <summary>模拟开始那一刻的真实集中（用来把队列球里已经烘进去的集中剔除干净）。</summary>
    private readonly int _startFocus;
    /// <summary>**本回合投射的集中** —— 会随「碎片整理 / 偏差认知 / 暴涨」这类牌往后涨。</summary>
    private int _focus;
    private int _capacity;
    private int _stormAmount;
    /// <summary>本 mod 的「偏差认知」让力量与集中恒等 ⇒ 获得力量也等于获得集中。</summary>
    private readonly bool _focusFollowsStrength;

    /// <summary>这只 Boss 的生物（用来读「雷霆」这类能力牌的层数）。</summary>
    private readonly Creature? _bossCreature;

    /// <summary>
    /// 本回合**会被激发**的球（真实队列里的实例；模拟中新建的球不在里面，它们此刻还没画在屏幕上）。
    /// 球位 UI 用它决定哪些球显示「待激发」的蓝色激发值。
    /// </summary>
    public HashSet<OrbModel> Evoked { get; } = new();

    public BossOrbSim(Creature boss, OrbQueue? queue)
    {
        try
        {
            _bossCreature = boss;
            _startFocus = BossOrbMath.FocusOf(boss);
            _focus = _startFocus;
            _focusFollowsStrength = boss?.GetPower<CharacterBosses.Powers.DeviationPower>() != null;
            _stormAmount = boss?.GetPower<StormPower>()?.Amount ?? 0;
            if (queue != null)
            {
                _capacity = queue.Capacity;
                foreach (var orb in queue.Orbs) _orbs.Add(orb);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 球位模拟初始化失败: " + e.Message);
        }
    }

    /// <summary>按这张牌推进队列，返回它会激发出什么。</summary>
    public Result Predict(CardModel card, int xEnergy = 0)
    {
        var result = new Result();
        try
        {
            // ① 牌自己的激发（双重释放 / 碎片 / 四重释放 …）
            OwnEvokes(card, result);

            // ② 牌自己生成的球（满了就先激发队首）
            // ★ v0.2.472（用户 2026-11-07）：**彩虹**要生成 **电 + 冰 + 暗** 各 1 颗，
            //   一张牌对应一种球的那张表表示不了 ⇒ 这里单独处理（以前只按 1 颗电球记账，
            //   于是「同步」「冰雹风暴」「球被动」三项都少算了）。
            if (Entry(card) == "RAINBOW")
            {
                Channel(typeof(LightningOrb), 1, result);
                Channel(typeof(FrostOrb), 1, result);
                Channel(typeof(DarkOrb), 1, result);
            }
            else if (BossOrbMath.TryChannels(card, xEnergy, out var orbType, out int channelCount))
            {
                Channel(orbType, channelCount, result);
            }

            // ★ v0.2.492（用户 2026-11-08：「让特斯拉线圈的意图中计算它会触发的闪电充能球被动的伤害」）：
            //   「特斯拉线圈」的原文是「造成 3 点伤害；**对该敌人触发你的所有闪电充能球的被动**」——
            //   以前意图只画了那 3 点，球被动那一笔完全没算。
            //   这里按当前队列里**闪电球的数量** ×（3 + 本回合投射的集中）补进去。
            if (Entry(card) == "TESLA_COIL")
            {
                int lightning = 0;
                foreach (var orb in _orbs) { if (orb is LightningOrb) lightning++; }
                if (lightning > 0)
                {
                    int per = 3 + _focus;
                    if (per > 0) result.AddDamage(per, lightning);
                }
            }
            // ③ 扩容加球位 —— **必须在「雷暴补球」之前**。
            //   原版顺序是：这张牌自己结算（扩容立刻 +2 槽）→ 牌打出后再触发雷暴/子程序这类
            //   `AfterCardPlayed` 能力 → 那时雷暴引导的球已经有空位、不会激发任何东西。
            //   早先这里放在雷暴之后 ⇒ 球位还按旧容量算，于是凭空「激发队首」掉一颗冰球，
            //   冰雹风暴的意图因此算出「回合结束没有冰球」而整条不显示（用户实测：第 8 回合）。
            if (Entry(card) == "CAPACITOR") _capacity += 2;

            // ④ 雷暴：每张能力牌再生成 1（Amount）个电球；「雷暴」自己那张也是能力牌
            bool isPower = false;
            try { isPower = card.Type == CardType.Power; } catch { /* 忽略 */ }
            if (isPower && _stormAmount > 0) Channel(typeof(LightningOrb), _stormAmount, result);

            // ④b **本回合才打出的「雷暴」**（用户 2026-11-06：「能力机第三回合预览时没有显示
            //   即将生成的 2 个闪电充能球的被动」）。
            //
            //   根因：预览是在**玩家回合**算的，那时「雷暴」还没上身（`_stormAmount = 0`），
            //   于是模拟出来的回合结束队列里一颗新电球都没有 —— 可实际打起来，第 3 回合的
            //   「雷暴 + 碎片整理+（能力牌）+ 冷却剂（能力牌）」会当场补 2 颗电球，
            //   它们在回合结束照常各打一次被动。
            //   所以模拟器必须**在模拟过程中把雷暴装上**：打完这一张之后，
            //   后面的能力牌才开始补球（雷暴自己不会触发自己 —— 原版 `BeforeCardPlayed`
            //   登记时这层 Power 还没有，见 `StormPower.BeforeCardPlayed/AfterCardPlayed`）。
            int stormGain = StormAmountOf(card);
            if (stormGain <= 0 && Entry(card) == "STORM") stormGain = 1;   // 变量名对不上时的兜底
            if (stormGain > 0) _stormAmount += stormGain;

            // ⑤ 这张牌给的集中要记进"投射集中"——它影响**后面**那几张牌激发的数值
            //   （用户实测：激发意图原来完全不含集中，也不含回合内新获得的集中）。
            _focus += FocusGainOf(card);
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 球位模拟失败（{Entry(card)}）: {e.Message}");
        }
        return result;
    }

    /// <summary>把激发结果做成意图追加到 <paramref name="intents"/>（<paramref name="pendingDrop"/> 见 <see cref="BossIntentBuilder.PendingDrop"/>）。</summary>
    /// <param name="randomAimElsewhere">
    /// 多人时「这条伤害是**随机目标**、而且她锁的不是本地玩家」⇒ 对本地显示 0。
    /// 依据：跟踪锁定的文案就是「单体攻击、**随机攻击**和充能球只会攻击你」，
    /// 而 `HittableEnemies` 在她出手时被收窄到被锁定的那位 ⇒ 闪电球这类随机伤害实际只打他。
    /// 以前不做这一步，于是**每个玩家的手牌栏都显示自己会挨激发伤害**（用户 2026-10-27 反馈群蛇形态同一问题）。
    /// </param>
    public static void AppendIntents(List<AbstractIntent> intents, Result result, int pendingDrop = 0,
                                     bool randomAimElsewhere = false)
    {
        // 同一份伤害出现多次 → 用「原样数字」意图显示总伤害。
        //
        // ⚠ 这里**不能**用 SingleAttackIntent / MultiAttackIntent：它们的数字会再过一次
        // `Hook.ModifyDamage`（力量、虚弱、易伤…），而**激发充能球的伤害不吃虚弱**
        // （球不是攻击牌）→ 意图会显示比实际低 25% 的数字（用户实测）。
        // RawAttackIntent 直接覆盖 GetTotalDamage，原样显示。
        foreach (var (perHit, times) in result.DamageHits)
        {
            int damage = Math.Max(0, perHit - pendingDrop);
            if (damage <= 0) continue;
            if (randomAimElsewhere) damage = 0;      // 随机攻击且没锁到本地玩家 ⇒ 对本地显示 0
            // 分段显示（用户要求）：激发 2 次就是「14×2」，不是把总数 28 拍成一个数字。
            intents.Add(times > 1
                ? new RawMultiAttackIntent(damage, times)
                : new RawAttackIntent(damage));
        }

        // 冰球激发 = 格挡
        if (result.BlockHits > 0) intents.Add(new DefendIntent());
    }

    /// <summary>按给定顺序把整份计划模拟一遍，返回结束时队列里的冰球数（冰雹风暴的触发条件）。</summary>
    public static int FrostOrbsAfter(Creature boss, OrbQueue? queue, IEnumerable<CardModel> cards)
    {
        var sim = new BossOrbSim(boss, queue);
        foreach (var card in cards) sim.Predict(card);
        int n = 0;
        foreach (var orb in sim._orbs) if (orb is FrostOrb) n++;
        return n;
    }

    /// <summary>
    /// 按给定顺序把整份计划模拟一遍，返回**回合结束时**队列里还剩哪些球（按队列顺序，[0] = 最右侧）。
    /// 用途：手牌栏里「本回合充能球被动会造成的伤害」那一条（用户 2026-10-24）。
    /// </summary>
    public static List<OrbModel> OrbsAfter(Creature boss, OrbQueue? queue, IEnumerable<CardModel> cards)
        => OrbsAfter(boss, queue, cards.Select(c => (Card: c, X: 0)));

    /// <summary>
    /// 同上，但每张牌带「它出手时的剩余能量」—— X 费牌（暴风雨）靠它算生成几张闪电球。
    /// </summary>
    public static List<OrbModel> OrbsAfter(Creature boss, OrbQueue? queue,
                                           IEnumerable<(CardModel Card, int X)> cards)
    {
        var sim = new BossOrbSim(boss, queue);
        foreach (var (card, x) in cards) sim.Predict(card, x);
        return new List<OrbModel>(sim._orbs);
    }

    private void OwnEvokes(CardModel card, Result result)
    {
        OrbEvokeType type;
        try { type = card.OrbEvokeType; }
        catch { return; }
        if (type == OrbEvokeType.None || _orbs.Count == 0) return;

        int times = BossOrbMath.EvokesPerOrb(card);

        if (type == OrbEvokeType.All)
        {
            // 所有球各激发 times 次，然后全部移除
            foreach (var orb in _orbs.ToArray()) Evoke(orb, times, result);
            _orbs.Clear();
            return;
        }

        // Front：同一颗球连续激发 times 次（双重释放=保留激发一次、再激发并移除），随后移除它
        var front = _orbs[0];
        Evoke(front, times, result);
        _orbs.RemoveAt(0);
    }

    private void Channel(Type orbType, int count, Result result)
    {
        for (int i = 0; i < count; i++)
        {
            // 原版顺序：先判断满、满了就先激发队首，然后才入队
            if (_capacity > 0 && _orbs.Count >= _capacity && _orbs.Count > 0)
            {
                Evoke(_orbs[0], 1, result);
                _orbs.RemoveAt(0);
            }
            var orb = OrbOf(orbType);
            if (orb != null && _orbs.Count < Math.Max(_capacity, 1)) _orbs.Add(orb);
        }
    }

    private void Evoke(OrbModel? orb, int times, Result result)
    {
        if (orb == null) return;
        Evoked.Add(orb);
        if (orb is FrostOrb) { result.AddBlock(times); return; }
        if (orb is PlasmaOrb) return;                       // 等离子激发只给能量

        // 激发伤害 = **球的基础值** + **本回合投射的集中**。
        // 球的基础值：`EvokeVal` 里已经含有当时的集中（FocusPower.ModifyOrbValue），
        //   所以先用 `_startFocus` 把它剔除干净；
        // 然后再加 `_focus`（含回合中新获得的集中）。
        // 早先这里是 `EvokeDamageOf(orb, focus)` 直接把集中减掉 → 意图上完全看不到集中
        //（用户实测）。现在意图用的是 RawAttackIntent，它的数字取自 GetTotalDamage、
        // **不会**再被 Hook.ModifyDamage 加一次力量，所以可以放心把集中算进来。
        int baseDamage = BossOrbMath.EvokeDamageOf(orb, _startFocus);
        result.AddDamage(baseDamage + _focus, times);

        // ★ v0.2.474（用户 2026-11-07：「激发电球似乎没有标注『雷霆』的伤害」）：
        //   「雷霆」(`Thunder`) 是一张能力牌，效果是「每当你**激发**闪电充能球时，
        //   对被命中的敌人造成 [ThunderPower] 点伤害」—— 反编译里挂在 `ThunderPower.AfterOrbEvoked`。
        //   以前激发意图只画了球本身的激发值，那笔额外伤害完全没标。
        if (orb is LightningOrb)
        {
            try
            {
                var thunder = _bossCreature?.GetPower<MegaCrit.Sts2.Core.Models.Powers.ThunderPower>();
                int amount = 0;
                if (thunder != null) amount = (int)Math.Round((double)thunder.Amount);
                if (amount > 0) result.AddDamage(amount, times);
            }
            catch (Exception e) { Logger.Log("[CharBoss] 雷霆伤害计入激发意图失败: " + e.Message); }
        }
    }

    /// <summary>
    /// 这张牌会给多少集中：<c>PowerVar&lt;FocusPower&gt;</c>（碎片整理 / 偏差认知 / 集中打击…）。
    /// 本 mod 的「偏差认知」让力量与集中恒等 ⇒ 获得力量（暴涨 +2）同样等于获得集中。
    /// </summary>
    private int FocusGainOf(CardModel card)
    {
        int gain = 0;
        try
        {
            if (card.DynamicVars.TryGetValue("Focus", out var focusVar) && focusVar != null)
                gain += (int)Math.Round((double)focusVar.BaseValue);
            else
            {
                // ★ v0.2.493（用户 2026-11-08：「彩虹机第一回合彩虹激发电球没有考虑本回合会获得的集中
                //   （碎片整理）」）——「碎片整理」的集中变量**不叫 "Focus"**，而是 `PowerVar<FocusPower>`
                //   （名字是 "FocusPower"），上面按 "Focus" 取不到 ⇒ 一直是 0，
                //   于是同一回合后面那次激发用的还是旧集中。这里按**泛型类型**再兜一遍。
                try
                {
                    foreach (var v in card.DynamicVars.Values)
                    {
                        if (v == null) continue;
                        var vt = v.GetType();
                        if (!vt.IsGenericType) continue;
                        if (vt.GetGenericTypeDefinition() != typeof(MegaCrit.Sts2.Core.Localization.DynamicVars.PowerVar<>)) continue;
                        if (vt.GetGenericArguments()[0] != typeof(MegaCrit.Sts2.Core.Models.Powers.FocusPower)) continue;
                        gain += (int)Math.Round((double)v.BaseValue);
                        break;
                    }
                }
                catch { /* 忽略 */ }
            }
        }
        catch { /* 没有就是 0 */ }

        if (_focusFollowsStrength)
        {
            try
            {
                if (card.DynamicVars.TryGetValue("Strength", out var strVar) && strVar != null)
                    gain += (int)Math.Round((double)strVar.BaseValue);
            }
            catch { /* 忽略 */ }
        }
        return gain;
    }

    /// <summary>
    /// 这张牌会给多少层 `StormPower`（**0 = 它不是「雷暴」**）——
    /// 未升级 1 / 升级 2（反编译：`PowerVar&lt;StormPower&gt;(1m)`，`OnUpgrade` 里 `UpgradeValueBy(1m)`）。
    /// 变量名按类型名来（和 `Storm` 自己的 `DynamicVars["StormPower"]` 一致），取不到就返回 0。
    /// </summary>
    private static int StormAmountOf(CardModel card)
    {
        foreach (var key in new[] { "StormPower", "Storm" })
        {
            try
            {
                if (card.DynamicVars.TryGetValue(key, out var v) && v != null)
                    return Math.Max(0, (int)Math.Round((double)v.BaseValue));
            }
            catch { /* 试下一个名字 */ }
        }
        return 0;
    }

    private static OrbModel? OrbOf(Type type)
    {
        try { return ModelDb.GetById<OrbModel>(ModelDb.GetId(type))?.ToMutable(1); }
        catch { return null; }
    }

    private static string Entry(CardModel card)
    {
        try { return card.Id.Entry; } catch { return card.GetType().Name; }
    }
}
