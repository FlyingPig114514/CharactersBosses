# 第四个 Boss：2 层静默猎手「涂毒猎」（Poison）

> 用户设计。当前**只做特殊效果**，卡组 / 遗物 / 意图尚未设计（先用占位卡组）。

## 1. 定位

- 第二幕（巢穴，`ActModel.Index == 1`）**三个槽全是她**：`ActTable[1] = { 1, 1, 1 }`（静默猎手下标 1）。
- 强度档 / 血量 / 能量沿用 `BossDefs.Silent` 的第二幕档（`Hp[1] = 185` + 涅奥祝福 `150×2 = 300` ⇒ 485 血）。
- 卡组/遗物/剧本暂时为空 → `BossLoadout` 按卡池生成占位卡组。

## 2. 特殊效果「带毒刺击」（已实现）

用户原话：
> 中毒改为在你回合结束时结算且伤害能被格挡抵消。每当你对静默猎手造成未被格挡的伤害时，
> 将你身上的 1 点中毒转移给她。

补充（用户）：双方的中毒都在**你（玩家）回合结束时**结算；对她来说结算时间基本没变
（她原本在「她的回合开始」结算，而她的回合紧接着玩家回合结束）；触媒会让双方的中毒都多结算，
「立刻触发中毒」的牌也会让双方的中毒都立刻触发（那两张牌没有主语）。

### 原版机制（反编译 `references/sts2.dll`）

| 事实 | 出处 |
|---|---|
| 中毒在**某一方回合开始**触发：`AfterSideTurnStart` 里 `participants.Contains(Owner)` → `Trigger()` | `PoisonPower` |
| `Trigger()`：循环 `TriggerCount = min(层数, 1 + 触媒层数)` 次，每次 `CreatureCmd.Damage(Owner, 当前层数, Unblockable\|Unpowered)`，然后 `PowerCmd.Decrement` | 同上 |
| 所以原版中毒**无视格挡**，且「回合开始」结算 | 同上 |

### 实现

| 文件 | 作用 |
|---|---|
| `Powers/PoisonStrikePower.cs`（新） | ① `BeforeSideTurnEnd(side == Player)` 统一结算**双方**中毒，伤害用 `ValueProp.Unpowered`（去掉 `Unblockable`）⇒ 可被格挡；循环/递减/触媒次数照抄原版；② `AfterDamageGiven` —— 玩家侧对她造成 `UnblockedDamage > 0` 且玩家身上有中毒 → 玩家 −1、她 +1 |
| `Patches/PoisonTimingPatch.cs`（新） | `PoisonPower.AfterSideTurnStart` 前缀：**本场战斗有 `PoisonStrikePower` 时**跳过原版触发（否则一回合结算两次）；其它战斗完全走原版 |
| `Core/BossDefs.cs` | `Silent.SpecialPowerByAct[1] = typeof(PoisonStrikePower)`、`SpecialLocByAct[1] = "SILENT_BOSS.special_act2"` |
| `Encounters/CharBossEncounters.cs` | `ActTable[1] = { 1, 1, 1 }` |
| `content/localization/{zhs,eng}/{powers,monsters}.json` | `POISON_STRIKE_POWER.*`、`SILENT_BOSS.special_act2` |

### 已知偏差（已修）

~~「立刻触发中毒」走原版 `PoisonPower.Trigger()`，仍是 `Unblockable`~~ →
用户明确「且伤害能被格挡抵消」是对**所有**中毒伤害的要求，所以补了
`Patches/PoisonTriggerBlockablePatch.cs`：接管 `PoisonPower.Trigger()`，
改走 `PoisonStrikePower.TriggerBlockableAsync`（与原版循环一一对应，只把
`ValueProp.Unblockable|Unpowered` 换成 `ValueProp.Unpowered`）。
于是周期性结算与「立刻触发」（毒性爆发那类）**都能被格挡**。

## 3. 卡组 / 遗物 / 剧本（用户设计，已落地）

名字→类型全部取自仓库里的 `docs/plans/game-object-lookup.tsv`（权威对照表）。

### 卡组（23 张）

涂毒 `Envenom`、背刺 `Backstab`、无尽刀刃 `InfiniteBlades`、毒雾 `NoxiousFumes`、
防御 `DefendSilent`×5、打击 `StrikeSilent`×4、生存者 `Survivor`、中和 `Neutralize`、
连续反弹 `Ricochet`、匕首雨 `DaggerSpray`、回响斩击 `EchoingSlash`、偏折 `Deflect`、
带毒刺击 `PoisonedStab`、尖啸 `PiercingWail`、刀刃之舞 `BladeDance`、狩猎 `TheHunt`。

### 遗物（5）

蛇之戒指 `RingOfTheSnake`、涅奥的祝福（本 mod）、异蛇头骨 `SneckoSkull`、
**烘焙手套 `ToastyMittens`**、锚 `Anchor`。

> 烘焙手套 = 「烧牌」的真身：`AfterPlayerTurnStart` → `CardSelectCmd.FromHand(ExhaustSelectionPrompt, 1)`
> → `CardCmd.Exhaust`，即每回合开始从手牌**消耗 1 张**（反编译确认）。
> 剧本用 `ScriptCard.Burned` 点名这回合烧哪张 → 登记成「优先消耗」，
> 否则自动选择器会按价值乱烧，把本来要打的牌烧掉。

### 剧本（10 回合，8～10 循环，每回合 2 费）

| 回合 | 打出（按顺序） | 被烧掉 / 备注 |
|---|---|---|
| 1 | 涂毒+、背刺、偏折 | 打击被烧；防御没费 |
| 2 | 生存者、连续反弹（奇巧免费打出） | 防御被烧 |
| 3 | 带毒刺击、匕首雨 | 打击被烧 |
| 4 | 毒雾、尖啸 | 防御被烧 |
| 5 | 打击、回响斩击 | 打击被烧 |
| 6 | 无尽刀刃、狩猎 | 打击被烧 |
| 7 | 小刀、中和、刀刃之舞 | 防御被烧；累计 打击×4、防御×3 |
| 8 | 带毒刺击、回响斩击、偏折 | 小刀被烧 |
| 9 | 生存者、连续反弹、防御 | 小刀被烧 |
| 10 | 匕首雨、中和+、防御 | 小刀被烧 |

循环：`LoopFrom = 8, LoopLength = 3`。洗牌后只剩 9 张（防御×2、生存者、中和+、带毒刺击、
匕首雨、回响斩击、连续反弹、偏折）—— 消耗牌与能力牌不再参与，这点由剧本本身表达。

`连续反弹` 是「奇巧」牌（被丢弃时自动打出、不花钱）→ 剧本给它 `CostOverride = 0`，
否则按卡面 2 费规划会被误判成"打不起"。

## 4. 待办

- 实机验证：第 2 幕 Boss（或临时把 `charbosses_debug.txt` 放回去写 `silent@2`）。

## 4. 顺带修的

- **普通战斗恢复成正常小怪**：删掉游戏目录里的调试开关 `charbosses_debug.txt`。
  想再测试时把文件放回去即可（内容 `silent@2` = 静默猎手 + 强制第二幕档位，`silent@3` = 第三幕刀猎）。
- `BossPlanner.ChooseTarget` 增加「排除自己与自阵营」安全网；
  `CharBossMonster.PlayOne` 的出牌日志改成带 `hp=… side=…`（玩家与 Boss 可能同名，只看名字会误判）。
- `CharBossMonster.ApplyNeurosurgeToPlayers` 跳过自己（影子 Player 挂在 Boss 生物上）。

## 5. 第三方兼容：炼金术士模组「解毒剂」（2026-10-25，已修）

**症状**（用户实测）：二层涂毒猎打炼金术士（工坊 `Alchemist` v0.14.29），中毒层数**没超过解毒剂**时，
毒本该完全无效，实际照样结算 → 被格挡吃掉（「偷护甲」）。

**根因**（反编译 `Alchemist.dll`）：

```csharp
// AlchemistCode.Powers.AntitoxinRules
HasPoisonTickShape(props, dealer, cardSource) =>
    dealer == null && cardSource == null
    && props.HasFlag(ValueProp.Unblockable) && props.HasFlag(ValueProp.Unpowered);
IsPoisonTick(...) => HasPoisonTickShape(...) && amount > 0 && 目标中毒层数 >= amount;
// AlchemistCode.Powers.AntitoxinPower
ModifyDamageAdditive(...) => IsPoisonTick(...) ? -min(解毒剂层数, amount) : 0;
```

解毒剂是**靠伤害属性形状**认「这是不是中毒跳伤」的。而「带毒刺击」为了让中毒可被格挡，
我们的 tick 只传 `ValueProp.Unpowered`（没有 `Unblockable`）⇒ 它认不出来、一点不吸收。

**修法**（`src/Hooks/Core/AlchemistAntitoxinCompat.cs` + `PoisonStrikePower.TriggerBlockableAsync`）：
结算毒伤**之前**，先用**原版中毒 tick 的属性**（`Unblockable | Unpowered`、dealer / cardSource 都为 null）
反射调一次她身上 `*.Powers.AntitoxinPower` 的 `ModifyDamageAdditive`，返回值是负的就等于「解毒剂吃掉这么多」；
剩下的部分再用我们自己的 `Unpowered`（可格挡）结算。

- 只用反射、**不引用 Alchemist.dll**：没装该模组时这段代码零影响；
- 反射失败时按反编译公式兜底（`min(解毒剂层数, 伤害)`，且要求中毒层数 ≥ 伤害）；
- 两个产物都仍然满足：毒没超过解毒剂 → 吸收完剩 0 → **完全不打**（不扣血、不吃格挡）；
  毒超过解毒剂 → 超出部分照旧可被格挡（这是「带毒刺击」的设计）；
- 日志：`[CharBoss] 解毒剂兼容：… 吸收 N 点（剩 M 点照常按可格挡结算）`。

