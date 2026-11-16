# 自检：多人联机 n10「第三幕第二个 Boss」疑似不同步（2026-11-16，v0.2.710）

> 反馈来源：工坊评论区，无日志。「房主看到的是俩玩家 Boss，客人看到的是一个原来的 Boss」。
> 本轮做的是**静态自检**：把「决定第三幕第二场上哪只 Boss」的全过程逐环节过了一遍。
> 结论：**没有找到「必然分叉」的代码**，但找到了 **1 个真 bug**（已修）和 **4 处「只要两台机器输入不同就会分叉」的输入口**（其中 2 处只能靠改协议根治）。

## 0. 这一场的决策链（先认路）

```
PullNextEncounter(Boss)                      ← ActModel.PullNextEncounterPatch（唯一漏斗）
 └ __result = RoomSet.NextBossEncounter       ← bossEncountersVisited != 0 && SecondBoss != null ? SecondBoss : Boss
    （反编译确认：SecondBoss 是 RoomSet 的普通字段，随存档/状态一起序列化）
 ├ IsOurs(__result) ? 沿用地图上那只 : ForVanillaBoss(act, __result, secondBoss)
 │   └ 客人分支：同步槽位是我们的 ⇒ **直接跟随房主**
 │               否则 ⇒ 用「确定性规则」自己算一遍（= 房主用的同一套算法）
 └ 造怪：EncounterModel.GenerateMonsters … MultiBossPlan.PartnersForWithKeys(myKey)
```

两条铁律：①「第几场」由原版 `RoomSet.bossEncountersVisited` 判（存档字段，两端一致）；
②「这一场是谁」由**阵容** `BossLineups.For(幕, 场)` 决定，而阵容是 `(种子, 幕, 场, 勾选)` 的纯函数。

## 1. 找到的真 bug（已修）：`WantsSecondBoss` 与 `PlayerCount` 口径不一致

```csharp
// MultiBossPlan.WantsSecondBoss（改前）
playerCount = state?.Players?.Count ?? 1;      // ← 读不到就按「1 个人」
return playerCount >= 2;

// BossLineups.PlayerCount（2026-11-05 就加了兜底）
if (n > 0) { _lastKnownPlayers = n; return n; }
return _lastKnownPlayers > 0 ? _lastKnownPlayers : 1;   // ← 读不到就沿用记住的
```

`BossLineups.Build` 用 `PlayersPerFight()`（= 有兜底的那个）决定**排几只**，
而 `PartnersForWithKeys` 用 `WantsSecondBoss()`（= 没兜底的那个）决定**造几只**。
只要有一瞬间 `RunManager.Instance` 拿不到（拉房间 / 换幕那一刻就会），
就会「阵容按 2 只排、伙伴一只都不加」⇒ **一方 2 只、另一方 1 只**。

改法（v0.2.710）：`WantsSecondBoss` 读不到时改用 `BossLineups.PlayerCount()`，两边同一个数，并打一行
`多人判定：这一刻读不到 RunState 的玩家数 ⇒ 沿用记住的 N 人`。

## 2. 四处「输入可能不同」的口子（按可能性排序）

### A. 观者模组装没装，会改变**三层候补表**（最可疑，正好只在第三幕）

`BossKeys.RosterFor(2)` 末尾 `Concat(WatcherRoster())`，而 `WatcherRoster()` 只看本机
`WatcherModPresence.Installed` ⇒ 装了 = 三层候补多两只（观者），没装 = 少两只。

后果：`Bucketize` 的角色集合不同 ⇒ ⑤「按种子排好的角色次序里挑第一只没当过主 Boss 的」
可能挑到**不同角色**（一边挑到观者，另一边跳过观者挑下一个）⇒ 阵容分叉 ⇒ 第二场的主 Boss 不同。
`SubstituteIfUnavailable` 只会把 `WATCHER_*` 换成 `FOOL_TAROT`（防止坏档），**不会让两端打同一只**。

- 确认行（两端各自）：`※阵容输入※ … 候补表=16 条（含观者=True）` vs `候补表=14 条（含观者=False）`。
- 根治：多人时「全端都装了观者」才把观者放进阵容（要客人上报能力位），或多人一律不放观者。

### B. 客人没收到房主的勾选/玩法设置 ⇒ 退回「整张表 + 模组地图默认不替换」

`BossPicksSync` 走 BaseLib 的自定义消息。**客人没装 BaseLib、或消息还没到、或房主刚改过设置**，
客人就会：主 Boss 候选表 = 整张表（房主可能勾掉了不少）；地图替换开关 = 默认值
（`IsVanillaMap` ⇒ 原版图替换、**模组加的图不替换**）。

最后一条正是「客人看到一个原来的 Boss」的形态：房主在配置里给某张**模组地图**开了替换 ⇒ 房主换成角色 Boss；
客人按默认「模组图不替换」⇒ **整场不替换**。

- 确认行：客人 `本机是**客人**⇒ 不读本机「地图替换开关」…（地图 X 按默认值：不替换）`、
  `勾选同步：本机没有 BaseLib ⇒ 关闭「房主勾选/玩法设置广播」`、`※阵容输入※ … 已收到房主勾选=False`。
- 根治：房主把「本幕两场的**最终遭遇**」直接广播（客人无条件采用），而不是只广播「设置」。

### C. 第二场的两条路径不对称（放大 A/B 的后果）

- 第一场：`ApplyDiscoveryOrderModifications` / `GenerateRooms` 时房主就写了 `_rooms.Boss`，
  客人通常能从同步状态里 `IsOurs(stored)` ⇒ **直接跟随**。
- 第二场：只有 `ReplaceOne(..., secondBoss: true)` 会写 `_rooms.SecondBoss`，客人端
  `SafeSecondBossEncounter` 读的是**原值**；读到的还是原版时只能自己算一遍
  ⇒ 一旦 A/B 里任何一项不同，**第二场就分叉**（第一场反而可能碰巧对上）。

所以「反馈只提第二场」是合理的：它不是独有 bug，而是**最容易暴露 A/B 的那一场**。

### D. 两端游戏/模组版本不同

`FOOL_TAROT` 只在 111 构建的候补表里（`#if STS2_111`）⇒ 一端 0.113、一端 0.107（或一端是新版模组、
一端是旧版勾选键）也会让候补表/勾选对不上。请让反馈者确认两端 `mod` 版本串一致（日志里 `v0.2.71x`）。

## 3. 本轮改动（v0.2.710，已装机）

| 改动 | 文件 |
|---|---|
| `WantsSecondBoss` 与 `PlayerCount` 统一口径（真 bug） | `src/Hooks/Core/MultiBossPlan.cs` |
| 新增 `LogInputs`：每个 (幕, 场) 打一行 `※阵容输入※ …` | `src/Hooks/Core/BossLineups.cs` |
| 版本号 | `src/CharacterBosses.json`、`src/Hooks/Core/BuildInfo.cs` |

诊断行的样子（两份日志并排一比即可定位）：

```
※阵容输入※ 第 3 层第 2 场 · 本机=Host · 玩家=2 · 进阶=10 · 种子=… · 地图=HIVE（替换=True）
   · 候补表=16 条（含观者=True）· 勾选后候选=16 条 · 已收到房主勾选=False（玩法设置=False）
   · 共用匹配池=False · 指纹=A1B2C3D4（两端这一行不同 ⇒ 就是它导致 Boss 不同）
```

## 4. 用户拍板后已实现（v0.2.711）

### 4.1 「广播」= 房主把**每场最终遭遇**直接发给客人（`BossPicksSync`）

`BossPicksMessage` 从「房主单向下发」改成**双向**，多了两个字段：

| 字段 | 方向 | 内容 |
|---|---|---|
| `Bosses` | 房主 → 客人 | `幕:场\|KEY;幕:场\|VANILLA`（每场最终是哪一只 / 保持原版） |
| `Caps` | 客人 → 房主 | 客人的观者实现身份（`w=boninall` / `w=lamali` / `w=`） |

配合改动：
- `CharBossEncounters.ForVanillaBoss` 拆成「薄壳 + `ForVanillaBossCore`」：
  · **客人**先进新分支：房主说 `VANILLA` ⇒ 本机也保持原版；房主给 Key ⇒ 直接用；没收到才退回确定性规则；
  · **房主**每次算完调 `BossPicksSync.NoteHostBoss(act, fight, resolved)`，结论变了就重播。
- 新客人一上线（首次上报）时**强制重播一次**（清 `_lastSent`），否则迟到入场的客人永远收不到结论。
- 换局时 `ResetForNewRun()` 清空全部同步状态（免得上局的结论漏到新局）。

日志确认点（客人侧）：
```
客人端：第 3 幕第二场按**房主的结论**替换 ⇒ FOOL_TAROT_BOSS_ENCOUNTER（房主说 FOOL_TAROT）
客人端：第 3 幕第二场房主决定**保持原版 Boss** ⇒ 本机也不替换（不再自己算一遍）
勾选同步：已收到房主（NetId=…）的 … **每场最终遭遇 2 条**（第3层第1场=…；第3层第2场=…）
```

### 4.2 「观者只在全端同一实现时出场」

- `WatcherCards.ProviderId` → `boninall` / `lamali` / 空。
- 客人每次算阵容时上报自己的实现（`SendCaps`，内容不变不重发）。
- 房主 `RecomputeWatcherAllowed`：**玩家数 − 1** 份上报到齐 **且每份都跟房主一样** 才放观者；
  客人用房主广播的 `wok=1/0`，**没收到就保守不放**。
- `BossKeys.WatcherRoster()` 已接这条闸门 ⇒ 候补表条数不再随本机装没装而变（阵容才不会分叉）。
- 结论变化会清阵容缓存 + 重播；单人局行为完全不变（只看本机）。

日志确认点：
```
观者 Boss：客人已上报本机观者实现（w=lamali）
观者 Boss：收到客人（NetId=…）的观者实现 = lamali（共 1 份上报）
观者 Boss：房主判定「可以出现」=True（有新客人上报）—— 本机实现=lamali、期待客人上报 1 份、已收到 1 份；全端一致 ⇒ 观者进三层候补表
```

### 4.3 ⚠ 协议变了

`BossPicksMessage` 的序列化多了两个字符串 ⇒ **多人两端必须装同一版本**（否则消息解析错位）。
BaseLib 缺失时整条通道不可用，退回 v0.2.710 的确定性规则（不会崩，但也没有这层保护）。

## 5. 如果反馈者愿意配合，让他发两份日志，只看这几行

```
Select-String -Path "$env:APPDATA\SlayTheSpire2\logs\mod_log.txt" -Pattern "※阵容输入※|※本机生成※|※多 Boss 生成指纹※|客人端：|勾选同步：|本机是\*\*客人\*\*|拉 Boss 房间|第二场"
```
