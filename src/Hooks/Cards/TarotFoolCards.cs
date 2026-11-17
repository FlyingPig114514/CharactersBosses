#if STS2_113   // ⚠ 0.113.0 才有「愚者」与整套塔罗牌 ⇒ 用户 2026-11-14：107 版本不加这个 Boss，整个文件只在 111 构建里编译
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Powers;

namespace CharacterBosses.Cards;
/// <summary>
/// **这 4 张牌没有自己的卡图** ⇒ 借一张原版塔罗牌的立绘。
///
/// 用户 2026-11-14（实机截图）：「怎么中间那 2 张牌没有卡图？」——
/// 截图里空着的那两张正是第 9 回合的「天人形态」和「永恒形态」（以及第 12 回合的「斋戒」「斗殴」）。
///
/// 反编译 `CardModel`：
/// <code>
/// public virtual string PortraitPath => ImageHelper.GetImagePath($"atlases/card_atlas.sprites/{Pool.Title.ToLowerInvariant()}/{Id.Entry.ToLowerInvariant()}.tres");
/// </code>
/// 也就是**按模型 id 拼贴图路径** —— 我们的 `TAROT_DEVA_FORM` 之类在游戏资源里当然没有，
/// 于是 `HasPortrait == false`、卡面就是空白。
/// `PortraitPath` 是 `public virtual` ⇒ 直接覆写成「某张原版塔罗牌的路径」即可。
/// 挑的都是气质相近的原版塔罗牌（同一套图集，尺寸/边框完全一致）。
/// </summary>
internal static class TarotCardArt
{
    /// <summary>斋戒（观者 mod 的 `FASTING` 卡图）。</summary>
    internal const string Fasting = "res://CharacterBosses/images/cards/tarot_fasting.png";
    /// <summary>天人形态（观者 mod 的 `DEVA_FORM` 卡图）。</summary>
    internal const string DevaForm = "res://CharacterBosses/images/cards/tarot_deva_form.png";
    /// <summary>斗殴（崩坠 Hermit 的 `BRAWL` 卡图）。</summary>
    internal const string Brawl = "res://CharacterBosses/images/cards/tarot_brawl.png";
    /// <summary>永恒形态（崩坠 Hermit 的 `ETERNAL_FORM` 卡图）。</summary>
    internal const string EternalForm = "res://CharacterBosses/images/cards/tarot_eternal_form.png";

    /// <summary>观者紫费用宝石（本体中性灰宝石 × 观者紫 #9E68FF，见 tools\art\tint_png.gd）。</summary>
    internal const string WatcherGem = "res://CharacterBosses/images/cards/tarot_energy_watcher.png";
    /// <summary>亡命之徒黄褐费用宝石（本体中性灰宝石 × 亡命之徒色 #CEA477）。</summary>
    internal const string HermitGem = "res://CharacterBosses/images/cards/tarot_energy_hermit.png";

    /// <summary>资源真的存在就返回它，否则退回原版「缺图」路径（绝不会比空白更糟）。</summary>
    internal static string ExistingOrMissing(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && Godot.ResourceLoader.Exists(path)) return path;
        }
        catch { /* 走缺图 */ }
        return MegaCrit.Sts2.Core.Models.CardModel.MissingPortraitPath;
    }

    /// <summary>
    /// **卡池**。这四个类如果不管，`CardModel.Pool` 会返回 `null`
    ///（它默认在 `ModelDb.AllCardPools` 里找「谁的 `AllCardIds` 含有这张牌的 id」——
    /// 我们自造的 id 不在任何池子里），而 `FrameMaterial` / `VisualCardPool` / 图鉴都直接读它。
    /// 这里借一张原版塔罗牌（皇帝）所在的池子（`token`）。
    /// </summary>
    internal static MegaCrit.Sts2.Core.Models.CardPoolModel? TokenPool()
    {
        try
        {
            var id = MegaCrit.Sts2.Core.Models.ModelDb.GetId(typeof(MegaCrit.Sts2.Core.Models.Cards.TheEmperor));
            var emperor = id == null ? null : MegaCrit.Sts2.Core.Models.ModelDb.GetById<MegaCrit.Sts2.Core.Models.CardModel>(id);
            return emperor?.Pool;
        }
        catch { return null; }
    }
}
public sealed class TarotFasting : CardModel
{
    /// <summary>卡图：借力量/女皇（+力量 +敏捷的能力牌）那一张（见 <see cref="TarotCardArt"/>）。</summary>
    public override string PortraitPath => TarotCardArt.ExistingOrMissing(TarotCardArt.Fasting);
    protected override string PortraitPngPath => TarotCardArt.ExistingOrMissing(TarotCardArt.Fasting);
    public override MegaCrit.Sts2.Core.Models.CardPoolModel Pool => TarotCardArt.TokenPool()!;

    public TarotFasting() : base(2, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }

    /// <summary>力量 / 敏捷各加这么多（原版观者 mod：3，升级 4）。</summary>
    protected override IEnumerable<DynamicVar> CanonicalVars
        => new DynamicVar[] { new DynamicVar("MagicNumber", 3m) };

    protected override void OnUpgrade()
    {
        base.DynamicVars["MagicNumber"].UpgradeValueBy(1m);
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var owner = Owner?.Creature;
        if (owner == null) return;
        decimal amount = DynamicVars["MagicNumber"].BaseValue;

        await PowerCmd.Apply<StrengthPower>(choiceContext, owner, amount, owner, this);
        await PowerCmd.Apply<DexterityPower>(choiceContext, owner, amount, owner, this);
        // 每回合开始时少获得 1 点能量（原版是「少获得 1」，与力量/敏捷的数值无关）
        await PowerCmd.Apply<TarotEnergyDownPower>(choiceContext, owner, 1m, owner, this);

        Logger.Log($"[CharBoss] 塔罗·斋戒：{owner.Name} +{amount} 力量 / +{amount} 敏捷，" +
                   "此后每回合少 1 点能量");
    }
}

/// <summary>
/// 天人形态（原版观者 mod 的 `WATCHER_DEVA_FORM`）：每回合开始获得**递增**的能量。
/// 用户 2026-11-14 定的口径：**3 费**，**升级后去掉「虚无」**（数值不变，起始仍是 1）。
/// </summary>
public sealed class TarotDevaForm : CardModel
{
    /// <summary>卡图：借星星（每回合给能量）那一张（见 <see cref="TarotCardArt"/>）。</summary>
    public override string PortraitPath => TarotCardArt.ExistingOrMissing(TarotCardArt.DevaForm);
    protected override string PortraitPngPath => TarotCardArt.ExistingOrMissing(TarotCardArt.DevaForm);
    public override MegaCrit.Sts2.Core.Models.CardPoolModel Pool => TarotCardArt.TokenPool()!;

    public TarotDevaForm() : base(3, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    /// <summary>起始能量（不随升级变化）。</summary>
    protected override IEnumerable<DynamicVar> CanonicalVars
        => new DynamicVar[] { new DynamicVar("MagicNumber", 1m) };

    /// <summary>原版是「虚无」，升级后去掉（见 <see cref="OnUpgrade"/>）。</summary>
    public override IEnumerable<CardKeyword> CanonicalKeywords
        => new[] { CardKeyword.Ethereal };

    protected override void OnUpgrade()
    {
        // 用户口径：「升级后去掉虚无」
        RemoveKeyword(CardKeyword.Ethereal);
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var owner = Owner?.Creature;
        if (owner == null) return;
        decimal start = DynamicVars["MagicNumber"].BaseValue;

        // ⚠ 2026-11-15（用户实测）：「塔罗愚打天人形态的时候还是看不到动画，而且还会卡一下。
        //    你干脆别让天人形态播放动画了，也别让它打天人形态的时候卡一下（审判照常）」
        //   ⇒ 不再借观者的「开眼扫场」：那套动画要 4 秒，而我们在 OnPlay 里 await 它
        //   ⇒ 直接把 Boss 的出牌序列卡住 4 秒，画面还看不出来。审判（一次性演出）保留。
        await PowerCmd.Apply<TarotDevaPower>(choiceContext, owner, start, owner, this);
        Logger.Log($"[CharBoss] 塔罗·天人形态：{owner.Name} 每回合开始获得递增能量（起始 {start}）");
    }
}

/// <summary>
/// 斗殴（崩坠 Hermit 的 `HERMIT-BRAWL`）：每回合开始给**所有敌人**上瘀伤。
/// 用户 2026-11-14 定的口径：**2 费**，每回合 **3 层**瘀伤，**升级后 5 层**。
/// 瘀伤是它间接给的 buff，见 <see cref="TarotBruisePower"/>。
/// </summary>
public sealed class TarotBrawl : CardModel
{
    /// <summary>卡图：借正义（攻击/瘀伤）那一张（见 <see cref="TarotCardArt"/>）。</summary>
    public override string PortraitPath => TarotCardArt.ExistingOrMissing(TarotCardArt.Brawl);
    protected override string PortraitPngPath => TarotCardArt.ExistingOrMissing(TarotCardArt.Brawl);
    public override MegaCrit.Sts2.Core.Models.CardPoolModel Pool => TarotCardArt.TokenPool()!;

    public TarotBrawl() : base(2, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }

    /// <summary>每回合给的瘀伤层数（3，升级 5）。</summary>
    protected override IEnumerable<DynamicVar> CanonicalVars
        => new DynamicVar[] { new DynamicVar("BrawlPower", 3m) };

    protected override void OnUpgrade()
    {
        // 用户口径：升级后每回合 5 层（3 → 5）
        base.DynamicVars["BrawlPower"].UpgradeValueBy(2m);
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var owner = Owner?.Creature;
        if (owner == null) return;
        decimal amount = DynamicVars["BrawlPower"].BaseValue;

        await PowerCmd.Apply<TarotBrawlPower>(choiceContext, owner, amount, owner, this);
        Logger.Log($"[CharBoss] 塔罗·斗殴：{owner.Name} 此后每回合开始给所有敌人 {amount} 层瘀伤");
    }
}

/// <summary>
/// 永恒形态（崩坠 Hermit 的 `HERMIT-ETERNAL_FORM`）：
/// 每回合抽到的**前 4 张**可打出的牌，本回合内耗能减少 N。
/// 用户 2026-11-14 定的口径：**3 费**，**升级后去掉「虚无」**（减费数值不变，仍是 1）。
/// </summary>
public sealed class TarotEternalForm : CardModel
{
    /// <summary>卡图：借回响形态（同类能力牌）那一张（见 <see cref="TarotCardArt"/>）。</summary>
    public override string PortraitPath => TarotCardArt.ExistingOrMissing(TarotCardArt.EternalForm);
    protected override string PortraitPngPath => TarotCardArt.ExistingOrMissing(TarotCardArt.EternalForm);
    public override MegaCrit.Sts2.Core.Models.CardPoolModel Pool => TarotCardArt.TokenPool()!;

    public TarotEternalForm() : base(3, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    /// <summary>每张牌减多少费（原版 1，不随升级变化）。</summary>
    protected override IEnumerable<DynamicVar> CanonicalVars
        => new DynamicVar[] { new DynamicVar("EternalPower", 1m) };

    /// <summary>原版是「虚无」，升级后去掉（见 <see cref="OnUpgrade"/>）。</summary>
    public override IEnumerable<CardKeyword> CanonicalKeywords
        => new[] { CardKeyword.Ethereal };

    protected override void OnUpgrade()
    {
        // 用户口径：「升级后去掉虚无」
        RemoveKeyword(CardKeyword.Ethereal);
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var owner = Owner?.Creature;
        if (owner == null) return;
        decimal amount = DynamicVars["EternalPower"].BaseValue;

        await PowerCmd.Apply<TarotEternalPower>(choiceContext, owner, amount, owner, this);
        Logger.Log($"[CharBoss] 塔罗·永恒形态：{owner.Name} 每回合前 4 张手抽牌各减 {amount} 费");
    }
}
#endif
