# 交接报告：恐惧模式（Dread）批次 — 2026-11-13

> 给**下一个会话**。读完这一页就能直接开工，不需要再问用户口径。
> 工程：`D:\Sts2ModProject\Downfall Expansion Vanilla Characters as Bosses`（Mod 名 CharacterBosses，作者 飞猪，Workshop 3830639796）。

## 0. 现在的状态（一句话）

已装进游戏的是 **v0.2.616**，恐惧增强完成 **18 / 28**；剩下 10 只 Boss + 用户新给口径的 2 只（华丽猎、抉择储）＋ 1 个玩家 bug（「我的小钱钱」打死炼药猎后瓶中精灵不复活）没做。

## 1. 环境与常用命令

- .NET 10 SDK：`tools\dotnet\dotnet.exe`；双目标编译 `-p:Sts2Target=107`（正式）/ `111`（beta，= 现在装机的 0.113.0）。
- 编译：`.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111 -v q --nologo`（107 同理）。
- 安装：`cmd /c 'install-mod.bat "D:\SteamLibrary\steamapps\common\Slay the Spire 2"'`（**必须先关游戏**；需要 danger-full-access 沙箱）。
  成功标志：`[verify] 两份实现的版本串都是 vX.Y.Z  OK`。
- 本地化检查：`powershell -ExecutionPolicy Bypass -File 'tools\check-localization.ps1'`，必须结束于「全部通过：16 个语言目录」。
- 反编译：`$env:DOTNET_ROOT=<repo>\tools\dotnet; $env:DOTNET_MULTILEVEL_LOOKUP=0; .\tools\ilspy-cli\bin\Release\net10.0\ilspycli.exe <types|type|members|search> .refs\sts2_111.dll [pattern]`
- 打包 / 上传（**没有用户当轮明确指令就别做**）：`tools\pack-dist.ps1`、`tools\upload-workshop.ps1`；上传前必须重写 `workshop\changenote.txt` 与 `workshop\workshop.json` 的 `changeNote`。
- 线上创意工坊仍是 **0.2.590**；GitHub 未推送（`HEAD == origin/main == 75ca727`，工作区全部改动未提交）。

## 2. 恐惧模式（Dread）的机制要点

- `GameMode.Dread` 只在 0.113（111 目标）有 ⇒ **所有 Dread 代码必须 `#if STS2_111`**，107 目标编译不得引用 `Models.Dread` / `GameMode.Dread`。
- 判断当前是否恐惧模式：`Core.DreadMode.IsActive`（`src\Hooks\Core\CharDominions.cs`）。
- 每幕支配（Dominion）：`CharBossEncounterBase.GetDominion()` 按 `BossKeys.CharacterOf` 映射到 5 个 `DominionModel` 子类；只在普通房间生效（Boss 房会被原版清掉）。文案在 `content\localization\<lang>\ascension.json` 的 `DREAD.<角色>.description`。
- 恐惧版特殊效果的地基：`BossDefinition.SpecialLocDreadByAct` / `SpecialPowerDreadByAct`、`BossVariant.SpecialLocDread` / `SpecialPowerDread`，`SpecialLocForAct` / `SpecialPowerForAct` 在 `DreadMode.IsActive` 时优先取恐惧版，取不到自动回退正常版。
- **用户的硬规则**：恐惧版特殊效果**名字与普通模式完全相同**，只是效果更强；`PS` 之类的实现备注**不写进游戏内描述**。

## 3. 标准流程（每做一只 Boss 就这么走一遍）

1. 找这只 Boss 的接线：`src\Hooks\Core\BossDefs.cs` 里 `Key = "XXX"` 的 `BossVariant`（或按幕的 `SpecialLocByAct` / `SpecialPowerByAct`）。
2. 读它现在的特殊效果实现（`src\Hooks\Powers\*Power.cs` 或 `src\Hooks\Core\*Script.cs`），确认「正常模式现在到底怎么算」。**不要凭设计表猜**——设计表里已经出现过两次「写的那句其实就是现状」。
3. 加恐惧分支：优���在**同一个 Power 里**用 `DreadMode.IsActive` 切换数值/行为（最省事）；行为结构性不同才新建一个 `XxxDreadPower`。
4. 文案：在 16 个语言目录的 `content\localization\<lang>\monsters.json` 里，把正常 key 复制一份 `<key>_dread`，**标题行逐字沿用正常模式**，正文写更强的效果。注意按 `title\n正文` 的换行结构写，且**不要出现半角双引号**。
5. 接线（`SpecialLocDread` 或按幕的 `SpecialLocDreadByAct[n]`）。
6. 版本号**两处一起改**：`src\CharacterBosses.json` 的 `version` ＋ `src\Hooks\Core\BuildInfo.cs` 里 `Tag = "vX.Y.Z / ` 的**前缀**（只改一处会在安装时报「部署版本不一致」）。
7. 双目标编译 → 本地化检查 → 关游戏 → 安装 → 确认 `[verify]` 通过。
8. 往 `docs\plans\dread-mode.md` 追加一节「进度（第 N 轮，vX.Y.Z）」。

## 4. 已完成（18 只，别重做）

| 角色 | Boss | 恐惧版效果 | 落点 |
|---|---|---|---|
| 故障机器人 | 物理机 | 「人工合成」3 → **5 层人工制品** | 新类 `Powers\SynthesisDreadPower.cs` |
| 故障机器人 | 彩虹机 | 「彩虹」每 5 球 +1 → **+2 集中** | `Powers\BossRainbowPower.cs` |
| 故障机器人 | 黑球机 | 「冷却剂」冷光 2 张（堆 1+手 1）→ **只给手牌 1 张** | `Core\BossHookBridge.SideTurn.cs` |
| 故障机器人 | 电球机 | 「引雷针」3 张 −1 → **5 张 −1、每回合 1 次**；且第 3/10 回合**暴风雨+**（意图 +1 球已同步） | `Powers\BossLightningRodPower.cs`、`Core\BossOrbMath.cs`、`UI\BossHandPanel.Extra.cs`、`Core\DefectThunderScript.cs` |
| 故障机器人 | 能力机 | 「偏差认知」+ **免疫偏差认知**（获得即移除） | `Powers\DeviationPower.cs` |
| 故障机器人 | 事件机 | 「一心化万」第 4、9 回合**万物一心后补打疯狂进食+** | `Core\EventMachineScript.cs` |
| 故障机器人 | 状态机 | 「状态机」**同名同效**（用户口径：文案与现状一致） | 只有文案 `DEFECT_BOSS.status_dread` |
| 铁甲战士 | 愤怒战 | 「群情激奋」+ **额外遗物奥利哈钢** | `Core\IroncladAngerScript.cs`（Relics 加 `DreadMode.IsActive` 分支） |
| 铁甲战士 | 打击战 | 「痛击」每张「打击」+2 → **+3**（进阶 9 起 3 → 4） | `Powers\BossBashPower.cs` |
| 铁甲战士 | 放血战 | 「死亡收割」只回未格挡 → **按伤害总量回血** | `Powers\BossRendingPower.cs` |
| 铁甲战士 | 杂耍战 | 「燃烧契约」+ **玩家的打击牌也加成他的完美打击**（快照、不打折） | `Core\BossScript.cs`（`TakeOverPerfectedStrike`） |
| 铁甲战士 | 易伤战 | 「主宰」给易伤附带 1 虚弱 → **2 虚弱 + 2 脆弱** | `Powers\DominateWeakPower.cs` |
| 铁甲战士 | 巨石战 | 「原始力量」巨石 **+2 力量 +6 覆甲**；第 1、7 回合**多打 1 张巨石+**（执迷 −1 费 ⇒ 该回合 +1 能量） | `Powers\BossPrimalForcePower.cs`、`Core\BoulderIroncladScript.cs` |
| 猎手 | 奇巧猎 | 「命运同担」下回合弃 1 → **弃 2** | `Powers\SharedFatePower.cs` |
| 猎手 | 蛇咬猎 | 「群蛇形态」你洗牌 1 张蛇咬 → **3 张** | `Powers\BossSnakeFormPower.cs` |
| 猎手 | 蛇眼猎 | 「计划妥当」谋杀只在保留时加伤 → **只要抽牌就加伤** | `Core\BossDefinition.cs`（`MurderCountsDrawnCardsFor` 运行时分支） |
| 猎手 | 涂毒贼 | 「带毒刺击」中毒回合结束可格挡 → **不再可格挡** | `Powers\PoisonStrikePower.cs` |
| 猎手 | 炼药猎 | 「调制」**完全不变**；只是她的**「群蛇形态」升级** | `Core\SilentBrewerScript.cs` 的 `C<SerpentForm>(Core.DreadMode.IsActive)` |

## 5. 还没做（按建议顺序）

### 5.1 用户当轮新给的，优先
- **华丽猎**（`SILENT_GRANDE_FINALE` / 特殊效果「压制」）：原文前后不动，中间加一条——**每当她打出华丽收场时，给所有玩家 1 层「昏鸣」**（= 仪式兽给的 debuff：下回合只能打出 1 张牌）。
  要出三处恐惧版文案：`powers` 表 `BOSS_SUPPRESSION_POWER.description`（107 用）、`.description_v113`（0.113 用）、`monsters` 表 `SILENT_BOSS.suppression`；16 语言。
- **抉择储**（`REGENT_CHOICE` / 「指导」）：每当储君打出「抉择，抉择」时，**由玩家替他挑 2 张**要被重复打出的牌。
  实现方式用户明确指定：**再打一次「抉择，抉择」并拦掉它的抽牌**（不要在牌上加「重放」，否则抽牌数变多、意图全部算错）。
  流程：5 张里选 1 → 他打出 3 次 → 剩下 4 张里选 1 → 他再打出 3 次 → 继续后面的牌。下次打抉择抉择照旧「5 选 → 4 选」。
  相关代码：`Core\ChoiceRegentScript.cs`、`Core\ChoiceRegent.cs`、`Patches\ChoiceRegentPickPatch.cs`、`SelectionCompat`。

### 5.2 剩下的 10 只（设计表在 `docs\plans\dread-mode.md` §2.2）
刀猎、根除骨、养手骨；灾厄骨、血肉骨、灵魂骨、虚无骨；碎屑储、轰击储、铸剑储、王储、仆从储、无色储、辉星储。
设计表里凡是「写的那句其实就是现状」的，照打击战/杂耍战/炼药猎的做法：**落在数值或结构上做真正的强化**，并在文档里写清「用户口径 vs 现状」的差异，别默默照抄。

### 5.3 玩家反馈的 bug（普通模式也复现）
**「我的小钱钱」把炼药猎打死时，她的瓶中精灵没有复活。**
这张牌是 0.113 新增的**无色攻击**，1 费，「你每有 20 金币，造成一次 1 点伤害」= 多段、每段 1 点、段数随金币增长（中文名搜不到，按数值特征在无色卡池里找）。
排查顺序：
1. 反编译该牌，看它调哪个伤害 API（`CreatureCmd.Damage` 还是直接 `LoseHpInternal`）；
2. 若绕过「将要死亡」钩子 ⇒ 照之前「彗星」的修法，改回原版伤害出口（`CreatureCmd.Damage(..., Unblockable|Unpowered, dealer:null)` 那种）；
3. 若走正常管线 ⇒ 查 `Patches\BossBlockAbsorbPatch.cs`（每段伤害都会进它的前缀，调制用）与影子玩家复活链路（`Core\BossPotions.cs` + 瓶中精灵）是否被多段小额伤害遮住。

## 6. 这个仓库的坑（每轮都会撞）

- **有些文件是 LF 换行**（`BossPrimalForcePower.cs`、`BoulderIroncladScript.cs`、`BossSnakeFormPower.cs` 等）：用 CRLF 拼的字符串 `Replace` 会**静默失败**（返回 False 但同脚本里的文案改动已经生效 ⇒ 出现「描述写 2 力量、代码还给 1 力量」）。一律用 `[regex]::Replace($t, '(?m)^([ \t]*)原行', ...)`，改完 **grep 新标识符确认落地**。
- 三元表达式 `? A : B,` 跨行拼接容易拆坏 ⇒ 优先**按行号整行改写**并删掉残留续行。
- 游戏内文案**不要出现半角双引号**（会打断 JSON，本地化检查报「共 14 处问题」）。
- 版本号两处同步（见 §3.6）。
- `install-mod.bat` 里 `copy` 的错误被 `>nul` 吞掉；装完一定看 `[verify]`。
- Harmony 的模糊签名在新版会编译失败，必须写显式参数表（`CardPileCmd.AutoPlayFromDrawPile`、`ActModel.GenerateRooms` 已经踩过）。
- 用户规矩：**没有当轮明确指令，不上传创意工坊、不推送 GitHub**；上传前先改 changelog。

## 7. 做完之后的验收清单

- [ ] 每种改动都双目标编译 0 错误（107 + 111）。
- [ ] `check-localization.ps1` → 「全部通过：16 个语言目录」。
- [ ] `install-mod.bat` → `[verify] 两份实现的版本串都是 vX.Y.Z  OK`。
- [ ] `docs\plans\dread-mode.md` 有对应「进度」小节。
- [ ] 28 只 Boss 都有 `<key>_dread` 文案 + 接线（可用脚本扫 `SpecialLocDread` / `SpecialLocDreadByAct` 覆盖度自检）。
---

## 发布记录：v0.2.650（2026-11-14）

- 打包：`tools\pack-dist.ps1` → `workshop\content\CharacterBosses\` + `.dist\CharacterBosses\` + `.dist\CharacterBosses-v0.2.650.zip`（13.75 MB，双端版本串均 v0.2.650）
- 工坊：条目 **3830639796** 已更新（`tools\upload-workshop.ps1`），Steam 日志 `Upload finished for workshop item 3830639796 : OK`
- GitHub：`1b056f8 release: v0.2.650 恐惧模式完整适配 + 选牌/抉择/支配/特效朝向修复` 已推到 `main`
- 更新日志：`workshop\changenote.txt`（`tools\mk-workshop-json.ps1` 同步进 `workshop.json`）

### ⚠ 工坊上传踩到的坑（下次别再踩）

连着重试上传会把 Steam 账号打进 `LogonFailure Rate Limit Exceeded`，而 Steam 客户端被踢下线后
**自己不会重连**（`connection_log.txt` 里会留 `not auto reconnecting due to Rate Limit Exceeded`
和 `Sending SteamServerConnectFailure_t … Do not reconnect`），此后每次上传都秒失败：

```
Upload workshop item 3830639796 failed (Failed to initialize build on server (No Connection))
```

判断方法（都是纯读日志，不用瞎试）：
- `C:\Program Files (x86)\Steam\logs\connection_log.txt` —— 有没有新的 `[Logged On`；
- `C:\Program Files (x86)\Steam\logs\workshop_log.txt` —— 失败原因是 `Rate Limit` 还是 `No Connection`；
- `connection_log.txt` 的 LastWriteTime 停在被踢的那一刻 = 客户端仍是离线。

解法：**`Steam.exe -shutdown` 之后重新启动**（带 JWT persistence，不用重输密码），
等 `connection_log.txt` 出现 `[Logged On` 再上传，一次就过。
另外：能打开工坊网页 ≠ 客户端在线（浏览器走的是 steamcommunity.com 的 HTTPS，与 CM 会话无关）。
