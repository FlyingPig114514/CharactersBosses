# 玩家崩溃报告：亡命之徒（HermitRework）打「电球机」· 2026-11-22

> 日志来源：玩家发来的 `mod_log (61).txt`（96 MB，多次启动累积）+ `godot (39).log`。
> 版本：本模组 **本地 v0.2.895**（工坊 0.2.893 被判定为旧版而禁用）。
> 同场其它 mod：BaseLib、SpeedX、Downfall、ChampBeta、HermitRework。

## 一、两份日志里「没有」的东西（重要）

| 想找的 | 结果 |
|---|---|
| C# 未处理异常 / 堆栈 | **一条都没有**（`godot.log` 里连 `Unhandled exception` 都没有） |
| 崩溃转储 `.dmp` | 玩家机器上没取（`%LOCALAPPDATA%\CrashDumps`，用 `tools\read-crash-dump.ps1` 能读） |
| 崩溃那一刻的模组日志 | `mod_log` 停在 **22:53:31**，`godot.log` 一直写到 **22:54:55** —— 因为我们的 `SafeLogPatch` 是**后台线程缓冲**写盘，硬崩会把尾巴丢掉（见 `CrashTrace` 的注释） |

⇒ 结论：这是一次**硬崩溃**（原生层），管理层的钩子没有机会记录；光靠这两份日志定不了性。

## 二、日志里「有」的线索

### 1. canary：`evoke value failed` 63 次

```
[22:53:30] [CharBoss] evoke value failed: Object reference not set to an instance of an object.
```

- 整个文件累计 352 次，但**最后一局（v0.2.895，22:37–22:53，共 2016 行）里就有 63 次**，且全在打「电球机」（`DEFECT_BOSS` + `BOSS_LIGHTNING_ROD_POWER` + `defect_thunder_boss_encounter`）时。
- 反编译根因：`OrbModel.Owner` **可空**，而

```csharp
// LightningOrb
public override decimal PassiveVal => ModifyOrbValue(3m);
public override decimal EvokeVal  => ModifyOrbValue(8m);
// OrbModel
protected decimal ModifyOrbValue(decimal result) => Hook.ModifyOrbValue(Owner.Creature.CombatState, this, result);
// OrbModel
public ICombatState CombatState => Owner.Creature.CombatState;
// LightningOrb.ApplyLightningDamage
var list = CombatState.GetOpponentsOf(Owner.Creature).Where(e => e.IsHittable)...
```

⇒ **`Owner` 为空的球，读 `EvokeVal` 会空引用；原版真正激发它时同样会空引用**。而那一下跑在 Boss 回合的协程里 —— 抛出来就可能整场停住（我们只看到「⚠ 战斗 N 秒没有推进」），也可能把进程带走。

### 2. 每回合都卡 12～18 秒

```
[22:52:46] ⚠ 战斗 12 秒没有任何推进 … 她这一手停在「-」            Boss=[DEFECT_BOSS hp=183 turn=1]
[22:53:06] ⚠ 战斗 12 秒没有任何推进 … 「回合收尾完成(第1回合)」      Boss=[DEFECT_BOSS hp=125 turn=2]
[22:53:23] ⚠ 战斗 12 秒没有任何推进 … 「回合收尾完成(第2回合)」      Boss=[DEFECT_BOSS hp=125 turn=3]
[22:53:30] ⚠ 战斗 18 秒没有任何推进 … 「回合收尾完成(第2回合)」      Boss=[DEFECT_BOSS hp=125 turn=3]
[22:53:30] 原版回合结束步骤：AwaitTurnEndAndSwitchSides = 18286ms
```

`side=Player`、玩家**已经点了结束回合**、按钮还是 `Enabled` ⇒ 卡在「等出牌队列排空 / 等换边」。
（fps 一直 60、我们自己的面板重建都在 1ms 量级 ⇒ 不是我们帧回调的锅，是队列/换边那一段。）

### 3. 崩溃前最后几十行（`godot.log`）

```
[INFO] Monster DEFECT_BOSS performing move CHARBOSS_PLAY
[WARN] Asset not cached: res://scenes/vfx/orbs/lightning/vfx_lightning_orb_evoke.tscn
[WARN] could not find 'hurt' animation on 'Visuals'      ×5
<文件到此结束>
```

宠物 `小异鸟`（BYRDINE，HP 9998/9999，玩家用 `GOD_BYRD` 召的）替玩家吃了全部球伤害
（`球目标：LightningOrb 主人=故障机器人 传入=<null> ⇒ 改成 小异鸟` —— 这是**设计行为**：球先打锁定玩家的召唤物）。

## 三、v0.2.898 先做了什么

1. **`Core/OrbOwnerRepair`**（新增）：4Hz 慢车道扫一遍她球队列，`Owner` 为空的球补回影子玩家
   （`OrbModel.Owner` 的 setter 只在「已有主人且换成别人」时才抛，**从空补上永远合法**）。
2. **`BossOrbMath`**：不再读无主球的 `EvokeVal` / `PassiveVal`，改用反编译常量兜底
   （闪电球激发 8、玻璃球 8、等离子球 1），并且**限流**不再刷屏。
3. **`CrashTrace` 加首过异常面包屑**：`FirstChanceException` 里只记「**原版帧**抛出、又不是我们自己
   代码抛的」空引用 / 无效操作 / 参数为空 / 越界，去重 + 全程最多 20 行。
   下次崩溃时 `logs/characterbosses_trace.txt` 里就会出现「崩之前原版哪一句先空引用了」。

## 三·补：玩家的 `characterbosses_trace.txt` 已经看到了（2026-11-22 23:11 收到）

```
===== 外壳入口 pid=64348 =====
[22:37:32.696] [外壳] Initialize 开始
[22:37:32.701] [外壳] 游戏版本=0.1.0+d09d48e…，探测 beta=True
[22:37:32.705] [外壳] 已载入实现 CharacterBosses.Impl113 1.0.0.0
[22:37:33.080] · Install（进程 pid=64348）
[22:37:33.087] · 启动阶段 1/5 … 5/5
[22:37:46.359] · BaseLib 配置：RegisterCore 完成
<文件到此结束>
```

这份文件是**逐行 `Flush(true)`** 落的盘（不受缓冲影响），结论很硬：

| 期望 | 实际 |
|---|---|
| `!! 未处理异常（进程可能立刻退出）` | **没有** |
| `· 进程正常退出（ProcessExit）` | **没有** |

⇒ 崩的那一刻**没有任何被 CLR 看到的托管异常**，也不是正常退出。
`AppDomain.UnhandledException` 只有「托管异常逃到线程顶层」才会触发；它没触发 + 日志在原生层断掉
⇒ **基本可以断定是原生层硬崩（访问冲突一类）或者被强杀**，托管侧只能靠「崩溃转储」看栈。

所以下一步只有两条路：
1. 让玩家开 **Windows 崩溃转储**（`%LOCALAPPDATA%\CrashDumps`，注册表 `LocalDumps` 或 WER 默认即可），
   崩完把 `SlayTheSpire2.exe.*.dmp` 发来，我们用 `tools\read-crash-dump.ps1` 读异常码 / 归属模块；
2. 或者用二分法定位：先关掉 SpeedX（它在同一帧也在动结束回合按钮），再关掉 HermitRework 的宠物（`GOD_BYRD` 召的小异鸟，
   它是我们球索敌的首选目标）各打一次同款战斗，看还崩不崩。

## 四、还需要玩家提供（一次就能定性）

1. `%APPDATA%\SlayTheSpire2\logs\characterbosses_trace.txt`（**面包屑**，逐行 `Flush(true)` 落盘，
   崩得再快也留得住；v0.2.880 起就有这个文件）。
2. 若有：`%LOCALAPPDATA%\CrashDumps\SlayTheSpire2.exe.*.dmp`
   —— 我们自己的 `tools\read-crash-dump.ps1` 能直接读出「异常码 / 归属模块 / 当时加载的 mod」。
3. 复现步骤（哪一回合、Boss 那张牌、有没有激发球）。

## 五、还没解决的问题（留档）

- **每回合 12～18 秒的换边等待**：`AwaitTurnEndAndSwitchSides` 长达 18 秒，与「球的 Owner 丢失」可能是同一个根因（激发协程抛异常 ⇒ 队列排空不了），也可能是 `StrayBossCardQueuePatch` 与第三方 mod（HermitRework / ChampBeta）的卡面队列互相看不见。v0.2.898 修掉球主人之后需要再看一次是否复现。
- 玩家的 `SpeedX`（皮皮极速，自动继续）与我们的「结束回合探针」在同一帧都在动按钮，若下次还卡，先把 SpeedX 关掉对比。
