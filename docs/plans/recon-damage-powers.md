# 侦察报告：伤害预测 与 负面状态（Power）侦测

- 目标程序集：`references/sts2.dll`（9,757,184 bytes）
- 反编译工具：`tools/ilspy-cli`（见 `docs/plans/recon-tooling.md`）
- 原始反编译产物：`docs/plans/recon-out/*.txt`（本次侦察的完整 dump，可复查）
- 本地化真值来源：`.recon/pck/localization/{eng,zhs}/*.json`（已从 `SlayTheSpire2.pck` 解包）
- 名称对照表：`docs/plans/game-object-lookup.tsv`（kind 0=角色 1=卡牌 2=遗物 3=怪物 4=事件）

> 文中所有签名均直接反编译得到，非推测。凡属"由反编译代码推导"的结论都会显式标注【推导】。

---

## 0. 结论速览（TL;DR）

| 需求 | 正确切入点 |
|---|---|
| 算"这张牌现在打出去会造成多少伤害" | `Hook.ModifyDamage(...)`（唯一全局入口）+ `CalculatedDamageVar.UpdateCardPreview` / `DamageVar.UpdateCardPreview` |
| 游戏自己显示卡面伤害数字的代码 | `NCard.UpdateVisuals(PileType, CardPreviewMode)` → `CardModel.UpdateDynamicVarPreview(...)` → `DamageVar/CalculatedDamageVar.UpdateCardPreview(...)` → `Hook.ModifyDamage(..., ModifyDamageHookType.All, previewMode, out _)` |
| 多段攻击 | `AttackCommand._hitCount` / `WithHitCount(int)` / `_damagePerHit` / `_calculatedDamageVar`；最终段数 = `Hook.ModifyAttackHitCount(combatState, attackCommand, _hitCount)` |
| 重复打出 N 次（Replay） | `CardModel.GeneratePlayCount(combatState, target)` = `GetEnchantedReplayCount() + 1` 再过 `Hook.ModifyCardPlayCount`；结果写进 `CardPlay.PlayCount` / `PlayIndex` |
| 充能球 Evoke 伤害 | `OrbModel.EvokeVal`（`LightningOrb.EvokeVal => ModifyOrbValue(8m)`），`Hook.ModifyOrbValue` 是唯一修正点 |
| 状态施加（来源卡 + 数量 + 临时/永久） | `Hook.BeforePowerAmountChanged(combatState, power, amount, target, applier, cardSource)`，配合 `Hook.ModifyPowerAmountGiven/Received` 与 `Hook.AfterPowerAmountChanged` |
| 临时力量 vs 永久力量 | 临时 = `TemporaryStrengthPower`（抽象基类，实现 `ITemporaryPower`）；永久 = `StrengthPower`（`AllowNegative = true`，可负） |
| 卡的稳定 ID | `CardModel.Id` 是 `ModelId(Category, Entry)`；`Id.Entry` 就是本地化 key 前缀 |
| 卡的中文名 | `CardModel.Title`（含升级 `+` 后缀）/ `CardModel.TitleLocString.GetFormattedText()` |
| 枚举所有卡 | `ModelDb.AllCards` |
| 玩家是否有某遗物 | `Player.GetRelic<T>()` / `Player.GetRelicById(ModelId)` / `Player.Relics` |
| `ValueProp` | `[Flags]`，只有 4 个 flag；**没有"攻击"标志**，攻击性靠 `IsPoweredAttack()` 扩展方法判定 |

---

# A. 伤害预测

## A1. `Hook.ModifyDamage` 完整签名与实现

### 完整签名（反编译原文）

```csharp
// MegaCrit.Sts2.Core.Hooks.Hook
public static decimal ModifyDamage(
    IRunState runState,
    ICombatState? combatState,
    Creature? target,
    Creature? dealer,
    decimal damage,
    ValueProp props,
    CardModel? cardSource,
    CardPlay? cardPlay,
    ModifyDamageHookType modifyDamageHookType,
    CardPreviewMode previewMode,
    out System.Collections.Generic.IEnumerable<AbstractModel> modifiers)
```

在 ILSpy 输出中泛型参数被渲染为完整程序集全名（`System.Decimal` / `System.Collections.Generic.IEnumerable\`1[[MegaCrit.Sts2.Core.Models.AbstractModel]]&`），上表为等价 C# 写法。

### 实现要点（`docs/plans/recon-out/hook-modifydamage.txt`）

```csharp
public static decimal ModifyDamage(...)
{
    List<AbstractModel> modifiers2 = new List<AbstractModel>();
    decimal num = damage;
    if (cardSource != null && cardSource.Enchantment != null)
    {
        if (((System.Enum)modifyDamageHookType).HasFlag((System.Enum)ModifyDamageHookType.Additive))
            num += cardSource.Enchantment.EnchantDamageAdditive(num, props);
        if (((System.Enum)modifyDamageHookType).HasFlag((System.Enum)ModifyDamageHookType.Multiplicative))
            num *= cardSource.Enchantment.EnchantDamageMultiplicative(num, props);
    }
    // 若 target == null 且 previewMode == MultiCreatureTargeting，且牌是
    // TargetType 3/4 且牌在 Hand/Play 堆 -> 遍历 combatState.HittableEnemies
    // 逐个 ModifyDamageInternal；只有所有敌人都算出同一数值时才采用该值
    bool flag = target == null && previewMode == CardPreviewMode.MultiCreatureTargeting;
    ...
    if (!flag4 || !flag5)
        num = ModifyDamageInternal(runState, combatState, target, dealer, num,
                                   props, cardSource, cardPlay, modifyDamageHookType, out modifiers2);
    modifiers = (System.Collections.Generic.IEnumerable<AbstractModel>)modifiers2;
    return Math.Max(0m, num);          // ← 最终结果被 clamp 到 >= 0
}
```

`ModifyDamageInternal`（`private static`）是真正跑监听器的地方：

```csharp
private static decimal ModifyDamageInternal(..., decimal damage, ValueProp props,
    CardModel? cardSource, CardPlay? cardPlay, ModifyDamageHookType modifyDamageHookType,
    out List<AbstractModel> modifiers)
{
    decimal num = damage;
    List<AbstractModel> val = new List<AbstractModel>();
    if (modifyDamageHookType.HasFlag(ModifyDamageHookType.Additive))
        foreach (var current in runState.IterateHookListeners(combatState))
        {
            decimal num2 = current.ModifyDamageAdditive(target, num, props, dealer, cardSource, cardPlay);
            num += num2;
            if (num2 != 0m) val.Add(current);
        }
    if (modifyDamageHookType.HasFlag(ModifyDamageHookType.Multiplicative))
        foreach (var current2 in runState.IterateHookListeners(combatState))
        {
            decimal num3 = current2.ModifyDamageMultiplicative(target, num, props, dealer, cardSource, cardPlay);
            num *= num3;
            if (num3 != 1m) val.Add(current2);
        }
    if (modifyDamageHookType.HasFlag(ModifyDamageHookType.Cap))
    {
        decimal num4 = decimal.MaxValue;
        foreach (var current3 in runState.IterateHookListeners(combatState))
        {
            decimal num5 = current3.ModifyDamageCap(target, props, dealer, cardSource, cardPlay);
            if (num5 < num4) { num4 = num5; if (num > num5) { num = num5; val.Add(current3); } }
        }
    }
    modifiers = val;
    return num;
}
```

**关键顺序（对预测很重要）**：Additive 全部 → Multiplicative 全部 → Cap 全部；每次 `num` 都是上一阶段的结果喂给下一阶段。

### 三个配套枚举/结构（反编译原文）

```csharp
// MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType
[Flags]
public enum ModifyDamageHookType
{
    None = 0,
    Additive = 2,
    Multiplicative = 4,
    Cap = 8,
    All = 0xE          // 注意：没有 1，Additive=2 起
}

// MegaCrit.Sts2.Core.Entities.Cards.CardPreviewMode
public enum CardPreviewMode
{
    None,
    Normal,
    Upgrade,
    MultiCreatureTargeting
}

// MegaCrit.Sts2.Core.ValueProps.ValueProp
[Flags]
public enum ValueProp
{
    Unblockable = 2,
    Unpowered = 4,
    Move = 8,
    SkipHurtAnim = 0x10
}
```

### `modifiers` 含义
`IEnumerable<AbstractModel> modifiers` 是**本次真正改动了数值的监听者**（Power/Relic/Enchantment 等），只在 `Additive` 改动 != 0、`Multiplicative` 改动 != 1、`Cap` 实际压低了数值时才加入。`Hook.AfterModifyingDamageAmount(runState, combatState, cardSource, modifiers)` 是配套的后置通知（`hook-members.txt:45`）。

### 调用 `ModifyDamage` 时 `combatState` / `previewMode` 用哪个？
`CalculatedDamageVar` 与 `DamageVar` 的预览实现给出了标准用法（见 A3）：

- `combatState = card.CombatState ?? card.Owner.Creature.CombatState`（`CalculatedDamageVar`）
- `combatState = card.CombatState`（`DamageVar`）
- `previewMode`：手上悬浮 = `CardPreviewMode.Normal`；拖拽选中多目标 = `CardPreviewMode.MultiCreatureTargeting`
- `dealer`：`IsFromOsty ? card.Owner.Osty : card.Owner.Creature`
- `cardPlay = null`（预览阶段没有 CardPlay）

> ⚠️ 注意：预览路径传 `cardPlay = null`，所以任何依赖 `cardPlay.PlayCount` / `X` 值的 hook（例如"第 N 次打出时加伤"）在预览里是拿不到的。

---

## A2. 伤害计算入口点清单

### `MegaCrit.Sts2.Core.Commands` 下与伤害相关的类型

| 类型 | 作用 | 证据 |
|---|---|---|
| `DamageCmd` | 唯一公开的"发起攻击"入口（只 2 个方法） | `recon-out/damagecmd-members.txt` |
| `CreatureCmd.Damage(...)` | 真正结算伤害（8 个重载） | `recon-out/creaturecmd-members.txt` |
| `Builders.AttackCommand` | 攻击构建器（多段/多目标/动画） | `recon-out/attackcommand.txt` |
| `Builders.AttackContext` | 攻击上下文（动画同步） | `types-preview.txt` |

```csharp
// MegaCrit.Sts2.Core.Commands.DamageCmd —— 完整成员（只有这两个）
public static AttackCommand Attack(decimal damagePerHit);
public static AttackCommand Attack(CalculatedDamageVar calculatedDamageVar);
```

```csharp
// MegaCrit.Sts2.Core.Commands.CreatureCmd —— Damage 的 8 个重载（节选，全列表见 dump）
Task<IEnumerable<DamageResult>> Damage(PlayerChoiceContext, Creature target, decimal amount,
        ValueProp props, Creature dealer);
Task<IEnumerable<DamageResult>> Damage(PlayerChoiceContext, Creature target, decimal amount,
        ValueProp props, Creature dealer, CardModel cardSource, CardPlay cardPlay);
Task<IEnumerable<DamageResult>> Damage(PlayerChoiceContext, IEnumerable<Creature> targets,
        decimal amount, ValueProp props, Creature dealer, CardModel cardSource, CardPlay cardPlay);
Task<IEnumerable<DamageResult>> Damage(PlayerChoiceContext, Creature target, DamageVar damageVar,
        Creature dealer, CardModel cardSource, CardPlay cardPlay);
// …共 8 个（target / targets × decimal / DamageVar × 有无 cardSource+cardPlay）
```

### `MegaCrit.Sts2.Core.Combat` / `Entities.Creatures` 相关类型

- `MegaCrit.Sts2.Core.Entities.Creatures.DamageResult`（结算结果）
- `MegaCrit.Sts2.Core.Combat.History.Entries.DamageReceivedEntry`（战斗历史条目）
- `MegaCrit.Sts2.Core.Hooks.Hook` 的 `AfterAttack` / `AfterDamageGiven` / `BeforeDamageReceived` / `AfterDamageReceived`

```csharp
// MegaCrit.Sts2.Core.Entities.Creatures.DamageResult（完整）
public class DamageResult
{
    public Creature Receiver { get; }
    public ValueProp Props { get; }
    public int BlockedDamage { get; set; }
    public int UnblockedDamage { get; init; }
    public int OverkillDamage { get; init; }
    public int TotalDamage => BlockedDamage + UnblockedDamage;
    public bool WasBlockBroken { get; set; }
    public bool WasFullyBlocked { get; set; }
    public bool WasTargetKilled { get; init; }
    public DamageResult(Creature receiver, ValueProp props);
}
```

**注意**：`DamageResult` 只在伤害**已结算**后才有值 —— 它不能用于"结算前预测"。

### 与"预测"直接相关的类型/方法（预览链路）

| 类型 | 成员 | 说明 |
|---|---|---|
| `Localization.DynamicVars.DamageVar` | `UpdateCardPreview(card, previewMode, target, runGlobalHooks)` | 固定数值伤害（如 `new DamageVar(20m, ValueProp.Move)`） |
| `Localization.DynamicVars.CalculatedDamageVar` | `UpdateCardPreview(...)` / `Calculate(Creature? target)` | 计算型伤害（`CalculationBase + CalculationExtra * multiplier`） |
| `Localization.DynamicVars.ExtraDamageVar` / `OstyDamageVar` | — | 额外伤害 / Osty 伤害 |
| `Localization.DynamicVars.CalculatedVar` | `Calculate(Creature? target)` | 基类，`base + extra*multiplier` |
| `Models.CardModel` | `UpdateDynamicVarPreview(previewMode, target, dynamicVarSet)` | 遍历所有 DynamicVar 调 `UpdateCardPreview` |
| `Localization.DynamicVars.DynamicVarSet` | `ClearPreview()` / `Damage` / `CalculatedDamage` / `ExtraDamage` | 卡上所有变量的容器 |
| `Nodes.Cards.NCard` | `UpdateVisuals(PileType, CardPreviewMode)` | UI 层入口 |

`CalculatedVar.Calculate` 原文：

```csharp
public decimal Calculate(Creature? target)
{
    if (_multiplierCalc == null)
        throw new InvalidOperationException("Extra multiplier calc must be specified!");
    CardModel cardModel = (CardModel)_owner;
    decimal num = ((CombatManager.Instance.IsInProgress && cardModel.CombatState != null)
                    ? _multiplierCalc.Invoke(cardModel, target) : 0m);
    return GetBaseVar().BaseValue + GetExtraVar().BaseValue * num;
}
```

注意这个重要细节：**不在战斗中时 multiplier 视为 0**，所以 `Calculate` 只在 combat 里可用。

`DynamicVar` 三个值域：

```csharp
public class DynamicVar : IConvertible
{
    private decimal _baseValue;      // 面板基础值（升级后 = 升级值）
    private decimal _enchantedValue; // 附魔后
    private decimal _previewValue;   // 预览值（跑完 hook 后）
    public decimal BaseValue { get; set; }       // setter 会 ResetToBase()
    public decimal EnchantedValue { get; set; }
    public decimal PreviewValue { get; set; }
    public int IntValue => (int)BaseValue;
    public void ResetToBase() { EnchantedValue = BaseValue; PreviewValue = BaseValue; }
    public virtual void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks) { }
    public void UpgradeValueBy(decimal addend) { BaseValue += addend; WasJustUpgraded = true; }
}
```

---

## A3. 卡在手上时伤害数字是怎么算出来的（完整链路）

这是本次侦察最重要的发现：**游戏已经在棋盘上算好了预测伤害，只是写在 `DynamicVar.PreviewValue` 里**。

### 完整调用链

```
NCard.UpdateVisuals(PileType pileType, CardPreviewMode previewMode)
  └─ Model.DynamicVars.ClearPreview()                                  // 先把所有 PreviewValue 重置为 BaseValue
  └─ Model.UpdateDynamicVarPreview(previewMode, target, Model.DynamicVars)
       └─ foreach (var v in dynamicVarSet.Values)
              v.UpdateCardPreview(model, previewMode, target, runGlobalHooks)
                 ├─ DamageVar.UpdateCardPreview           → PreviewValue = Hook.ModifyDamage(..., ModifyDamageHookType.All, previewMode, out _)
                 └─ CalculatedDamageVar.UpdateCardPreview → PreviewValue = Hook.ModifyDamage(...)
  └─ Model.GetDescriptionForPile(pileType, target) → 把 {Damage:diff()} 替换为 PreviewValue
```

### 逐段证据

**① `NCard.UpdateVisuals`（`recon-out/ncard-updatevisuals.txt`，完整原文）**

```csharp
public void UpdateVisuals(PileType pileType, CardPreviewMode previewMode)
{
    if (!IsNodeReady()) return;
    if (Model == null) throw new InvalidOperationException("Cannot update text with no model.");
    DisplayingPile = pileType;
    Creature target = _previewTarget ?? Model.CurrentTarget;
    UpdatePortrait();
    UpdateTitleLabel();
    UpdateEnergyCostVisuals(pileType);
    UpdateStarCostVisuals(pileType);
    UpdateEnchantmentVisuals();
    Model.DynamicVars.ClearPreview();
    if (!_forceUnpoweredPreview)
    {
        Model.UpdateDynamicVarPreview(previewMode, target, Model.DynamicVars);
        if (Model.Enchantment != null)
        {
            Model.Enchantment.DynamicVars.ClearPreview();
            Model.UpdateDynamicVarPreview(previewMode, target, Model.Enchantment.DynamicVars);
        }
    }
    string text = ((previewMode != CardPreviewMode.Upgrade) ? Model.GetDescriptionForPile(pileType, target)
                                                            : Model.GetDescriptionForUpgradePreview());
    _descriptionLabel.SetTextAutoSize("[center]" + text + "[/center]");
    // ModelVisibility.NotSeen / Locked 分支略
}
```

`NCard` 另有两个关键成员（`recon-out/ncard-members.txt`）：

```csharp
public void SetPreviewTarget(Creature creature);    // ← 悬浮到敌人时由 NTargetManager 调用
public void SetForceUnpoweredPreview(bool forceUnpoweredPreview);
// 私有字段
private Creature _previewTarget;
private bool _forceUnpoweredPreview;
```

调用方（`.recon/t-NCardPlay.txt:272,289`）：

```
CardNode?.UpdateVisuals((Card?.Pile?.Type).GetValueOrDefault(), CardPreviewMode.Normal);
CardNode.UpdateVisuals((CardNode.Model?.Pile?.Type).GetValueOrDefault(), CardPreviewMode.MultiCreatureTargeting);
```

**② `CardModel.UpdateDynamicVarPreview`（`recon-out/cardmodel-preview-methods.txt`）**

```csharp
public void UpdateDynamicVarPreview(CardPreviewMode previewMode, Creature? target, DynamicVarSet dynamicVarSet)
{
    if (RunState == null && CombatState == null) return;
    bool flag = CombatState != null;
    bool flag2 = flag;
    if (flag2)
    {
        bool flag3;
        switch (Pile?.Type)
        {
        case PileType.Hand:
        case PileType.Play:
            flag3 = true; break;
        default:
            flag3 = false; break;
        }
        flag2 = flag3 || UpgradePreviewType == CardUpgradePreviewType.Combat;
    }
    bool runGlobalHooks = flag2;                     // ← 只有手牌/出牌区/战斗内升级预览才跑全局 hook
    var enumerable = Enumerable.ToList<DynamicVar>(dynamicVarSet.Values);
    foreach (var current in enumerable)
        current.UpdateCardPreview(this, previewMode, target, runGlobalHooks);
}
```

**③ `DamageVar.UpdateCardPreview`（`recon-out/damagevar.txt`，完整原文）**

```csharp
public class DamageVar : DynamicVar
{
    public const string defaultName = "Damage";
    public ValueProp Props { get; set; }

    public DamageVar(decimal damage, ValueProp props) : base("Damage", damage) { Props = props; }
    public DamageVar(string name, decimal damage, ValueProp props) : base(name, damage) { Props = props; }

    public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks)
    {
        decimal num = base.BaseValue;
        EnchantmentModel enchantment = card.Enchantment;
        if (enchantment != null)
        {
            num += enchantment.EnchantDamageAdditive(num, Props);
            num *= enchantment.EnchantDamageMultiplicative(num, Props);
            if (!card.IsEnchantmentPreview) base.EnchantedValue = num;
        }
        if (runGlobalHooks)
        {
            num = Hook.ModifyDamage(card.Owner.RunState, card.CombatState, target, card.Owner.Creature,
                                    base.BaseValue, Props, card, null,
                                    ModifyDamageHookType.All, previewMode,
                                    out System.Collections.Generic.IEnumerable<AbstractModel> _);
        }
        base.PreviewValue = num;
    }
}
```

**④ `CalculatedDamageVar.UpdateCardPreview`（`recon-out/calculateddamagevar.txt`，完整原文）**

```csharp
public class CalculatedDamageVar : CalculatedVar
{
    public const string defaultName = "CalculatedDamage";
    public ValueProp Props { get; }
    public bool IsFromOsty { get; private set; }

    public CalculatedDamageVar(ValueProp props) : base("CalculatedDamage") { Props = props; }
    public CalculatedDamageVar FromOsty() { IsFromOsty = true; return this; }

    public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks)
    {
        EnchantmentModel enchantment = card.Enchantment;
        if (enchantment != null)
        {
            decimal baseValue = GetBaseVar().BaseValue;
            baseValue += enchantment.EnchantDamageAdditive(baseValue, Props);
            baseValue *= enchantment.EnchantDamageMultiplicative(baseValue, Props);
            baseValue = Math.Max(baseValue, 0m);
            if (card.IsEnchantmentPreview) base.PreviewValue = baseValue;
            else                          base.EnchantedValue = baseValue;
        }
        decimal num = Calculate(target);
        if (runGlobalHooks)
        {
            ICombatState combatState = card.CombatState ?? card.Owner.Creature.CombatState;
            base.PreviewValue = Hook.ModifyDamage(card.Owner.RunState, combatState, target,
                IsFromOsty ? card.Owner.Osty : card.Owner.Creature,
                num, Props, card, null, ModifyDamageHookType.All, previewMode, out _);
        }
        else if (!card.IsEnchantmentPreview)
        {
            if (enchantment != null)
            {
                num += enchantment.EnchantDamageAdditive(num, Props);
                num *= enchantment.EnchantDamageMultiplicative(num, Props);
            }
            base.PreviewValue = num;
        }
        base.PreviewValue = Math.Max(base.PreviewValue, 0m);
    }

    protected override DynamicVar GetExtraVar() => ((CardModel)_owner).DynamicVars.ExtraDamage;
}
```

### 精确调用方法（mod 应这样做）

【推导】基于上面 4 段源码，等价复刻"卡面数字"的正确写法：

```csharp
// target 为 null 时表示"未指定目标"，此时对 MultiCreatureTargeting 会返回所有敌人一致的数值
decimal PredictCardDamage(CardModel card, Creature? target, CardPreviewMode mode = CardPreviewMode.Normal)
{
    if (!card.DynamicVars.ContainsKey("Damage") && !card.DynamicVars.ContainsKey("CalculatedDamage"))
        return 0m;                                    // 不是伤害牌

    card.DynamicVars.ClearPreview();                  // 与 NCard 一致：先重置
    card.UpdateDynamicVarPreview(mode, target, card.DynamicVars);

    if (card.DynamicVars.ContainsKey("CalculatedDamage"))
        return card.DynamicVars["CalculatedDamage"].PreviewValue;
    return card.DynamicVars["Damage"].PreviewValue;
}
```

或者更直接、绕开 UI 层，自行调 `Hook.ModifyDamage`（只对固定伤害牌适用）：

```csharp
decimal d = Hook.ModifyDamage(
    card.Owner.RunState,
    card.CombatState,
    target,
    card.Owner.Creature,
    card.DynamicVars.Damage.BaseValue,     // 或 EnchantedValue
    card.DynamicVars.Damage.Props,
    card,
    null,
    ModifyDamageHookType.All,
    CardPreviewMode.Normal,
    out _);
```

**必须注意的坑**：

1. `PreviewValue` 会被 `ClearPreview()` 覆写，也可能被 UI 的其他调用改写 —— 读之前先 `ClearPreview()` + `UpdateDynamicVarPreview`，或者把值立刻拷贝出来。
2. `UpdateDynamicVarPreview` 只有在 `CombatState != null` 且牌在 `Hand`/`Play` 堆（或战斗内升级预览）时才 `runGlobalHooks = true`。**如果你的逻辑在"出牌前"跑而牌已经在 Play 堆，仍然有效；在牌库/弃牌堆则不跑 hook。**
3. 这是**单次**伤害。多段攻击要再乘段数（见 A4），多目标要再乘目标数，Replay 要再乘次数（见 A5）。
4. `Hook.ModifyDamage` 内部已 `Math.Max(0m, ...)`，不会为负。
5. 预测用的是"当前战斗状态"，`ModifyDamageMultiplicative` 里的 `VulnerablePower`、`WeakPower`、`StrengthPower` 都会自动生效（它们都实现 `ModifyDamageMultiplicative/Additive` 并检查 `props.IsPoweredAttack()`）。

---

## A4. 多段攻击：`AttackCommand` 骨架

`MegaCrit.Sts2.Core.Commands.Builders.AttackCommand`

### 类骨架（字段 + 属性，反编译原文）

```csharp
public class AttackCommand
{
    // ---- 核心数值字段 ----
    private readonly decimal _damagePerHit;                        // 固定每段伤害
    private readonly CalculatedDamageVar? _calculatedDamageVar;    // 或计算型每段伤害（二选一）
    private int _hitCount = 1;                                     // ★ 段数（默认 1）

    // ---- 目标/来源 ----
    private SourceType _sourceType;                                // enum { Card, Monster, ... }
    private ICombatState? _combatState;                            // 非 null => 全体/随机目标
    private Creature? _singleTarget;                               // 非 null => 单体目标

    // ---- 表现层（与研究无关，略列）----
    private bool _spawnVfxOnEachCreature, _spawnVfxOnCreatureCenter = true;
    private bool _doesRandomTargetingAllowDuplicates = true;
    private bool _shouldPlayAnimation = true;
    private readonly List<List<DamageResult>> _results = new List<List<DamageResult>>();
    private string? _attackerAnimName; private float _attackerAnimDelay;
    private Creature? _visualAttacker; private bool _playOnEveryHit = true;
    private string? _attackerVfx, _attackerSfx, _tmpAttackerSfx;
    private readonly float[] _waitBeforeHit = new float[2] { -1f, -1f };
    private readonly List<Func<Node2D?>> _customAttackerVfxNodes = new();
    private readonly List<Func<Creature, Node2D?>> _customHitVfxNodes = new();
    private Func<Task>? _afterAttackerAnim;
    private Func<Task>? _beforeDamage;

    // ---- 公开属性 ----
    public Creature? Attacker { get; private set; }
    public AbstractModel? ModelSource { get; private set; }        // 卡牌时为 CardModel
    public CardPlay? CardPlay { get; private set; }
    public CombatSide TargetSide { get; private set; }
    public ValueProp DamageProps { get; private set; } = ValueProp.Move;   // ★ 默认 Move
    public bool IsSingleTargeted => _singleTarget != null;
    public bool IsMultiTargeted  => _combatState != null;
    public bool IsRandomlyTargeted { get; private set; }
    public IEnumerable<List<DamageResult>> Results => _results;
    public string? HitSfx { get; private set; }
    public string? TmpHitSfx { get; private set; }
    public string? HitVfx { get; private set; }

    // ---- 构造函数 ----
    public AttackCommand(decimal damagePerHit) { _damagePerHit = damagePerHit; _calculatedDamageVar = null; }
    public AttackCommand(CalculatedDamageVar calculatedDamageVar) { _damagePerHit = -1m; _calculatedDamageVar = calculatedDamageVar; }

    // ---- 构建器（Fluent）----
    public AttackCommand FromCard(CardModel card, CardPlay? cardPlay);
    public AttackCommand FromOsty(Creature osty, CardModel card, CardPlay? cardPlay);
    public AttackCommand FromMonster(MonsterModel monster);
    public AttackCommand Targeting(Creature target);
    public AttackCommand TargetingAllOpponents(ICombatState combatState);
    public AttackCommand TargetingRandomOpponents(ICombatState combatState, bool allowDuplicates = true);
    public AttackCommand Unpowered();
    public AttackCommand WithHitCount(int hitCount);                       // ★ 段数
    public AttackCommand BeforeDamage(Func<Task> beforeDamage);
    public AttackCommand WithAttackerAnim(string animName, float delay, Creature visualAttacker);
    public AttackCommand WithNoAttackerAnim();
    public AttackCommand AfterAttackerAnim(Func<Task> afterAttackerAnim);
    public AttackCommand WithAttackerFx(string vfx, string sfx, string tmpSfx);
    public AttackCommand WithAttackerFx(Func<Node2D?> createAttackerVfx);
    public AttackCommand WithWaitBeforeHit(float fastSeconds, float standardSeconds);
    public AttackCommand WithHitFx(string vfx, string sfx, string tmpSfx);
    public AttackCommand SpawningHitVfxOnEachCreature();
    public AttackCommand WithHitVfxSpawnedAtBase();
    public AttackCommand WithHitVfxNode(Func<Creature, Node2D?> createHitVfxNode);
    public AttackCommand OnlyPlayAnimOnce();

    // ---- 执行 ----
    public static Task<AttackContext> CreateContextAsync(ICombatState, PlayerChoiceContext, CardPlay);
    public async Task<AttackCommand> Execute(PlayerChoiceContext? choiceContext);

    // ---- 内部 ----
    private IReadOnlyList<Creature> GetPossibleTargets();
    public void IncrementHitsInternal();     // _hitCount++
    public void AddResultsInternal(IEnumerable<DamageResult> results);
}
```

### 关键实现片段

```csharp
public AttackCommand WithHitCount(int hitCount) { _hitCount = hitCount; return this; }
public void IncrementHitsInternal() { _hitCount++; }
```

```csharp
public AttackCommand FromCard(CardModel card, CardPlay? cardPlay)
{
    if (Attacker != null) throw new InvalidOperationException("Attacker has already been set.");
    if (ModelSource != null) throw new InvalidOperationException("ModelSource has already been set.");
    if (cardPlay != null && CardPlay != null) throw new InvalidOperationException("CardPlay has already been set.");
    Player owner = card.Owner;
    Attacker = owner.Creature;
    _attackerAnimName = "Attack";
    _attackerAnimDelay = owner.Character.AttackAnimDelay;
    ModelSource = card;
    CardPlay = cardPlay;
    _sourceType = SourceType.Card;
    return this;
}

public AttackCommand Targeting(Creature target)
{
    if (_singleTarget != null) throw new InvalidOperationException("Targets already set.");
    if (_combatState != null) throw new InvalidOperationException("Already set to target opponents of attacker");
    _singleTarget = target;
    TargetSide = target.Side;
    return this;
}

public AttackCommand TargetingAllOpponents(ICombatState combatState)
{
    if (_singleTarget != null) throw new InvalidOperationException("Targets already set.");
    if (_combatState != null) throw new InvalidOperationException("Already set to target opponents of attacker");
    if (Attacker == null) throw new InvalidOperationException("We require an attacker to be able to grab its opponents");
    _combatState = combatState;
    TargetSide = ((Attacker.Side == CombatSide.Enemy) ? CombatSide.Player : CombatSide.Enemy);
    return this;
}
```

### ★ 段数的最终裁决：`Hook.ModifyAttackHitCount`

```csharp
// Execute(PlayerChoiceContext?) 体
await Hook.BeforeAttack(combatState, this);
decimal attackCount = Hook.ModifyAttackHitCount(combatState, this, _hitCount);   // ★
for (int i = 0; decimal.op_Implicit(i) < attackCount; i++)
{
    if (Attacker.IsDead) break;
    List<Creature> validTargets = GetPossibleTargets().Where(c => c.IsAlive).ToList();
    if (validTargets.Count == 0 && combatState.IsLiveCombat()) break;
    ...
    // 每段：
    decimal amount = ((_calculatedDamageVar == null) ? _damagePerHit
                                                     : _calculatedDamageVar.Calculate(singleTarget));
    await CreatureCmd.Damage(choiceContext ?? new BlockingPlayerChoiceContext(), targetList,
                             amount, DamageProps, Attacker, ModelSource as CardModel, CardPlay);
}
CombatManager.Instance.History.CreatureAttacked(combatState, Attacker, _results.SelectMany(r => r).ToList());
await Hook.AfterAttack(combatState, choiceContext ?? new BlockingPlayerChoiceContext(), this);
```

```csharp
// Hook.ModifyAttackHitCount —— 注意它**返回 decimal**（不是 int）
public static decimal ModifyAttackHitCount(ICombatState combatState, AttackCommand attackCommand, int originalHitCount)
{
    int num = originalHitCount;
    foreach (AbstractModel current in IterateCombatHookListeners(combatState))
        num = current.ModifyAttackHitCount(attackCommand, num);
    return decimal.op_Implicit(num);
}
```

`AbstractModel` 上的扩展点：

```csharp
public virtual int ModifyAttackHitCount(AttackCommand attack, int hitCount);   // 默认不改
```

### 预测某张卡"总伤害"的正确公式【推导】

```
总伤害 = Σ_{每次 CardsPlay}  Σ_{每段 hit}  段伤害(card, target)
其中：
  段伤害 = Hook.ModifyDamage(runState, combatState, target, dealer, 基础/计算伤害, props, card, cardPlay, All, previewMode, out _)
  段数   = Hook.ModifyAttackHitCount(combatState, attackCommand, 卡声明的段数)
```

问题在于 **`AttackCommand` 只在 `OnPlay` 里才被构造**，预测阶段拿不到实例。所以实际可行的两种方案：

1. **UI 预览法**（推荐）：`card.UpdateDynamicVarPreview(...)` → `DynamicVars["Damage"].PreviewValue` 得到单段预测伤害；段数从卡自身的 `DynamicVars` 里读（多段卡通常有 `RepeatVar` 或同名 `DamageVar` + `HitCount` 语义，见下），或用 `Hook.BeforeAttack` / `Hook.AfterAttack` 在真实执行时校准。
2. **`Hook.BeforeAttack(combatState, attackCommand)` 拦截法**：这是唯一能拿到已构造好的 `AttackCommand` 的位置（`AttackCommand.Attacker`、`DamageProps`、`_hitCount`（私有，读 `Hook.ModifyAttackHitCount` 之前的 `attackCommand` 只能通过 `ModifyAttackHitCount` 覆盖点读到段数））。段数可通过实现 `AbstractModel.ModifyAttackHitCount(AttackCommand attack, int hitCount)` 读取 `hitCount`。

> ⚠️ 重要限制：`AttackCommand._hitCount` 和 `_damagePerHit` 是 **private**，没有公开 getter。想读段数只能通过 `AbstractModel.ModifyAttackHitCount(attack, hitCount)` 回调的 `hitCount` 参数（或 Harmony 反射）。`attackCommand.DamageProps`、`attackCommand.Attacker`、`attackCommand.ModelSource`、`attackCommand.CardPlay`、`attackCommand.IsSingleTargeted/IsMultiTargeted/IsRandomlyTargeted`、`attackCommand.Results` 是**公开**的。

示例卡（`Mangle`，`recon-out/card-mangle.txt`）展示了典型构造：

```csharp
protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[2]
{
    new DamageVar(20m, ValueProp.Move),
    new DynamicVar("StrengthLoss", 10m)
};

protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
{
    ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
    await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
                  .FromCard(this, cardPlay)
                  .Targeting(cardPlay.Target)
                  .WithAttackerAnim(Ironclad.GetHeavyAnimIfApplicable(Owner.Character),
                                    Ironclad.GetHeavyAttackDelayIfApplicable(Owner.Character))
                  .WithHitFx("vfx/vfx_heavy_blunt", null, "heavy_attack.mp3")
                  .WithHitVfxSpawnedAtBase()
                  .Execute(choiceContext);
    await PowerCmd.Apply<ManglePower>(choiceContext, cardPlay.Target,
                                     DynamicVars["StrengthLoss"].BaseValue, Owner.Creature, this);
}
```

---

## A5. Replay / "打出 N 次"

### 相关成员清单

| 类型 | 成员 | 证据 |
|---|---|---|
| `CardModel` | `public int BaseReplayCount { get; set; }`（setter 触发 `ReplayCountChanged`） | `cardmodel-full.txt:1825` |
| `CardModel` | `public int GetEnchantedReplayCount() => Enchantment?.EnchantPlayCount(BaseReplayCount) ?? BaseReplayCount;` | `cardmodel-full.txt:2755` |
| `CardModel` | `protected async Task<int> GeneratePlayCount(ICombatState combatState, Creature? target)` | `cardmodel-full.txt:3466` |
| `CardModel` | `public int CurrentPlayIndex { get; set; }` | `cardmodel-members.txt` |
| `CardPlay` | `public int PlayIndex { get; init; }` / `public int PlayCount { get; init; }` / `IsFirstInSeries` / `IsLastInSeries` | `cardplay.txt` |
| `Hook` | `public static int ModifyCardPlayCount(ICombatState, CardModel, int playCount, Creature? target, out List<AbstractModel> modifyingModels)` | `hook-power-cardplaycount.txt:74` |
| `Hook` | `public static Task AfterModifyingCardPlayCount(ICombatState, CardModel, IEnumerable<AbstractModel> modifiers)` | `hook-power-cardplaycount.txt:1` |
| `AbstractModel` | `public virtual int ModifyCardPlayCount(CardModel card, Creature target, int playCount)` | `.recon/M-AbstractModel.txt` |
| `AbstractModel` | `public virtual Task AfterModifyingCardPlayCount(CardModel card)` | 同上 |
| `CardModel` | `public event Action ReplayCountChanged` | `cardmodel-full.txt:2518` |

### `GeneratePlayCount` 原文

```csharp
protected async Task<int> GeneratePlayCount(ICombatState combatState, Creature? target)
{
    int playCount = GetEnchantedReplayCount() + 1;                 // ★ Replay 次数 + 1
    playCount = Hook.ModifyCardPlayCount(combatState, this, playCount, target, out List<AbstractModel> modifyingModels);
    await Hook.AfterModifyingCardPlayCount(combatState, this, (IEnumerable<AbstractModel>)modifyingModels);
    return playCount;
}
```

### `Hook.ModifyCardPlayCount` 原文

```csharp
public static int ModifyCardPlayCount(ICombatState combatState, CardModel card, int playCount,
                                      Creature? target, out List<AbstractModel> modifyingModels)
{
    modifyingModels = new List<AbstractModel>();
    int num = playCount;
    foreach (AbstractModel current in IterateCombatHookListeners(combatState))
    {
        int num2 = num;
        num = current.ModifyCardPlayCount(card, target, num);
        if (num != num2) modifyingModels.Add(current);
    }
    return num;
}
```

### `CardPlay` 完整定义

```csharp
public class CardPlay
{
    public CardModel Card { get; init; }
    public Player Player { get; init; }
    public Creature? Target { get; init; }
    public PileType ResultPile { get; init; }
    public ResourceInfo Resources { get; init; }
    public bool IsAutoPlay { get; init; }
    public int PlayIndex { get; init; }              // 本次是第几次（0-based）
    public int PlayCount { get; init; }              // 总共打几次
    public bool IsFirstInSeries => PlayIndex == 0;
    public bool IsLastInSeries  => PlayIndex == PlayCount - 1;
}
```

### mod 如何拿到"这次出牌总共打出几次"

**最重要的事实：`CardPlay.PlayCount` 已经直接携带答案，而且是在效果执行之前填充的。**

`CardModel.OnPlayWrapper` 的执行顺序（`cardmodel-full.txt`）：

```csharp
// 1) 先算 playCount
<playCount>5__4 = await GeneratePlayCount(<combatState>5__2, target);   // 行 1005-1006
...
// 2) 循环每一次 play
if (<i>5__11 < <playCount>5__4 && !CombatManager.Instance.IsOverOrEnding)
{
    cardModel.CurrentPlayIndex = <i>5__11;                             // 行 621
    ...
    // 3) 每次构造 CardPlay（PlayCount/PlayIndex 都填好）
    <cardPlay>5__12 = new CardPlay
    {
        Card = cardModel, Player = cardModel.Owner, Target = target,
        ResultPile = <resultLocation>5__3.pileType, Resources = resources,
        IsAutoPlay = isAutoPlay,
        PlayIndex = <i>5__11,
        PlayCount = <playCount>5__4                                     // ★
    };
    // 4) 然后才通知 hook，再执行 OnPlay
    await Hook.BeforeCardPlayed(<combatState>5__2, <cardPlay>5__12);
    ...
}
```

因此 **`Hook.BeforeCardPlayed(ICombatState combatState, CardPlay cardPlay)` 就是"效果结算前、且已知道总次数"的那个 Hook**：

```csharp
public static Task BeforeCardPlayed(ICombatState combatState, CardPlay cardPlay);
public static Task AfterCardPlayed(ICombatState combatState, PlayerChoiceContext choiceContext, CardPlay cardPlay);
```

对应 `AbstractModel` 上的可覆写虚方法：

```csharp
public virtual Task BeforeCardPlayed(CardPlay cardPlay);
public virtual Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay);
public virtual Task AfterCardPlayedLate(PlayerChoiceContext choiceContext, CardPlay cardPlay);
```

【推导】总伤害 = `预测单段伤害 × 段数 × cardPlay.PlayCount × 目标数`。

> 副作用提示：`PlayIndex` 是每次 play 的序号，所以 `BeforeCardPlayed` 会被调用 **PlayCount 次**。如果 mod 想"整次出牌只判定一次"，应在 `BeforeCardPlayed` 里用 `cardPlay.IsFirstInSeries` 去重，或者改用 `Hook.BeforeAttack`（每次 hit 之前）/ `Hook.AfterCardPlayed`（每次 play 之后，配合 `IsLastInSeries`）。

---

## A6. 充能球（Orb）伤害

### `OrbModel` 关键成员（`recon-out/orbmodel.txt`）

```csharp
public abstract class OrbModel : AbstractModel
{
    public const string locTable = "orbs";

    public abstract decimal PassiveVal { get; }      // 被动触发数值
    public abstract decimal EvokeVal { get; }        // ★ 激发数值
    public abstract Color DarkenedColor { get; }

    public LocString Title       => new LocString("orbs", base.Id.Entry + ".title");
    public LocString Description => new LocString("orbs", base.Id.Entry + ".description");
    public CompressedTexture2D Icon => PreloadManager.Cache.GetCompressedTexture2D(IconPath);

    public Player Owner { get; set; }
    public ICombatState CombatState => Owner.Creature.CombatState;
    public override bool ShouldReceiveCombatHooks => true;

    public event Action PassiveActivated;
    public event Action<Creature[]>? EvokeActivated;

    public static OrbModel GetRandomOrb(Rng rng);
    public OrbModel ToMutable(int initialAmount = 0);

    public virtual Task BeforeTurnEndOrbTrigger(PlayerChoiceContext choiceContext);
    public virtual Task AfterTurnStartOrbTrigger(PlayerChoiceContext choiceContext);
    public Task TriggerPassive(PlayerChoiceContext choiceContext, Creature? target);
    public virtual Task Passive(PlayerChoiceContext choiceContext, Creature? target);
    public virtual Task<IEnumerable<Creature>> Evoke(PlayerChoiceContext playerChoiceContext);

    protected decimal ModifyOrbValue(decimal result);      // ★ 唯一数值修正入口
    protected void ActivatePassive();
    public void ActivateEvoke(Creature[] targets);
    public void PlayChannelSfx();
    public Node2D CreateSprite();
    public void RemoveInternal();

    // 合法球列表（硬编码）
    private static readonly ModelId[] _validOrbs = new ModelId[5]
    {
        ModelDb.GetId<LightningOrb>(), ModelDb.GetId<FrostOrb>(), ModelDb.GetId<DarkOrb>(),
        ModelDb.GetId<PlasmaOrb>(),    ModelDb.GetId<GlassOrb>()
    };
}
```

```csharp
protected decimal ModifyOrbValue(decimal result)
{
    return Hook.ModifyOrbValue(Owner.Creature.CombatState, this, result);
}
```

### `Hook.ModifyOrbValue` / `ModifyOrbPassiveTriggerCount` 原文

```csharp
public static decimal ModifyOrbValue(ICombatState combatState, OrbModel orb, decimal amount)
{
    decimal num = amount;
    foreach (AbstractModel current in IterateCombatHookListeners(combatState))
        num = current.ModifyOrbValue(orb, num);
    return num;
}

public static int ModifyOrbPassiveTriggerCount(ICombatState combatState, OrbModel orb, int triggerCount,
                                               out List<AbstractModel> modifyingModels)
{
    modifyingModels = new List<AbstractModel>();
    int num = triggerCount;
    foreach (AbstractModel current in IterateCombatHookListeners(combatState))
    {
        int num2 = num;
        num = current.ModifyOrbPassiveTriggerCounts(orb, num);
        if (num != num2) modifyingModels.Add(current);
    }
    return num;
}

public static Task AfterModifyingOrbPassiveTriggerCount(ICombatState combatState, OrbModel orb,
                                                        IEnumerable<AbstractModel> modifiers);
```

### `LightningOrb` —— 展示 Evoke 伤害如何构成（完整原文摘录）

```csharp
public class LightningOrb : OrbModel
{
    public override decimal PassiveVal => ModifyOrbValue(3m);   // ★ 被动 3
    public override decimal EvokeVal   => ModifyOrbValue(8m);   // ★ 激发 8

    public override Task BeforeTurnEndOrbTrigger(PlayerChoiceContext choiceContext)
        => TriggerPassive(choiceContext, null);

    public override Task Passive(PlayerChoiceContext choiceContext, Creature? target)
        => ApplyLightningDamage(PassiveVal, target, choiceContext, isEvoke: false);

    public override async Task<IEnumerable<Creature>> Evoke(PlayerChoiceContext playerChoiceContext)
        => await ApplyLightningDamage(EvokeVal, null, playerChoiceContext, isEvoke: true);   // ★

    private async Task<IEnumerable<Creature>> ApplyLightningDamage(decimal value, Creature? target,
                                                                  PlayerChoiceContext choiceContext, bool isEvoke)
    {
        List<Creature> val = CombatState.GetOpponentsOf(Owner.Creature).Where(e => e.IsHittable).ToList();
        if (val.Count == 0) return Array.Empty<Creature>();
        IReadOnlyList<Creature> targets = (target == null)
            ? new <>z__ReadOnlySingleElementList<Creature>(Owner.RunState.Rng.CombatTargets.NextItem(val))
            : new <>z__ReadOnlySingleElementList<Creature>(target);
        if (isEvoke) ActivateEvoke(targets.ToArray());
        foreach (Creature current in targets) VfxCmd.PlayOnCreature(current, "vfx/vfx_attack_lightning");
        PlayEvokeSfx();
        await CreatureCmd.Damage(choiceContext, targets, value, ValueProp.Unpowered, Owner.Creature);   // ★
        return targets;
    }
}
```

### Orb 与其他系统

```csharp
// MegaCrit.Sts2.Core.Commands.OrbCmd（静态）
public static Task Channel<T>(PlayerChoiceContext choiceContext, Player player) where T : OrbModel;
public static Task Channel(PlayerChoiceContext choiceContext, OrbModel orb, Player player);
// （另有 Channel 的变体，见 recon-out/orbcmd.txt）

// 球的容器（在 PlayerCombatState 上）
// MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState
public OrbQueue OrbQueue { get; }
// MegaCrit.Sts2.Core.Entities.Orbs.OrbQueue
public IReadOnlyList<OrbModel> Orbs { get; }
public int Capacity { get; set; }
public Task<bool> TryEnqueue(OrbModel orb);
public bool Remove(OrbModel orb);
public void Insert(int idx, OrbModel orb);
public void AddCapacity(int capacity);
public void RemoveCapacity(int capacity);
public void Clear();
public Task BeforeTurnEnd(PlayerChoiceContext choiceContext);
public Task AfterTurnStart(PlayerChoiceContext choiceContext);

// Hook 侧
public static Task AfterOrbEvoked(PlayerChoiceContext choiceContext, ICombatState combatState,
                                  OrbModel orb, IEnumerable<Creature> targets);
public static Task AfterOrbChanneled(ICombatState combatState, PlayerChoiceContext choiceContext,
                                     Player player, OrbModel orb);
// AbstractModel 侧
public virtual Task AfterOrbEvoked(PlayerChoiceContext choiceContext, OrbModel orb, IEnumerable<Creature> targets);
```

### 计算某球 Evoke 会造成多少伤害【推导】

```csharp
// orb 必须是 mutable 实例（有 Owner）；canonical 实例的 Owner 为 null，会抛异常
decimal PredictOrbEvokeDamage(OrbModel orb)
{
    decimal v = orb.EvokeVal;      // 已经过 Hook.ModifyOrbValue
    // 但 LightningOrb/DarkOrb 的 Evoke 用 ValueProp.Unpowered 打伤害
    // => 不受 Strength/Vulnerable 影响（见 B12 的 IsPoweredAttack 判定）
    return v;
}
```

**关键点**：

- `EvokeVal` / `PassiveVal` 已是 **`ModifyOrbValue` 之后**的值（`Hook.ModifyOrbValue` 会被 `Focus` 类 power 调用）。
- 球的最终伤害 **不走 `Hook.ModifyDamage` 的 Additive/Multiplicative 阶段**，因为它用 `ValueProp.Unpowered` 调 `CreatureCmd.Damage`；`StrengthPower.ModifyDamageAdditive` 和 `VulnerablePower.ModifyDamageMultiplicative` 都以 `props.IsPoweredAttack()` 为前置条件（见 B12），`Unpowered` 会直接返回 0 / 1。
- `DarkOrb`（`recon-out/darkorb.txt`）的 `EvokeVal` 是**累积值**，不是常量，且只打"最弱敌人"：

```csharp
public class DarkOrb : OrbModel
{
    private decimal _evokeVal;
    public override decimal PassiveVal => ModifyOrbValue(6m);
    public override decimal EvokeVal   => _evokeVal;          // ★ 不是常量，而是累积的充能值

    // 每次被动触发都会给 _evokeVal 加一次 PassiveVal（即 +6，且已过 ModifyOrbValue）
    // 行 197: _evokeVal += PassiveVal;

    public override async Task<IEnumerable<Creature>> Evoke(PlayerChoiceContext playerChoiceContext)
    {
        Creature weakestEnemy = /* 选最弱敌人 */;
        ActivateEvoke(new Creature[1] { weakestEnemy });
        await CreatureCmd.Damage(playerChoiceContext, weakestEnemy, EvokeVal, ValueProp.Unpowered,
                                 base.Owner.Creature);
    }
}
```

  所以对 `DarkOrb`，预测前必须先读 `EvokeVal`（实例属性），而不是猜测数值。
- **`FrostOrb` 的 Evoke 不给伤害而给格挡**，所以"球伤害"只对 `LightningOrb` / `DarkOrb` / `GlassOrb` 有意义。`PlasmaOrb` 的 Evoke 给能量与充能。
- 所有球都必须乘 `Hook.ModifyOrbPassiveTriggerCount(...)` 得到的触发次数（被动触发用）。

---

# B. Power / 状态效果

## B7. `PowerModel` 子类枚举 & 目标状态的确切类型名

### `PowerModel` 抽象基类

```csharp
// MegaCrit.Sts2.Core.Models.PowerModel : AbstractModel
public abstract class PowerModel : AbstractModel
{
    public const string locTable = "powers";

    public virtual LocString Title       => new LocString("powers", base.Id.Entry + ".title");
    public virtual LocString Description => new LocString("powers", base.Id.Entry + ".description");
    public LocString SmartDescription { get; }      // 若存在 "<ENTRY>.smartDescription" 则用之
    public LocString RemoteDescription { get; }

    public int Amount { get; set; }                 // ★ 见 B8
    public int AmountOnTurnStart { get; set; }
    public virtual int DisplayAmount => Amount;
    public virtual Color AmountLabelColor { get; }

    public abstract PowerType Type { get; }         // Buff / Debuff
    public abstract PowerStackType StackType { get; }   // None / Counter / Single
    public virtual bool AllowNegative => false;     // ★ StrengthPower 覆写为 true
    public virtual PowerInstanceType InstanceType => PowerInstanceType.None;   // None / Instanced / InstancedPerApplier
    public virtual bool ShouldScaleInMultiplayer => false;
    public DynamicVarSet DynamicVars { get; }       // 需要先 InitializeWithOwner，见 PowerModel 内部
    public PowerType TypeForCurrentAmount => GetTypeForAmount((decimal)Amount);
    public bool SkipNextDurationTick { get; set; }  // 玩家身上新上的 debuff 会被置 true
    public virtual bool IsVisible => ...;           // 默认 true
    protected virtual bool IsVisibleInternal => true;

    public Creature Owner { get; set; }             // 拥有者；切换 owner 会抛异常
    public ICombatState CombatState => Owner.CombatState;
    public Creature? Applier { get; set; }
    public Creature? Target { get; set; }
    public override bool ShouldReceiveCombatHooks => true;

    public void SetAmount(int amount, bool silent = false);          // clamp ±999999999
    public void ApplyInternal(Creature owner, decimal amount, bool silent = false);
    public void RemoveInternal();
    public PowerModel ToMutable(int initialAmount = 0);
    public bool ShouldRemoveDueToAmount();
    public PowerType GetTypeForAmount(decimal customAmount);
    public virtual decimal GetScaledAmountForMultiplayer(ICombatState, Creature? applier, decimal amount,
                                                         Creature target, CardModel? cardSource);

    public virtual Task BeforeApplied(Creature target, decimal amount, Creature? applier, CardModel? cardSource);
    public virtual Task AfterApplied(Creature? applier, CardModel? cardSource);
    public virtual Task AfterRemoved(Creature oldOwner);
    public virtual bool ShouldPowerBeRemovedAfterOwnerDeath();
    public virtual bool ShouldOwnerDeathTriggerFatal();
}
```

```csharp
// MegaCrit.Sts2.Core.Entities.Powers
public enum PowerType     { None, Buff, Debuff }
public enum PowerStackType{ None, Counter, Single }
public enum PowerInstanceType { None, Instanced, InstancedPerApplier }
```

### 目标状态的精确类型名

| 中文名 | 类型全名 | `Id.Entry`（= 本地化 key 前缀） | 英文标题 | 验证 |
|---|---|---|---|---|
| 易伤 | `MegaCrit.Sts2.Core.Models.Powers.VulnerablePower` | `VULNERABLE_POWER` | `"Vulnerable"` | `eng/powers.json` → `"VULNERABLE_POWER.title": "Vulnerable"`；`zhs/powers.json` → `"易伤"` |
| 虚弱 | `MegaCrit.Sts2.Core.Models.Powers.WeakPower` | `WEAK_POWER` | `"Weak"` | `eng/powers.json`；`zhs` → `"虚弱"` |
| 力量 | `MegaCrit.Sts2.Core.Models.Powers.StrengthPower` | `STRENGTH_POWER` | `"Strength"` | `eng/powers.json`；`zhs` → `"力量"`，描述 `"{Amount:cond:<0?减少|增加}攻击牌造成的伤害值[blue]{Amount:abs()}[/blue]点。"` |
| 临时力量 | `MegaCrit.Sts2.Core.Models.Powers.TemporaryStrengthPower`（**abstract**） | —（子类各自有 Entry，但 `Description` 固定用 `TEMPORARY_STRENGTH_POWER` / `TEMPORARY_STRENGTH_DOWN`） | 见 B9 | `temporarystrengthpower.txt` |
| 灾厄 | `MegaCrit.Sts2.Core.Models.Powers.DoomPower` | `DOOM_POWER` | `"Doom"` | `eng/powers.json`；`zhs` → `"灾厄"`，描述含"如果其灾厄值等于或大于其生命，则其直接死亡" |
| 中毒 | `MegaCrit.Sts2.Core.Models.Powers.PoisonPower` | `POISON_POWER` | `"Poison"` | `eng/powers.json`；`zhs` → `"中毒"` |

### 如何识别一个 power？（三种等价的稳定标识）

```csharp
// (1) CLR 类型（最可靠，编译期检查）
if (power is VulnerablePower) { ... }

// (2) ModelId（稳定、可序列化、跨版本友好）
ModelId id = power.Id;                        // 例："POWER.VULNERABLE_POWER"
if (id == ModelDb.GetId<VulnerablePower>()) { ... }
if (power.Id.Entry == "VULNERABLE_POWER") { ... }
if (power.Id.Category == "POWER") { ... }

// (3) 本地化 key
new LocString("powers", power.Id.Entry + ".title").GetFormattedText();   // → "易伤"
```

### `ModelId` 定义（`recon-out/modelid.txt`，完整原文节选）

```csharp
public record ModelId : IComparable<ModelId>
{
    public string Category { get; }
    public string Entry { get; }
    public static readonly ModelId none = new ModelId("NONE", "NONE");
    private const string _bannedSuffix = "_MODEL";

    public ModelId(string category, string entry)
    {
        if (category.EndsWith("_MODEL")) throw new ArgumentException("Category cannot end with '_MODEL'.", "category");
        Category = category; Entry = entry;
    }

    public static ModelId Deserialize(string json)      // "CARD.MANGLE"
    {
        string[] array = json.Split('.', (StringSplitOptions)0);
        if (array.Length != 2) throw new JsonException("'" + json + "' does not match the expected ModelId form.");
        return new ModelId(array[0], array[1]);
    }
    public override string ToString() => Category + "." + Entry;
}
```

### 【推导】Category 字符串是怎么来的

```csharp
// ModelDb（recon-out/modeldb.txt:461-489）
public static ModelId GetId<T>() where T : AbstractModel => GetId(typeof(T));
public static ModelId GetId(System.Type type) => new ModelId(GetCategory(type), GetEntry(type));

public static System.Type GetCategoryType(System.Type type)
{
    System.Type type2 = type;
    while (type2.BaseType != typeof(AbstractModel)) type2 = type2.BaseType;
    return type2;
}
public static string GetCategory(System.Type type) => ModelId.SlugifyCategory(GetCategoryType(type).Name);
public static string GetEntry(System.Type type)    => StringHelper.Slugify(type.Name);

// ModelId.SlugifyCategory
public static string SlugifyCategory(string category)
{
    string text = StringHelper.Slugify(category);       // "CardModel" -> "CARD_MODEL"
    if (text.EndsWith("_MODEL")) text = text.Substring(0, text.Length - "_MODEL".Length);   // -> "CARD"
    return text;
}

// StringHelper.Slugify
public static string Slugify(string txt)
{
    string text  = CamelCaseRegex().Replace(txt.Trim(), "$1_$2");   // ([A-Za-z0-9]|\G(?!^))([A-Z])
    string text2 = WhitespaceRegex().Replace(text.ToUpperInvariant(), "_");
    return ...;
}
```

因此：

| 基类 | Category |
|---|---|
| `CardModel` | `CARD` |
| `PowerModel` | `POWER` |
| `RelicModel` | `RELIC` |
| `OrbModel` | `ORB` |
| `MonsterModel` | `MONSTER` |
| `PotionModel` | `POTION` |

**对 Power 的 `Id.Entry` 可以直接用本地化文件反查**（`powers.json` 里所有 key 都是 `<ENTRY>.title`）。同理 `cards.json` / `relics.json` / `orbs.json`。

Power 完整清单可以用 `recon-out/types-powers-ns2.txt`（633 行，`MegaCrit.Sts2.Core.Models.Powers.*` 全部类型），或运行时用：

```csharp
// ModelDb（recon-out/modeldb.txt:197）
public static IEnumerable<PowerModel> AllPowers =>
    AllAbstractModelSubtypes.Where(t => t.IsSubclassOf(typeof(PowerModel))).Select(t => (PowerModel)Get(t));
```

### 用于卡牌数值的 `PowerVar<T>` 与 `DynamicVarSet` 快捷属性

```csharp
// MegaCrit.Sts2.Core.Localization.DynamicVars.PowerVar<T : PowerModel> : DynamicVar
public PowerVar(decimal powerAmount);
public PowerVar(string name, decimal powerAmount);
public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks);
// 键名 = typeof(T).Name，即 "VulnerablePower" / "StrengthPower" / "PoisonPower" / "DoomPower" / "DexterityPower"
```

```csharp
// DynamicVarSet 的公开索引器属性（recon-out/dynamicvarset.txt）
public BlockVar            Block            => (BlockVar)_vars["Block"];
public CalculatedBlockVar  CalculatedBlock  => (CalculatedBlockVar)_vars["CalculatedBlock"];
public CalculatedDamageVar CalculatedDamage => (CalculatedDamageVar)_vars["CalculatedDamage"];
public DamageVar           Damage           => (DamageVar)_vars["Damage"];
public PowerVar<DexterityPower>  Dexterity  => (PowerVar<DexterityPower>)_vars["DexterityPower"];
public PowerVar<DoomPower>       Doom       => (PowerVar<DoomPower>)_vars["DoomPower"];
public PowerVar<PoisonPower>     Poison     => (PowerVar<PoisonPower>)_vars["PoisonPower"];
public PowerVar<StrengthPower>   Strength   => (PowerVar<StrengthPower>)_vars["StrengthPower"];
public PowerVar<VulnerablePower> Vulnerable => (PowerVar<VulnerablePower>)_vars["VulnerablePower"];
public PowerVar<WeakPower>       Weak       => (PowerVar<WeakPower>)_vars["WeakPower"];
public ExtraDamageVar ExtraDamage; public HealVar Heal; public HpLossVar HpLoss;
public CardsVar Cards; public EnergyVar Energy; public RepeatVar Repeat; public StarsVar Stars;
public SummonVar Summon; public ForgeVar Forge; public GoldVar Gold; public MaxHpVar MaxHp;
public OstyDamageVar OstyDamage; public IfUpgradedVar IfUpgraded;
public DynamicVar this[string key] => _vars[key];      // 通用访问，如 _vars["StrengthLoss"]
public bool ContainsKey(string key);
public bool TryGetValue(string key, out DynamicVar value);
```

> 注意：`DynamicVarSet.Vulnerable` 等快捷属性是**强制类型转换**（`(PowerVar<VulnerablePower>)_vars[...]`），若卡上没有该变量会抛 `KeyNotFoundException`。**必须先用 `ContainsKey("VulnerablePower")` 判断**。
> 临时力量失去用的是**自定义 key**，例如 `Mangle` 用 `"StrengthLoss"`（`private const string _strengthLossKey = "StrengthLoss";`）。

---

## B8. Power 数量是如何施加到 creature 上的

### `Hook` 侧的 4 个方法（完整签名 + 实现）

```csharp
// ① 前置通知：在任何修正之前，拿到"原始"偏移量
public static System.Threading.Tasks.Task BeforePowerAmountChanged(
    ICombatState combatState, PowerModel power, decimal amount,
    Creature target, Creature? applier, CardModel? cardSource);

// ② 给出方修正（叠加式：加法后乘法）
public static decimal ModifyPowerAmountGiven(
    ICombatState combatState, PowerModel power, Creature giver, decimal amount,
    Creature? target, CardModel? cardSource, out IEnumerable<AbstractModel> modifiers);

// ③ 接收方修正（覆盖式：TryModifyPowerAmountReceived 返回 bool + out decimal）
public static decimal ModifyPowerAmountReceived(
    ICombatState combatState, PowerModel canonicalPower, Creature target, decimal amount,
    Creature? giver, out IEnumerable<AbstractModel> modifiers);

// ④ 后置通知：拿到最终修正后的偏移量
public static System.Threading.Tasks.Task AfterPowerAmountChanged(
    ICombatState combatState, PlayerChoiceContext choiceContext, PowerModel power,
    decimal amount, Creature? applier, CardModel? cardSource);

// ⑤ 修饰者通知（仅当 modifiers 非空/非默认时）
public static Task AfterModifyingPowerAmountGiven(ICombatState, IEnumerable<AbstractModel> modifiers, PowerModel modifiedPower);
public static Task AfterModifyingPowerAmountReceived(ICombatState, IEnumerable<AbstractModel> modifiers, PowerModel modifiedPower);
```

**★ 关键：`ModifyPowerAmountGiven` 里 `amount` 参数会随前一个 listener 改变，而 `modifiers` 用来记录"谁改了"**：

```csharp
public static decimal ModifyPowerAmountGiven(ICombatState combatState, PowerModel power, Creature giver,
                                             decimal amount, Creature? target, CardModel? cardSource,
                                             out System.Collections.Generic.IEnumerable<AbstractModel> modifiers)
{
    decimal num = amount;
    List<AbstractModel> val = new List<AbstractModel>();
    foreach (AbstractModel current in IterateCombatHookListeners(combatState))          // 第一遍：加法
    {
        decimal num2 = current.ModifyPowerAmountGivenAdditive(power, giver, num, target, cardSource);
        num += num2;
        if (num2 != 0m) val.Add(current);
    }
    foreach (AbstractModel current2 in IterateCombatHookListeners(combatState))         // 第二遍：乘法
    {
        decimal num3 = current2.ModifyPowerAmountGivenMultiplicative(power, giver, num, target, cardSource);
        num *= num3;
        if (num3 != 1m) val.Add(current2);
    }
    modifiers = val;
    return num;
}
```

```csharp
public static decimal ModifyPowerAmountReceived(ICombatState combatState, PowerModel canonicalPower,
                                                Creature target, decimal amount, Creature? giver,
                                                out IEnumerable<AbstractModel> modifiers)
{
    decimal num = amount;
    List<AbstractModel> val = new List<AbstractModel>();
    foreach (AbstractModel current in IterateCombatHookListeners(combatState))
    {
        if (current.TryModifyPowerAmountReceived(canonicalPower, target, num, giver, out var modifiedAmount))
        {
            num = modifiedAmount;
            val.Add(current);
        }
    }
    modifiers = val;
    return num;
}
```

`BeforePowerAmountChanged` / `AfterPowerAmountChanged` 都由 `AsyncStateMachine` 承载（`<BeforePowerAmountChanged>d__63` / `<AfterPowerAmountChanged>d__64`），签名与参数顺序如上。

`AbstractModel` 上对应的可覆写虚方法（`.recon/M-AbstractModel.txt`）：

```csharp
public virtual decimal ModifyPowerAmountGivenAdditive(PowerModel power, Creature giver, decimal amount,
                                                      Creature target, CardModel cardSource);
public virtual decimal ModifyPowerAmountGivenMultiplicative(PowerModel power, Creature giver, decimal amount,
                                                            Creature target, CardModel cardSource);
public virtual bool TryModifyPowerAmountReceived(PowerModel canonicalPower, Creature target, decimal amount,
                                                 Creature applier, out decimal modifiedAmount);
public virtual Task BeforePowerAmountChanged(PowerModel power, decimal amount, Creature target,
                                             Creature applier, CardModel cardSource);
public virtual Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount,
                                            Creature applier, CardModel cardSource);
public virtual Task AfterModifyingPowerAmountGiven(PowerModel power);
public virtual Task AfterModifyingPowerAmountReceived(PowerModel power);
```

### 完整执行流程（`PowerCmd.Apply` 两个分支，反编译 `powercmd-full.txt`）

**入口（4 个重载）**：

```csharp
public static async Task<IReadOnlyList<T>> Apply<T>(PlayerChoiceContext choiceContext,
    IEnumerable<Creature>? targets, decimal amount, Creature? applier, CardModel? cardSource, bool silent = false)
    where T : PowerModel;

public static async Task<T?> Apply<T>(PlayerChoiceContext choiceContext, Creature target,
    decimal amount, Creature? applier, CardModel? cardSource, bool silent = false) where T : PowerModel;

public static Task Apply(PlayerChoiceContext choiceContext, PowerModel power, Creature target,
    decimal amount, Creature? applier, CardModel? cardSource, bool silent = false);

public static async Task<int> ModifyAmount(PlayerChoiceContext choiceContext, PowerModel power, decimal offset,
    Creature? applier, CardModel? cardSource, bool silent = false);
```

**`Apply<T>` 分流**：

```csharp
public static async Task<T?> Apply<T>(PlayerChoiceContext choiceContext, Creature target, decimal amount,
                                      Creature? applier, CardModel? cardSource, bool silent = false) where T : PowerModel
{
    if (CombatManager.Instance.IsEnding) return null;
    if (!target.CanReceivePowers) return null;
    PowerModel powerModel = ModelDb.Power<T>();                       // canonical 实例
    PowerModel power = FindExistingInstanceForStacking(powerModel, target, applier);
    if (power == null)
    {
        power = powerModel.ToMutable();
        await Apply(choiceContext, power, target, amount, applier, cardSource, silent);   // ← 分支 A：首次施加
    }
    else if (await ModifyAmount(choiceContext, power, amount, applier, cardSource, silent) == 0)
    {
        power = null;                                                  // ← 分支 B：叠加
    }
    return power as T;
}
```

**分支 B（已存在同名 power → 叠加）：`PowerCmd.ModifyAmount`（完整原文）**

```csharp
public static async Task<int> ModifyAmount(PlayerChoiceContext choiceContext, PowerModel power, decimal offset,
                                           Creature? applier, CardModel? cardSource, bool silent = false)
{
    if (CombatManager.Instance.IsEnding) return 0;
    Creature owner = power.Owner;
    ICombatState combatState = owner.CombatState;
    if (combatState == null) return 0;

    await Hook.BeforePowerAmountChanged(combatState, power, offset, owner, applier, cardSource);   // ①

    decimal modifiedOffset = offset;
    System.Collections.Generic.IEnumerable<AbstractModel> modifiers = null;
    if (applier != null && combatState.ContainsCreature(applier))
        modifiedOffset = Hook.ModifyPowerAmountGiven(combatState, power, applier, modifiedOffset,
                                                     owner, cardSource, out modifiers);           // ②
    modifiedOffset = Hook.ModifyPowerAmountReceived(combatState, power, owner, modifiedOffset, applier,
                                                    out IEnumerable<AbstractModel> receivedModifiers); // ③

    CombatManager.Instance.History.PowerReceived(combatState, power, modifiedOffset, applier);

    int newAmount = power.Amount + (int)modifiedOffset;
    power.SetAmount(newAmount, silent);                                                            // ④
    if (modifiers != null) await Hook.AfterModifyingPowerAmountGiven(combatState, modifiers, power);
    await Hook.AfterModifyingPowerAmountReceived(combatState, receivedModifiers, power);
    if ((int)modifiedOffset != 0)
        await Hook.AfterPowerAmountChanged(combatState, choiceContext, power, modifiedOffset, applier, cardSource); // ⑤
    if (power.ShouldRemoveDueToAmount()) await Remove(power);
    // … UI 刷新 / Intent 更新 / Cmd.CustomScaledWait 略
    return newAmount;
}
```

**分支 A（首次施加，状态机 `<Apply>d__2` 的 MoveNext，反编译还原）**

```csharp
// PowerCmd.Apply(choiceContext, power, target, amount, applier, cardSource, silent)
if (!CombatManager.Instance.IsEnding && !(amount == 0m) && target.CanReceivePowers)
{
    combatState = target.CombatState;
    if (combatState != null)
    {
        PowerModel powerModel = FindExistingInstanceForStacking(power, target, applier);
        if (powerModel != null)
        {
            await ModifyAmount(choiceContext, powerModel, amount, applier, cardSource);   // → 走分支 B
            return;
        }

        power.AssertMutable();
        power.Applier = applier;                                                          // ← applier 在这里被写入
        await Hook.BeforePowerAmountChanged(combatState, power, amount, target, applier, cardSource);  // ①
        // （此处未调 ModifyPowerAmountGiven 之前的 SetAmount；新 power 走下面：）
        modifiedAmount = amount;
        givenModifiers = null;
        if (applier != null && combatState.ContainsCreature(applier))
            modifiedAmount = Hook.ModifyPowerAmountGiven(combatState, power, applier, modifiedAmount,
                                                         target, cardSource, out givenModifiers);       // ②
        modifiedAmount = Hook.ModifyPowerAmountReceived(combatState, power, target, modifiedAmount,
                                                        applier, out receivedModifiers);               // ③
        if (combatState.Players.Count > 1 && (target.IsPrimaryEnemy || target.IsSecondaryEnemy)
            && power.ShouldScaleInMultiplayer)
            modifiedAmount = power.GetScaledAmountForMultiplayer(combatState, applier, modifiedAmount, target, cardSource);

        await power.BeforeApplied(target, modifiedAmount, applier, cardSource);                         // ④
        if (target.CanReceivePowers)
        {
            power.ApplyInternal(target, modifiedAmount, silent);      // 内部：Owner=target; SetAmount; Owner.ApplyPowerInternal
            if (modifiedAmount != 0m)
                CombatManager.Instance.History.PowerReceived(combatState, power, modifiedAmount, applier);
            // UI wait…
        }
        if (target.Side == CombatSide.Player && power.Type == PowerType.Debuff)
            power.SkipNextDurationTick = true;                        // 玩家身上新上的 debuff 当回合不 tick
        if (givenModifiers != null)
            await Hook.AfterModifyingPowerAmountGiven(combatState, givenModifiers, power);
        await Hook.AfterModifyingPowerAmountReceived(combatState, receivedModifiers, power);            // ⑤
        if (modifiedAmount != 0m)
            await power.AfterApplied(applier, cardSource);
        await Hook.AfterPowerAmountChanged(combatState, choiceContext, power, modifiedAmount, applier, cardSource); // ⑥
    }
}
```

### 总结：mod 应该挂哪个点

| 想要什么 | 用哪个 | 为什么 |
|---|---|---|
| **来源卡 + 原始数量 + 目标 + 施加者** | `Hook.BeforePowerAmountChanged(combatState, power, amount, target, applier, cardSource)` | ① 两个分支都先调它；② `amount` 是**未经 ×2 等修正的原始数值**；③ `cardSource` 就是来源卡；④ `power.Amount` 此时是"改动前的层数" |
| **实际生效的最终增量** | `Hook.AfterPowerAmountChanged(choiceContext, power, amount, applier, cardSource)` | `amount` 是修正 + 多人缩放后的偏移量（注意：**此签名没有 `target`**，target 需自己用 `power.Owner`） |
| **只关心"有没有被加成"** | `AfterModifyingPowerAmountGiven/Received` | 只在有 modifiers 时触发 |

> **注意 `AfterPowerAmountChanged` 缺失 `target` 参数**：这是反编译确认的签名差异。target 用 `power.Owner` 取（`Owner` 在 `BeforeApplied/ApplyInternal` 时已被设为 target）。

### `PowerModel.Amount`

```csharp
private int _amount;
public int Amount
{
    get => _amount;
    set => SetAmount(value);
}
public int AmountOnTurnStart { get => _amountOnTurnStart; set => _amountOnTurnStart = value; }
public virtual int DisplayAmount => Amount;

public void SetAmount(int amount, bool silent = false)
{
    amount = Math.Clamp(amount, -999999999, 999999999);
    int num = amount - _amount;
    if (num != 0)
    {
        _amount = amount;
        InvokeDisplayAmountChanged();       // DisplayAmountChanged 事件
        Owner.InvokePowerModified(this, num, silent);
    }
}

public void ApplyInternal(Creature owner, decimal amount, bool silent = false)
{
    if (!(amount == 0m))
    {
        Owner = owner;
        SetAmount((int)amount, silent);
        Owner.ApplyPowerInternal(this);
    }
}

// Creature 侧的读取 API（recon-out/creature.txt:1143）
public bool HasPower<T>() where T : PowerModel;
public bool HasPower(ModelId id);
public T? GetPower<T>() where T : PowerModel;
public PowerModel? GetPower(ModelId id);
public IEnumerable<T> GetPowerInstances<T>() where T : PowerModel;
public IEnumerable<PowerModel> GetPowerInstances(ModelId id);
public PowerModel? GetPowerById(ModelId id);
public int GetPowerAmount<T>() where T : PowerModel => GetPower<T>()?.Amount ?? 0;
public IReadOnlyList<PowerModel> Powers { get; }
```

**`Amount` 就是"层数"（int）**。`PoisonPower` 是"层数 = 每回合开始时失去的生命"，`DoomPower` 是"层数 = 触发死亡阈值的血量"，`VulnerablePower`/`WeakPower` 是"剩余回合数"，`StrengthPower` 是"力量点数"。

`PowerModel.GetTypeForAmount(decimal)` 决定"这个数值算 Buff 还是 Debuff"，**对判断"负面状态"很有用**：

```csharp
public PowerType GetTypeForAmount(decimal customAmount)
{
    if (StackType == PowerStackType.Counter && AllowNegative && customAmount < 0m) return PowerType.Buff;   // 负层数 = Buff
    if (!AllowNegative && Type == PowerType.Debuff && customAmount < 0m) return ...;                         // 实现细节略
    return Type;
}
public bool ShouldRemoveDueToAmount()
{
    if (AllowNegative || Amount > 0) return ...;
    return Amount == 0;
}
```

`VulnerablePower` 参考实现（说明 Buff/Debuff 是怎么决定的，`recon-out/vulnerablepower.txt` 完整原文节选）：

```csharp
public sealed class VulnerablePower : PowerModel
{
    private const string _damageIncrease = "DamageIncrease";

    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new <>z__ReadOnlySingleElementList<DynamicVar>(new DynamicVar("DamageIncrease", 1.5m));   // ★ ×1.5

    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (target != base.Owner) return 1m;
        if (!props.IsPoweredAttack()) return 1m;
        decimal num = base.DynamicVars["DamageIncrease"].BaseValue;
        if (dealer != null)
        {
            PaperPhrog paperPhrog = dealer.Player?.GetRelic<PaperPhrog>();
            if (paperPhrog != null) num = paperPhrog.ModifyVulnerableMultiplier(target, num, props, dealer, cardSource);
            CrueltyPower crueltyPower = dealer.GetPower<CrueltyPower>()
                                        ?? dealer.PetOwner?.Creature.GetPower<CrueltyPower>();
            if (crueltyPower != null) num = crueltyPower.ModifyVulnerableMultiplier(target, num, props, dealer, cardSource);
        }
        DebilitatePower power = target.GetPower<DebilitatePower>();
        if (power != null) num = power.ModifyVulnerableMultiplier(target, num, props, dealer, cardSource);
        return num;
    }

    public override Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
                                          IEnumerable<Creature> participants)
        => /* side == CombatSide.Enemy 时 */ PowerCmd.TickDownDuration(this);
}
```

---

## B9. 临时力量 vs 永久力量（"一次性使敌人失去总共至少 10 点临时力量"）

### 两种类型（完整原文）

**永久力量：`StrengthPower`**

```csharp
public sealed class StrengthPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override bool AllowNegative => true;              // ★ 关键：允许负层数 = 力量削弱

    public override decimal ModifyDamageAdditive(Creature? target, decimal amount, ValueProp props,
                                                 Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (base.Owner != dealer) return 0m;
        if (!props.IsPoweredAttack()) return 0m;
        return decimal.op_Implicit(base.Amount);             // 直接把自己加到伤害上
    }
}
```

**临时力量：`TemporaryStrengthPower`（abstract，实现 `ITemporaryPower`）**

```csharp
public abstract class TemporaryStrengthPower : PowerModel, ITemporaryPower
{
    public override PowerType Type => IsPositive ? PowerType.Buff : PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public abstract AbstractModel OriginModel { get; }                     // 来源（Card / Potion / Relic）
    public PowerModel InternallyAppliedPower => ModelDb.Power<StrengthPower>();
    protected virtual bool IsPositive => true;                             // ★ false = 临时力量降低

    private int Sign => IsPositive ? 1 : -1;

    public override LocString Title
    {
        get
        {
            AbstractModel originModel = OriginModel;
            if (originModel is CardModel cardModel)  return cardModel.TitleLocString;
            if (originModel is PotionModel potionModel) return potionModel.Title;
            if (originModel is RelicModel relicModel)   return relicModel.Title;
            throw new InvalidOperationException();
        }
    }
    public override LocString Description =>
        new LocString("powers", IsPositive ? "TEMPORARY_STRENGTH_POWER.description" : "TEMPORARY_STRENGTH_DOWN.description");
    protected override string SmartDescriptionLocKey =>
        IsPositive ? "TEMPORARY_STRENGTH_POWER.smartDescription" : "TEMPORARY_STRENGTH_DOWN.smartDescription";

    // ★ 施加时：同步向目标施加/移除等量的永久 StrengthPower
    public override async Task BeforeApplied(Creature target, decimal amount, Creature? applier, CardModel? cardSource)
        => await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), target,
                                               Sign * amount, applier, cardSource, silent: true);

    // ★ 层数变化时（叠加/削减）同步调整永久 StrengthPower
    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power,
                                                       decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (!(amount == (decimal)Amount) && power == this)
            await PowerCmd.Apply<StrengthPower>(choiceContext, Owner, Sign * amount, applier, cardSource, silent: true);
    }

    // ★ 本回合结束时：移除自己，并把之前借出的 StrengthPower 还给反向
    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
                                                IEnumerable<Creature> participants)
    {
        if (Enumerable.Contains(participants, Owner))
        {
            Flash();
            await PowerCmd.Remove(this);
            await PowerCmd.Apply<StrengthPower>(choiceContext, Owner, -Sign * Amount, Owner, null);
        }
    }
}
```

### 具体子类（实例）

```csharp
// 储君"王之凝视"的临时力量削减
public class MonarchsGazeStrengthDownPower : TemporaryStrengthPower
{
    public override AbstractModel OriginModel => ModelDb.Card<MonarchsGaze>();
    protected override bool IsPositive => false;               // ★ 临时力量降低
}

// 铁甲战士"凌虐"
public class ManglePower : TemporaryStrengthPower
{
    public override AbstractModel OriginModel => ModelDb.Card<Mangle>();
    protected override bool IsPositive => false;               // （由 card-mangle.txt / ManglePower 反编译确认其继承自 TemporaryStrengthPower）
}
```

> 完整的 TemporaryStrengthPower 子类清单需要遍历 CLR 类型（ilspy-cli 的 `types` 不显示基类）。可用 `ModelDb.AllPowers.Where(p => p is TemporaryStrengthPower)` 在运行时枚举；本次已确认存在的有 `MonarchsGazeStrengthDownPower`、`ManglePower`、`MockTemporaryStrengthLossPower`（`MegaCrit.Sts2.Core.Models.Powers.Mocks.*`，测试用）。同族还有 `TemporaryDexterityPower` / `TemporaryFocusPower`。

### `ITemporaryPower` 接口（公开，可直接 `is` 判断）

```csharp
// MegaCrit.Sts2.Core.Models.ITemporaryPower
public interface ITemporaryPower
{
    AbstractModel OriginModel { get; }
    PowerModel InternallyAppliedPower { get; }
}
```

### "失去 N 点力量"的两种表示法（对"临时 vs 永久"判定至关重要）

通过本地化文件可以确证游戏用**三条不同的路径**：

**路径 1 — 永久减少：`PowerVar<StrengthPower>` 传负值 / `inverseDiff()`**（源自 `zhs/cards.json`）

```
"RESONANCE.description":  "获得{StrengthPower:diff()}点[gold]力量[/gold]。所有敌人失去1点[gold]力量[/gold]。"
"MALAISE.description":    "敌人失去X{IfUpgraded:show:+1}点[gold]力量[/gold]。给予X{IfUpgraded:show:+1}层[gold]虚弱[/gold]。"
"FRIENDSHIP.description": "失去{StrengthPower:inverseDiff()}点[gold]力量[/gold]。…"
"SHARED_FATE.description":"失去{PlayerStrengthLoss:diff()}点[gold]力量[/gold]。\n敌人失去{EnemyStrengthLoss:diff()}点[gold]力量[/gold]。"
"TEZCATARAS_MIGHT.description": "获得{StrengthPower:diff()}点[gold]力量[/gold]。\n在你的回合结束时，失去{StrengthLoss:diff()}点[gold]力量[/gold]。"
```

**路径 2 — 本回合（临时）减少：自定义 `DynamicVar("StrengthLoss", N)` + `PowerCmd.Apply<TemporaryStrengthPower 子类>`**

```
"CRUSH_UNDER.description":     "…所有敌人在本回合失去{StrengthLoss:diff()}点[gold]力量[/gold]。"
"DARK_SHACKLES.description":   "使一名敌人在本回合失去{StrengthLoss:diff()}点[gold]力量[/gold]。"
"DYING_STAR.description":      "…所有敌人在本回合中失去{StrengthLoss:diff()}点[gold]力量[/gold]。"
"ENFEEBLING_TOUCH.description":"让一名敌人在本回合内失去{StrengthLoss:diff()}点[gold]力量[/gold]。"
"MANGLE.description":          "造成{Damage:diff()}点伤害。\n敌人在本回合失去{StrengthLoss:diff()}点[gold]力量[/gold]。"
"MONARCHS_GAZE.description":   "每当你攻击敌人的时候，这名敌人在本回合失去{StrengthLoss:diff()}点[gold]力量[/gold]。"
"PIERCING_WAIL.description":   "所有敌人在本回合失去{StrengthLoss:diff()}点[gold]力量[/gold]。"
```

对应实现（`Mangle`，已在上文 A4 引用）：

```csharp
protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[2]
{
    new DamageVar(20m, ValueProp.Move),
    new DynamicVar("StrengthLoss", 10m)        // ★ 临时力量削减量
};
// OnPlay 中：
await PowerCmd.Apply<ManglePower>(choiceContext, cardPlay.Target, DynamicVars["StrengthLoss"].BaseValue, Owner.Creature, this);
```

**路径 3 — 从临时力量层数中扣除时，`TemporaryStrengthPower.AfterPowerAmountChanged` 会向 `StrengthPower` 传递 `Sign * amount`**（负 `Sign` → 负增量 → `StrengthPower` 层数下降）。

### 【推导】mod 检测"一次性使敌人失去总共至少 10 点临时力量"的正确做法

```csharp
// 在所有 Hook 监听点（或 Harmony patch Hook.BeforePowerAmountChanged）里：
//   combatState, power, amount, target, applier, cardSource
if (power is TemporaryStrengthPower                     // ← 临时力量族
    && power.Type == PowerType.Debuff                   // ← IsPositive == false（临时力量降低）
    && target.Side != applier?.Side                     // ← 作用于敌方（可选）
    && applier == cardSource?.Owner?.Creature)          // ← 来源是自己的卡（可选）
{
    // amount 是本次施加的"临时力量降低"原始层数（正数）
    pendingTempStrengthLoss += (int)amount;             // 按 cardSource 聚合成"一张牌的总额"
}

// 想拿"最终生效值"（含遗物 ×2 等）改用：
//   Hook.AfterPowerAmountChanged(choiceContext, power, amount /*最终增量*/, applier, cardSource) → power.Owner 是目标
```

**关键鉴别点（不用反射就能区分临时/永久）**：

| 问题 | 判据 |
|---|---|
| 是不是"临时"力量变化？ | `power is TemporaryStrengthPower`（同时 `power is ITemporaryPower`；`power.InternallyAppliedPower is StrengthPower`） |
| 是"降低"还是"提升"？ | `power.Type == PowerType.Debuff`（= `IsPositive == false`）；**`IsPositive` 是 `protected`，无法直接读** |
| 是不是"永久"力量变化？ | `power is StrengthPower`，且 `amount < 0`（`AllowNegative == true` 使负层数合法） |
| 目标身上还剩多少临时力量？ | 【推导】`target.GetPowerInstances<TemporaryStrengthPower>()` 不可用（泛型约束是 `where T : PowerModel`，可以传基类！）→ `target.GetPowerInstances<TemporaryStrengthPower>().Sum(p => p.Amount)`；或用 `target.Powers.Where(p => p is TemporaryStrengthPower).Sum(p => p.Amount)` |

> ⚠️ **重要语义提醒**：`TemporaryStrengthPower.Amount` 存的是"借出的力量点数"，而**永久 `StrengthPower` 的层数已在 `BeforeApplied` 时同步变化**。所以对同一个敌人，一次 `Apply<ManglePower>(..., 10)` 会**同时**产生：
> - `BeforePowerAmountChanged`（power = ManglePower，amount = 10）
> - 内部嵌套的 `PowerCmd.Apply<StrengthPower>(..., -10, silent: true)` → 又一次 `BeforePowerAmountChanged`（power = StrengthPower，amount = -10，cardSource = 同一张卡）
>
> **如果 mod 只统计 `StrengthPower` 的负增量，就已经能覆盖永久+临时两条路径的总和**；如果想严格区分"本回合临时"，则只统计 `TemporaryStrengthPower` 的事件。**不要两个都统计，否则会重复计数。**

---

## B10. 卡牌身份：稳定 ID + 显示名

### `CardModel` 相关成员

```csharp
// MegaCrit.Sts2.Core.Models.CardModel : AbstractModel（recon-out/cardmodel-members.txt + cardmodel-full.txt）
public abstract class CardModel : AbstractModel
{
    protected CardModel(int canonicalEnergyCost, CardType type, CardRarity rarity,
                        TargetType targetType, bool shouldShowInCardLibrary);

    // ---- 身份 ----
    public ModelId Id { get; }                                  // 继承自 AbstractModel
    public LocString TitleLocString => _titleLocString ?? (_titleLocString = new LocString("cards", base.Id.Entry + ".title"));
    public virtual string Title
    {
        get
        {
            LocString titleLocString = TitleLocString;
            if (!IsUpgraded) return titleLocString.GetFormattedText();
            if (MaxUpgradeLevel > 1) return $"{titleLocString.GetFormattedText()}+{CurrentUpgradeLevel}";
            return titleLocString.GetFormattedText() + "+";
        }
    }
    public LocString Description => new LocString("cards", base.Id.Entry + ".description");

    // ---- 稀有度 / 类型 / 目标 ----
    public CardRarity Rarity { get; }
    public CardType Type { get; }                               // Attack / Skill / Power / Status / Curse …
    public TargetType TargetType { get; }

    // ---- 升级状态 ----
    public int CurrentUpgradeLevel { get; set; }
    public int MaxUpgradeLevel { get; }
    public bool IsUpgraded => CurrentUpgradeLevel > 0;
    public bool IsUpgradable { get; }
    public CardUpgradePreviewType UpgradePreviewType { get; set; }

    // ---- Replay ----
    public int BaseReplayCount { get; set; }
    public int GetEnchantedReplayCount();
    public event Action ReplayCountChanged;

    // ---- 数值 ----
    public DynamicVarSet DynamicVars { get; }
    public IEnumerable<DynamicVar> CanonicalVars { get; }

    // ---- 运行时 ----
    public Creature? CurrentTarget { get; set; }
    public int CurrentPlayIndex { get; set; }
    public bool IsEnchantmentPreview { get; set; }

    // ---- 预览 ----
    public void UpdateDynamicVarPreview(CardPreviewMode previewMode, Creature? target, DynamicVarSet dynamicVarSet);
    public string GetDescriptionForPile(PileType pileType, Creature? target = null);
    public string GetDescriptionForUpgradePreview();
}
```

`AbstractModel` 上的 `Id`：

```csharp
public abstract class AbstractModel : IComparable<AbstractModel>
{
    [field: CompilerGenerated] public ModelId Id { get; }
    public bool IsMutable { get; private set; }
    public bool IsCanonical => !IsMutable;
    public virtual bool IsMock => false;

    protected AbstractModel()
    {
        System.Type type = base.GetType();
        ModelId id = ModelDb.GetId(type);
        AbstractModel byIdOrNull = ModelDb.GetByIdOrNull<AbstractModel>(id);
        if (byIdOrNull != null)
            throw new DuplicateModelException($"ModelDb already contains ID {id} mapped to type …");
        Id = id;
    }
    public void AssertMutable();     // 非 mutable 会抛
    public void AssertCanonical();   // mutable 会抛
}
```

### 【推导】`CardModel.Id` 的实际字符串

`ModelDb.GetId(typeof(Mangle))` → `new ModelId(GetCategory(typeof(Mangle)), GetEntry(typeof(Mangle)))`
→ `Category = SlugifyCategory("CardModel") = "CARD"`，`Entry = Slugify("Mangle") = "MANGLE"`
→ **`Id.ToString() == "CARD.MANGLE"`**

`Id.Entry` 与本地化文件完全对应（已用 `eng/cards.json` / `zhs/cards.json` 逐条验证）：

```json
// .recon/pck/localization/eng/cards.json
{
  "ABRASIVE.description": "Gain {DexterityPower:diff()} [gold]Dexterity[/gold].\nGain {ThornsPower:diff()} [gold]Thorns[/gold].",
  "ABRASIVE.title": "Abrasive",
  "ADAPTIVE_STRIKE.description": "Deal {Damage:diff()} damage.\nAdd a 0{energyPrefix:energyIcons(1)} copy of this card into your [gold]Discard Pile[/gold].",
  ...
}
```

教程文档也确认了同一规则（`Modding-Tutorial/docs/06-assets-pck-and-localization.md:9-14`）：

> a class named `FieldNotesCard : CardModel` receives the entry slug `FIELD_NOTES_CARD`. The base card class then requests:
> ```
> cards: FIELD_NOTES_CARD.title
> cards: FIELD_NOTES_CARD.description
> ```

### 全局注册表：`ModelDb`

```csharp
public static class ModelDb
{
    private static readonly Dictionary<ModelId, AbstractModel> _contentById = new Dictionary<ModelId, AbstractModel>(4096);

    // ---- 枚举 ----
    public static IEnumerable<AbstractModel> All => _contentById.Values;
    public static IEnumerable<CardModel> AllCards { get; }        // CardPool.AllCards ∪ Character.StartingDeck（Distinct）
    public static IEnumerable<CardPoolModel> AllCardPools { get; }
    public static IEnumerable<CardPoolModel> AllSharedCardPools { get; }
    public static IEnumerable<CardPoolModel> AllCharacterCardPools { get; }
    public static IEnumerable<CharacterModel> AllCharacters { get; }  // Ironclad/Silent/Regent/Necrobinder/Defect
    public static IEnumerable<PowerModel> AllPowers { get; }
    public static IEnumerable<RelicModel> AllRelics { get; }
    public static IEnumerable<PotionModel> AllPotions { get; }
    public static IEnumerable<OrbModel>/*DebugOrb*/ … ;
    public static System.Type[] AllAbstractModelSubtypes { get; }  // 含 ReflectionHelper.GetSubtypesInMods<AbstractModel>()

    // ---- 查 ID ----
    public static ModelId GetId<T>() where T : AbstractModel;
    public static ModelId GetId(System.Type type);
    public static string  GetCategory(System.Type type);
    public static string  GetEntry(System.Type type);
    public static System.Type GetCategoryType(System.Type type);

    // ---- 按 ID 取（含 mod 内容）----
    public static T? GetByIdOrNull<T>(ModelId id) where T : AbstractModel;
    public static T  GetById<T>(ModelId id) where T : AbstractModel;      // 找不到抛 ModelNotFoundException
    public static bool Contains(System.Type type);

    // ---- 强类型规范实例 ----
    public static T Card<T>()       where T : CardModel;
    public static T CardPool<T>()   where T : CardPoolModel;
    public static T Character<T>()  where T : CharacterModel;
    public static T Power<T>()      where T : PowerModel;
    public static T Relic<T>()      where T : RelicModel;
    public static T Orb<T>()        where T : OrbModel;
    public static T Potion<T>()     where T : PotionModel;
    public static T Monster<T>()    where T : MonsterModel;
    public static T Encounter<T>()  where T : EncounterModel;
    public static T Event<T>()      where T : EventModel;
    public static T Act<T>()        where T : ActModel;
    public static T Affliction<T>() where T : AfflictionModel;
    public static T Enchantment<T>()where T : EnchantmentModel;
    public static T Modifier<T>()   where T : ModifierModel;
    public static T Badge<T>()      where T : BadgeModel;
    public static T Achievement<T>()where T : AchievementModel;
    public static T Singleton<T>()  where T : SingletonModel;
    public static PowerModel DebugPower(System.Type type);
    public static OrbModel?  DebugOrb(System.Type type);
}
```

### 按字符串 ID 查卡

`ModelDb` **没有** `GetById(string)` 重载，需要先构造 `ModelId`：

```csharp
// MegaCrit.Sts2.Core.Models.ModelId.Deserialize(string)  —— 格式 "CATEGORY.ENTRY"
public static ModelId Deserialize(string json)
{
    string[] array = json.Split('.', (StringSplitOptions)0);
    if (array.Length != 2) throw new JsonException("'" + json + "' does not match the expected ModelId form.");
    return new ModelId(array[0], array[1]);
}
```

【推导】按 id 字符串查卡：

```csharp
CardModel? FindCardById(string entry)              // entry 例："MANGLE"
{
    // 方式 A：直接构造 ModelId（Category 固定为 "CARD"）
    return ModelDb.GetByIdOrNull<CardModel>(new ModelId("CARD", entry.ToUpperInvariant()));

    // 方式 B：全局扫描（不依赖 Category 规则，最稳妥）
    // return ModelDb.AllCards.FirstOrDefault(c => c.Id.Entry == entry);
}
```

### 枚举所有卡 id → 名称映射

```csharp
// 【推导】基于 ModelDb.AllCards / CardModel.Title / LocString
var map = new Dictionary<string, string>();       // Entry -> 本地化显示名
foreach (CardModel card in ModelDb.AllCards)
{
    string entry = card.Id.Entry;                 // "MANGLE"
    string cat   = card.Id.Category;              // "CARD"
    string name  = card.Title;                    // "凌虐"（已含升级后缀逻辑，mutable 实例才准）
    // 若只要基础名（不含 "+"），用 TitleLocString.GetFormattedText()
    string baseName = card.TitleLocString.GetFormattedText();
    map[entry] = baseName;
}
```

> ⚠️ `ModelDb.AllCards` 返回的是 **canonical（不可变）实例**，`Title` 里 `IsUpgraded` 为 false，所以 `Title` 等价于 `TitleLocString.GetFormattedText()`。对 mutable 实例（手牌里的卡）`Title` 才会带 `+`。
>
> ⚠️ `ModelDb.AllCards` = `AllCardPools.SelectMany(p => p.AllCards)` ∪ `AllCharacters.SelectMany(c => c.StartingDeck)`。**只存在于某个 pool 里的卡才会被枚举到**，理论上可能有"未注册到任何 pool 的卡"（例如纯 token 卡）不在列表里 —— 需要 100% 覆盖率时用 `ModelDb.AllAbstractModelSubtypes.Where(t => t.IsSubclassOf(typeof(CardModel)))`。

### 示例卡牌 ID（可复现验证）

来源：`.recon/pck/localization/{eng,zhs}/cards.json` 的 key，与 `docs/plans/game-object-lookup.tsv` 交叉验证（tsv 的 `id` 列 = `Id.Entry`，`name` 列 = 中文 `Title`）。

| `Id.Entry` | 完整 `ModelId.ToString()`【推导】 | 中文名 | 英文名 | Class【推导】 | 验证来源 |
|---|---|---|---|---|---|
| `STRIKE_IRONCLAD` | `CARD.STRIKE_IRONCLAD` | 打击 | Strike | `Models.Cards.StrikeIronclad` | tsv:82 + zhs/cards.json |
| `DEFEND_IRONCLAD` | `CARD.DEFEND_IRONCLAD` | 防御 | Defend | `Models.Cards.DefendIronclad` | tsv:32 |
| `BASH` | `CARD.BASH` | 痛击 | Bash | `Models.Cards.Bash` | tsv:12 |
| `ANGER` | `CARD.ANGER` | 愤怒 | Anger | `Models.Cards.Anger` | tsv:8 |
| `INFLAME` | `CARD.INFLAME` | 燃烧 | Inflame | `Models.Cards.Inflame` | tsv:54 |
| `SHRUG_IT_OFF` | `CARD.SHRUG_IT_OFF` | 耸肩无视 | Shrug It Off | `Models.Cards.ShrugItOff` | tsv:76 |
| `MANGLE` | `CARD.MANGLE` | 凌虐 | Mangle | `Models.Cards.Mangle` | tsv:58 + 反编译 `card-mangle.txt` |
| `PIERCING_WAIL` | `CARD.PIERCING_WAIL` | 尖啸 | Piercing Wail | `Models.Cards.PiercingWail` | tsv:157 |
| `MALAISE` | `CARD.MALAISE` | 萎靡 | Malaise | `Models.Cards.Malaise` | tsv:147 |
| `DARK_SHACKLES` | `CARD.DARK_SHACKLES` | 黑暗镣铐 | Dark Shackles | `Models.Cards.DarkShackles` | tsv:471 |
| `ENFEEBLING_TOUCH` | `CARD.ENFEEBLING_TOUCH` | 弱化之触 | Enfeebling Touch | `Models.Cards.EnfeeblingTouch` | tsv:308 |
| `MONARCHS_GAZE` | `CARD.MONARCHS_GAZE` | 王之凝视 | Monarch's Gaze | `Models.Cards.MonarchsGaze` | tsv:241 + 反编译 `monarchsgaze.txt` |
| `RESONANCE` | `CARD.RESONANCE` | 共鸣 | Resonance | `Models.Cards.Resonance` | tsv:257 |
| `DEFEND_DEFECT` | `CARD.DEFEND_DEFECT` | 防御 | Defend | `Models.Cards.DefendDefect` | tsv:393 |
| `FRIENDSHIP` | `CARD.FRIENDSHIP` | 友谊 | Friendship | `Models.Cards.Friendship` | tsv:314 |
| `ADAPTIVE_STRIKE` | `CARD.ADAPTIVE_STRIKE` | 适应打击 | Adaptive Strike | `Models.Cards.AdaptiveStrike` | zhs/cards.json |
| `TEZCATARAS_MIGHT` | `CARD.TEZCATARAS_MIGHT` | 特兹卡塔拉之力 | Tezcatara's Might | `Models.Cards.TezcatarasMight` | zhs/cards.json |
| `SHARED_FATE` | `CARD.SHARED_FATE` | 命运同担 | Shared Fate | `Models.Cards.SharedFate` | zhs/cards.json |

> 注意：`docs/plans/game-object-lookup.tsv` 里的 597 张 `kind=1` 记录**不是**全部卡（`ModelDb.AllCards` 包含 token / quest / status / curse 等 pool 卡），例如 `TEZCATARAS_MIGHT` / `SHARED_FATE` 在 tsv 里查不到但存在于 `cards.json`。**以 `.recon/pck/localization/*/cards.json` 为权威全集**。
> 另外 `cleave` 在本版本中不是一个独立 entry（tsv 与 `cards.json` 均无 `CLEAVE`），不要照搬 STS1 的记忆。

示例 power entry（用于对照 `ModelId` 的 `POWER.` 前缀）：

| `Id.Entry` | 中文 | 英文 |
|---|---|---|
| `VULNERABLE_POWER` | 易伤 | Vulnerable |
| `WEAK_POWER` | 虚弱 | Weak |
| `STRENGTH_POWER` | 力量 | Strength |
| `DOOM_POWER` | 灾厄 | Doom |
| `POISON_POWER` | 中毒 | Poison |
| `TEMPORARY_STRENGTH_POWER` / `TEMPORARY_STRENGTH_DOWN` | 临时力量↑/↓（共用 Description） | — |
| `MONARCHS_GAZE_POWER` | 王之凝视 | Monarch's Gaze |
| `POSSESS_STRENGTH_POWER` | 抢夺力量 | Possess Strength |
| `MOCK_TEMPORARY_STRENGTH_LOSS` | （测试用） | Mock Temporary Strength Loss |

本地化文件里的**全部** card / power / relic / orb entry 都可用同样方式提取，见 `.recon/pck/localization/{eng,zhs}/*.json`。

---

## B11. 遗物身份 + 判断玩家是否拥有某遗物

### `RelicModel`

```csharp
// MegaCrit.Sts2.Core.Models.RelicModel : AbstractModel（recon-out/relicmodel.txt）
public abstract class RelicModel : AbstractModel
{
    public const string locTable = "relics";

    public virtual LocString Title       => new LocString("relics", base.Id.Entry + ".title");
    public virtual LocString Description => new LocString("relics", base.Id.Entry + ".description");
    public LocString FlavorText { get; }
    public RelicRarity Rarity { get; }
    public RelicStatus Status { get; set; }              // Active / Normal
    public bool IsVisible { get; }
    public Player Owner { get; }
    public Creature OwnerCreature { get; }
    public virtual int DisplayAmount => Amount;
    // …事件、Hook 覆写等
}
```

```csharp
// MegaCrit.Sts2.Core.Entities.Relics.RelicRarity
public enum RelicRarity { None, Starter, Common, Uncommon, Rare, Shop, Event, Ancient }

// MegaCrit.Sts2.Core.Entities.Relics.RelicStatus
public enum RelicStatus { Normal, Active, Disabled }
```

### `Player` 侧的遗物 API（完整原文）

```csharp
// MegaCrit.Sts2.Core.Entities.Players.Player
public IReadOnlyList<RelicModel> Relics { get; }                  // ← 全部遗物
public List<ModelId> DiscoveredRelics { get; set; }
public RelicGrabBag RelicGrabBag { get; }
public event Action<RelicModel> RelicObtained;
public event Action<RelicModel> RelicRemoved;
public void AddRelicInternal(RelicModel relic, int index, bool silent);
public void RemoveRelicInternal(RelicModel relic, bool silent);
public void MeltRelicInternal(RelicModel relic);

public T? GetRelic<T>() where T : RelicModel
{
    return Enumerable.FirstOrDefault<RelicModel>(Relics, (Func<RelicModel, bool>)((RelicModel r) => r is T)) as T;
}

public RelicModel? GetRelicById(ModelId id)
{
    return Enumerable.FirstOrDefault<RelicModel>(Relics, (Func<RelicModel, bool>)((RelicModel r) => r.Id == id));
}
```

### "不安灯油" → `UnsettlingLamp`（真名 **不安油灯**）

`docs/plans/game-object-lookup.tsv:884`：`2  UNSETTLING_LAMP  不安油灯`
→ CLR 类型 `MegaCrit.Sts2.Core.Models.Relics.UnsettlingLamp`（`types-oil.txt` 命中 `MegaCrit.Sts2.Core.Models.Relics.UnsettlingLamp`）

### 完整反编译（`recon-out/unsettlinglamp.txt`，节选关键部分）

```csharp
namespace MegaCrit.Sts2.Core.Models.Relics;

public sealed class UnsettlingLamp : RelicModel
{
    private CardModel? _triggeringCard;
    private List<PowerModel>? _doubledPowers;
    private bool _isFinishedTriggering;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override Task BeforeCombatStart()
    {
        TriggeringCard = null; DoubledPowers.Clear(); IsFinishedTriggering = false;
        base.Status = RelicStatus.Active;
        return Task.CompletedTask;
    }

    // ★★★ 这是"本回合第一张给敌人上 debuff 的牌"的识别逻辑
    public override Task BeforePowerAmountChanged(PowerModel power, decimal amount, Creature target,
                                                  Creature? applier, CardModel? cardSource)
    {
        if (TriggeringCard != null)                    return Task.CompletedTask;
        if (IsFinishedTriggering)                      return Task.CompletedTask;
        if (cardSource == null)                        return Task.CompletedTask;
        if (applier != base.Owner.Creature)            return Task.CompletedTask;
        if (target.Side == base.Owner.Creature.Side)   return Task.CompletedTask;   // 必须是敌方目标
        if (!power.IsVisible)                          return Task.CompletedTask;
        if (power.GetTypeForAmount(amount) != PowerType.Debuff) return Task.CompletedTask;   // ★ 负面状态判定
        if (target.HasPower<ArtifactPower>())          return Task.CompletedTask;
        TriggeringCard = cardSource;
        DoubledPowers.Add(power);
        return Task.CompletedTask;
    }

    public override decimal ModifyPowerAmountGivenMultiplicative(PowerModel power, Creature giver, decimal amount,
                                                                 Creature? target, CardModel? cardSource)
    {
        if (TriggeringCard == null)             return 1m;
        if (cardSource != TriggeringCard)       return 1m;
        if (IsFinishedTriggering)               return 1m;
        if (HasDoubledTemporaryPowerSource(power)) return 1m;
        if (power.GetTypeForAmount(amount) != PowerType.Debuff) return 1m;
        return 2m;                                                       // ★ ×2
    }

    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Card != TriggeringCard) return Task.CompletedTask;
        if (IsFinishedTriggering)            return Task.CompletedTask;
        Flash();
        IsFinishedTriggering = true;
        base.Status = RelicStatus.Normal;
        return Task.CompletedTask;
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        TriggeringCard = null; DoubledPowers.Clear(); IsFinishedTriggering = false;
        base.Status = RelicStatus.Normal;
        return Task.CompletedTask;
    }

    private bool HasDoubledTemporaryPowerSource(PowerModel power)
        => DoubledPowers.OfType<ITemporaryPower>()
                       .Any(p => ((object)p.InternallyAppliedPower).GetType() == ((object)power).GetType());
}
```

**这是整个侦察里最有价值的参考实现**：它的目标（"本回合第一张给敌人上负面状态的牌，其 debuff 层数翻倍"）与 mod 的目标（"判断一张牌会不会造成大量负面状态"）高度重合，可以直接照抄判定条件：

```csharp
bool IsDebuffApplication(PowerModel power, decimal amount, Creature target, Creature applier)
    => power.IsVisible
    && power.GetTypeForAmount(amount) == PowerType.Debuff        // ← 比 Type == Debuff 更准（考虑负层数）
    && target.Side != applier.Side
    && !target.HasPower<ArtifactPower>();
```

### 【推导】mod 判断"玩家是否拥有遗物 X"

```csharp
// 方式 1：强类型（推荐）
Player player = /* 从 RunState / CombatState / CardModel.Owner 取 */;
bool has = player.GetRelic<UnsettlingLamp>() != null;

// 方式 2：按 ModelId
ModelId lampId = ModelDb.GetId<UnsettlingLamp>();       // RELIC.UNSETTLING_LAMP
bool has2 = player.GetRelicById(lampId) != null;

// 方式 3：按 Entry 字符串（不引用具体类型时）
bool has3 = player.Relics.Any(r => r.Id.Entry == "UNSETTLING_LAMP");

// 方式 4：按本地化名（不推荐，依赖语言）
bool has4 = player.Relics.Any(r => r.Title.GetFormattedText() == "不安油灯");
```

`Player.Relics` 是 `IReadOnlyList<RelicModel>`，所以也可以枚举全部并读 `Id.Entry` / `Id.Category` / `Title` 做 UI。

---

## B12. `ValueProp` 是什么 / 哪个值表示"攻击伤害"

### `ValueProp` 完整定义

```csharp
// MegaCrit.Sts2.Core.ValueProps.ValueProp
[Flags]
public enum ValueProp
{
    Unblockable  = 2,
    Unpowered    = 4,
    Move         = 8,
    SkipHurtAnim = 0x10
}
```

**只有 4 个 flag，没有 1 这个值。** 语义：

| Flag | 含义 |
|---|---|
| `Unblockable` | 无视格挡（HP loss 类） |
| `Unpowered` | **不受力量/易伤/虚弱等"攻击加成"影响**（即"未强化"） |
| `Move` | 来源于一次"行动"（卡牌或怪物意图） |
| `SkipHurtAnim` | 跳过受击动画 |

### ★ 哪个值表示"攻击伤害"？—— 没有专门的 flag，判据是 `Move && !Unpowered`

```csharp
// MegaCrit.Sts2.Core.ValueProps.ValuePropExtensions（完整原文）
public static class ValuePropExtensions
{
    public static bool IsPoweredAttack(this ValueProp props)
    {
        if (((System.Enum)props).HasFlag((System.Enum)ValueProp.Move))
            return !((System.Enum)props).HasFlag((System.Enum)ValueProp.Unpowered);
        return false;
    }

    public static bool IsPoweredCardOrMonsterMoveBlock(this ValueProp props)
    {
        if (((System.Enum)props).HasFlag((System.Enum)ValueProp.Move))
            return !((System.Enum)props).HasFlag((System.Enum)ValueProp.Unpowered);
        return false;
    }

    public static bool IsCardOrMonsterMove(this ValueProp props)
        => ((System.Enum)props).HasFlag((System.Enum)ValueProp.Move);
}
```

**`IsPoweredAttack()` 是判定"这次伤害是否吃力量/易伤/虚弱"的唯一正确方式。**
`StrengthPower.ModifyDamageAdditive`、`VulnerablePower.ModifyDamageMultiplicative`、`WeakPower` 全部以它作为门控 —— 这正是为什么充能球的伤害（`ValueProp.Unpowered`）不受力量/易伤影响。

### 预定义组合（`DamageProps`）

```csharp
// MegaCrit.Sts2.Core.ValueProps.DamageProps（完整原文）
public static class DamageProps
{
    public const ValueProp card            = ValueProp.Move;                                       // 0x8
    public const ValueProp cardUnpowered   = ValueProp.Unpowered | ValueProp.Move;                 // 0x8|0x4 = 0xC
    public const ValueProp cardHpLoss      = ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move; // 0xE
    public const ValueProp monsterMove     = ValueProp.Move;                                       // 0x8
    public const ValueProp nonCardUnpowered= ValueProp.Unpowered;                                  // 0x4
    public const ValueProp nonCardHpLoss   = ValueProp.Unblockable | ValueProp.Unpowered;          // 0x6
}
```

典型用法（`Mangle`）：`new DamageVar(20m, ValueProp.Move)` → 是一张**吃力量/易伤的普通攻击牌**。
`AttackCommand.DamageProps` 默认值也是 `ValueProp.Move`（`= ValueProp.Move` 字段初始化器）。
`AttackCommand.Unpowered()` 构建器方法可把它改成 `Move | Unpowered`。

---

# C. 综合：给 mod 的两条实现路线

## C1. "先判断这张牌会不会造成超过阈值伤害"

```csharp
// 在 Hook.BeforeCardPlayed（或 BeforeCardPlayed 的 Harmony patch）里
// 此时 CardPlay.PlayCount 已知，效果尚未结算
void OnBeforeCardPlayed(ICombatState combatState, CardPlay cardPlay)
{
    CardModel card = cardPlay.Card;

    // 1) 单段预测伤害
    decimal perHit = 0m;
    card.DynamicVars.ClearPreview();
    card.UpdateDynamicVarPreview(CardPreviewMode.Normal, cardPlay.Target, card.DynamicVars);
    if (card.DynamicVars.ContainsKey("CalculatedDamage")) perHit = card.DynamicVars["CalculatedDamage"].PreviewValue;
    else if (card.DynamicVars.ContainsKey("Damage"))      perHit = card.DynamicVars["Damage"].PreviewValue;
    else return;                                          // 非伤害牌

    // 2) 段数（拿不到 AttackCommand，只能等 Hook.BeforeAttack 或
    //    实现 AbstractModel.ModifyAttackHitCount(attack, hitCount) 读取 hitCount）
    int hits = 1;

    // 3) 目标数
    int targets = cardPlay.Target != null
        ? 1
        : combatState.GetOpponentsOf(cardPlay.Player.Creature).Count(c => c.IsHittable);

    // 4) Replay 次数
    int plays = cardPlay.PlayCount;

    decimal total = perHit * hits * targets * plays;
    if (total >= THRESHOLD) { /* … */ }
}
```

更精确的做法（能拿到 `AttackCommand` 与真实段数）：

`Hook` 侧（静态，2 个参数）：

```csharp
public static Task BeforeAttack(ICombatState combatState, AttackCommand command);   // 在 ModifyAttackHitCount 之前
public static Task AfterAttack(ICombatState combatState, PlayerChoiceContext choiceContext, AttackCommand command);
```

`AbstractModel` 侧（可覆写虚方法，**只有 1 个参数**）：

```csharp
public virtual Task BeforeAttack(AttackCommand command);
public virtual Task AfterAttack(PlayerChoiceContext choiceContext, AttackCommand command);
public virtual int ModifyAttackHitCount(AttackCommand attack, int hitCount);        // ★ 唯一能读到段数的扩展点
```

```csharp
// 在自己的 RelicModel / PowerModel / ModifierModel 里覆写
public override Task BeforeAttack(AttackCommand command)
{
    // command.Attacker / command.DamageProps / command.ModelSource / command.CardPlay 都是 public
    return Task.CompletedTask;
}
public override int ModifyAttackHitCount(AttackCommand attack, int hitCount)
{
    _lastObservedHitCount = hitCount;                     // ← 段数（AttackCommand._hitCount）
    return hitCount;                                      // 原样返回，不修改
}
```

> ⚠️ `Hook.BeforeAttack` 是**每张攻击牌/每个怪物意图**一次；`Hook.AfterAttack` 在整次攻击（所有段/所有目标）结束后一次。`ModifyAttackHitCount` 也是每次攻击调用一次。

## C2. "先判断这张牌会不会施加大量负面状态"

```csharp
// 推荐：直接覆写 AbstractModel 的 BeforePowerAmountChanged（PowerModel / RelicModel 都可以）
// 签名：Task BeforePowerAmountChanged(PowerModel power, decimal amount, Creature target,
//                                     Creature? applier, CardModel? cardSource)

private readonly Dictionary<CardModel, int> _pending = new();

public override Task BeforePowerAmountChanged(PowerModel power, decimal amount, Creature target,
                                              Creature? applier, CardModel? cardSource)
{
    if (cardSource == null || applier == null) return Task.CompletedTask;
    if (power.GetTypeForAmount(amount) != PowerType.Debuff) return Task.CompletedTask;   // 只算负面
    if (target.Side == applier.Side) return Task.CompletedTask;                          // 只算施加给敌方

    _pending.TryGetValue(cardSource, out int acc);
    _pending[cardSource] = acc + (int)amount;      // ★ 同一次出牌内聚合（含多目标/多段）
    return Task.CompletedTask;
}

// 在 AfterCardPlayed / AfterCardPlayedLate 收尾判定并清空
public override Task AfterCardPlayed(PlayerChoiceContext ctx, CardPlay cardPlay)
{
    if (_pending.TryGetValue(cardPlay.Card, out int total) && total >= DEBUFF_THRESHOLD)
    {
        // 触发 mod 逻辑
    }
    _pending.Remove(cardPlay.Card);
    return Task.CompletedTask;
}
```

**注意重复计数陷阱**：临时力量削减会同时产生 `TemporaryStrengthPower` 的事件和内部嵌套 `StrengthPower` 的事件（见 B9）。若"负面状态总量"要包含力量削减，**只统计一种**（例如只统计 `Amount` 变化为负的 `StrengthPower`，或只统计 `TemporaryStrengthPower`），否则会算两遍。

---

# D. 参考文件索引

| 文件 | 内容 |
|---|---|
| `docs/plans/recon-out/hook-modifydamage.txt` | `Hook.ModifyDamage` + `ModifyDamageInternal` 完整反编译 |
| `docs/plans/recon-out/modifydamagehooktype.txt` / `valueprop.txt` / `damageprops.txt` / `cardpreviewmode.txt` | 4 个关键枚举/常量类的完整源码 |
| `docs/plans/recon-out/hook-members.txt` | `Hook` 全部 150+ 成员签名（含所有 hook 入口） |
| `docs/plans/recon-out/attackcommand.txt` | `AttackCommand` 完整反编译（含 `Execute` 状态机） |
| `docs/plans/recon-out/damagevar.txt` / `calculateddamagevar.txt` / `calculatedvar.txt` / `dynamicvar.txt` / `dynamicvarset.txt` / `powervar.txt` | 数值/预览体系 |
| `docs/plans/recon-out/cardmodel-full.txt` | `CardModel` 完整反编译（3369 行，含 `OnPlayWrapper` / `GeneratePlayCount` / `UpdateDynamicVarPreview`） |
| `docs/plans/recon-out/cardplay.txt` | `CardPlay` 完整定义（`PlayCount` / `PlayIndex`） |
| `docs/plans/recon-out/hook-power-cardplaycount.txt` | `ModifyCardPlayCount` / `ModifyPowerAmountGiven` / `ModifyPowerAmountReceived` / `Before|AfterPowerAmountChanged` |
| `docs/plans/recon-out/powercmd-full.txt` | `PowerCmd` 完整反编译（1387 行，含首次施加/叠加两条完整流程） |
| `docs/plans/recon-out/powermodel-type.txt` | `PowerModel` 完整反编译（738 行） |
| `docs/plans/recon-out/creature.txt` | `Creature` 完整反编译（含 `HasPower<T>` / `GetPowerAmount<T>` 全家族） |
| `docs/plans/recon-out/types-powers-ns2.txt` | `Models.Powers.*` 全部 633 个类型 |
| `docs/plans/recon-out/vulnerablepower.txt` / `strengthpower.txt` / `temporarystrengthpower.txt` / `manglepower.txt` / `monarchsgaze.txt` | 目标状态的具体实现 |
| `docs/plans/recon-out/orbmodel.txt` / `lightningorb.txt` / `darkorb.txt` / `orbcmd.txt` / `orbqueue-members.txt` | 充能球体系 |
| `docs/plans/recon-out/modelid.txt` / `modeldb.txt` / `abstractmodel.txt` / `stringhelper.txt` | 身份体系 |
| `docs/plans/recon-out/relicmodel.txt` / `unsettlinglamp.txt` / `player-getrelic.txt` | 遗物体系 + 不安油灯参考实现 |
| `docs/plans/recon-out/ncard-updatevisuals.txt` / `cardmodel-preview-methods.txt` | 卡面伤害显示链路 |
| `.recon/pck/localization/{eng,zhs}/*.json` | 本地化真值（`cards.json` / `powers.json` / `relics.json` / `orbs.json`） |
| `docs/plans/game-object-lookup.tsv` | Entry ↔ 中文名 对照表（1073 条） |

## D.1 复现侦察的命令

```powershell
$d = "D:\Sts2ModProject\SlaytheSpire2-ModScaffold\tools\dotnet\dotnet.exe"
$c = "D:\Sts2ModProject\SlaytheSpire2-ModScaffold\tools\ilspy-cli\bin\Release\net10.0\ilspycli.dll"
$a = "D:\Sts2ModProject\SlaytheSpire2-ModScaffold\references\sts2.dll"

& $d $c types   $a "Powers\." 999
& $d $c type    $a "MegaCrit\.Sts2\.Core\.Commands\.PowerCmd$" 1
& $d $c method  $a "MegaCrit\.Sts2\.Core\.Hooks\.Hook$" "^ModifyDamage$" 1
& $d $c members $a "MegaCrit\.Sts2\.Core\.Models\.CardModel$" 1
& $d $c search  $a "ModifyDamage" 30
```

> 注意（Windows PowerShell 5.1）：重定向 `>` 会写出 UTF-16LE，用 `| Out-File -Encoding utf8` 代替，否则 `read` 工具会判定为二进制文件。
