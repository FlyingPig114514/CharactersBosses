# 联机不同步：2026-11-06 那次报告之后**我已经做了什么 / 下一步**

> 复盘数据在 `docs/plans/desync-2026-11-06-review.md`（那份是逐条时间线 + 数值对账，写得很细，先读它）。
> 这一页只记「因此改了哪些代码、下次要收什么」。

## 一、结论（一句话）

**不是「哪张牌没同步」** —— 两端动作队列逐条一致（同 id、同卡、同目标，连弃牌选择
`[SNEAKY,FLICK_FLACK]` / `[STRIKE_IRONCLAD]` 都逐字相同）。分叉在**同一个动作的结算**上：

| | 房主（AUTOMATON 视角） | 客人（GUARDIAN 视角） |
|---|---|---|
| 翻越撑击（奇巧自动打出，全体 7 伤） | **落地了**（4/3 点被格挡吃掉，各掉 3/4 HP） | **没落地**（格挡 4/3 分文未动） |
| 反伤 / 反击（对两只 Boss） | 没有 | **落地了**（SILENT −3 格挡 −4 HP，IRONCLAD −4 HP） |

两边各做了「一半」，所以是**结算相位**问题（谁赶在 `After enemy turn end` 校验和之前完成），
不是网络、不是丢包。三局（两次 QuickSL 读档）同一签名复现，房主敌方回合 8.0 s / 客人 3.8 s。

落点最可疑的是我们自己的 **fire-and-forget**（不 await 的异步）副作用 —— 它天生会跨过校验点：

- `Patches/BossRelicHookBridge.cs` → `_ = RunCharonsAshesAsync(...)`（卡戎之灰，**消耗一张牌 → 全体 3 伤**，
  由 `Hook.AfterCardExhausted`（**void 钩子**）的 postfix 触发 ⇒ 想 await 也 await 不了）；
- `Core/BossHookBridge.SideTurn.cs` → `TaskHelper.RunSafely(...)`（战斗开始类遗物）；
- `Core/BossHookBridge.cs` → `FireCombatStartWithoutAwait(...)`；
- `Patches/SoulBoundDrawPatch.cs` → `_ = RunAutomationAsync(...)`。

## 二、本次已经落地的改动

### v0.2.448 —— 回合快照环（`Patches/DivergenceStateProbe.cs`）

在 `Hook.AfterSideTurnStart`（107/111 都有、两端都会被 await 的同步钩子）上记一条紧凑快照：
**所有生物 HP/最大HP/格挡 + 全部 power（按名字排序，保证两端逐字可比）**，环形缓冲 40 条。
不同步时把最近 12 条一起打进 `mod_log.txt`。

⇒ 以后一份 `mod_log.txt` 就能看出「从哪一回合开始不一样」，两份一对就是第一处差别。
**只读，绝不改状态**（帧回调里改状态正是历史上几次不同步的根因，见 `ShadowDiamondDiademOncePatch` 的注释）。

### v0.2.449 —— 逐次 HP / 格挡追踪（`Patches/DesyncEffectTracePatch.cs`）

给两个**唯一数值入口**各挂一条只读日志：

- `Creature.LoseHpInternal`（伤害 / 荆棘反伤 / 自伤都走它）→ `效果追踪 HP  <谁> −N（格挡 X HP a/b props=…）← 调用栈线索`
- `Creature.GainBlockInternal` → `效果追踪 BLK <谁> +N（原格挡 X）← 调用栈线索`

门槛：**本场战斗里有我们的 Boss 才记**（普通战斗零开销），总上限 4000 行，第一次记时会打一行
`效果追踪：本机角色=Host/客人`。

⇒ 这样「翻越撑击那 7 点到底在哪一端、什么时刻落地」就变成可直接判读的事实，
不用再靠两份状态快照反推（那份诊断包里 `damage`/`LoseHp`/`ApplyPower` 命中数全是 **0**）。

## 二·补、玩家 `mod_log (16).txt` 带来的新证据（2026-11-06 晚）

拿到**房主那一端**的 `mod_log.txt`（玩家自发提供，本机日志一条都没有）。它把我们自己的
`玩家侧快照` / `卡戎之灰` / `翻侧` 这些行都记下来了，比对结果：

1. **两端各差恰好 7 点**（玩家侧：`4 格挡 + 3 HP` / `3 格挡 + 4 HP`），
   而那一回合「无卡牌来源」的群伤正好是 **速行者 2×2 = 4** 加 **卡戎之灰 3 = 7**。
2. 房主日志里卡戎之灰是这样打出去的：
   ```
   [15:02:20] 卡戎之灰：STRIKE_IRONCLAD 被消耗 → 对 虚无度日,紫影龙 造成 3 点伤害
   ```
   而它的实现是 `Hook.AfterCardExhausted`（**void** 钩子）postfix 里的
   `_ = RunCharonsAshesAsync(...)` —— 典型的 **fire-and-forget**，任务会跨过回合结束的校验和。
3. 另外发现一条**时间敏感的记账**在真实对局里确实触发过：
   ```
   [13:12:07] 真人玩家出牌登记超时（15s）⇒ 已清空，避免「可命中敌人」整场不翻侧
   ```
   `HittableEnemiesSidePatch` 的翻侧决策是**纯本机记账**（深度计数 + 登记表 + 15 秒过期兜底），
   它决定「敌人」被解释成哪一边。一旦两端记账不同，同一张群攻牌就会**一端打玩家、另一端打 Boss**
   —— 正好是这次「玩家血没动、Boss 掉 4 点」的形状。

### v0.2.450 因此改了什么

- **卡戎之灰改成确定性拍点**：`FireAfterCardExhausted` 里**只登记**，
  由 `FlushPendingAsync` 在「她这张牌打完（`PlayOne` 的每张牌收尾）」和
  「她回合收尾（`FireSideTurnEnd` 开头）」这两个**两端都会被 await 的**同步点结算。
- 新增两行只读取证：出牌前的 `翻侧状态（出牌前 X）：BossActingDepth=… MultiTargetCardDepth=… NarrowToTargetDepth=… 真人出牌登记=N/自动=M 最近登记=…s前`，
  以及 v0.2.449 的逐次 `效果追踪 HP/BLK`。

### 下一份日志要看的行（两端各一份，按时间对齐）

```
效果追踪 HP  …        ← 同一笔伤害在哪一端、哪一刻落地
翻侧状态（出牌前 …）  ← 两端记账是否一致（这是「打错边」的判据）
不同步状态摘要 … + 回合快照 #N
真人玩家出牌登记超时  ← 一旦出现，说明那一段的翻侧记账已经不可信
```



1. **两端都装同一个版本**（这次报告里两端都是 workshop 的 `0.2.416`，而仓库已经到 `0.2.449`；
   这中间有一批联机相关修复）。一端新一端旧必然分叉。
2. 复现之后再导一次 RitsuLib 诊断包 **＋ 两端的 `%APPDATA%\SlayTheSpire2\logs\mod_log.txt`**。
   现在 mod_log 里会自带上面两套探针，直接能对。
3. 我需要的三样东西：
   - 两端 `效果追踪 HP/BLK` 行（看同一笔伤害在哪一端、哪一刻）；
   - 两端 `不同步状态摘要` 里的「最近回合快照」（看第一处不同的回合）；
   - 仍然是同一句：`第 N 层第 M 场阵容` / `变体判定` 行（确认两端抽到的是同一套设计）。

## 四、下一步（有证据之后再做，别盲改）

1. 若 `效果追踪` 证实「卡戎之灰 / 其它 fire-and-forget 副作用跨过了校验点」：
   **把它改成确定性拍点** —— 不在 void 钩子里直接施加，改成「记一笔待结算」，
   然后在下一次**被 await 的**我方流程里结算（`CharBossMonster.PlayOne` 每张牌之后、
   `PlayPlannedCards` 开头、`BossRelicHookBridge.FireSideTurnEnd` 里各 flush 一次）。
   这就是当年修「护喉甲/钻石头冠格挡两端不同步」的同一套写法（压掉原版 + 我们在同步点自己做一次）。
2. 若证实是「奇巧丢弃后自动打出」那次的 `await` 没等完：查 `BossCardSelector` 给出的选择是否
   走的是「假上下文同步返回」，导致原版 `CardCmd.Discard → AutoPlay(SlyDiscard)` 的续体跑到回合之后。
3. 别在帧回调 / UI tick 里改战斗状态（历史教训，见 `docs/plans/multiplayer-block-desync.md`）。
