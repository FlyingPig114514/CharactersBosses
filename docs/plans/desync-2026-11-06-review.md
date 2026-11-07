# 2026-11-06 联机 desync 复盘 — 一层双 CharacterBoss（SILENT_BOSS + IRONCLAD_BOSS），checksum #24「After enemy turn end」

> 只读分析。输入是 `ritsudesync_150225` 里的两份 5000 条 JSON 导出 + `state-divergence-report.txt`。
> 报告文件未做任何修改。分析用的临时中间文件（`.scratch-desync/`）已删除。

## 0. 输入文件的实际位置

任务里给的路径不存在；实际文件在 Temp 下的一个 hash 子目录里：

```
C:\Users\<user>\AppData\Local\Temp\dsh-sxFb2I\ritsudesync_150225\
    local-debug-log.records.json        5000 条  06:51:54.673 .. 07:02:25.223   （HOST  = 7656119… / AUTOMATON）
    remote-debug-log.records.json       5000 条  06:52:28.233 .. 07:02:24.041   （CLIENT= 7656119… / GUARDIAN）
    state-divergence-report.txt         1494 行
```

两台机器时钟不同步（同一事件两端时间戳相差约 0.1～1.0 s，且抖动不稳定），所以下文
**同一行内的两端时间戳只用于排序与相对时长，不做跨端绝对比较**；跨端比较一律用「同一端的相邻事件间隔」。

## 1. 结论摘要（TL;DR）

1. **没有任何一条排队/执行的动作只存在于一端。** 两端的 `ActionQueueSynchronizer` / `Executed action`
   序列完全一致：第 2 回合敌方回合是同样两个 Boss 动作 —— SILENT_BOSS 一次 `CHARBOSS_PLAY`
   （内部：早有准备+ → 丢弃 **鬼祟** 与 **翻越撑击**，两张都是「奇巧」⇒ 被丢弃后自动打出）
   和 IRONCLAD_BOSS 一次 `CHARBOSS_PLAY`（内部：**打击**）。两端的「chose cards」选择结果逐字相同：
   `[SNEAKY,FLICK_FLACK]`、`[STRIKE_IRONCLAD]`。
2. **第一处真正的分歧是把「已经执行过的同一个动作的结算」算出了不同结果**：HOST 把自动打出的
   **翻越撑击（FlickFlack，对所有敌人造成 7 点伤害）** 的 7 点伤害结算到了两名玩家身上；CLIENT 在同一个
   `#24 After enemy turn end` 检查点上**没有**这笔伤害 —— 玩家的格挡与 HP 完全没动。见 §5.3 的逐点对账（四个数值全部对上）。
3. 与此同时 HOST 多了另一处结构性差异：`#24` 这个检查点上 **HOST 的本地玩家（AUTOMATON）已经把手牌抽好了**
   （手牌 3 / 抽牌堆 3），而 CLIENT 同一检查点还是 **手牌 0 / 抽牌堆 6**；两边是同样那 6 张牌，纯粹是「回合开始抽牌」这一个相位谁先谁后。
   远端玩家（GUARDIAN）在两端都是手牌 0 —— 差异只落在**本机玩家**身上。
4. 本次会话一共有 **三局**（两次 QuickSL 读档重开），三局**在同一上下文、同一数值签名上重复 desync**，
   HOST 的敌方回合都比 CLIENT 长一倍多（8.0 s vs 3.8 s），多出来的时间全在 SILENT_BOSS 自动打出那一段里。
   ⇒ 不是网络抖动、不是丢包，是**确定性**分歧。
5. 两端日志里**完全没有**伤害/回血/格挡/能力施加的日志（`damage` / `Damage` / `LoseHp` / `ApplyPower` / `GainBlock`
   命中数全为 0，"Block" 只出现在两份 state dump 里）。所以数值必须从两份 dump + 卡牌数值表反推，见 §5.3、§8。

## 2. 证据基础与降噪规则

用的过滤（丢弃）：
`Act music bank failed*` / `music bank failed to load*`、`State 'CHARBOSS_PLAY' is not included*`、
`Asset not cached*`（后续 §6/§7 单独拿出来当「这一端做了什么 UI 刷新」的信号用）、
`cannot find sfx path*`（同样单独统计次数）、`RunLocationTargetedMessageBuffer: Register message handler*`、
`com.ritsukage.sts2-RitsuLib opcode*`、`NetQualityTracker*`、`BaseLib Looking for NCard*`、
`ActionQueueSet` 的轮询噪声（`Attempting to find ready action` / `Queue for player ... is empty` /
`No action is ready` / `Got ready action` / `becomes new ready action` / `Pausing all player queues`）、
`NetMessageBus Received message ... sending to 1 handlers`（除 divergence 之外）。

保留：所有 `Enqueueing action*` / `Executing action*` / `began|finished execution` / `Sending message to clients to enqueue*` /
`chose cards [...]` / `Generating checksum for context*` / `Played Gem*` / `State divergence*` /
`Current HP ... Block ... Power ...`（dump 内的）、以及 `PlayerChoiceSynchronizer` / `CombatStateSynchronizer` 的状态迁移。

`performing move CHARBOSS_PLAY` 按要求不进事件表，但在 §4 / §5.2 里**保留了两处作为敌方回合的分段锚点**
（它是 HOST 端唯一能标记「哪个 Boss 开始动了」的行）。

关键的系统性旁观日志（非常重要，这是端侧唯一可比的「谁做了什么」的证据）：
- `Player 18446744069414616560 chose cards [SNEAKY,FLICK_FLACK]` —— 影子玩家（SILENT_BOSS 的 netId）做出弃牌选择；
- `Played Gem : GUARDIAN-xxx_GEM`（Downfall Guardian 的日志器 `[Guardian]`）——宝石被打出；
- `Asset not cached: res://images/powers/xxx_power.png` —— 该端**新建/刷新了某个能力图标**；
- `Asset not cached: Guardian\images\gems\emptysocket.png` + `res://Guardian/images/character/energy_text_icon.png`
  —— 该端**重建了 Guardian 的宝石槽/能量 UI**（这是本复盘里最有用的「这一端额外做了事」的痕迹）。

## 3. 本次会话 = 三局，三局都在同一上下文、同一数值签名上炸

`ChecksumTracker` 的 id 在每次读档后从 0 重新计数，所以第 1 局是 #164/165，第 2、3 局都是 #24/#25：

| 局 | HOST 敌方回合(第 2 回合) | CLIENT 同段 | HOST 时长 | CLIENT 时长 | 首次不一致 | 上下文 |
|----|--------------------------|-------------|-----------|-------------|-----------|--------|
| 1 | 06:58:34.834 → 06:58:43.746 | 06:58:34.852 → 06:58:38.730 | **8.91 s** | 3.88 s | #164 | After enemy turn end |
| 2 | 07:00:41.936 → 07:00:49.869 | 07:00:41.957 → 07:00:45.792 | **7.93 s** | 3.84 s | #24 | After enemy turn end |
| 3 | 07:02:13.201 → 07:02:21.203 | 07:02:13.213 → 07:02:17.024 | **8.00 s** | 3.81 s | #24 | After enemy turn end |

第 2、3 局完全相同（同一存档重开），三局的 **Boss 动作序列、弃牌选择、sfx 次数** 全部逐字相同。
三局都各自记了「After player turn start」(#165 / #25) 的第二次不一致，因为 HOST 在断开前又往前走了一步。

## 4. 逐条时间线（第 3 局，即报告对应的那一局；07:01:34 战斗重开 → 07:02:25）

`HOST` = local-debug-log（主机，AUTOMATON），`CLIENT` = remote-debug-log（客机，GUARDIAN）。
`—` 表示该端没有这条记录。**加粗**的是分歧相关行。

| HOST (07:0x) | CLIENT (07:0x) | 事件（已降噪） |
|---|---|---|
| 01:34.423 | 01:35.1* | 战斗开始，`Combat started` / `PreCombatSetup → NotPlayPhase` |
| 01:37.727 | 01:38.6* | ck `After player turn start` id 0 |
| 01:37.755 | 01:38.4* | CLIENT：`PlayCardAction GUARDIAN-DELAY_FIELD`（主机收 `NetPlayCardAction 20`） |
| 01:38.952 | 01:39.5* | HOST：`UsePotionAction FIRE_POTION → Creature 铁甲战士` |
| 01:39.885 | 01:40.4* | CLIENT：`UsePotionAction RADIANT_TINCTURE → 自己` |
| 01:40.394 | 01:40.854 | `radiance_power.png` 图标（两端都建了） |
| 01:42.737 | 01:43.3* | CLIENT：`GUARDIAN-ORBWALK` |
| 01:43.454 | 01:44.0* | HOST：`AUTOMATON-FRONTLOAD` |
| 01:43.126 | 01:43.933 | `strength_power.png` |
| 01:46.143 | 01:46.5* | HOST：`AUTOMATON-CLEAN_CODE` |
| 01:47.630 | 01:48.578 | `Reserved choice id 0`（CLEAN_CODE 的选牌） |
| 01:51.595 | 01:52.543 | 玩家选牌结果 `[AUTOMATON-POSTPONE]`（两端一致） |
| 01:52.213 | 01:52.679 | ck `After enemy turn start` id 10 |
| 01:52.595 | 01:52.713 | *(锚点)* SILENT_BOSS 开始动作 |
| 01:53.249 | 01:53.015 | `speedster_power.png`（鬼祟/速行者相关图标） |
| 01:53.932 | 01:53.113 | *(锚点)* IRONCLAD_BOSS 开始动作 |
| **01:57.948** | **01:54.887** | ck `After enemy turn end` id 11 —— 这里 HOST 已经比 CLIENT 慢 ~3.0 s |
| 01:59.705 ×3 | 01:58.501 ×3 | `Played Gem : GUARDIAN-SAPPHIRE_GEM ×2, GUARDIAN-TOURMALINE_GEM`（两端一致；**这是 Tourmaline=碧玺 的来源**） |
| 01:59.707 | 01:58.502 | `thorns_power.png` ⇒ 荆棘被施加（两端一致，7 点） |
| 01:59.383 | 01:58.461 | CLIENT：`GUARDIAN-PRISMATIC_BARRIER`（宝石就是它打出的）→ 随后 `GEAR_UP`、`SHIELD_CHARGER` |
| 02:01.045 | 02:01.728 | CLIENT 结束回合（`EndPlayerTurnAction turn 2`） |
| 02:02.262 | 02:03.211 | HOST：`AUTOMATON-SAFEGUARD` |
| 02:05.237 | 02:06.178 | HOST：`AUTOMATON-FUNCTION_CARD → target 3`（铁甲战士） |
| 02:08.055 | 02:08.994 | HOST：`AUTOMATON-VERIFY`（选牌 → `[ANGER]`） |
| 02:10.266 | 02:11.211 | HOST：`AUTOMATON-DEFEND_AUTOMATON` |
| 02:11.164 | 02:12.109 | HOST 结束回合（`EndPlayerTurnAction turn 2`） |
| 02:11.169 | 02:12.111 | `Reserved choice id 2` |
| 02:12.082 | 02:13.026 | 选牌 `[AUTOMATON-POSTPONE]`（两端一致） |
| 02:12.096 | 02:13.031 | ck id 21 `After player turn phase one end` |
| 02:12.188 | 02:13.133 | ck id 22 `after player turn phase two end` |
| **02:12.821** | **02:13.180** | **ck id 23 `After enemy turn start`** ← 两端 checklist 最后一个一致点 |
| 02:13.201 | 02:13.213 | *(锚点)* SILENT_BOSS 开始动作 |
| 02:13.730 / 02:13.833 | 02:13.479 / 02:13.501 | `cannot find sfx path: slash_attack.mp3` **×2（两端各 2 次）** |
| 02:13.930 | 02:13.517 | `Player 18446744069414616560 chose cards [SNEAKY,FLICK_FLACK]`（两端一致） |
| 02:13.933 | 02:13.519 | `dampen_power.png` |
| 02:14.847 | 02:13.851 | `sneaky_power.png` |
| **02:18.903** | **02:15.863** | *(锚点)* IRONCLAD_BOSS 开始动作 —— 相对上文，HOST 比 CLIENT 多用了 **2.6 s** |
| **02:19.154 / 02:19.156** | **—（本段没有）** | **HOST 独有：`Guardian\images\gems\emptysocket.png` + `Guardian/.../energy_text_icon.png`**（Guardian 宝石槽/能量 UI 重建，发生在敌方回合**之内**） |
| **02:20.755** | **02:16.754** | `Player 18446744069414649819 chose cards [STRIKE_IRONCLAD]`（两端一致，但 HOST 晚 4.0 s） |
| 02:20.998 | — | `Automaton/images/character/energy_text_icon.png`（HOST 独有） |
| **02:21.203** | **02:17.024** | **ck id 24 `After enemy turn end` ← 首次不一致处**（HOST 1075170546 / CLIENT 3030988735） |
| 02:20.113 | — | HOST 已收到 CLIENT 的 `NetPlayCardAction (NetCombatCard 29) target: 3`，被记成 `paused and candidate action ... has type CombatPlayPhaseOnly`（**说明 CLIENT 此时已经进入出牌相位，HOST 还在敌方回合里**） |
| — | 02:17.106 | CLIENT：`Reserved choice id 3`（命运同担：AUTOMATON 下一回合开始弃 1 张） |
| — | 02:17.571 / 02:17.583 | CLIENT 的 Guardian 宝石槽/能量 UI 重建（**在敌方回合结束之后**才做，与 HOST 的 02:19.15 是同一件事、不同相位） |
| — | 02:17.804 | CLIENT：ck id 25 `After player turn start`（HOST 在断开前**从未**生成 id 25） |
| — | 02:20.984 → 02:21.124 | CLIENT：`GUARDIAN-TEMPORAL_STRIKE → 3` 打出并执行完 |
| 02:21.205 | — | HOST：`State divergence detected! Checksum with ID 24 ... Local: 1075170546. Remote: 3030988735. Context: After enemy turn end` |
| 02:22.884 | 02:23.192 | `GUARDIAN-TWIN_SLAM → 3`（CLIENT 发出，HOST 收到，同样被 pause） |
| 02:24.840 | — | HOST：`Reserved choice id 3`（比 CLIENT 晚了 7.7 s） |
| 02:25.223 | 02:24.041 | 两端 `NetMessageBus Received message ... StateDivergenceMessage` |
| — | 02:2x（第 1/2 局） | CLIENT：`Disconnected. Reason: StateDivergence`（第 3 局日志截断在 07:02:24.041） |

## 5. 第一处分歧：精确表述

### 5.1 首先排除「动作缺失」

- 两端的动作队列内容逐条相同（同 id、同卡、同目标）：`#12 PRISMATIC_BARRIER`、`#13 GEAR_UP`、`#14 SHIELD_CHARGER`、
  `#15 EndPlayerTurn(CLIENT)`、`#16 SAFEGUARD`、`#17 FUNCTION_CARD→3`、`#18/19 VERIFY(+ANGER)`、`#20 DEFEND_AUTOMATON`、
  `#21 EndPlayerTurn(HOST)`、`#24/25 ReadyToBeginEnemyTurn`、`#22/23 GenericHook(CLEAN_CODE)`。
- 敌方回合两个 `CHARBOSS_PLAY` 内部的选牌结果两端逐字相同：`[SNEAKY,FLICK_FLACK]` / `[STRIKE_IRONCLAD]`。
- `slash_attack.mp3` 两端各 2 次（= 翻越撑击对两名玩家各 1 次命中）⇒ **攻击确实执行了**。
- 因此「哪个 action 只在一端」这个问法的答案是：**队列层面没有**。

### 5.2 真正的第一处分歧（两句话）

> **同一个已经被两端执行的动作——SILENT_BOSS 的「奇巧」自动打出 翻越撑击（FlickFlack，对所有敌人 7 点伤害）——
> 在 HOST 上把 7 点伤害结算给了两名玩家；在 CLIENT 的同一个 `#24 After enemy turn end` 检查点上，这笔伤害还没有（或永远没有）落地。**
>
> 与之镜像地，CLIENT 侧的**反击/反伤类结算**（对 SILENT_BOSS 的 7 点、对 IRONCLAD_BOSS 的 4 点）发生了，而 HOST 侧没有。
> 也就是说：一端只完成了「攻击命中玩家」那一半，另一端只完成了「反应回击攻击者」那一半，**没有任何一端把两半都做完**。

时间点上，最靠近分歧的可观测行是 SILENT_BOSS 那次 `CHARBOSS_PLAY` 的自动作段：
HOST `02:13.93 chose [SNEAKY,FLICK_FLACK]` → `02:18.90 IRONCLAD_BOSS 开始动`（**中间 5.0 s**，其中 `02:19.15` 还在重建 Guardian 宝石 UI），
CLIENT 同一段 `02:13.52 → 02:15.86`（**2.3 s**，无任何 UI 重建）。整个敌方回合两端 8.00 s / 3.81 s。
⇒ **多出来的解析工作发生在 HOST 的「自动打出」那一段里**，而状态差也只体现在那段应该产生的效果上。

### 5.3 数值对账：那 7 点就是翻越撑击

`翻越撑击 / FlickFlack`：本仓库自带的两份独立来源都写着 **全体 7 伤**：
- `docs/plans/cards-silent.md:27` —— `| 1 | 普通 | 翻越撑击 | 对所有敌人造成7点伤害。 | FlickFlack |`
- `docs/plans/boss-silent-sly.md:48` —— `| 翻越撑击 | FlickFlack | 1 | **全体 7 伤**（升级 +2）；自带 Sly |`
- `.build/gameloc/eng/cards.json` —— `FLICK_FLACK.description = "Deal {Damage:diff()} damage to ALL enemies."`，
  `SPEEDSTER.description = "Whenever you draw a card during your turn, deal ... damage to ALL enemies."`（**只在她的回合、她抽牌时**，
  所以 HOST 抽 3 张牌**不会**触发速行者，7 点不是速行者）

第 3 局（报告那一局）逐点对账 —— 前提：`#23 After enemy turn start` 校验和一致 ⇒ 进敌方回合时两端 HP/格挡完全相同：

| 生物 | HOST | CLIENT | CLIENT−HOST | 解释 |
|---|---|---|---|---|
| p0 AUTOMATON | HP 24 / 格挡 0 | HP 27 / 格挡 **4** | +3 HP, +4 格挡 | HOST：挨 7 点，**4 点被格挡吃掉**，掉 3 HP ✔<br>CLIENT：格挡 4 分文未动 ⇒ **0 点伤害** |
| p1 GUARDIAN | HP 19 / 格挡 0 | HP 23 / 格挡 **3** | +4 HP, +3 格挡 | HOST：挨 7 点，**3 点被格挡吃掉**，掉 4 HP ✔<br>CLIENT：格挡 3 分文未动 ⇒ **0 点伤害** |
| SILENT_BOSS | HP 183 / 格挡 7 | HP 179 / 格挡 4 | −4 HP, −3 格挡 | 见 §7（CLIENT 侧 7 点反伤：格挡 3→0、掉 4 HP，随后 +4 格挡） |
| IRONCLAD_BOSS | HP 180 / 格挡 9 | HP 176 / 格挡 9 | −4 HP, 格挡不变 | 见 §7（4 点直穿格挡） |
| p1 MODE_SHIFT | 26 | **30** | +4 | 见 §7（防御模式/碧玺相关，未定论） |

第 1 局（另一份状态）给出**无格挡干扰的干净样本**，同一个 7 点再次出现：

| 生物 | HOST | CLIENT | 差 |
|---|---|---|---|
| p0 AUTOMATON | HP 19 / 格挡 **0** | HP 26 / 格挡 **0** | **恰好 7 HP**（无格挡，原样可见） |
| p1 GUARDIAN | HP 21 / 格挡 0 | HP 16 / 格挡 **12** | 差 −5 HP / +12 格挡（Guardian 防御模式保留格挡，见 §7） |
| SILENT_BOSS | HP 186 / 格挡 7 | HP 182 / 格挡 4 | −4 HP, −3 格挡（**与第 3 局完全同签名**） |
| IRONCLAD_BOSS | HP 174 / 格挡 9 | HP 173 / 格挡 9 | −1 HP |

> 结论的可信度：两名玩家的「7 点 + 格挡原封不动」在**两局独立状态**下都对上，且第 1 局 p0 格挡为 0 时差值就是干净的 7。
> 单靠巧合凑出 (4 格挡→0 掉 3) 与 (3 格挡→0 掉 4) 同时成立的概率极低。

### 5.4 另一处独立的结构性差异：**本机玩家**的回合开始抽牌

`state-divergence-report.txt` 的 `players[0].piles.*`（同一 `#24` 检查点）：

```
players[0].piles.Hand   Local: 3 card(s); remote: 0 card(s)
  00: local=CARD.AUTOMATON-BRANCH        ; remote=Missing
  01: local=CARD.AUTOMATON-DEFEND_AUTOMATON ; remote=Missing
  02: local=CARD.AUTOMATON-BUGGY_MESS    ; remote=Missing
players[0].piles.Draw   Local: 3 card(s); remote: 6 card(s)
  00: local=CARD.ASCENDERS_BANE      ; remote=CARD.AUTOMATON-BRANCH
  01: local=CARD.AUTOMATON-DEFEND_AUTOMATON ; remote=CARD.AUTOMATON-DEFEND_AUTOMATON
  02: local=CARD.AUTOMATON-DEPRECATE +1 ; remote=CARD.AUTOMATON-BUGGY_MESS
  03: local=Missing ; remote=CARD.ASCENDERS_BANE
  04: local=Missing ; remote=CARD.AUTOMATON-DEFEND_AUTOMATON
  05: local=Missing ; remote=CARD.AUTOMATON-DEPRECATE +1
```

- 两端是同一组 6 张牌；CLIENT 的抽牌堆顺序 = HOST 抽牌堆 + HOST 手牌（`BRANCH, DEFEND, BUGGY_MESS` 在前）
  ⇒ **HOST 已经把顶 3 张抽进手牌，CLIENT 一张都没抽**。
- 第 1 局 dump 里是**同一个签名**（HOST 手牌 3 = BRANCH/DEFEND/BUGGY_MESS，抽牌堆 3 = BANE/DEFEND/DEPRECATE+1；
  CLIENT 手牌 0、抽牌堆 6）⇒ 可复现。
- `players[1]`（GUARDIAN）两端都是 `Hand 0 / Draw 6`，`Discard` 两端都是 7 张、`Exhaust`/`Play` 两端一致。
  ⇒ 差异**只出现在本机玩家**（HOST 端的 AUTOMATON / CLIENT 端的 GUARDIAN 各自视角里的「自己」），
  远端玩家没有差异。这不是某个动作没同步，而是**「玩家回合开始」这一步在 HOST 上被提前算进了敌方回合结束窗口**。
- 交叉验证：HOST 直到 `02:24.840` 才 `Reserved choice id 3`（命运同担的弃牌选择），CLIENT 在 `02:17.106` 就做了 ——
  HOST 的「回合开始」链整条都被拖进了敌方回合尾部/被 divergence 打断。

## 6. 重复出现 / 只在一端出现的记录

**没有任何一条动作被重复执行**（每个 `ActionQueueSet` id 只执行一次，两端 id 序列一致）。可能的「双份」都是合法的两段效果：

| 记录 | 出现次数 | 判定 |
|---|---|---|
| `Played Gem : GUARDIAN-SAPPHIRE_GEM`（同一毫秒两条） | 两端各 2（01:59.705 / 01:58.501） | **合法**：同一次 `PRISMATIC_BARRIER` 真的插了 2 颗蓝宝石 + 1 颗碧玺 |
| `cannot find sfx path: slash_attack.mp3` | 两端各 2 | **合法**：翻越撑击命中两名玩家各 1 次 |
| `Reserved choice id N`（同 id 重复） | 无 | — |
| `Generating checksum ... id: N` | 两端各 1 次/上下文（除 `#25`） | 见下 |

**只在一端出现（HOST）**：
1. `02:19.154 Guardian\images\gems\emptysocket.png` + `02:19.156 .../energy_text_icon.png`
   —— 在**敌方回合之内**重建 Guardian 宝石槽 UI。第 2 局同样：`07:00:47.004 / 47.009`（甚至在 IRONCLAD_BOSS 开始动作 `47.590` 之前）。
   第 1 局该处没有 gems 资源行，取而代之是 `06:58:42.202 res://Automaton/images/character/energy_text_icon.png`（同样落在敌方回合窗口内）。
2. `02:20.998 res://Automaton/images/character/energy_text_icon.png`（HOST 的本地玩家能量 UI 重建）。
3. `ck id 24` 之后 HOST 从未生成 `ck id 25 After player turn start`。
4. `State divergence detected! Checksum with ID 24 ...`（HOST 是检测方，只有它有这条）。

**只在一端出现（CLIENT）**：
1. `02:17.571/17.583` Guardian 宝石槽 + 能量 UI 重建 —— 与 HOST 的 `02:19.15` 是同一件事，但**在敌方回合结束之后**。
2. `02:17.804 ck id 25 After player turn start`，以及随后正常打出的 `TEMPORAL_STRIKE` / `TWIN_SLAM`。
3. `Reserved choice id 3` 早于 HOST 7.7 s。
4. `sts2.piyixiajiuhenfen.speedx: skipped AnyEnemy: multiple valid targets, quickPlay=False`（CLIENT 独有 mod，无 gameplay 影响）。
5. `Disconnected. Reason: StateDivergence` / `JoinFlow` / `HandshakeManager`（第 1、2 局的断线重连流程）。
6. **CLIENT 端的 `#24` 之后没有任何一步把「翻越撑击的 7 点」补算回来**：`02:17.024 → 02:17.804` 之间只做了
   `Reserved choice id 3` 与 UI 重建，然后直接 `After player turn start`。

## 7. 荆棘 / 格挡顺序 / 伤害反伤专项

- **荆棘（THORNS）7 点在两端的行为能力完全一致**：`POWER.THORNS_POWER (荆棘):7` 两端都有、`thorns_power.png`
  两端都在 `01:59` 建过图标（来自 `GUARDIAN-PRISMATIC_BARRIER` 打进 2×蓝宝石 + 1×**碧玺**）。
  值得注意的是碧玺 `GUARDIAN-TOURMALINE_GEM_POWER (碧玺):4` = **4**，正好等于两端对两名 Boss 的 HP 差（4 / 4）。
- **玩家侧（HOST 独有）**：两名玩家被打中 7 点，且**先扣格挡、再扣 HP**（4→0 掉 3 HP；3→0 掉 4 HP）
  —— 说明 HOST 这条路上「格挡抵扣」是正常的，问题只在于 **CLIENT 这条路整笔没算**。
- **Boss 侧（CLIENT 独有）**：
  - SILENT_BOSS：CLIENT 少 4 HP、少 3 格挡。若把它读成「先有 3 格挡、挨 7 点 ⇒ 格挡 3→0、掉 4 HP，随后又 +4 格挡」，
    则它同样是一笔**恰好 7 点**的反伤，与玩家侧的 7 点形成镜像（正是荆棘 7 点反射给攻击者）。
  - IRONCLAD_BOSS：CLIENT 少 4 HP、格挡 9 两端相同 ⇒ 一笔 **4 点直穿格挡**（等价于碧玺 4 点的反击）。
  - 第 1 局 SILENT_BOSS 同样是「−4 HP / −3 格挡」，IRONCLAD_BOSS 只差 1 HP ⇒ Boss 侧的数值随局面而变，但 4/3 这一组很稳定。
- **GUARDIAN-MODE_SHIFT 26 vs 30（CLIENT +4）**：与「碧玺 4 / 反伤 4」同量级，且 Guardian 的**防御模式**
  （`GUARDIAN-DEFENSIVE_MODE_POWER:1`）意味着她的格挡会**跨回合保留**（第 1 局 CLIENT 侧 p1 在回合开始后格挡从 12 涨到 22），
  因此「格挡/模式转换的结算顺序」在这套角色+这套 Boss 上天然敏感。
- **没有任何一端出现真正的「双向正确」**：HOST 有玩家受伤、无 Boss 反伤；CLIENT 有 Boss 反伤、无玩家受伤。
  这正是「格挡/反伤/伤害三者的结算顺序在两端不同」的形态，而不是「某张牌少了/多了」。

## 8. 仍未解释的残差（诚实清单）

1. IRONCLAD_BOSS 的 −4 HP（CLIENT 侧）—— 4 点直穿格挡的来源没有直接日志证据；候选：碧玺（4）/ 荆棘的部分反射 / `SNEAKY`（"Whenever another player attacks an enemy, gain Block"，是**她**获得格挡，不是别人掉血）。
2. `MODE_SHIFT` +4 的触发条件（Downfall 侧代码不在本仓库，`references/` 里只有 `sts2.dll`）。
3. 玩家侧「7 点」为何是 ROUND 整数：如果 CLIENT 走的是「先 +7 格挡、再挨 7 点」的**顺序**，数值上也能得到
   `格挡 4 / 不受伤`。这条备选假设同样属于「格挡增益 vs 伤害的先后顺序」这一类，没有证据可以否掉它；
   要区分只能靠「谁加了 7 点格挡」的日志（当前不存在）。
4. 三局都是**同一局战斗的第 2 回合**炸，所以无法判断这是「只在敌人回合的自动打出路径」还是「回合开始抽牌相位」独占的问题。

## 9. 这些 dump 不能告诉我们什么（以及要补什么日志）

已核实：两份 JSON 里
`damage` / `Damage` / `LoseHp` / `ApplyPower` / `PowerApplied` **命中数都是 0**；
`Block` 只出现在两份 state dump 文本里；`GainBlock` / `Channel` 的命中全部是 RitsuLib 的 `channel=-1` 与异常栈文本。
⇒ **本 bundle 里没有任何逐次伤害/格挡/回血记录**，本报告的数值全部由「两端 state dump + 卡面数值表」反推，逐点对账见 §5.3。

要把这件事钉死，下次 desync 只需要在这些位置各加一行日志（本模组自己的 `Logger.Log` 就够）：
1. `Hook.AfterCardDrawn` / `CreatureCmd.Damage` / `CreatureCmd.GainBlock` 的**入口与出口**（带 `creature.NetId`、amount、source 卡/能力、`LocalContext.NetId`）；
2. 「奇巧（Sly）被丢弃 ⇒ 自动打出」的那次 `CardCmd.AutoPlay`：谁触发、打出的是哪张、`await` 有没有等它完成；
3. `Hook.AfterPlayerTurnStart` 的**开始/结束**（仓库里已经有 `src/Hooks/Patches/EndTurnButtonTracePatch.cs` 在打这个点，把这行同时写进 `git` 侧的 dump 即可）；
4. `PowerModel.OnApplied/AfterRemoved` 与 `THORNS` 反射伤害的目标 `creature`（用来确定反伤到底打给了谁）。

## 10. 代码侧审计短名单（按怀疑度排序，均为假设）

1. **「不 await 的异步钩子」**——这是最符合本现象的一类：
   `src/Hooks/Core/BossHookBridge.SideTurn.cs:991 SafeRunWithoutAwait(...)`、
   `src/Hooks/Patches/BossRelicHookBridge.cs:89 _ = RunCharonsAshesAsync(...)`（Charon's Ashes = 消耗牌时对全体造成伤害）、
   `src/Hooks/Patches/SoulBoundDrawPatch.cs:200 _ = RunAutomationAsync(...)`、
   `src/Hooks/Core/BossHookBridge.cs:322 FireCombatStartWithoutAwait(...)`。
   fire-and-forget 的伤害/格挡结算会**落在不同的相位里**（HOST 在敌方回合内、CLIENT 在 checksum 之后），
   与「HOST 状态超前 + 时长多 4 s」完全吻合。
2. **`SneakyOnBossAttack`（`src/Hooks/Patches/SneakyOnBossAttack.cs`）与 `SneakyOnlyBossAttacksPatch`**：
   它们给 SILENT_BOSS 补算 `SneakyPower` 的格挡，且在 `CharBossMonster.cs:1444-1453` 里是**出牌前快照 + 出牌后补差**的两段式。
   这套「快照→补差」在两端若能读出不同的 `sneakyBefore`，就会得到不同的格挡（对应 §7 里 Boss 侧的 3 点格挡差）。
3. **`Hook.AfterPlayerTurnStart` 相关的接管**：`src/Hooks/Patches/PlayerTurnStartHookSafetyPatch.cs`、
   `EndTurnButtonTracePatch.cs`、`SharedFateDiscardPower.cs:37`（`AfterPlayerTurnStart` 里选牌）。
   本机玩家的「回合开始抽牌 + 命运同担选牌」在 HOST 上明显跑进了敌方回合尾部。

## 11. 一句话给下一个会话

> 别去查「哪张牌没同步」——没有动作丢。查 **`翻越撑击` 被丢弃后自动打出那一次的结算顺序**：
> 一端算出了「7 点打到两名玩家」，另一端算出了「反伤打到两名 Boss」，而且两端对「本机玩家的回合开始抽牌」
> 是否已经发生也对不上（HOST 手牌 3 / CLIENT 手牌 0）。三局复现、时长差 4 s，说明是**同步/异步结算相位**问题，
> 不是网络问题。
