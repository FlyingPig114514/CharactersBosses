# 不同步分析：`mod_log (25).txt`（2026-11-09，玩家上报，非本人）

> 文件 20.6 MB / 181301 行 / 最后一次写入 **2026-11-09 17:00**。里面是很多段会话拼在一起（旧日志没清）。
> 结论所依据的都是日志原文 + 反编译，不是猜的；**「推断」的部分单独标出来了**。

---

## 一、先排掉「版本混装」这条老账

上一轮（`docs/plans/desync-2026-11-08-modlog23.md`）那次不同步的根因是**包内两份实现版本不一致**。
这次先查了：

```
workshop\content\CharacterBosses\CharacterBosses.Impl107.dll => 0.2.513
workshop\content\CharacterBosses\CharacterBosses.Impl111.dll => 0.2.513
.dist\CharacterBosses-v0.2.513.zip 内两份                 => 0.2.513 / 0.2.513
```

玩家日志里的构建横幅（第 178295 行）也是 **`v0.2.513`**，加载器选的是 `Impl111`（游戏 `0.1.0+41cef1ea…`，beta）。
⇒ **这次不是混版**，是 0.2.513 上真实复现的不同步。护栏（`tools\pack-dist.ps1`）没被绕过。

## 二、这次的两次不同步发生在哪

摘要由原版 `ChecksumTracker.LogStateDivergence` 触发（**真分叉**，不是我们的探针误报）。

| 日志行 | 时间 | 版本 | 遭遇 | 本机生成 | 轮次 |
|---|---|---|---|---|---|
| 178078 | 2026-11-07 20:46:27 | v0.2.462 | `NECROBINDER_SOUL_BOSS_ENCOUNTER` | **1 只** `[NECROBINDER_BOSS]` | 1 |
| 178238 | 2026-11-07 20:46:54 | v0.2.462 | 同上（重开同一场） | **1 只** | 1 |
| 181232 | 2026-11-09 16:57:12 | **v0.2.513** | `DEFECT_RAINBOW_BOSS_ENCOUNTER` | **1 只** `[DEFECT_BOSS]` | 1 |

三次都是 **2 人局**、都是**轮次 1**（开局约 10 秒，Boss 还没动过）、都是「boss 满血 375/375、玩家已出过牌」。

## 三、真正可疑的东西：**阵容里出现了同一只 Boss 两次**

同一份日志里，这一局开局的阵容预告（第 178305 行）：

```
第 1 层第 1 场阵容：（2 人局，进阶 10，种子 3406292289754331018，指纹 0E8A1970）[1 层 铁甲 1 号（IRONCLAD_ANGER） | 1 层 亡灵 1 号（NECROBINDER_DOOM）]
第 2 层第 1 场阵容：（2 人局，进阶 10，种子 3406292289754331018，指纹 1622DD4D）[2 层 故障 2 号（DEFECT_RAINBOW） | 2 层 故障 2 号（DEFECT_RAINBOW）]   ← 同一只两次
第 3 层第 1 场阵容：（2 人局，进阶 10，种子 3406292289754331018，指纹 96F92A0D）[3 层 猎人 1 号（SILENT_SHIV）   | 3 层 猎人 1 号（SILENT_SHIV）]      ← 同一只两次
```

而 11-07 那次（`NECROBINDER_SOUL×2`）也是一模一样的重复。**对照关系非常干净**：

| 场次 | 阵容 | 是否不同步 |
|---|---|---|
| 一层第 1 场 | `[IRONCLAD_ANGER, NECROBINDER_DOOM]`（两只不同） | **没有**（打了 5 回合，正常） |
| 一层第 2 场 | `[DEFECT_DARK, IRONCLAD_STRIKE]`（两只不同） | **没有** |
| 二层第 1 场 | `[DEFECT_RAINBOW, DEFECT_RAINBOW]`（同一只两次） | **有**（轮次 1） |
| 三层第 1 场 | `[SILENT_SHIV, SILENT_SHIV]`（同一只两次） | （这局没打到） |
| 11-07 二层 | `[NECROBINDER_SOUL, NECROBINDER_SOUL]` | **有**（轮次 1，两次） |
| 11-08 那四次的日志 | 也全是「同族重名」的场次 | **有**（轮次 1 / 3） |

### 3.1 重复阵容的直接后果：2 人局只出 **1 只** Boss

`MultiBossPlan.PartnersForWithKeys` 旧代码是「跳过所有与 `myKey` 相同的条目」：

```csharp
foreach (var key in lineup) { if (myKey != null && key == myKey) continue; ... }
```

阵容 = `[DEFECT_RAINBOW, DEFECT_RAINBOW]`、我 = `DEFECT_RAINBOW` ⇒ **两个条目全被跳过** ⇒ 一只伙伴都不加。
日志正是这么写的：

```
※多 Boss 生成指纹※ 第 2 层第 1 场 · 玩家=2 · 阵容=[DEFECT_RAINBOW,DEFECT_RAINBOW] · 我=DEFECT_RAINBOW · 伙伴=[]（两端这一行必须一致）
※本机生成※ DEFECT_RAINBOW_BOSS_ENCOUNTER → 怪物 1 只 [DEFECT_BOSS]
```

⇒ 2 人局只有 **1 只 Boss**；锁定分配 `DEFECT_BOSS→Awakened`，另一名玩家 `锁定图标 0 个`（摘要里有）。
对照一层那场（阵容不重复）：`我=IRONCLAD_ANGER · 伙伴=[NECROBINDER_DOOM=NECROBINDER_BOSS]` ⇒ 2 只，正常。

### 3.2 为什么会排出重复阵容（反编译 + 代码走查）

`BossLineups.Build` 的 ⑥ 遍（搭子）原来这么写：

```csharp
int mainChar = SyncedMainCharacter(act, fight, BossKeys.CharacterOf(picked[0]));   // ← 读同步槽位
foreach (var ch in partnerChars) { if (ch == mainChar) continue; picked.Add(partnerBuckets[ch][0]); }
```

`SyncedMainCharacter` 读的是 `act.BossEncounter` / `act.SecondBossEncounter`（v0.2.513 里还没有 v0.2.515 的
「读 `_rooms.Boss` 原值」那段）。**这两个 getter 本身是本机状态 + 我们自己的补丁重算出来的**：

* `BossSlotPatches` 里客人端**不写**同步状态、房主端会写（v0.2.237 的规矩）；
* `SecondBossEncounterGetPatch` 把 getter 改成「按当前版本重算」，而重算又会回到 `BossLineups.For`；
* 本局三层阵容是**开局那一刻就预告**的（16:28:57），那时二、三层的槽位还没被替换成「我们的遭遇」。

⇒ 在算「第 2 层第 1 场」的搭子时，读到的「主角色」很可能根本不是 Defect（日志里
`（避开了本局已当过主 Boss 的角色下标：0）` 说明一层主 Boss 是 Ironclad=0），
于是 `ch == mainChar` 没拦住 Defect ⇒ 从搭子桶里又取了一次 Defect 的 1 号键 ⇒ **同一只两次**。

**（推断，未直接证实）** 这一段就是不同步的入口：它是**唯一**一处「阵容由本机/同步时机相关的值决定」的地方，
而阵容长度直接决定「场上几只 Boss」。上一轮 v0.2.503 修掉的正是同一类问题
（当时是「主 Boss 候选表读本机勾选」⇒ 两端阵容长度不同 ⇒ 轮次 1 分叉），症状完全一样
（`房主 2 只 / 客人 1 只`）。这一处只是换了个输入，而它**没有被修过**。

## 四、本次改动（v0.2.526）

1. **阵容只由（种子, 幕, 场）决定** —— `BossLineups.Build` ⑥ 不再读 `SyncedMainCharacter`，
   主角色直接用本机刚抽中的那只（它本身就是种子派生的 ⇒ 两端必然一致）。
   同步值**只保留为诊断**：两端不一致时打
   `⚠ 阵容自检：第 N 层第 M 场「同步遭遇的主角色」=X、「本机抽到的主角色」=Y ⇒ 以本机为准`。
   ⑥ 里还加了「同一个 Key 绝不重复」的硬保险。
2. **`PartnersForWithKeys` 按「只数 = 玩家数」补齐并夹上限**：
   先取与我不相同的条目，不够再把重复的那只也上（宁可上两只同款，也不能 2 人局只出 1 只），
   并夹到 `玩家数 − 1`。这样即使遇到旧阵容（或旧存档里缓存过的阵容）也不会再「少一只」。
3. **`FindFightOf` 采用同步槽位前要求「那场阵容里真的有我」**：以前只要「阵容非空」就用，
   万一槽位指向另一场，就会拿一场根本不含我的阵容去算伙伴数量。
4. **不同步摘要新增一行**（以后**一份日志**就能定位）：
   ```
   多人阵容：玩家=2 第 2 层 阵容指纹[第1场=1622DD4D 第2场=XXXXXXXX] 场上我方 Boss=1 只（两端这一行必须一致）
   ```
   * 两端这一行**不同** ⇒ 阵容分叉；
   * 这一行**相同**但「场上我方 Boss」数不同 ⇒ 生成阶段分叉。
   以前摘要里只有本机自己的 Boss 列表，「该上几只」根本看不到 —— 上一轮只能要求玩家「每人各发一份」。

## 五、还要玩家做什么

1. 让这几名玩家用 **v0.2.526 同一个包**再复现一次（创意工坊要重新上传，目前线上还是 0.2.513）。
2. 如果还不同步：**每人各一份 `mod_log.txt`**。这次摘要里的 `多人阵容：…` 那行 + `※多 Boss 生成指纹※`
   两行并排一比，就能区分「阵容不同」还是「生成不同」，不用再猜。
3. 顺带确认：多人局的 boss 数量是否已经 = 玩家数（2 人局应看到 2 只 Boss、每人各 1 个锁定图标）。

## 六、日志里的其它已知现象（**不是 bug，别再查**）

* `锁定图标巡检：Awakened(本机)=0/1` 紧跟在「锁定图标实例 #3 创建」后面 ——
  `PowerCmd.Apply` 返回时那份 power 还没进 `creature.Powers`，是既知的瞬时态（v0.2.172 起就有注释）。
* 2 人局里「一名玩家锁定图标 0 个」在**阵容重复**时才出现；修好阵容后应当变成 1/1。
* `原版回合结束步骤：AwaitTurnEndAndSwitchSides = 36720ms` 出现在**退出战斗**那一帧，
  是 `CloseToMenu` 之后原版自己在等，与不同步无关（同一会话里正常值是 6.6 秒）。
