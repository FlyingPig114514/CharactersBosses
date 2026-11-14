# 意图（Intent）类型总表 —— 原版 + 本模组新增

> 判定源头：`MegaCrit.Sts2.Core.MonsterMoves.Intents`（0.113.0 反编译）＋本仓库
> `src/Hooks/Core/{RawAttackIntent,SacrificeIntent,TemptationIntent}.cs`、`src/Hooks/Core/BossIntentBuilder.cs`。
> 最后核对：v0.2.625（2026-11-13）。

## 0. 公共机制

- 所有意图继承 `AbstractIntent`，用 `IntentType` 分类（枚举共 15 个值）。
- 悬浮说明读 **`intents` 表的 `<PREFIX>.title` / `<PREFIX>.description`**，说明里可用占位符 `{IsMultiplayer}`。
- 数字标签读 `FORMAT_*`：`FORMAT_DAMAGE_SINGLE`（`{Damage}`）、`FORMAT_DAMAGE_MULTI`（`{Damage}×{Repeat}`）、
  `FORMAT_STATUS_CARD_COUNT`（`{CardCount}`）、`FORMAT_EMPTY`。
- 图标动画名默认 = `IntentPrefix.ToLowerInvariant()`；**自定义前缀必须把 `GetAnimation` 回落到原版动画名**，
  否则 `NIntent` 查不到动画帧 ⇒ 图标不动、不上下浮动。
- 图标资源在 `intent_atlas`：`intent_attack` / `intent_buff` / `intent_debuff` / `intent_defend` /
  `intent_heal` / `intent_summon` / `intent_sleep` / `intent_stun` / `intent_status_card` /
  `intent_card_debuff` / `intent_escape` / `intent_death_blow` / `intent_unknown`。

## 1. 原版 16 个类 / 15 个 IntentType

| 类 | `IntentType` | 图标 | 数字标签 | 说明键 | 含义 |
|---|---|---|---|---|---|
| `AttackIntent`（**抽象基类**） | `Attack` | `intent_attack` | `FORMAT_DAMAGE_SINGLE` | `ATTACK` | 攻击的父类，不直接实例化 |
| `SingleAttackIntent` | `Attack` | 同上 | `{Damage}` | `ATTACK` | 单段攻击：造成 N 点伤害 |
| `MultiAttackIntent` | `Attack` | 同上 | `{Damage}×{Repeat}` | `ATTACK` | 多段攻击：造成 N 点伤害 ×M 次 |
| `DeathBlowIntent` | `DeathBlow` | `intent_death_blow` | `{Damage}`（继承单段） | `DEATH_BLOW` | 处决一击：这一下会直接打死你 |
| `BuffIntent` | `Buff` | `intent_buff` | 无 | `BUFF` | 强化：给自己上增益（蓝上箭头） |
| `DebuffIntent(strong:false)` | `Debuff` | `intent_debuff` | 无 | `DEBUFF` | 策略：给你上负面（红下箭头） |
| `DebuffIntent(strong:true)` | `DebuffStrong` | 同一张 `intent_debuff` | 无 | `DEBUFF` | 强力减益：**文案与图标同上**，只有 `IntentType` 不同 |
| `DefendIntent` | `Defend` | `intent_defend` | 无 | `DEFEND` | 守势：他要加格挡 |
| `HealIntent` | `Heal` | `intent_heal` | 无 | `HEAL` | 回复：他要回血 |
| `SummonIntent` | `Summon` | `intent_summon` | 无 | `SUMMON` | 召唤：他要叫帮手 |
| `StatusIntent(int count)` | `StatusCard` | `intent_status_card` | `{CardCount}` | `STATUS` | 塞状态牌：往你牌堆里塞 N 张状态牌 |
| `CardDebuffIntent` | `CardDebuff` | `intent_card_debuff` | 无 | `CARD_DEBUFF` | 卡牌被污染：给你的牌挂邪咒类效果 |
| `EscapeIntent` | `Escape` | `intent_escape` | 无 | `ESCAPE` | 逃跑：他这回合要跑 |
| `SleepIntent` | `Sleep` | `intent_sleep` | 无 | `SLEEP` | 睡眠：他睡着了，不会动 |
| `StunIntent` | `Stun` | `intent_stun` | 无 | `STUN` | 眩晕：他被打断，出手作废 |
| `HiddenIntent` | `Hidden` | **无贴图**（`SpritePath => null`） | 无 | `HIDDEN` | 隐藏意图：故意不告诉你要做什么 |
| `UnknownIntent` | `Unknown` | `intent_unknown` | 无 | `UNKNOWN` | 未知：还没算出来（例如刚进战斗） |

> 注：`DebuffStrong` 与 `Debuff` 共用 `DEBUFF` 说明键与同一张贴图 —— 二者的差别只有 `IntentType`。

## 2. 本模组新增的意图

### 2.1 「牺牲」`SACRIFICE` —— 真·新增类型（v0.2.151 起）

- 类：`CharacterBosses.Core.SacrificeIntent : BuffIntent`（`src/Hooks/Core/SacrificeIntent.cs`）
- 语义：**这名敌人会付出代价来换取强力效果**（扣自己的血、给自己上负面、放弃球位/抽牌权、牺牲召唤物）。
- 图标：复用 `intent_buff` 贴图，由 UI 层**上下翻转**（`UI/BossHandCard.AddIntent → FlipIconDown`），
  动画名回落 `buff`。
- 说明键：`intents.SACRIFICE.title` / `.description`（本模组 16 语言自带）。
- 触发（`BossIntentBuilder.ClassifyPowers` + `SacrificeCards` 点名表）：
  `BIASED_COGNITION` 偏差认知、`WRAITH_FORM` 幽魂形态、`HYPERBEAM` 超能光束、
  `BULLET_TIME` 子弹时间、`BATTLE_TRANCE` 战斗专注、`FRIENDSHIP` 友谊、`NEUROSURGE` 精神过载、
  `BULK_UP` 暴涨、`BONE_SHARDS` 碎骨、`SHARED_FATE` 命运同担；另有「变量里有 `HpLoss`」的自动判据。

### 2.2 「诱惑」`TEMPTATION` —— 真·新增类型（v0.2.625）

- 类：`CharacterBosses.Core.TemptationIntent : DebuffIntent`（`src/Hooks/Core/TemptationIntent.cs`）
- 语义：**这名敌人将以给予你正面效果为代价换取强力效果**。
- 图标：复用**策略**（`intent_debuff`）贴图，UI 层**上下翻转**（与牺牲对称），动画名回落 `debuff`。
- 说明键：`intents.TEMPTATION.title` / `.description`（16 语言）。
  - zhs：`诱惑` / `这名敌人将以给予你正面效果为代价换取强力效果。`
- 触发：`TemptationCards` 点名表 + 通用兜底。
  - 点名表：`FIGHT_ME`（与我一战）—— 反编译 `FightMe.OnPlay`：
    `Apply<StrengthPower>(Target, EnemyStrength=1)`（给玩家 +1 力量）
    ＋ `Apply<StrengthPower>(Owner, StrengthPower=3)`（自己 +3 力量）。
  - 通用兜底：动态变量名里带 **`Enemy`**（作用在对方身上）、且**不含** `Loss` / `Down` / `Reduce` / `Minus`、
    且数值 > 0 ⇒ 视为「给对方的正面效果」。（`命运同担` 的 `EnemyStrengthLoss` 含 Loss ⇒ 不会误判。）

### 2.3 「审判」`JUDGEMENT` —— 真·新增类型（v0.2.625，**仅 0.113 构建**）

- 类：`CharacterBosses.Core.JudgmentIntent : DebuffIntent`（`src/Hooks/Core/TemptationIntent.cs`，`#if STS2_111`）
- 语义：**这名敌人将立刻使你死亡**。
- 图标：借原版**「灾厄」（`DoomPower`）的能力图标**（`ModelDb.Power<DoomPower>().PackedIconPath`
  → `PreloadManager.Cache.GetTexture2D(...)`），取不到才退回策略箭头；
  动画名回落 `debuff` ⇒ **和其它意图一样上下浮动**。
- 说明键：`intents.JUDGEMENT.title` / `.description`（16 语言）。
  - zhs：`审判` / `这名敌人将立刻使你死亡。`
- 触发：`BossIntentBuilder.JudgmentApplies(card, target)`
  - 只认 0.113 的衍生牌 **`Judgement`（审判）**：反编译 `CanonicalVars = { IntVar("Threshold", 30) }`、
    `OnUpgrade` `+10 ⇒ 40`；`OnPlay` = `if (Target.IsAlive && Target.CurrentHp <= Threshold)
    CreatureCmd.SetCurrentHp(Target, 0)`。
  - **只有满足秒杀条件才显示**：被该牌锁定/计划打的那位玩家 `CurrentHp <= Threshold` 时才有这条意图，
    否则一条都不加。
  - 多人：判断依据始终是**被锁定的那位**玩家的血量（`IntentsFor` 的 `target`）。
  - 107 构建**没有**这个类型（用户口径：「更低的版本不增加这个意图类型」）。

### 2.4 「外观是原版、数字规则不同」的五个类（不是新图标，但是新类型）

`src/Hooks/Core/RawAttackIntent.cs`，全部复用原版攻击图标与 `FORMAT_DAMAGE_*`：

| 类 | 数字语义 | 用途 |
|---|---|---|
| `RawAttackIntent` | **原样显示**，不再跑 `Hook.ModifyDamage` | 冰雹风暴这类 `Unpowered` 伤害，预览必须等于层数本身 |
| `RawMultiAttackIntent` | 同上，多段（`N×M`） | 充能球激发的 `Unpowered` 多段 |
| `LiveSingleAttackIntent` | **每帧重算**一次原版伤害管道（`BossDamagePredictor.FinalDamage`） | 玩家在她回合外改她的力量/给玩家上易伤时，预览要跟着变 |
| `LiveDelegateSingleAttackIntent` | 数字由调用方**委托**算，每帧重算 | 段数只有 1 时必须画成单段（否则「隐秘匕首」会画成 `16、7×1`） |
| `LiveMultiAttackIntent` | 委托重算的多段（`Repeats` 也是动态的） | X 费牌（根除 / 天际钻头）的段数随能量变 |

配套工厂 `CharacterBosses.Core.AttackIntent`：`Multi(perHit, times)` / `Raw(perHit, times)` ——
**段数 ≤ 1 时一律退回单段**，统一收口「多余的 ×1」。

## 3. 我们给一张牌画哪几条意图

`BossIntentBuilder.IntentsFor(card, target, …)` 把每张牌拆成最多 7 条：

1. **攻势**（原版攻击意图，含 Raw / Live 变体）
2. **守势**（`DefendIntent`）
3. **策略**（`DebuffIntent`）
4. **强化**（`BuffIntent`）
5. **牺牲**（`SacrificeIntent`）
6. **诱惑**（`TemptationIntent`）—— v0.2.625
7. **审判**（`JudgmentIntent`）—— v0.2.625，仅 0.113 且满足秒杀条件

自动识别走 `ClassifyPowers`（认 `PowerVar<>` 的正负与目标侧）；识别不了的用点名表补：

| 表 | 用途 |
|---|---|
| `ForceBuffCards` | 能量汲取、锻打成型、主宰 |
| `NoBuffIntentCards` | 如此甚好、女妖之嚎（禁止画强化） |
| `ForceDebuffCards` | 无处可逃、荒疫打击、尖啸、黑暗镣铐、震荡波、命运同担 |
| `BuffVarCards` | 残影 |
| `SacrificeCards` | 见 §2.1 |
| `TemptationCards` | 与我一战（见 §2.2） |

兜底：**技能牌没有可识别变量 ⇒ 给「强化」**（历史上几次「蓝箭头画错」都来自这条兜底）。

## 4. 不属于「意图」的两类显示

1. **独立意图行**（手牌栏左侧另起一行，用**原版意图节点**、不进 `MoveState.Intents`）：
   电球被动/激发伤害、炸弹、招架盾的格挡、辉星储「黑洞（下回合辉星到账）」、
   血肉骨的吞噬生命/血肉戏法追加伤害、抉择储「天际钻头会多打多少」的预告。
2. **被击晕**不画 `StunIntent` —— 用原版 `STUNNED` 空 `MoveState`（撤掉全部意图、本回合零出牌），
   下回合补打被打断的那一套。
