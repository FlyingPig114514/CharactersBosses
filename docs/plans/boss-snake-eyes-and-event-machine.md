# 新 Boss 设计稿（用户 2026-11-03 原话，逐字记录）

> 状态：**已全部落地**。蛇眼猎（`SILENT_SNECKO`）落地 v0.2.337；事件机（`DEFECT_EVENT`）落地 v0.2.338。
> 本文件是唯一权威记录，实现时逐条对照。

## 一、二层变体「蛇眼猎」（静默猎手）

> 加2个boss：二层变体蛇眼猎
> 遗物：蛇之戒指、涅奥的祝福、面包、锚、异蛇之眼、抱抱先生、
> 特殊效果：计划妥当：静默猎手每回合第一次抽到耗能为3的卡牌时，将其保留。
> 意图：（由于异蛇之眼，猎人的所有卡牌耗能随机。你把原版的随机拦下来，把卡牌的耗能改为每张牌后的数字。要求改完之后的样子就像是蛇眼改的一样）
>
> 第一回合
> 背刺0、刺杀0。不出：子弹时间3(保留)、先制打击1、打击1、防御2、匕首雨1
> 第二回合
> 冲刺2、突然一拳1。不出：切割2、刀刃之舞1、跟踪3(保留)、子弹时间3（保留）
> 第三回合
> 预判1、闪避翻滚2。不出：斗篷与匕首1、笨拙、受伤、跟踪3（保留）、子弹时间3（保留）
> 第四回合
> 尖啸2、声东击西0（印触不可及）、触不可及（0）。不出：笨拙、究极防御2、谋杀3(保留)、跟踪3（保留）、子弹时间（保留）
> 第五回合
> 子弹时间3（将本回合后面所有牌降为0费）跟踪、谋杀、先制打击、打击、防御、切割
> 第六回合
> 预判1、冲刺0、突然一拳2。不出：斗篷与匕首2、触不可及2
> 第七回合
> 闪躲翻滚1、究极防御1、触不可及1。不出：匕首雨3(保留) 、受伤
> 第八回合
> 子弹时间（将本回合后面所有牌降为0费）、谋杀、匕首雨、先制打击、打击、防御、切割、触不可及
> 第九回合
> 预判0、冲刺2、突然一拳1。不出：斗篷与匕首1、闪躲翻滚1
> 第十回合
> 子弹时间、究极防御、谋杀、匕首雨、受伤
> 第十一回合
> 先制打击1、触不可及0、突然一拳2。不出：打击1、防御2
> 第十二回合
> 预判0、闪躲翻滚1、冲刺2。不出：斗篷与匕首2、切割2
> 之后10～12循环。

### 名字 → 类 / entry

| 中文 | 类 | entry |
|---|---|---|
| 背刺 | `Backstab` | `BACKSTAB` |
| 刺杀 | `Assassinate` | `ASSASSINATE` |
| 子弹时间 | `BulletTime` | `BULLET_TIME` |
| 先制打击 | `LeadingStrike` | `LEADING_STRIKE` |
| 打击 / 防御 | `StrikeSilent` / `DefendSilent` | `STRIKE_SILENT` / `DEFEND_SILENT` |
| 匕首雨 | `DaggerSpray` | `DAGGER_SPRAY` |
| 冲刺 | `Dash` | `DASH` |
| 突然一拳 | `SuckerPunch` | `SUCKER_PUNCH` |
| 切割 | `Slice` | `SLICE` |
| 刀刃之舞 | `BladeDance` | `BLADE_DANCE` |
| 跟踪 | `Tracking` | `TRACKING` |
| 预判 | `Anticipate` | `ANTICIPATE` |
| 闪避翻滚 | `DodgeAndRoll` | `DODGE_AND_ROLL` |
| 斗篷与匕首 | `CloakAndDagger` | `CLOAK_AND_DAGGER` |
| 尖啸 | `PiercingWail` | `PIERCING_WAIL` |
| 声东击西 | `Distraction` | `DISTRACTION` |
| 触不可及 | `Untouchable` | `UNTOUCHABLE` |
| 究极防御 | `UltimateDefend` | `ULTIMATE_DEFEND` |
| 谋杀 | `Murder` | `MURDER` |
| 笨拙 / 受伤 | `Clumsy` / `Injury` | 诅咒 |
| 蛇之戒指 / 面包 / 锚 / 异蛇之眼 / 抱抱先生 | `RingOfTheSnake` / `Bread` / `Anchor` / `SneckoEye` / `MrStruggles` | — |

### 能量（按「打出的牌」倒推，写死在剧本里）

| 回合 | 打出 | 能量 |
|---|---|---|
| 1 | 背刺 0 + 刺杀 0 | 0（`ForceZeroEnergy`） |
| 2 | 冲刺 2 + 突然一拳 1 | 3 |
| 3 | 预判 1 + 闪避翻滚 2 | 3 |
| 4 | 尖啸 2 + 声东击西 0 + 触不可及 0 | 2 |
| 5 | 子弹时间 3 +（后面全部 0 费） | 3 |
| 6 | 预判 1 + 冲刺 0 + 突然一拳 2 | 3 |
| 7 | 闪躲翻滚 1 + 究极防御 1 + 触不可及 1 | 3 |
| 8 | 子弹时间 3 +（后面全部 0 费） | 3 |
| 9 | 预判 0 + 冲刺 2 + 突然一拳 1 | 3 |
| 10 | 子弹时间 3 + 究极防御/谋杀/匕首雨 0 + 受伤 | 3 |
| 11 | 先制打击 1 + 触不可及 0 + 突然一拳 2 | 3 |
| 12 | 预判 0 + 闪躲翻滚 1 + 冲刺 2 | 3 |

### 实现方式

* **异蛇之眼（随机费用）**：原版会给手牌随机改费。我们**拦掉**（剧本手牌本来不用原版随机），
  改用 `ScriptCard.CostThisTurn` 把每张牌的费用写成用户给的数字 ⇒ 卡面显示、规划扣费、实际扣费一致，
  观感就是「蛇眼随出来的那个数」。
* **特殊效果「计划妥当」**：她每回合第一次「抽到」费用为 3 的牌 → 保留。
  我们的手牌是剧本重建的（不是真抽牌），所以直接在剧本里把这些牌标 `Retain`（用户原文里已用「(保留)」标注），
  外加一层 display-only 的 power 说明。
* **子弹时间的「后面所有牌降为 0 费」**：那几张牌的费用直接写 0（`CostThisTurn = 0`），效果与原版一致。

## 二、三层变体「事件机」（故障机器人）

> 3层变体boss事件机（因为它很多牌和遗物都是事件里的）
> 特殊效果:一心化万：故障机器人的万物一心只会将弃牌堆中的0费攻击牌放回它的手牌。每当它有一张牌从弃牌堆回到手牌，所有单位都获得1点力量。
> 遗物：数据磁盘、南瓜蜡烛（5充能，每回合+1费）、旺购客户感恩徽章、选择悖论、大蘑菇、王室猛毒、
> 　　（2026-11-06 两次改动：**削弱事件机，进阶 9 不再获得数据磁盘** ⇒ 数据磁盘从常备遗物移除；
> 　　随后用户要求「**在原来数据磁盘的地方添加意外光滑的石头**」 ⇒ 那一格现在是 `OddlySmoothStone`
> 　　（每场战斗开始 +1 敏捷，由 `BossHookBridge` 在战斗开始补一次）。）
> 1:羽化(印0费的万物，冰枪，超能光束，在玩家面前展示一下这三张牌，就像亡灵契约师使用能量汲取敲牌那样展示)，飞跃(选择悖论印的，带有保留)
> 2:声东击西(印白噪声）、白噪声（0费，印吞噬暗影)、吞噬暗影（0费）冰之长枪（0费）、双重释放
> 3:疯狂科学(混沌，印启动流程（单人）/能量涌动（多人）)、启动流程/能量涌动（0费）、超能光束（0费）、疑虑。PS：让其它3层boss在第三回合加一个判定：没费的牌有费就打
> 4:疯狂进食、热修复+、万物一心(0费，返手超能光束，冰之长枪)、冰之长枪（0费）、超能光束（0费）
> 5:防御（附魔漩涡）、打击、贪婪
> 6:超能光束（0费）、飞跃、冰之长枪（0费）
> 7:疯狂科学(混沌，只印全息影像)、全息影像（0费）、冰之长枪（0费）、双重释放、贪婪
> 8:热修复+、打击、疑虑
> 9:疯狂进食、万物一心(0费，返手超能光束，冰之长枪)、冰之长枪（0费）、超能光束（0费）、防御。
> 之后5～9循环

### 名字 → 类 / entry

| 中文 | 类 | entry |
|---|---|---|
| 羽化 | `Metamorphosis` | `METAMORPHOSIS`（事件牌） |
| 飞跃 | `Leap` | `LEAP` |
| 声东击西 | `Distraction` | `DISTRACTION` |
| 白噪声 | `WhiteNoise` | `WHITE_NOISE` |
| 吞噬暗影 | `ConsumingShadow` | `CONSUMING_SHADOW` |
| 冰之长枪 | `IceLance` | `ICE_LANCE` |
| 双重释放 | `Dualcast` | `DUALCAST` |
| 疯狂科学 | `MadScience` | `MAD_SCIENCE`（事件牌） |
| 混沌 | `Chaos` | `CHAOS` |
| 启动流程 | `BootSequence` | `BOOT_SEQUENCE` |
| 能量涌动 | `EnergySurge` | `ENERGY_SURGE` |
| 超能光束 | `Hyperbeam` | `HYPERBEAM` |
| 疑虑 | `Doubt` | `DOUBT`（诅咒） |
| 疯狂进食 | `FeedingFrenzy` | `FEEDING_FRENZY`（事件牌） |
| 热修复 | `Hotfix` | `HOTFIX` |
| 万物一心 | `AllForOne` | `ALL_FOR_ONE` |
| 全息影像 | `Hologram` | `HOLOGRAM` |
| 防御 / 打击 | `DefendDefect` / `StrikeDefect` | — |
| 贪婪 | `Greed` | `GREED`（诅咒） |
| 附魔「漩涡」 | `Spiral`（`Enchantments.Spiral`） | — |
| 数据磁盘 / 南瓜蜡烛 / 旺购客户感恩徽章 / 选择悖论 / 大蘑菇 / 王室猛毒 | `DataDisk` / `PumpkinCandle` / `WongoCustomerAppreciationBadge` / `ChoicesParadox` / `BigMushroom` / `RoyalPoison` | — |
| **意外光滑的石头**（2026-11-06 取代数据磁盘那一格） | `OddlySmoothStone` | `ODDLY_SMOOTH_STONE`；原版钩子是 `AfterRoomEntered`（影子玩家收不到）⇒ 由 `BossHookBridge.GrantOddlySmoothStoneDexterityAtCombatStart` 在战斗开始补 +1 敏捷 |

### 待实现的机制（都比数据复杂）

1. **一心化万**：`AllForOne` 原版把弃牌堆里的 0 费牌全拿回来；这里改成**只拿 0 费攻击牌**。
   而且要「每当它有一张牌从弃牌堆回到手牌 → 所有单位 +1 力量」⇒ 需要在「弃牌堆→手牌」这条路径上挂计数，
   触发 `PowerCmd.Apply<StrengthPower>`（她 + 每个真人玩家都 +1 = 「所有单位」）。
2. **南瓜蜡烛**（5 充能、每回合 +1 费）：影子玩家的充能类遗物要手动结算。
3. **羽化/疯狂科学的「印牌 + 展示」**：`Metamorphosis` / `MadScience` 的原版是「印 3 张随机牌」，
   我们要拦成**指定牌**（万物/冰之长枪/超能光束 等），并且像「能量汲取」那样**把这三张展示给玩家**。
4. **「让其它 3 层 boss 在第三回合加一个判定：没费的牌有费就打」**：影响所有三层 Boss 的规划器
   （不只为事件机）—— 需要在 `BossScriptPlanner` 里对三层加一条「第 3 回合额外尝试打出本无费的牌」。
5. **选择悖论印的「飞跃」带保留**：`ScriptCard.Retain`（或等价）+ 遗物来源说明。

## 三、事件机落地记录（v0.2.338，2026-11-03）

用户 2026-11-03 的两条补充口径：

* 「疯狂科学印的牌**不用展示**，因为是直接印手牌的。羽化展示是因为印到抽牌堆了。」
  ⇒ 只有**羽化**（`Metamorphosis`）会把 3 张牌摆出来给玩家看（复用 `UI/BossCardShowcase`）；
  疯狂科学 / 白噪声 / 声东击西印的是手牌，本来就看得见，不展示。
* 「以后做完就直接装游戏」⇒ 以后每次 build 完直接把 dll/json/pck 装进
  `<游戏>\mods\CharacterBosses` 与 `steamapps\workshop\content\2868840\3830639796\CharacterBosses`。

### 剧本与能量（落点 `src/Hooks/Core/EventMachineScript.cs`）

| 回合 | 打出（括号里是**实际**费用） | 花掉 |
|---|---|---|
| 1 | 羽化(2，印 0 费万物一心/冰之长枪/超能光束)、飞跃(1，选择悖论印的、带保留) | 3 |
| 2 | 声东击西(1，印白噪声)、白噪声(0，印吞噬暗影)、吞噬暗影(0)、冰之长枪(0)、双重释放(1) | 2 |
| 3 | 疯狂科学(1)、启动流程 单人 / 能量涌动 多人(0)、超能光束(0)；疑虑留手 | 1 |
| 4 | 疯狂进食(0)、热修复+(0)、万物一心(0，返手 2 张)、冰之长枪(0)、超能光束(0) | 0 |
| 5 | 防御(1，附魔漩涡)、打击(1)；贪婪留手 | 2 |
| 6 | 超能光束(0)、飞跃(1)、冰之长枪(0) | 1 |
| 7 | 疯狂科学(1)、全息影像(0)、冰之长枪(0)、双重释放(1)；贪婪留手 | 2 |
| 8 | 热修复+(0)、打击(1)；疑虑留手 | 1 |
| 9 | 疯狂进食(0)、万物一心(0，返手 2 张)、冰之长枪(0)、超能光束(0)、防御(1) | 1 |

之后 **5～9 循环**（`LoopFrom=5, LoopLength=5`）。
能量逐回合写死 **3** = 基础 3 − 涅奥的祝福 1 + **南瓜蜡烛** 1
（`PumpkinCandle.ModifyMaxEnergy`：`KindleCount > 0` 时 +1 能量；充能是「每场战斗结束 -1」，
一场战斗内不会熄灭 ⇒ 整场都是 3）。

### 五条机制各自落在哪

1. **一心化万** —— 「只返 0 费攻击牌」：原版 `AllForOne.Filter` 被
   `Patches/BossAllForOneFilterPatch` 打成恒 false（原版会把 0 费技能/能力也拿回来），
   返哪几张由剧本点名（`ScriptCard.ReturnsToHand`）；「每返一张 ⇒ 所有单位 +1 力量」在
   `Core/EventMachineMechanics.AllForOneReturnsAsync`（她 + 每位真人玩家各 +N）。
   拿牌顺序：手牌已有 → 直接用；弃牌堆有 → 搬回手牌；都没有 → 新建一张 0 费
   （避免「手上 1 张 + 另印 1 张」的两张不同牌）。
2. **南瓜蜡烛** —— 不用额外代码：`ModifyMaxEnergy` 进的是 `MaxEnergy`，
   而 `PrepareTurn` 的 `relicEnergy = pcs.MaxEnergy - Character.MaxEnergy` 已经把它算进去了；
   但剧本仍逐回合写死 3（更抗第三方 mod 干扰）。
3. **羽化的印牌 + 展示** —— 原版随机印牌被 `ShadowBossNoRandomCardPatch` 拦掉，
   改由 `Core/EventMachineMechanics.MetamorphosisPrintsAsync` 按剧本名单造 3 张 0 费牌放进
   **她的抽牌堆**，再用 `UI/BossCardShowcase` 摆出来 2.5 秒（并 `CustomScaledWait(1.1)` 给玩家看清）。
4. **「其它三层 Boss 第三回合没费的牌有费就打」** —— `CharBossMonster.PlayPlannedCards` 里那条
   「余费补打」原本只对灵魂骨（`BossSoulBoundPower`）开放，现在条件加上
   `ActIndex == 2 && TurnNumber == 3` ⇒ **所有第三幕 Boss 的第 3 回合**都补打。
5. **选择悖论印的飞跃带保留** —— `ScriptCard.Retain`（v0.2.337 已有）+ 遗物 `ChoicesParadox`。

### 另外两处必须手写的地方（反编译得出的）

* **疯狂科学**（`MadScience`，TinkerTime 事件牌）的 `Type` 就是 `TinkerTimeType` 字段，
  而那个字段**默认是 `CardType.None`**；它 `OnPlay` 里的 `switch` 只认 Attack/Skill/Power、
  `default` 直接 `throw` ⇒ 造牌时必须显式 `TinkerTimeType = CardType.Attack`（12 伤害单体），
  否则一打出就抛异常。rider 保持 `None`（不额外印牌，印的那张由剧本给）。
* **启动流程 → 能量涌动**（单人/多人）走现成的 `Core/MultiplayerCardSwap` 换牌表
  （`(2, typeof(DefectBoss), typeof(BootSequence), typeof(EnergySurge))`）。

### 接线清单

`BossKeys`（Key / 三层候补 / 互补变体 能力机↔事件机 / `EncounterOf`）、
`BossLineups.EventMachineActive`（salt **57**）、`BossDefs.Defect.VariantsByAct[2]`、
`TierBossEncounters.DefectEventBossEncounter`（`TierAct=2`）、`CharBossEncounters`
（`TierEncounters` + `TierEncounterFor`，新增 `DefectCharacterIndex = 2`）、
`DownfallCollectibleCompat`（藏品 = 万物一心）、`BossSpecialPowerIconPatch`（灯泡图标，
顺手补上 v0.2.337 漏掉的「计划妥当」）。BGM 走三层兜底（女王），无需新增。