# 三层 Boss「炼药猎」（静默猎手·药水轴）—— 实验版

> 状态：**第一版（药水栏位 + 喝药）已实现**；进阶 / 多人站位**未做**（用户 2026-10-25 明确说先做出来实验）。
> 投稿来源：群友设计，用户口述逐字记录在下面 §1。

## 1. 用户原话（设计稿）

- **调制**：静默猎手的初始药水栏位没有被移除（总共 2+2+4 = **8 个药水栏位**），并且会在战斗中使用药水。
  ~~每当静默猎手使用药水时，她恢复 5 点生命。~~ **已删除**（用户 2026-10-26 削弱：去掉「喝药回血」这一条，`HealPerPotion` 5 → 0）。
  **她可以格挡「中毒」和「魂劫」（崩坠·收藏家的 debuff）造成的伤害**（只对她自己、不改变两者的结算时序）
  —— v0.2.10（当时误标 v0.3.0）实现，见 `src/Hooks/Patches/BossBlockableDotPatch.cs`。
- **遗物**：蛇之戒指（起始，第 1 件）、**涅奥的祝福（第 2 件，所有 Boss 都带）**、爬行动物饰品、炼金箱、药水腰带、白兽雕像、娇嫩蕨草、纸鹤、灯笼、吊灯。
- **初始药水**：混沌药水、明晰提取物、明耀酊剂、敏捷药水、镣铐药水、**爆炸药水**、罐装幽灵、瓶中精灵。
- **剧本**（8 回合，之后 5～8 循环；每回合「药水先喝、再出牌」）：
  1. 明晰提取物、明耀酊剂、敏捷药水｜扫腿、生存者（弃磨蚀）、磨蚀+（奇巧自动打出）、灵动步法、精确切击、腐朽
  2. 镣铐药水、爆炸药水（2026-10-26 从第 3 回合挪来）｜夜魇（复制炼药）、炼制药水+（附魔灵魂之力）→ **喝瓶中船**、中和（2026-10-26 由第 3 回合挪来，替换原「尖啸」，且排在炼药之后）、防御（没费）
  3. 瓶中船、罐装幽灵｜炼制药水+（附魔灵魂之力）×3（炼格挡药水、复制药水、异鱼之油）、灵动步法、爆发、残影（中和已挪到第 2 回合）
  4. 格挡药水、异鱼之油、混沌药水（获得与初始药水相同的药水；若瓶中精灵已被消耗则获得狡诈药水）｜预判、复制药水、坚韧之环×2（被复制）、拳斗、偏折
  5. 洗牌｜明晰提取物、明耀酊剂、敏捷药水｜炼制药水+（炼格挡药水）、扫腿、精确切击、生存者
  6. 镣铐药水｜炼制药水+（炼瓶中船）、炼制药水+（炼异鱼之油）、防御、腐朽
  7. 瓶中船、罐装幽灵、爆炸药水｜中和、炼制药水+（炼复制药水）、爆发、残影
  8. 格挡药水、异鱼之油、狡诈药水（如果有）、混沌药水｜预判、复制药水、坚韧之环×2、拳斗、偏折、小刀+×3（如果有）

## 2. 名字 → 游戏对象（已核对 `.recon\pck\localization\zhs`）

| 设计里的名字 | 实际名 | id / 类型 |
|---|---|---|
| 扫腿 | 扫腿 | `LEG_SWEEP` / `LegSweep` |
| 生存者 | 生存者 | `SURVIVOR` / `Survivor` |
| 磨蚀 | 磨蚀 | `ABRASIVE` / `Abrasive`（奇巧：被丢弃时自动打出） |
| 灵动步法 | 灵动步法 | `FOOTWORK` / `Footwork` |
| 精确切击 | 精确切击 | `PRECISE_CUT` / `PreciseCut` |
| 腐朽 | 腐朽 | `DECAY` / `Decay`（状态牌） |
| 夜魇 | 夜魇 | `NIGHTMARE` / `Nightmare` |
| 炼制药水 | 炼制药水 | `ALCHEMIZE` / `Alchemize`（**无色牌**；本模组里「炼哪一瓶」由剧本指定） |
| 防御 / 中和 / 偏折 / 小刀 | 同名 | `DEFEND_SILENT` / `NEUTRALIZE` / `DEFLECT` / `SHIV` |
| 爆发 / 残影 / 预判 | 同名 | `BURST` / `BLUR` / `ANTICIPATE` |
| 坚韧之环 / 拳斗 | 同名 | `TORIC_TOUGHNESS` / `FISTICUFFS` |
| 明耀酊剂 | 明耀酊剂 | `RADIANT_TINCTURE` / `RadiantTincture` |
| 明晰提取物 | 明晰提取物 | `CLARITY` / `Clarity` |
| 敏捷药水 | 敏捷药水 | `DEXTERITY_POTION` / `DexterityPotion` |
| 镣铐药水 | 镣铐药水 | `SHACKLING_POTION` / `ShacklingPotion` |
| 爆炸药水 | **爆炸安瓿** | `EXPLOSIVE_AMPOULE` / `ExplosiveAmpoule`（对所有敌人 = 对所有玩家造成伤害） |
| 罐装幽灵 | 罐装幽灵 | `GHOST_IN_A_JAR` / `GhostInAJar` |
| 瓶中精灵 | 瓶中精灵 | `FAIRY_IN_A_BOTTLE` / `FairyInABottle` |
| 瓶中船 | 瓶中船 | `SHIP_IN_A_BOTTLE` / `ShipInABottle` |
| 格挡药水 / 复制药水 | 同 | `BLOCK_POTION` / `DUPLICATOR` |
| 异鱼之油 | 异鱼之油 | `FYSH_OIL` / `FyshOil` |
| 混沌药水 | 混沌药水 | `ENTROPIC_BREW` / `EntropicBrew` |
| 狡诈药水 | 狡诈药水 | `CUNNING_POTION` / `CunningPotion` |
| 白象雕像（设计笔误） | **白兽雕像** | `WHITE_BEAST_STATUE` |
| 爬行动物饰品 / 炼金箱 / 药水腰带 / 娇嫩蕨草 / 纸鹤 | 同 | `REPTILE_TRINKET` / `ALCHEMICAL_COFFER` / `POTION_BELT` / `DELICATE_FROND` / `PAPER_KRANE` |

## 3. 实现（第一版）

### 3.1 药水栏位（UI）
- 位置：**遗物那一排的左边**，同一行、同样大小的图标（`UI/BossPotionRow.cs`，
  由 `BossStatusRow.Fill` 在排完遗物之后调用，排布沿用 `BossStatusRow.Pin` 的右→左逻辑 ⇒ 天然落在遗物左侧）。
- 图标：`PotionModel.Image`（原版药水贴图），尺寸与遗物图标一致（56）。
- **意图**：如果这瓶药水是「强化类」，图标**下面**写一个「强化」；不是强化类就什么都不写。
- 空栏位不画（只画她真的带着的药水）。

### 3.2 喝药
- 剧本模型：`BossTurn.Potions`（`ScriptPotion` 列表）。每回合**先按顺序喝药，再出牌**（设计稿每回合都是这个顺序）。
- 喝药 = `PotionModel.OnUseWrapper(ctx, target)` + `Player.RemoveUsedPotionInternal(potion)`；
  目标按药水自己的 `TargetType`（单体伤害类打玩家，其余给自己）。
- ~~**调制**：每次喝药她回复 5 点生命~~ —— 用户 2026-10-26 已砍掉（`HealPerPotion = 0`，代码保留）。
- 栏位：建影子玩家后把 `MaxPotionCount` 补到 **8**（原版对影子玩家不给药水栏位）。

### 3.3 炼制药水（`ALCHEMIZE`）
- 剧本里给这张牌标 `BrewPotion = typeof(某药水)`；出牌前把「这瓶要炼什么」记进
  `BossPotions.PendingBrew`，`Alchemize.OnPlay` 的前缀改成**用指定药水**代替随机。

### 3.4 进入方式
- 现在**不进正常阵容**（不碰 `BossLineups`，刀猎不受影响）：控制台
  `fight SILENT_BREWER_BOSS_ENCOUNTER` 直接打（`TierBossEncounters` 里钉死第三幕档位）。
- 后续若要进阵容，需要决定它和「刀猎」怎么共存（同一角色同一幕只能有一个档位设计）。

## 4. 未做 / 待定（用户已确认先跳过）

- **进阶升级**、**多人站位**（投稿未写）。
- 每回合**能量**：**规划按 3 费**（用户 2026-10-25：「因为明耀酊剂的无限续杯，把每回合计划都改成 3 费」），
  但**费用上限仍是 2**（定义值 3 − 「涅奥祝福那套数值」1 点；她身上**没有**这件遗物，那 1 点由 `KeepsBlessingStats` 手动扣）⇒ 能量球显示 `3/2`。
  「没费打不了」仍由剧本规划器按 3 费自然产生（例如第 2 回合 3 费被「夜魇」吃满，两张防御就没费）。
  实现：`SilentBrewerScript.TurnEnergy = 3`；`PrepareTurn` 里「明耀酊剂 +1」那一笔**只在剧本没写死能量时**才补
  （否则会变成 4 费），原版 `RadiancePower.AfterEnergyReset` 对我们已压掉。
- 夜魇复制、复制药水、坚韧之环「被复制」这些需要**跨回合记忆**的语义先按原版卡牌自己走，
  实测有偏差再按用户口径改。

## 5. v0.2.37：「调制」第三条改成通用格挡规则

用户 2026-10-26 改口径（**效果变了，描述也换了**）：

> 每当她在拥有格挡时将要失去生命，改为使用等量格挡抵消伤害。（PS：如果过量了还是会击破格挡的）

以前是「她可以格挡**中毒和魂劫**造成的伤害」——实现上要反射挂 `PoisonPower.Trigger` 与崩坠
`CollectorDoomPower.AfterSideTurnStart` 的**状态机**来认来源，再在 `CreatureCmd.Damage` 核心重载里摘掉
`Unblockable` 位。问题很直观：**别的模组的 DoT 一律吃不到**（灼魂/活体炸弹/各种流血灼烧……），
而且依赖状态机反射、崩坠一改版就可能静默失效。

新实现 `Patches/BossBlockAbsorbPatch.cs`（旧的 `BossBlockableDotPatch.cs` **已删除**）：
挂在所有掉血的唯一出口 ——

```csharp
Creature.LoseHpInternal(decimal amount, ValueProp props)   // 前缀：ref decimal amount
    · 目标不是我们的 Boss → 不动
    · 她的 Block <= 0     → 不动（普通掉血）
    · 否则 absorbed = min(Block, amount) → LoseBlockInternal(absorbed) → amount -= absorbed
```

为什么这样就够：

* 原版**普通攻击**先走 `Creature.DamageBlockInternal`（扣格挡）再把剩下的交给 `LoseHpInternal`
  ⇒ 到这里格挡通常已被扣掉，本补丁**不会重复吃**（这点很关键，否则等于双倍格挡）；
* 中毒 / 魂劫 / 「失去生命」这类 `Unblockable` 伤害**跳过** `DamageBlockInternal`、直接进 `LoseHpInternal`
  ⇒ 正好被这条规则接住，而且**不分来源**，任何 mod 的 DoT 都一样；
* 溢出：`amount` 只减掉被格挡的部分，剩下的照常扣血；格挡扣到 0 = 被击破（`LoseBlockInternal` 走原版
  `Block` setter，血条/格挡数字会跟着刷新）。

日志（前 12 次）：
`调制：她将失去 N 点生命 → 用格挡抵消 M，实际掉血 K，剩余格挡 R（溢出部分照常扣血）`

## 6. v0.2.54～v0.2.57：四处订正（用户 2026-10-27）

用户看上一版的数据说明时逐条纠错，四件事全部按「她原本应该是这样」改：

1. **涅奥的祝福在遗物里 —— 而且是第 2 件**（v0.2.55 更正）。
   ⚠ 上一版这里写的是「她不该有这件遗物、已经删掉」，那是**我听错了**：用户 2026-10-27 的原话是
   「所有 Boss 的第二个遗物都应该是涅奥的祝福（第一个是自己的初始），我是说你给的信息里没有，是信息错了」。
   所以：`SilentBrewerScript.Relics()` 里**显式把 `NeowBlessing` 排在第 2 位**
   （第 1 位是起始遗物蛇之戒指），和刀猎 / 涂毒猎 / 黑球机 / 物理机 / 易伤战那几套写法一致 ——
   以前她不写，`ShadowPlayerFactory` 就把祝福**追加在列表末尾**，于是状态栏里它排在第 10 位。
   `BossVariant` 上那两个「卸掉遗物 / 只留数值」的开关（v0.2.54 临时加的）**已整体回滚**，
   现在又只剩 `BossDefinition.CarriesNeowBlessing`（默认 true，所有 Boss 都带）。
2. **第 2 / 第 7 回合扔的是「爆炸药水」= 游戏里的「爆炸安瓿」（`ExplosiveAmpoule`）**，
   初始 8 瓶里的那一瓶也是它 —— 代码本来就是对的（v0.2.54 我一度误改成火焰药水 `FirePotion`，
   v0.2.57 已改回，用户 2026-10-27 反复强调）。
   意图沿用 `RawAttackIntent`（写死伤害，不吃力量/虚弱），见 `UI/BossPotionRow.IntentFor`。
3. **她的残影不升级**：第 7 回合那张 `残影+` → `残影`（进阶也不会升，进阶 8/9 只升中和、精确切击、拳斗）。
4. **地图图标回到「占位」**：以前借了刀猎的 `silent_shiv`，地图上根本分不出这两个 Boss
   ⇒ 删掉 `SilentBrewerBossEncounter.BossNodePath` 覆写，落回基类 = 原版 `TheKinBoss` 那套占位图标。
5. **「涅奥的祝福」的图标换成「涅奥的牺牲」**（v0.2.55，用户 2026-10-27；对所有 Boss 生效）：
   `Relics/NeowBlessing.cs` 的 `IconBaseName` `neows_talisman`（涅奥的护符）→ **`neows_sacrifice`**（涅奥的牺牲，`NEOWS_SACRIFICE`）。
   两张都是原版资源，不用新建贴图：`res://images/atlases/relic_atlas.sprites/neows_sacrifice.tres`、
   `res://images/relics/neows_sacrifice.png`、轮廓 `relic_outline_atlas.sprites/neows_sacrifice.tres`。
