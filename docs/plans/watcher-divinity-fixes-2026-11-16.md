# 神格观（三层 1 号观者 Boss）五条 bug · 修复计划（2026-11-16）

> 接手自 `docs/plans/handover-watcher-2026-11-15.md`。需求全文见 `docs/plans/boss-watcher-divinity.md`。
> 现行版本 v0.2.679。本轮只动**观者这只 Boss**，其它 32 只一行都不碰。

## 0. 先把事实摆出来（v0.2.679 的实机日志，`APPDATA\SlayTheSpire2\logs\mod_log.txt`，23:12~23:15 那一场）

```
9115: WATCHER_DIVINITY turn 1: hand=3 energy=3/2          ← 第 1 回合 3 费（2 + 灯笼 1）✓ 期望值
9307: 虔信：她进入神格 ⇒ +2 力量，本回合 +3 能量          ← 我们自己又补了一笔 +3
9470: done WATCHER_WISH_P: ... energy=10                  ← 她回合 1 手上能量 = 10
9513: 观者加费：灯笼 +0、神格 +3 ⇒ 本回合 5 能量           ← 第 2 回合又补一次（下回合重复加）
9516: WATCHER_DIVINITY turn 2: ... energy=5/2             ← 第 2 回合 5 费 ✗（应为 2）
9619: WATCHER_DIVINITY turn 3: ... energy=2/2             ✓
9621: 聚能成刃：本回合 X=3 ⇒ 之后的灭除之刃 3 段            ← 规划期算对了
9828: 剧本手牌(3): [WATCHER_EXPUNGER,WATCHER_DEVOTION,WATCHER_SWIVEL]
9831: 意图 WATCHER_EXPUNGER: [LiveSingleAttackIntent]      ← 第 5 回合只有 1 段 ✗（应为 3）
9836: 意图伤害管道 WATCHER_EXPUNGER: 牌面 9 → 11
```

**取舍结论（反编译 `3747526116\Watcher.dll`，别再过一遍）**

| 事实 | 出处 |
|---|---|
| 进神格时**原版自己就 +3 能量** | `Divinity.<AfterApplied>d__8.MoveNext` → `PlayerCmd.GainEnergy(3m, Owner.Player)` |
| 真言满 10 ⇒ `ModifyAmount(-10)` + `EnterDivinity`（**不**先退姿态，`ChangeStance` 内部会先 RemoveAllStances） | `Mantra.<AfterPowerAmountChangedCompat>d__5` |
| 聚能成刃生成的灭除之刃 **进 `(PileType)1`**（= 抽牌堆；实机 draw 堆 3→4、手牌栏里始终没有它） | `WatcherConjureBlade.<OnPlay>d__7` + 日志 9619/9732 |
| 灭除之刃段数 = 牌自己的 `DynamicVars.Repeat`（`HitCount` 属性就是它的 setter） | `WatcherExpunger`：`WithHitCount(DynamicVars.Repeat.IntValue)`；`public int HitCount {get; set;}` |
| 观者 B 版同样：`ConjureBlade` → `WatcherCmd.GiveCard<Expunger>(…, (PileType)1, …)` 且 `Repeat.UpgradeValueBy(x+1)` | `3747492505\Watcher.dll` |

⇒ ① 能量「重复加」的根因就是**我们自己补的两笔**（`BossDevotionPower` 立刻 +3、`_pendingEnergy` 下回合再 +3），
   原版那笔一直都在；② 段数恒为 1 的根因是**第 5 回合的灭除之刃是剧本新造的一张牌**（`Repeat` 是默认值），
   而带段数的那张实体躺在**抽牌堆**里；③ 不展示的根因是 `GeneratedExpungers()` 只扫 `pcs.Hand`。

## 1. A · 能量（最优先）

目标：第 1 回合 3 费、之后每回合 2 费、每次进神格当回合 +3 **且只加一次**（那一笔由原版给）。

1. 删 `CharBossMonster.Turn.cs` 里整块 `#if STS2_111` 的「观者加费」（含 `lantern = 0` 与
   `TakePendingDivinityEnergy`）—— 这是第 2 回合 5 费的唯一来源。顺手删掉只给它用的字段
   `CharBossMonster._watcherLanternGiven`。
2. 删 `BossDevotionPower` 的 `_pendingEnergy`（含 `TakePendingDivinityEnergy`）与 `WatchDivinity` 里的
   `pcs.Energy += 3`。改为**只观察 + 打日志**：进入神格那一刻把 `pcs.Energy` 原样打出来当证据。
3. `BossDefs.Watcher.Energy` 保持 **3**（涅奥的祝福每回合 −1 ⇒ 实际 2；第 1 回合灯笼 +1 ⇒ 3）。不动。

## 2. B · 进神格后意图预览不重算

根因：`RefreshPlannedIntents()` 只拿**旧 Plan** 重算，而计划是玩家回合开始前按 2 费用排的。
新增 `CharBossMonster.ReplanForExtraEnergy(why)`：按**当前真实能量**用 `BossScriptPlanner.Plan`
**整块重规划**本回合（`TurnNumber` 在玩家回合里已经指向她即将到来的那一回合），再
`BladesGrowth.RecomputeAll` → `PublishVanillaIntents` → `BossHandUi.RefreshForce`。
由 `BossDevotionPower.WatchDivinity` 在「她进了神格」那一刻调用。
日志：`进神格 ⇒ 按当前能量 N 整块重规划第 k 回合：M 张 [..]`。

## 3. C · 聚能成刃不展示灭除之刃

1. `GeneratedExpungers()` 改成扫 `pcs.AllPiles`（手牌/抽牌堆/弃牌堆/消耗堆），并**打印它落在哪个堆**。
2. `BossCardPreview.ShowAsync` 造展示副本时**把 `Repeat` 一起抄过去** ——
   它现在是 `ModelDb.GetById` + `CreateCard` 造一张**原版新牌**，段数会回到 1（这就是"段数不对"的另一半）。
   只抄 `Repeat`，其它 Boss（羽化/能量汲取）拿到的牌本来就没有非默认 `Repeat`，行为不变。

## 4. D · 灭除之刃段数恒为 1

1. 她**真的打出聚能成刃之后**，从她所有牌堆里找到那张生成的灭除之刃，读它的 `Repeat`（= 实付 X，含升级 +1），
   存进 `CharBossMonster.WatcherExpungerHits`（战斗内一直保留）。日志把「在哪个堆 + 段数」打出来。
2. 之后每次规划（`BossScriptPlanner.Plan` 开头）把**手牌里的灭除之刃**的 `Repeat` 设成这个数
   ⇒ 剧本第 5 / 9 回合那张新造的牌段数、意图、真实伤害三者一致。
   为此 `BossScriptPlanner` 里原来那条 `xHits = conjuredX - 1` 不再需要（同回合内两边算出来是同一个数，
   跨回合只有 `Repeat` 这条能带上），改为只打诊断日志。

## 5. E · 两个观者模组的卡 id（A `WATCHER_X` / B `WATCHER-X`）两套都认

在 `CharBossMonster.Watcher.cs` 新增 `WatcherIdIs(entry, "CONJURE_BLADE")`：把 `-` / `_` 全部去掉再比后缀
⇒ `WATCHER_CONJURE_BLADE` 与 `WATCHER-CONJURE_BLADE` 都命中。所有运行期按 id 比的地方都改用它
（`IsConjureBlade` / `GeneratedExpungers` / `BossScriptPlanner` 的聚能成刃与灭除之刃分支）。

## 6. 编译 / 安装 / 验证（✅ 已完成，v0.2.680）

1. `dotnet build -p:Sts2Target=107` 与 `111` 各一次，**0 错误** ✅；
2. `tools\check-localization.ps1` → 16 语言全绿 ✅；
3. 版本号两处一起改：`src/CharacterBosses.json` + `BuildInfo.cs` → **v0.2.680** ✅；
4. `cmd /c 'install-mod.bat "…\Slay the Spire 2"'` → `[verify] 两份实现的版本串都是 v0.2.680  OK` ✅；
5. 实机（`charbosses_debug.txt` 写 `watcher@3`，第一幕普通战斗直接变三层档位神格观），日志逐条核对 ✅：
   - `WATCHER_DIVINITY turn 1: energy=2/2`（预算；灯笼 1 点由原版 `Lantern.AfterSideTurnStart` 另给 ⇒ 实际 3）、
     `turn 2/3/4/5: energy=2/2`（不再有 5 费）；
   - `虔信：她进入神格 ⇒ … 能量**不补** … ⇒ 此刻能量 5`（2 + 原版 3）+ `此刻能量 = 5` →
     `done WATCHER_WISH_P: energy=6`（灯笼到账）；
   - `进神格多 3 点能量 ⇒ 按当前能量 5 整块重规划第 1 回合：2 张 […]` ⇒ 她真的多打了一张；
   - `聚能成刃：生成的灭除之刃在「Draw」(1 张)，段数(Repeat)=3` + `预览：用原版界面并排展示 1 张牌`；
   - `灭除之刃：剧本那张的段数 1 → 3` → `意图 WATCHER_EXPUNGER: [LiveMultiAttackIntent]`（11×3）→
     真实伤害 `Ironclad −11` ×3 = 33 ✅；
   - `观者姿态：WATCHER_EMPTY_FIST 退出「平静」⇒ 原版 +2 能量（规划期算入，之后预算 3）` ⇒
     `打 [EMPTY_FIST,WAVE_OF_THE_HAND,DECEIVE_REALITY,SAFETY] 余费=0`（手牌栏「4 张全部会打」）。

### 6.1 顺手修掉的第四笔能量（同一族根因）

「奇迹」（`WATCHER_MIRACLE`）在她身上会回 **2** 点：牌自己 `PlayerCmd.GainEnergy(1)`（反编译实证）+
`CharBossMonster.PlayOne` 里那条通用「回能补偿」又补 1。观者的牌**本来就会给影子玩家加能量**，
所以对这只 Boss 关掉那笔补偿（`Def.Key != "WATCHER_DIVINITY"`）⇒ 聚能成刃的 X 从 4 回到设计值 **3**。

### 6.2 还要记住的两条

- 灯笼那一笔**不要**再写进预算：`Relics.Lantern.AfterSideTurnStart` 的条件是
  `participants.Contains(owner.Creature) && owner.PlayerCombatState.TurnNumber <= 1`，
  影子玩家的回合数已被 `AlignTurnNumber` 对齐 ⇒ 她第 1 回合真的收到 +1（两处都加就是 4 费）。
- 「离开平静 +2 能量」是**她的原版机制**（用户 2026-11-16 自己实机确认），别当 bug 压掉；
  要做的只是让**规划期**把它算进预算，否则面板先压暗、回合里又补打。

### 6.3 装机坑（这一轮又踩了一次）

游戏进程名是 **`SlayTheSpire2`（中间没有空格）**，`Get-Process "Slay the Spire 2"` 永远查不到它
⇒ 会误判「游戏已关」而在游戏运行时装机：`install-mod.bat` 里 `copy /Y … >nul` 静默失败，
**只有正被加载的那份 Impl 会留在旧版**（这一轮就是 Impl111 被锁、Impl107 覆盖成功）。
`verify-deploy.ps1` 只能比对版本串，两份都写着同一个版本号时它**看不出来**。
装机后请再核一次文件大小 / `LastWriteTime`，或直接 `Copy-Item -Force` 试一次——被锁会抛
`being used by another process`。


---

## 7. 第二轮（用户 2026-11-16 晚）—— 三条能量/神格 + 光辉预览 + 平安双重印牌

### 7.1 反编译事实（新增，别再重查）

| 事实 | 出处 |
|---|---|
| 进神格 = `ChangeStance<Divinity>`：**已经在神格里就直接返回**（不退姿态、不给能量） | `WatcherCombatHelper.<ChangeStance>d__29` |
| 离开「平静」固定 +2 能量（**包括被 `RemoveAllStances` 顺手带走的那次**） | A `Calm.AfterRemoved` = `PlayerCmd.GainEnergy(2m, …)`；B `CalmStance.OnExitStance` |
| 真言满 10 层的自动触发点（要压掉它才能自己接管） | A `Mantra.AfterPowerAmountChangedCompat`（由模组的 postfix 反射调用）；B `MantraPower.AfterPowerAmountChanged` |
| 光辉真实伤害 = 牌面 + `WatcherStatePower.TotalMantraGainedThisCombat`（**累计获得**，扣真言不减） | `WatcherBrilliance.OnPlay` + `WatcherCombatHelper.GetTotalMantraGained` |
| 手摇鼓 = `AfterPlayerTurnStart` +1 真言（我们替影子玩家广播这条钩子，所以它真的会到账） | `WatcherMod.Damaru.AfterPlayerTurnStart` |
| 欺瞒现实会**往手牌塞一张 WatcherSafety** | `WatcherDeceiveReality.OnPlay`：`AddGeneratedCardToCombat(CreateCard<WatcherSafety>(), (PileType)2, …)` |
| 灯笼 +1 的时机在**她回合开始**之后（`participants.Contains(Owner.Creature) && TurnNumber <= 1`） | `Relics.Lantern.AfterSideTurnStart` |

### 7.2 落地

1. **灯笼进预算**：`PrepareTurn` 恢复 `energy += 1`；新增 `BossRelicSuppressLanternForWatcherPatch`
   （只压观者那只的原版钩子，其它 32 只不动）。
2. **神格触发接管**：`WatcherRuntimePatches.InstallMantraTakeover()` 压掉模组自动触发；
   `BossDevotionPower.ResolveDivinityAsync()` 做「扣 10 → 退姿态 → 力量 → 进神格」，
   期间 `SuppressCalmExitEnergy = true` 把「平静退姿态 +2」压掉（`InstallCalmEnergySuppress()`，
   A/B 两个钩子各一个前缀）。
3. **光辉预览**：`WatcherCards.TotalMantraGained` / `HasDamaru`；`BossIntentBuilder` 对
   `WATCHER_BRILLIANCE` 加「累计真言 +（玩家回合里）手摇鼓 pending 1」，只对 A 版加。
4. **平安打两次**：`CharBossMonster.BlocksGeneratedCardsEntries` 增加 `WATCHER_DECEIVE_REALITY`（两套 id）。
5. 删掉游戏目录里的 `charbosses_debug.txt`（用户：「不再让第一场战斗变为神格观」）。

### 7.3 装机坑（这一轮又踩了一次，务必记住）

**游戏进程名是 `SlayTheSpire2`（中间没有空格）**，`Get-Process "Slay the Spire 2"` 永远查不到它
⇒ 会误判「游戏已关」而在游戏运行时装机：`install-mod.bat` 里 `copy /Y … >nul` 静默失败，
**只有正被加载的那份 Impl 会留在旧版**，而 `verify-deploy.ps1` 只比版本串**看不出来**。
装机后请再核文件大小 / `LastWriteTime`，或直接 `Copy-Item -Force` 试一次（被锁会抛
`being used by another process`）。

### 7.4 用户傍晚订正（v0.2.683）

- **不再压「平静退姿态 +2」**：「那个第二回合我忘记算退平静了，不是bug，让那2费正常加」
  ⇒ 7.1 表里那条反编译结论仍然有效（`Calm.AfterRemoved` = `GainEnergy(2)`），
  但**不要**去压它：从平静进神格当回合 = 2（平静）+ 3（神格），不在平静 = 3。
  `WatcherRuntimePatches` 里那段 `InstallCalmEnergySuppress` 已整段删除。
- **进阶 8 升级目标是「警惕 / 旋身 / 欺瞒现实」**（不是摆手）。
