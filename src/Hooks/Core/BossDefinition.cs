using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Core;

/// <summary>
/// 一个「角色 Boss」的可编辑定义。★用户设计 Boss 时主要改 <see cref="BossDefs"/>。
///
/// 数据分两层：
///   · 基础层（<see cref="Deck"/> / <see cref="Relics"/> / <see cref="Priority"/>）—— 所有幕通用；
///   · 按幕覆盖层（<see cref="DeckByAct"/> / <see cref="RelicsByAct"/>）—— 某一幕想用更强的卡组时填这里。
/// 按幕覆盖为空时自动回落到基础层；基础层也为空时由 <see cref="BossLoadout"/> 用
/// 该角色的起始卡组/起始遗物兜底（占位，方便先跑起来再替换）。
/// </summary>
public sealed class BossDefinition
{
    /// <summary>日志用标签，建议写成对应怪物类的 UPPER_SNAKE 名（如 IRONCLAD_BOSS），便于对照本地化 key。</summary>
    public string Key = "UNNAMED_BOSS";

    /// <summary>复用哪个原版角色的战斗贴图：ironclad / silent / defect / necrobinder / regent。</summary>
    public string VisualScene = "ironclad";

    /// <summary>原版角色类型：用来取它的卡池、遗物池、起始卡组与起始遗物。</summary>
    public Type CharacterType = typeof(MegaCrit.Sts2.Core.Models.Characters.Ironclad);

    /// <summary>按幕 HP，下标 = <c>ActModel.Index</c>（0 = 第一幕，1 = 第二幕，2 = 第三幕），越界取最后一档。</summary>
    public int[] Hp = { 130, 170, 210, 240 };

    /// <summary>按幕每回合能量（下标同 <see cref="Hp"/>）。</summary>
    public int[] Energy = { 3, 3, 4, 4 };

    /// <summary>按幕每回合抽到手牌数（下标同 <see cref="Hp"/>）。</summary>
    public int[] HandSize = { 5, 5, 5, 5 };

    /// <summary>基础卡组（原版卡类型，取自该角色自己的卡池）。重复写多次 = 多张。空 = 用起始卡组占位。</summary>
    public List<Type> Deck = new();

    /// <summary>按幕覆盖卡组：<c>DeckByAct[act]</c> 非空时优先于 <see cref="Deck"/>。</summary>
    public List<Type>?[] DeckByAct = new List<Type>?[4];

    /// <summary>基础遗物（原版遗物类型，真实触发）。空 = 用该角色起始遗物占位。</summary>
    public List<Type> Relics = new();

    /// <summary>按幕覆盖遗物。</summary>
    public List<Type>?[] RelicsByAct = new List<Type>?[4];

    /// <summary>
    /// 按幕的「回合剧本」。填了就用剧本出牌（意图完全固定、可复现），
    /// 不填才走 <see cref="BossPlanner"/> 的贪心 AI。写法见 <see cref="BossScript"/> 与 BossDefs 里的例子。
    /// </summary>
    public BossScript?[] ScriptByAct = new BossScript?[4];

    /// <summary>
    /// 按幕的特殊效果说明（本地化 key）。**每一幕的特殊效果可以不同**，所以是按幕的数组：
    /// <c>SpecialLocByAct[act]</c> 为空表示这一幕没有特殊效果（不显示说明框、也不施加 Power）。
    /// </summary>
    public string?[] SpecialLocByAct = new string?[4];

    // ★ 2026-11-13（用户「恐惧模式的提升」）：恐惧模式下**同名但更强**的那一套特殊效果。
    //   为空的槽位表示「这只 Boss 没有单独的恐惧版」⇒ 退回正常模式的那份。
    //   文案另存 `<KEY>_BOSS.<special>_dread`（16 语言），由 SpecialLocForAct 按 DreadMode.IsActive 切换。
    public string?[] SpecialLocDreadByAct = new string?[4];

    /// <summary>按幕的特殊效果 Power，与 <see cref="SpecialLocByAct"/> 一一对应。</summary>
    public Type?[] SpecialPowerByAct = new Type?[4];

    /// <summary>恐惧模式专用的特殊效果 Power（与 <see cref="SpecialLocDreadByAct"/> 一一对应）。</summary>
    public Type?[] SpecialPowerDreadByAct = new Type?[4];

    /// <summary>
    /// ★ 2026-11-13（恐惧模式）：**恐惧模式专属的「升级某张牌」**（按幕）。
    ///
    /// 为什么需要单独一条通道：`AscensionUpgrades` 是**静态**表（按进阶等级），而恐惧模式是
    /// **运行时**才知道的（`DreadMode.IsActive`）—— 像「黑球机：**升级眼部攻击**」这种要求，
    /// 只能等到造手牌 / 组卡组的那一刻才判断。
    ///
    /// 用途：`CharBossMonster.AscensionUpgradeTypes()` 会在 `DreadMode.IsActive` 时把这里
    /// （以及变体的 <see cref="BossVariant.DreadUpgrades"/>）一并加进去 ⇒ 卡组与剧本手牌里
    /// 同名的那张牌都变成升级版。
    /// </summary>
    public List<Type>?[] DreadUpgradesByAct = new List<Type>?[4];

    /// <summary>这一幕恐惧模式要额外升级哪些牌（变体优先；没填就是空）。</summary>
    public List<Type> DreadUpgradeTypesFor(int act)
    {
        try
        {
            var variantList = ActiveVariant(act)?.DreadUpgrades;
            if (variantList != null) return variantList;
            var byAct = DreadUpgradesByAct[Clamp(act, DreadUpgradesByAct.Length)];
            if (byAct != null) return byAct;
        }
        catch { /* 忽略 */ }
        return new List<Type>();
    }

    /// <summary>按幕的**初始药水**（炼药猎：8 瓶；其它 Boss 留空）。</summary>
    public List<Type>?[] PotionsByAct = new List<Type>?[4];

    /// <summary>按幕的「每用一瓶药水回复多少生命」（调制 = 5）。</summary>
    public int[] HealPerPotionByAct = new int[4];

    /// <summary>按幕的**药水栏位数**（炼药猎 = 8；0 = 不动影子玩家的栏位）。</summary>
    public int[] PotionSlotsByAct = new int[4];

    /// <summary>按幕的**备选设计**（同一角色同一幕的第二套设计；目前只有三层静默猎手）。</summary>
    public BossVariant?[] VariantsByAct = new BossVariant?[4];

    /// <summary>
    /// 按幕的**额外设计**：某些角色同一幕有 2 套以上非主设计（三层故障机器人 = 主设计能力机
    /// + **事件机** + **状态机**）。
    ///
    /// ★ v0.2.419（用户实测 `fight DEFECT_EVENT_BOSS_ENCOUNTER` 打不出事件机）：
    ///   以前一幕只能挂**一套**变体（`VariantsByAct` 是 `BossVariant?[]`）——
    ///   我把事件机和状态机都塞进三层时，`ActiveVariant` 永远只读到一个 ⇒ **事件机连判定都不会被问**，
    ///   于是永远走主设计（能力机）。现在多出来的设计放这里，判定顺序 = `VariantsByAct` 优先、之后按本表顺序。
    /// </summary>
    public Dictionary<int, List<BossVariant>> ExtraVariants = new();

    /// <summary>
    /// 把**某一套设计**钉死在这一幕（非 null 时 <see cref="ActiveVariant"/> 只认这个 Key）。
    ///
    /// 用途：斗蛐蛐 / 控制台这类「外部指定要打哪一档」的入口 —— 它们的遭遇不是我们的
    /// `XxxBossEncounter`，`Enabled()` 的遭遇判定全部落空，只能按种子随机；
    /// 钉死之后同一只怪物每次进来都是同一套设计。
    /// **null = 老行为**（按遭遇 → 按种子），对正常流程零影响。
    /// </summary>
    public string? PinnedVariantKey;

    /// <summary>
    /// 返回一个「钉死某套设计」的副本。
    /// `BossDefs.Xxx` 是全局单例，不能就地改（同一角色在不同战斗里可能要用不同设计），
    /// 所以这里 `MemberwiseClone` 出一份只改 pin 的浅拷贝（其余数组/列表都是只读共享）。
    /// </summary>
    public BossDefinition WithVariantPin(string key)
    {
        var clone = (BossDefinition)MemberwiseClone();
        clone.PinnedVariantKey = key;
        return clone;
    }

    /// <summary>某一幕的全部备选设计（按判定顺序；没有就是空序列）。</summary>
    public IEnumerable<BossVariant> VariantsForAct(int act)
    {
        if (VariantsByAct.Length > 0)
        {
            BossVariant? single = null;
            try { single = VariantsByAct[Clamp(act, VariantsByAct.Length)]; } catch { single = null; }
            if (single != null) yield return single;
        }
        List<BossVariant>? extra = null;
        try { ExtraVariants.TryGetValue(act, out extra); } catch { extra = null; }
        if (extra != null)
        {
            foreach (var v in extra) if (v != null) yield return v;
        }
    }

    /// <summary>出牌优先级：靠前的先打。不在表里的牌按卡牌类型给默认优先级。</summary>
    public List<Type> Priority = new();

    /// <summary>
    /// 禁止 AI 打出的牌（会在排序前就被过滤掉）。
    /// 典型用途：召唤随从的牌 —— 随从会被算成**玩家侧盟友**，Boss 打它等于给玩家送个帮手
    /// （见 tools\scan-card-mechanics.ps1 的扫描结果）。
    /// </summary>
    public List<Type> SkipCards = new();

    /// <summary>
    /// 进阶加成：达到 <c>MinAscension</c> 时，卡组里这些牌是**升级版**（用户设计，例如亡灵契约师进阶 8 的厄运之衣+）。
    /// 同时会作用于**剧本造出来的手牌**（见 <c>CharBossMonster.PrepareTurn</c>）。
    /// </summary>
    public List<(int MinAscension, Type Card)> AscensionUpgrades = new();

    /// <summary>
    /// 进阶加成（**按幕**）：达到 <c>MinAscension</c> 时，**该幕**的卡组与剧本手牌里这些牌是升级版。
    /// <c>Act</c> 用 1 起的幕号，<c>0</c> = 所有幕。
    ///
    /// 为什么需要「按幕」：静默猎手是同一个定义挂了两个 Boss（第二幕涂毒猎 / 第三幕刀猎），
    /// 而像「中和」这种牌两套卡组里都有 —— 不分幕的话，给涂毒猎加的进阶升级会漏到刀猎身上。
    /// </summary>
    public List<(int MinAscension, int Act, Type Card)> AscensionUpgradesByAct = new();

    /// <summary>进阶加成：达到 <c>MinAscension</c> 时额外获得的遗物（例如进阶 8 的不死符文）。</summary>
    public List<(int MinAscension, Type Relic)> AscensionRelics = new();

    /// <summary>
    /// 进阶加成（**按幕**）：达到 <c>MinAscension</c> 时该幕额外获得的遗物。
    /// <c>InsertBefore</c> 非空时把它插在「那件遗物」前面 —— 用来控制状态条上的显示顺序
    /// （用户要求：风的女儿放在钗和螺线飞镖之间）。
    /// </summary>
    public List<(int MinAscension, int Act, Type Relic, Type? InsertBefore)> AscensionRelicsByAct = new();

    /// <summary>
    /// 第一回合**出完牌之后**说的一句话（LocString 的表名 + key）。
    /// 例：能力机用低语耳环那句 <c>("relics", "WHISPERING_EARRING.approval")</c>
    /// ＝ 本地化里的 <c>[red][shake]这样才对[/shake][/red]</c>。null = 不说。
    /// </summary>
    public (string Table, string Key)? FirstTurnLine;

    /// <summary>
    /// 是否携带「涅奥的祝福」（默认带）。带了就：最大生命 +120（拾起）/ +180（每进一幕）、每回合少抽 2 张牌、少 1 点能量。
    /// 这是给 Boss 的高血量一个合理说法、同时压低其强度的机制，见 <c>Relics/NeowBlessing.cs</c>。
    /// </summary>
    public bool CarriesNeowBlessing = true;

    /// <summary>
    /// 是否让 Boss 的**遗物**真的收到「战斗内事件」钩子。默认 **false**（老行为：一律不触发）。
    ///
    /// 为什么默认不触发：原版这些钩子只派发给 `CombatState` 里注册的监听者，而遗物是从
    /// `CombatState.Players` 取的 —— 影子 Player 不在里面（和「缚魂命匣不给奥斯提」
    /// 「天赋君权不给星」同一类问题）。需要时由 <c>Patches/BossRelicHookBridge.cs</c> 手动补跑：
    ///   · 卡戎之灰 `CharonsAshes`：消耗一张牌 → 对所有敌人 3 点伤害（`AfterCardExhausted`）；
    ///   · 奥利哈钢 `Orichalcum`：回合结束没有格挡 → 获得 6 格挡（`BeforeSideTurnEnd`）。
    /// 目前只有一层「愤怒战」打开它 ⇒ 其它 Boss 一点不受影响。
    /// </summary>
    public bool FireBossRelicHooks;

    /// <summary>
    /// 是否**拦掉她自己的原版抽牌**（手牌完全由剧本给）。默认 false。
    ///
    /// 为什么需要：她的手牌每回合由 `BossScriptRunner.BuildHand` 重建，意图里的伤害/格挡也都是按那份手牌
    /// 提前算好的；原版真抽一张进来，手牌就多一张、意图和实际打出的东西对不上。
    /// 二层「灵魂骨」是因为身上有「灵魂绑定」自动被拦（见 <c>Patches/ShadowBossNoDrawPatch.cs</c>）；
    /// 二层「物理机」的「超越光速」会抽牌，而剧本已经把那几张写好了 ⇒ 打开这个开关。
    /// </summary>
    public bool BlocksVanillaDraw;

    /// <summary>
    /// **哪些幕**要「打出的牌抽进来的牌放回抽牌堆顶」（手牌恒等于剧本，但抽牌本身照常发生）。
    /// 下标 = <c>ActModel.Index</c>（0 = 第一幕）。
    ///
    /// 为什么不能直接用 <see cref="BlocksVanillaDraw"/> 拦掉：一层「奇巧猎」的「速行者」
    /// （`SpeedsterPower`）是**每抽 1 张牌对所有敌人造成 2 点伤害**，拦掉抽牌等于把她的群伤也拦掉了。
    /// 所以这里保留抽牌、只在事后把新进手牌挪回牌堆顶（和「喝药水抽到牌」同一套处理，
    /// 见 <c>BossPotions.KeepScriptedHand</c>）。目前只有一层「奇巧猎」打开。
    /// </summary>
    public bool[] KeepsScriptedHandByAct = new bool[4];

    /// <summary>这一幕要不要「抽牌后把手牌还原成剧本」（<paramref name="act"/> 从 0 起）。</summary>
    public bool KeepsScriptedHandForAct(int act)
    {
        try { return KeepsScriptedHandByAct[Clamp(act, KeepsScriptedHandByAct.Length)]; }
        catch { return false; }
    }

    /// <summary>
    /// **哪些幕**要「真的兑现」「下回合 +N 能量」（<c>EnergyNextTurnPower</c>，来自「拖延」「汇流」这类牌）。
    /// 幕号从 **1** 起；空 = 一律不兑现（老行为）。
    ///
    /// 为什么默认不兑现：`EnergyNextTurnPower` 的到期是靠原版
    /// `AfterEnergyReset(player)` 派发的，而影子 Player 不在 `CombatState.Players` 里 ⇒
    /// 它既不兑现也永不过期（见 <c>BossHookBridge.RemoveNextTurnPowers</c>）。
    /// 以前的做法是「只删不发」，把那笔能量直接写进剧本的逐回合能量表
    /// （储君的「汇流」就是这么处理的）。
    ///
    /// 二层「灵魂骨」不行：她的「拖延+」给 2 点，第 1 回合打出来、第 2 回合才到账，
    /// 而第 2 回合要打「守墓人(1)+死者苏生(3)+死亡行军(1)=5 费」—— 3 基础 + 2 = 刚好 5，
    /// 逐回合写死反而会与「拖延+ 给下回合 2 费」打架。所以这一幕改成真的读那层 Power
    /// （见 <c>CharBossMonster.PrepareTurn</c>）。
    /// </summary>
    public int[] HonorsNextTurnEnergyActs = Array.Empty<int>();

    /// <summary>这一幕是否要真的兑现「下回合 +N 能量」（<paramref name="act"/> 从 0 起）。</summary>
    public bool HonorsNextTurnEnergyForAct(int act)
    {
        try
        {
            foreach (int a in HonorsNextTurnEnergyActs)
                if (a - 1 == act) return true;
        }
        catch { /* 忽略 */ }
        return false;
    }

    private static int Clamp(int act, int length) => Math.Clamp(act, 0, Math.Max(0, length - 1));

    /// <summary>
    /// 这一幕的血量上限 = **该角色自己的起始血量** + 这一幕的「额外加成」。
    ///
    /// 额外加成写在 <see cref="Hp"/> 数组里：**某一幕的值减去第 0 幕的值**就是那一幕额外的部分
    /// （基础血量统一按角色基础血量，逐幕变强只靠涅奥祝福：拾起 +120、每进一幕 +180）。
    ///
    /// ★ v0.2.286（用户 2026-10-31：「布制果实没有正确额外增加根除骨的最大生命值，把她的最大生命值
    ///   增加 31（增加到 547）」）：原来这里**无条件**返回 `CharacterModel.StartingHp`，把数组里
    ///   第三幕那个 97（= 66 + 31 布质果实）整个吃掉了 ⇒ 根除骨只有 66 + 450 = **516**。
    ///   现在改成「起始血量 + (Hp[幕] − Hp[0])」⇒ 第三幕 = 66 + 31 = 97 ⇒ 97 + 480 = **577**（涅奥 120/180 之后）。
    ///   其它 Boss 的数组各幕都等于基础血量 ⇒ 加成 0，行为完全不变。
    /// </summary>
    public int HpForAct(int act)
    {
        int idx = Clamp(act, Hp.Length);
        int perAct = Hp.Length > 0 ? Hp[idx] : 0;
        int baseRow = Hp.Length > 0 ? Hp[0] : 0;

        // ★ v0.2.368（用户 2026-11-04：「养手骨的最大生命值不对，你是不是也给她加根除骨的布制果实了？」）
        //   —— 确实加了。`Hp[幕] - Hp[0]` 那点「逐幕加成」是**主设计专属**的：
        //   第三幕亡灵契约师那个 +31 就是「根除骨」自己的**布质果实**（`Hp[2] = 97 = 66 + 31`）。
        //   而**变体接管这一幕时不该继承它**：养手骨没有布质果实，却因为共用同一行血量白吃 31 点
        //   （实测 577，应该是 546）。
        //   所以变体生效时「逐幕加成」按 0 算；变体真的需要额外血量时，由它自己的 `HpBonus` 显式给
        //   （例如事件机因为「大蘑菇」写的那 +20）。
        bool variantTakesOver = false;
        try { variantTakesOver = ActiveVariant(act) != null; } catch { variantTakesOver = false; }

        int extra = (!variantTakesOver && perAct > 0 && baseRow > 0 && perAct > baseRow)
            ? perAct - baseRow
            : 0;

        try
        {
            var character = ModelDb.GetById<CharacterModel>(ModelDb.GetId(CharacterType));
            if (character != null && character.StartingHp > 0)
            {
                return character.StartingHp + extra;
            }
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] {Key} 读角色基础血量失败，退回 Hp 数组: {e.Message}");
        }
        return perAct > 0 ? perAct : baseRow;
    }

    public int EnergyForAct(int act) => Energy[Clamp(act, Energy.Length)];

    public int HandSizeForAct(int act) => HandSize[Clamp(act, HandSize.Length)];

    public List<Type>? DeckForAct(int act) => ActiveVariant(act)?.Deck ?? DeckByAct[Clamp(act, DeckByAct.Length)];

    public List<Type>? RelicsForAct(int act) => ActiveVariant(act)?.Relics ?? RelicsByAct[Clamp(act, RelicsByAct.Length)];

    public BossScript? ScriptForAct(int act) => ActiveVariant(act)?.Script ?? ScriptByAct[Clamp(act, ScriptByAct.Length)];

    /// <summary>这一幕的特殊效果说明 key（没有则 null）。</summary>
    public string? SpecialLocForAct(int act)
    {
        var v = ActiveVariant(act);
        // 变体**优先**：变体的 SpecialLoc 为空就是「这套设计没有特殊效果说明」，
        // 不能 `??` 掉回主设计（否则炼药猎会显示刀猎的「袖里乾坤」，用户 2026-10-26 实测）。
        if (Core.DreadMode.IsActive)
        {
            var dread = v != null ? v.SpecialLocDread : SpecialLocDreadByAct[Clamp(act, SpecialLocDreadByAct.Length)];
            if (!string.IsNullOrWhiteSpace(dread)) return dread;
        }
        return v != null ? v.SpecialLoc : SpecialLocByAct[Clamp(act, SpecialLocByAct.Length)];
    }

    /// <summary>这一幕的特殊效果 Power 类型（没有则 null）。</summary>
    public Type? SpecialPowerForAct(int act)
    {
        var v = ActiveVariant(act);
        if (Core.DreadMode.IsActive)
        {
            var dread = v != null ? v.SpecialPowerDread : SpecialPowerDreadByAct[Clamp(act, SpecialPowerDreadByAct.Length)];
            if (dread != null) return dread;
        }
        return v != null ? v.SpecialPower : SpecialPowerByAct[Clamp(act, SpecialPowerByAct.Length)];
    }

    /// <summary>这一幕的**初始药水**（炼药猎用；其它 Boss 为空列表 = 不用药水）。</summary>
    public List<Type> PotionsForAct(int act)
        => ActiveVariant(act)?.Potions ?? PotionsByAct[Clamp(act, PotionsByAct.Length)];

    /// <summary>这一幕**每用一瓶药水回复多少生命**（调制；0 = 不回血）。</summary>
    public int HealPerPotionForAct(int act)
        => ActiveVariant(act)?.HealPerPotion ?? HealPerPotionByAct[Clamp(act, HealPerPotionByAct.Length)];

    /// <summary>这一幕的**药水栏位数**（0 = 不补栏位，用原版给影子玩家的值）。</summary>
    public int PotionSlotsForAct(int act)
        => ActiveVariant(act)?.PotionSlots ?? PotionSlotsByAct[Clamp(act, PotionSlotsByAct.Length)];

    /// <summary>
    /// 这一幕「生效的变体」自己的进阶升级表；没有变体时返回 null。
    /// 调用方（`ShadowPlayerFactory` / `CharBossMonster.AscensionUpgradeTypes`）拿到非 null
    /// 就**不要再叠加**主定义那一幕的行（两套设计互斥）。
    /// </summary>
    public List<(int MinAscension, Type Card)>? VariantAscensionUpgrades(int act)
        => ActiveVariant(act)?.AscensionUpgrades;

    /// <summary>同上，进阶**遗物**表。</summary>
    public List<(int MinAscension, Type Relic)>? VariantAscensionRelics(int act)
        => ActiveVariant(act)?.AscensionRelics;

    /// <summary>这一幕是不是由某个「备选设计」接管（接管时主定义这一幕的进阶行不生效）。</summary>
    public bool VariantTakesOver(int act) => ActiveVariant(act) != null;

    /// <summary>这一幕**实际生效**的「谋杀按本场拿到过的牌数起步」开关（变体优先）。</summary>
    public bool MurderCountsDrawnCardsFor(int act)
    {
        try
        {
            // 2026-11-13（恐惧模式「计划妥当」）：蛇眼猎的「谋杀」**只要她抽牌就加伤**，不再要求保留在手牌里
            if (Core.DreadMode.IsActive && ActiveVariant(act)?.Key == "SILENT_SNECKO") return true;
            return ActiveVariant(act)?.MurderCountsDrawnCards == true;
        }
        catch { return false; }
    }

    /// <summary>这一幕**实际生效**的「拦掉夜魇复制印牌」开关（变体优先）。</summary>
    public bool BlocksNightmareClonesFor(int act)
    {
        try
        {
            var v = ActiveVariant(act);
            return v != null && v.BlocksNightmareClones;
        }
        catch { return false; }
    }

    /// <summary>这一幕**实际生效**的「拦掉原版抽牌」开关（变体优先，没变体就看主定义）。</summary>
    public bool BlocksVanillaDrawFor(int act)
    {
        try
        {
            var v = ActiveVariant(act);
            return v != null ? v.BlocksVanillaDraw : BlocksVanillaDraw;
        }
        catch { return BlocksVanillaDraw; }
    }

    /// <summary>
    /// 这一幕**变体自带的额外最大生命**（没有变体 / 变体没写就是 0）。
    /// 用户 2026-11-03：三层「事件机」因为带着「大蘑菇」+20（影子玩家收不到拾起钩子 ⇒ 手动加）。
    /// </summary>
    public int VariantHpBonusForAct(int act)
    {
        try { return ActiveVariant(act)?.HpBonus ?? 0; }
        catch { return 0; }
    }
    // ── 设计变体（同一角色同一幕有两套设计时用；目前只有三层「刀猎 / 炼药猎」） ──

    /// <summary>
    /// 第 <paramref name="act"/> 幕当前生效的变体（没有则 null = 用主设计）。
    /// 顺序判定：一幕可以有多套设计（三层故障 = 事件机 / 状态机），取**第一个** `Enabled()` 为真的。
    /// </summary>
    private BossVariant? ActiveVariant(int act)
    {
        // 外部钉死的设计优先（斗蛐蛐 / 控制台）：只认这个 Key，判定函数一次都不问。
        // 找不到 ⇒ 说明钉的是「这一幕的主设计」本身，返回 null 走主定义那条路（行为正确）。
        if (PinnedVariantKey is { Length: > 0 } pinned)
        {
            foreach (var variant in VariantsForAct(act))
            {
                if (string.Equals(variant.Key, pinned, StringComparison.Ordinal)) return variant;
            }
            return null;
        }

        if (VariantsByAct.Length == 0) return null;
        foreach (var variant in VariantsForAct(act))
        {
            try
            {
                if (variant.Enabled()) return variant;
            }
            catch { /* 这一套判定失败 ⇒ 试下一套 */ }
        }
        return null;
    }
}

/// <summary>同一角色同一幕的**备选设计**（目前只有三层静默猎手：刀猎 / 炼药猎）。</summary>
public sealed class BossVariant
{
    public required string Key { get; init; }
    /// <summary>什么时候用这套设计（每帧都会被问，必须便宜且**两端结果一致**）。</summary>
    public required Func<bool> Enabled { get; init; }
    public List<Type>? Deck { get; init; }
    public List<Type>? Relics { get; init; }
    public BossScript? Script { get; init; }
    public string? SpecialLoc { get; init; }

    /// <summary>恐惧模式用的特殊效果文案（为空 = 与正常模式相同）。</summary>
    public string? SpecialLocDread { get; init; }
    public Type? SpecialPower { get; init; }

    /// <summary>恐惧模式用的特殊效果 Power（null = 与正常模式相同）。</summary>
    public Type? SpecialPowerDread { get; init; }
    /// <summary>
    /// 这套设计要不要**拦掉原版的抽牌**（ShadowBossNoDrawPatch）。默认 false = 跟主定义走。
    /// 用户 2026-11-09 的「华丽猎」：剧本把手牌写死了，杂技/肾上腺素那些「抽 N 张」不该再从牌堆里
    /// 抽进新牌（会把剧本手牌冲乱）⇒ 这一套单独打开。
    /// </summary>
    public bool BlocksVanillaDraw { get; init; }

    /// <summary>
    /// 这套设计要不要**拦掉「夜魇」的复制印牌**（NightmarePower 下回合往手牌里塞 N 张复制品）。
    /// 用户 2026-11-09 的「华丽猎」：印出来的「杂技」剧本里已经写好了 ⇒ 不要再凭空多一份。
    /// </summary>
    public bool BlocksNightmareClones { get; init; }

    /// <summary>
    /// 这套设计的「谋杀」（Murder）加伤要不要按**本场她已经拿到过的牌数**起步。
    ///
    /// 默认行为（蛇眼猎那种）是「谋杀一直保留在手里、每次进手才累积」；而「华丽猎」的谋杀
    /// 只在指定回合出现、每回合都是**新建实例** ⇒ 默认逻辑会把起点每次归零、伤害永远很小
    /// （用户 2026-11-09：「她的谋杀没有计算她抽牌的数量而增加伤害（这个和蛇眼猎的不一样）」）。
    /// 打开这条 ⇒ 第一次进手就用 CardsDrawnThisCombat 当起点。
    /// </summary>
    public bool MurderCountsDrawnCards { get; init; }

    public List<Type>? Potions { get; init; }
    public int HealPerPotion { get; init; }
    public int PotionSlots { get; init; }

    /// <summary>
    /// 这套设计**自己的进阶升级表**（非空时**取代**主定义在那一幕的 `AscensionUpgradesByAct` 行）。
    /// 用途：三层铁甲战士的两套设计（易伤战 / **巨石战**）进阶加成完全不同
    /// （易伤战进阶 8 升岩石铠甲 + 护喉甲；巨石战进阶 8 升无惧疼痛 + 奥利哈钢）。
    /// </summary>
    public List<(int MinAscension, Type Card)>? AscensionUpgrades { get; init; }

    /// <summary>这套设计**自己的进阶遗物表**（同上，取代主定义在那一幕的 `AscensionRelicsByAct` 行）。</summary>
    public List<(int MinAscension, Type Relic)>? AscensionRelics { get; init; }

    /// <summary>
    /// ★ 2026-11-13（恐惧模式）：这套设计在**恐惧模式**下额外升级哪些牌（空 = 不额外升级）。
    /// 例：二层「王储」= 升级「王者之拳」。
    /// </summary>
    public List<Type>? DreadUpgrades { get; init; }

    /// <summary>
    /// 这套设计额外的**最大生命加成**（用户 2026-11-03 的三层「事件机」：+20）。
    ///
    /// 那 20 点来自遗物「大蘑菇」（`BigMushroom.AfterObtained` 里抬高最大生命），
    /// 而影子玩家的「拾起时」钩子永远不会触发 ⇒ 只会少那 20 血。写进定义值最省事，
    /// 也避免和「涅奥的祝福」那套按幕加成打架（那个走 <c>BlessingHpBonus</c>）。
    /// </summary>
    public int HpBonus { get; init; }
}
