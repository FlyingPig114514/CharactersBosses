# 交接报告 · 2026-11-19 · 红蓝观（WATCHER_RUSHDOWN）收尾 + 塔罗愚滚石意图

> 写给**下一个对话**。当前仓库状态：`v0.2.770` 已构建、已安装、**只在本地提交（未推送 GitHub）**。
> 用户这轮明确要求：**不要自动 git push**，等他说「推」才推。

---

## 0. 先读这些（10 分钟）

1. 本文件
2. `docs/plans/boss-watcher-rushdown.md` —— 红蓝观的**逐字设计**（用户原文）+ ID 表 + 分阶段清单
3. `docs/plans/map-icons-policy.md` —— 地图图标「一图标一 Boss」口径
4. `rules.md` / `AGENTS.md` —— 本仓库开发规范

### 铁律（用户多次强调，违反会被骂）

- **用中文回复，结论必须带日志证据**（`%APPDATA%\SlayTheSpire2\logs\mod_log.txt`、`godot.log`），不许猜。
- **不许关掉用户的游戏**（`Get-Process SlayTheSpire2` 先查；游戏开着 DLL 会被占用，`install-mod.bat` 会静默失败）。
- 改版本号要**同时**改 `src/CharacterBosses.json` 的 `version` 和 `src/Hooks/Core/BuildInfo.cs` 的 `Tag`。
- 每次要 **`-p:Sts2Target=111` 和 `=113` 两个目标都 0 错误**，然后 `install-mod.bat`（`[verify] 两份实现的版本串都是 vX OK` 才算装上）。
- **不许自动推送**，只在本地 `git commit`。
- 写文件注意编码：`src/CharacterBosses.json` 必须是 **UTF-8 带 BOM**（否则游戏读成 ANSI ⇒ 模组列表乱码，这个坑刚修过）。

---

## 1. 环境与命令

```powershell
cd "D:\Sts2ModProject\Downfall Expansion Vanilla Characters as Bosses"
# 构建（两个目标）
.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111
.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=113
# 安装（会重建 PCK；沙箱下需要 danger-full-access 才能写游戏目录）
cmd /c install-mod.bat
# 反编译工具（替代 ILSpy MCP）
.\tools\dotnet\dotnet.exe run --project tools\ilspy-cli -c Release -- members <dll> "<TypeRegex>" 1
.\tools\dotnet\dotnet.exe run --project tools\ilspy-cli -c Release -- method <dll> "<TypeRegex>" "<MethodRegex>" 1
```

- 游戏：`D:\SteamLibrary\steamapps\common\Slay the Spire 2`（0.113.1）
- **观者 mod 两版**（很关键，见 §5）：
  - A 版（Boninall）= 工坊 `3747526116`，类名 `WatcherMod.*`，卡牌 id `WATCHER_WEAVE`（下划线）
  - B 版（lamali）= 工坊 `3747492505`，类名 `Watcher.Code.*`，卡牌 id `WATCHER-WEAVE`（连字符）
  - 用户**现在测的是 B 版**
- StS1 原版资源：`D:\SteamLibrary\steamapps\common\SlayTheSpire\desktop-1.0.jar`

---

## 2. 这轮已经做完的（0.2.764 ~ 0.2.770，都可视为「已落地待实机确认」）

| 版本 | 内容 |
| --- | --- |
| 0.2.764 | 模组列表乱码修复（清单被双重编码 + 缺 BOM 重建）；`净化+` 按剧本点名真消耗（`Core/BossPurify`） |
| 0.2.765 | 预见观「你预见 2」入口两版都认（A: `WatcherCombatHelper.Scry`；B: `Watcher.Code.Commands.MultiplayerCmd.Scry`） |
| 0.2.766 | 红蓝观：塔1 时间扭曲**钟 + 音效**（`Core/TimeWarpFx`，音效 `res://CharacterBosses/audio/STS_SFX_TimeWarp.ogg`）；第 12 回合动态换计划的框架（`BossScript.SwitchTurn/SwitchFromTurn/SwitchRequested`） |
| 0.2.767 | 预见观：让玩家预见 2 期间**全体 Boss 停手**（`Core/ScryGate`，`PlayOne` 开头 await，并立起 `StrayBossCardQueuePatch.HoldReap`） |
| 0.2.768 | 红蓝观：节制计数改 `PlayOne` 记账；`Core/PlayerAttackWatch`（挂 `CardCmd.AutoPlay`）；净化+ 延迟 0.3s 走 `CardPileCmd.Add(…, PileType.Exhaust, …)` |
| 0.2.769 | 削弱改法：T1「暴怒+」↔ T2「渐强」互换；**所有**粉碎关节 → **腰带抽打** `WATCHER_SASH_WHIP`；节制结算改成先 `PlayerCmd.EndTurn(她的 Shadow)` 再加力量；`PlayerAttackWatch` 改用 `object[] __args` 捞牌 |
| 0.2.770 | 塔罗愚：第 12 回合起在最左列（水银沙漏那个位置）显示滚石伤害 |

---

## 3. ⛔ 待修（用户 2026-11-19 实机反馈，这是交接的重点）

### 3.1 红蓝观「节制」计数错 + 不显示数字

**症状**（用户原话）：「你这怎么第二回合就开始给自己加力量了（6 张牌就开始加？明明是 12 张牌才加）？而且还是没有计数」。

**最可能的原因（按可能性排序）**

1. **重复计数**：`BossTemperancePower` 里**同时**有两条计数路径 ——
   - `PlayOne` 显式调 `NoteCardPlayedAsync`（v0.2.768 加的，确定会跑）；
   - 旧的 `PowerModel.AfterCardPlayed` 覆盖**还留着**（我当时注释成「保留旧的 Power 钩子作兜底」）。
   如果那条钩子其实**会**派发 ⇒ 每张牌数两次 ⇒ **6 张牌就满 12** ⇒ 正好对上用户看到的「第二回合 6 张牌就加力量」。
   ⇒ **先做**：把 `BossTemperancePower.AfterCardPlayed` 整个删掉（或让它在 `_played` 已被 `PlayOne` 记过时直接 return），
   只保留 `PlayOne` 一条计数路径，然后实机验「第 3 回合数到 12 才 +2 力量」。

2. **计数数字不显示**：`PowerStackType.Counter` 的能力在 buff 栏显示的是 **`Amount`**，而 `Amount` 现在是挂载时给的 1，
   我把计数放在实例字段 `_played` + 覆盖 `DisplayAmount` —— 很可能界面根本没读 `DisplayAmount`。
   ⇒ 改法：`Amount` 就当作计数用（但**绝不能是 0**，否则 `ShouldRemoveDueToAmount()` 会把整条 power 移除，
   v0.2.541「引雷针」踩过这个坑）⇒ 用 `Amount = 已打出张数 + 1`，`DisplayAmount => Amount - 1`；
   满 12 时 `ModifyAmount(-11)` 回到 1。

3. 附带确认：结算顺序现在是「先 `PlayerCmd.EndTurn(shadow)` 再加力量」，日志会打
   `节制：满 12 张 ⇒ 已结束她的回合 + 她与友方 +2 力量`（没结束会写「（回合没结束！）」）。

**相关文件**：`src/Hooks/Powers/BossTemperancePower.cs`、`src/Hooks/Monsters/CharBossMonster.cs`（`PlayOne` 里的
`NoteCardPlayedAsync` 调用，约 2024 行的 `BossPurify.Handle` 附近）。

### 3.2 第 4 回合「不惧妖邪」还是不让她进平静

**现状**：`Core/PlayerAttackWatch`（`src/Hooks/Core/PlayerAttackWatch.cs`）挂在
`MegaCrit.Sts2.Core.Commands.CardCmd.AutoPlay` 的 postfix 上，`Prepare()` 用
`GameCompat.DeclaredMethod(typeof(CardCmd), "AutoPlay")`，postfix 从 `object[] __args` 里捞 `CardModel`；
命中后 `Flag = true` + `BossScript.SwitchRequested = true`；`PlayOne` 里打完「不惧妖邪」时若 `Flag` 为真就 `EnterCalm`。

**怀疑点**

- 这条 Harmony **可能压根没装上**（`Prepare()` 返回 false 或补丁异常被吞）⇒ **第一步就是加日志**：
  在 `Postfix` 最前面无条件 `Logger.Log` 一次（限流 5 行）并在 `Prepare()` 里打一行说明命中与否，
  跑一场看 `mod_log.txt` 有没有这几行。
- 就算装上了，**B 版玩家出牌可能不走 `CardCmd.AutoPlay`**（走 `CardCmd.Play` / `CardPlay` 那套）
  ⇒ 备选：改挂 `MegaCrit.Sts2.Core.Hooks.Hook.AfterCardPlayed`（前缀/postfix 都行），或干脆挂
  `CardModel` 的播放入口。**先用日志确认再改，别再盲改。**
- 另一条更稳的思路：她已经有的 `Powers.WatcherFearNoEvilAimPower`（`PlayerAttacked` 字段）本来就是干这个的，
  但下面 3.2' 说明「她这只 Boss 身上的 Power 钩子不派发」⇒ 用 Power 钩子这条路要先用日志证实到底有没有派发。

### 3.2' 一个**必须先用日志证实**的架构问题

`PowerModel.AfterCardPlayed` 在**她（Boss 生物）**身上到底派发不派发？
- v0.2.768 之所以把节制计数改成 `PlayOne` 记账，是因为我判断「不派发」；
- 但如果 3.1 的「6 张就满 12」是重复计数导致的，那说明它**其实会派发** ⇒ 我当时的判断是错的，
  那么 `WatcherFearNoEvilAimPower.PlayerAttacked` 这条路本来是好的，只需修闸门/时机。

👉 **做法**：在 `BossTemperancePower.AfterCardPlayed` 里加一行限流日志（`[CharBoss] 节制钩子：<牌>`），
打一场看她出牌时有没有这些行。有 ⇒ 只留一条计数路径；没有 ⇒ 才走 `PlayOne`。

### 3.3 第 12 回合打了攻击牌还是不会换成「不惧妖邪无限」

依赖 3.2 的 `SwitchRequested`。修好 3.2 之后验证：
`BossScript.TurnAt(int turn)` 里会打 `N 回合：玩家打出过攻击牌 ⇒ 切换成「不惧妖邪无限」计划`。
若标记为真但没切换，检查 `SwitchFromTurn`/`SwitchTurn` 是否真的赋上（`WatcherRushdownScript.Build()` 末尾）。

### 3.4 ⚠ 剧本被我的全局替换改坏了（这一条最要紧）

用户：「第七和第八回合打腰带抽打干什么？那两张都应该是碰撞连击」。

我在 v0.2.769 做「所有粉碎关节 → 腰带抽打」时用了**全局字符串替换**，把不该动的也换了。
现在的正确口径（用户 2026-11-19）：

- **T2 打「腰带抽打」**（`WATCHER_SASH_WHIP`，替换原来的粉碎关节）；
- **T3 的「碰撞连击」位置不变**；
- **T7、T8 必须是「碰撞连击」**（现在被换成了腰带抽打 ⇒ 要改回来）；
- T10 的「粉碎关节（不出）」、T11 净化+ 消耗名单里的「粉碎关节」是否也换成腰带抽打 —— **问用户确认**（他说「所有」，但 T7/T8 显然不包括）。

**文件**：`src/Hooks/Core/WatcherRushdownScript.cs`（Turns 列表，每回合一个 `Turn(...)`，
`Turn` 序号 = 回合号；T7 第 7 个 Turn 块、T8 第 8 个）。改完**逐回合打印一遍**给用户核对。

---

## 4. 红蓝观其它已知状态

- 遗物 7 件：至纯之水 / 涅奥的祝福 / 泪滴吊坠盒 / 符文金字塔 / 音叉 / 布质果实 / 坚固钳子。
  血量 = 角色真实基础血量(72) + 涅奥 120 + `HpBonus = 31`（布质果实）= **223**（其它观者变体 `HpBonus = 0`）。
- 特殊效果「节制」= 她打出 12 张牌 ⇒ 强制结束自己回合 + 她与友方 +2 力量 + 播塔1 时间扭曲的钟和音效。
- 恐惧模式：另挂 `BossTimeWarpPower`（独立计数被锁定玩家的出牌，满 12 终止该玩家回合）。
- 净化+：全部附魔「灵魂之力」；打出去的那几张按剧本点名真消耗（`Core/BossPurify`）。
- 能力面板、16 语言文案都已落地。

---

## 5. 两版观者 mod 的关键差异（踩过很多次坑）

| | A 版 `3747526116` | B 版 `3747492505` |
| --- | --- | --- |
| 命名空间 | `WatcherMod.*` | `Watcher.Code.*` |
| 卡牌 id | `WATCHER_WEAVE`（下划线） | `WATCHER-WEAVE`（连字符） |
| 预见入口 | `WatcherMod.WatcherCombatHelper.Scry(ctx, player, amount, source)` | `Watcher.Code.Commands.MultiplayerCmd.Scry(ctx, source, players, amount)` |
| 迂回回手 | 在 `WatcherCombatHelper.OnScry` 后半段 | 在**卡片自己**的 `Weave.AfterScryed(...)` |
| 姿态 | 有 `WatcherCombatHelper.EnterCalm/EnterWrath` | 走 `Watcher.Code.Commands.StanceCmd.*` |

`src/Hooks/Core/WatcherCards.cs` 里的 `Card(boninallName, lamaliName, boninallId, lamaliId)` 就是两版对齐用的。

**教训**：任何「找不到某个类就 early-return」的代码，都会让 B 版静默失效（已经被坑了三次：
迂回补丁、预见入口、`WatcherCombatHelper`）。写这类反射代码时**先装补丁、再判 helper**，并把「没装上」打成日志。

---

## 6. 其它在用的坑（避免重复踩）

- Harmony 前缀里**绝不能** `MethodBase __originalMethod.Invoke(...)` 再包 Task ⇒ 反射拿到的仍是被替换的入口 ⇒
  无限递归 ⇒ 进程级 StackOverflow（她打天眼时崩过一次，见 `.dshwolf/buglog.json`）。
  要包 Task 就用**后缀** `ref Task<T> __result`。
- `PowerCmd.ModifyAmount` 把数量改成 **0** 会让原版 `ShouldRemoveDueToAmount()` **移除整条 power**。
- 她身上「Power 钩子是否派发」这件事**必须先用日志证实**（见 §3.2'）。
- 模组清单 json 必须 **UTF-8 + BOM**，否则游戏内显示乱码（0.2.764 修过）。
- 地图图标「一图标一 Boss」：`docs/plans/map-icons-policy.md`。
- 用户玩游戏时不要 `install-mod.bat`（DLL 被占用会静默失败，`[verify]` 会报版本不一致）。

---

## 7. 验证清单（每改一条就跑）

```powershell
# 1) 两个目标都 0 错误
.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111
.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=113
# 2) 游戏没开 → 安装
Get-Process SlayTheSpire2 -ErrorAction SilentlyContinue
cmd /c install-mod.bat        # 看 [verify] 两份实现的版本串都是 vX  OK
# 3) 实机打完看日志
Select-String -Path "$env:APPDATA\SlayTheSpire2\logs\mod_log.txt" -Pattern "节制|不惧妖邪|切换成|净化\+|滚石" | Select-Object -Last 30
```

## 8. 还没做的（用户已提出，未开始）

- 工坊（Steam item `3830639796`）**重新上传**：现在工坊页面的标题/描述是乱码、版本还停在 `0.2.730`。
  本地 `workshop/title.txt` / `description.txt` / `workshop.json` 都已经是正确的 UTF-8（带 BOM），
  重传用 `tools\upload-workshop.ps1`（**要先问用户**）。
