# 交接：铸剑储君（第二幕 Boss）— 当前进度与待修清单

> 给下一个会话：先读完这一页再动手。所有结论都是本会话**实机验证过**的（`mod_log.txt` 有日志行）。

## 0. 环境（照抄即可，别自己发明）

- 工作目录 `D:\Sts2ModProject\Downfall Expansion Vanilla Characters as Bosses`
- 编译：`& tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -v m`（产物 `src\bin\Release\CharacterBosses.dll`）
- **安装（关键）**：`install-mod.bat` 会静默失败/拷旧 DLL。必须手动：
  ```powershell
  Copy-Item "src\bin\Release\CharacterBosses.dll" "D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\CharacterBosses\CharacterBosses.dll" -Force
  ```
  这个路径在**工作区之外**，沙箱 workspace-write 会拒绝 → 需要用 `sandbox_permissions: danger-full-access` + 一句 justification 重试同一条命令。
  拷完**必须比对 SHA256**（src 与 dst 一致才算装上）。游戏进程活着时 DLL 被占用会 Access denied → 先杀进程。
- 启动：`Start-Process "steam://run/2868840"`（直接起 exe 会 `Steamworks initialization failed`）；约 55 秒后 `http://127.0.0.1:15526/api/v1/singleplayer` 可用（`state_type` / `battle.round` / 敌人 hp+status）。
- **MenuControl 8081 是坏的**（POST 一律 400）→ 主菜单点「继续」只能由**用户手动点**。需要进战斗时直接请用户点一次，别浪费时间试 API。
- 日志：`%APPDATA%\SlayTheSpire2\logs\mod_log.txt`，用 `Select-String -Encoding UTF8` 读；`[CharBoss]` 是 mod 行。
- 当前已装 DLL hash 前缀：**F406DB4E**（这份带诊断日志，见 §3）。

## 1. 已修好且**实机验证过**的（别再动）

| 问题 | 做法 | 证据 |
|---|---|---|
| 第 1 回合打不出群星斗篷 | 天赋君权（`DivineRight`）靠「进入房间 +3 辉星」，影子玩家收不到 → `CharBossMonster.EnsureStarterStars` 每回合直接 `pcs.Stars = want`（`Stars` 是**公开可写**属性，后备字段叫 `_stars`） | `剧本第 1 回合: 打 [SOVEREIGN_BLADE,CLOAK_OF_STARS]` |
| 回合结束免费打出「所向无敌」 | 原版这张牌自己实现了（`IAmInvincible.AfterAutoPostPlayPhaseEntered`：若自己在摸牌堆顶 → `CardPileCmd.AutoPlayFromDrawPile` 免费打出），但钩子只对真 Player 触发 → 剧本用 `BossTurn.FreePlayTopEntry` 标记回合，出牌阶段末尾把真牌挪到堆顶（`BossScriptRunner.MoveCardToDrawTop`）再**直接调用那张牌自己的钩子** `await freeCard.AfterAutoPostPlayPhaseEntered(ctx, Shadow)` | `回合结束免费打出 I_AM_INVINCIBLE（摸牌堆顶）：能量 0→0，辉星 3→3` |
| 所向无敌的格挡要出现在意图里 | `ScriptCard.DisplayOnly = true`：把这张牌当"展示牌"造进手牌，规划器跳过它（日志 `(仅显示意图)`），由手牌栏照常渲染它的格挡意图 | `跳过 [I_AM_INVINCIBLE(仅显示意图)]` |
| 剑圣重放 → 意图该显示 2 段 | `BossDamagePredictor.Hits` 末尾乘 `1 + card.GetEnchantedReplayCount()` | 待下一次实测确认 |
| 君王之剑意图要提前算上熔炉铸造 | 熔炉（`FurnacePower`）在**他回合开始**铸造，而计划发生在玩家回合开始前 → `BossScriptPlanner.PendingFurnaceForge` 把它加到剑的意图上；同回合先打的追踪之刃（`Forge` 变量 7）与剑圣（`SwordSagePower` 变量 = 重放）也一起记账 | 待实测 |
| 辉星显示 | 用**原版星星图标** `res://images/packed/sprite_fonts/star_icon.png`（原版 `StarIconsFormatter` 同一张）+ 数字居中叠上，作为**能量球节点的子节点**贴在右下角（挂在球下面，不影响状态条那排右起左排的排版） | `辉星徽章: 3 个，贴在能量球右下 offset=(79.36, 79.36)` |
| 第 4 回合剑圣打两次 | 第 4 回合剧本**删掉剑圣**（汇流保留它，第 5 回合才打一次） | 本轮刚改，需实测确认 |

## 1.5 本会话（F406DB4E → **EA8581D5**）的根因与改动

### 根因 A：§2 那两条「写好了却没生效」＝**每帧驱动从来没跑过**

证据（不是推测）：`mod_log.txt` 43080 行里 **`帧回调 ok` 一条都没有**（那是 `BossHandPanel` 前 3 秒的心跳），
而同期的 `手牌栏跟随` 却有 —— 但那条日志只在 `LayoutCells` 里直接调用时才打。
⇒ `BossHandPanel.OnFrame` **从未被调用**：
① `GetTree().ProcessFrame += OnFrame`（挂在 `_Ready` 里）没生效；② `_Process` 重载也没被调到。
而「君王之剑镜像」「随从朝向站位」当时正写在 `OnFrame` 里 ⇒ 一次没跑，诊断日志自然是空的
（**所以要找的节点/宠物的真实情况至今未知**，先让它跑起来才有证据）。

做法：`src/Hooks/Patches/BossFrameTickPatch.cs` 用 Harmony 挂**原版每帧一定会跑**的方法：
`NStarCounter._Process`、`NEnergyCounter._Process`、`NSovereignBladeVfx._Process`
（最后这个最可信 —— 那把剑肉眼可见地在绕圈飞，说明它每帧在跑），并在这条 postfix 里**直接镜像那一把剑**。
每个来源第一次生效会各打一条 `帧驱动源已生效: <来源>`，下一轮日志即可判定哪个源真的在跑。
⚠️ 结论：**mod 程序集里的 Godot 回调（`_Ready`/`_Process`/`ProcessFrame`）不可依赖，一律用 Harmony。**

### 根因 B：佩尔的士兵根本没被召唤（`PAELS_LEGION` 的钩子抛空引用）

日志铁证：`[CharBoss] 遗物 PAELS_LEGION 的 AfterSideTurnStart 失败: Object reference not set to an instance of an object.`

反编译结论（`references/sts2.dll`）：
- 召唤写在 `PaelsLegion.BeforeCombatStart()` 里 → `SummonPet()` → `PlayerCmd.AddPet<Monsters.PaelsLegion>(Owner)`；
- `AfterSideTurnStart` 里直接 `Owner.PlayerCombatState.GetPet<Monsters.PaelsLegion>().Monster` →
  **宠物不存在就 NRE**。

而 `BossHookBridge.FireCombatStart` 只补触发了 `BeforeSideTurnStart` / `AfterSideTurnStart`，
**从没触发过 `BeforeCombatStart`** ⇒ 宠物不存在 ⇒ 上面那条 NRE。
现已在 `FireCombatStart` 里按 `IsTurnStartRelic` 过滤后补 `BeforeCombatStart`，并在其后立刻 `BossShadowPets.Update`。

### 根因 C：辉星「贴图/字体都不对」——原版用的是哪个节点（已实机 dump）

`%StarCounter`（`res://scenes/combat/energy_counters/star_counter.tscn`）的真实结构：

| 部位 | 原版实际用的 |
|---|---|
| 星星贴图 | `res://images/ui/combat/energy_star.png`（+ 旋转两层 `energy_star_layer_2/3.png`，套 `star_counter.tscn` 的 HSV ShaderMaterial） |
| 数字 | `MegaRichTextLabel`，字号 **36**，字体走主题 `themes/fonts/zhs/noto_sans_mono_cjksc_regular_shared.tres` |
| 位置 | 能量球**左下角**：`StarCounter.Position = (-36, 40)`（同为 128×128） |

旧实现用的是 `images/packed/sprite_fonts/star_icon.png`（那只是**富文本内联小图标**）+ 普通 `Label`（默认字体，字号 24）
+ 挂在球的**右下**角 ⇒ 三处都不对。现在直接实例化原版场景 `star_counter.tscn` + `Initialize(shadow)`，
自绘版本只作兜底。诊断函数 `BossStatusRow.DumpVanillaStarCounter()` 保留（每场战斗打一次）。

### 另外两条用户反馈（手牌栏语义）

- 「所向无敌应该是**亮**的」（它虽不在出牌计划里，但回合结束会从摸牌堆顶免费打出）：
  新增 `ScriptCard.AutoPlayed` + `CharBossMonster.AutoPlayedCards`，手牌栏的亮/暗与 `Planned` 解耦
  （`BossHandPanel.Cell.Bright` / `BossHandCard.BuildCell(..., bright)`）。
- 「剑圣第 4 回合应该显示但是**暗**的，而不是直接没了」：手牌是 `BossScriptRunner.BuildHand`
  按剧本列表重建的，剧本里不列它就等于没有这张牌 ⇒ 第 4 回合剧本补一条
  `SwordSage(DisplayOnly: true)`（不标 `AutoPlayed`，所以是暗的）。

## 2. 还没修好的两条（用户的最后反馈："剑和士兵的方向还是不对"）

代码位置（都写好了，但**没有生效**，本轮加了诊断日志）：

- 浮空「君王之剑」剑尖朝右 → `src/Hooks/UI/BossSovereignBlade.cs`
  每帧由 `BossHandPanel.OnFrame` 调用；递归找 `NSovereignBladeVfx`，把内层 `SpineSword`（Node2D）的 `Scale.X` 取负。
- 「佩尔的士兵」朝向/站位 → `src/Hooks/Core/BossShadowPets.cs`
  遍历 `pcs.Pets`，`BossOsty.MirrorBody(node)` 镜像立绘（`NCreature.Body`），位置照原版公式 **X 镜像**
  （`Vector2.Right * 主人宽度/2 + Osty.MinOffset.Lerp(Osty.MaxOffset, hp/150)`，敌方侧取负 = 主人左手边），
  只在偏离 >120px 时才 snap（避免吃掉它自己的攻击位移），再 `BossOsty.ForceVisible`。

**下一步就是读这两条诊断日志**（新会话进战斗后立刻看）：

- `[CharBoss] 没找到君王之剑 VFX，NCreature 直接子节点=[...]`
  → 说明这把剑根本不是 boss 的 `NCreature` 子节点（原版 `SovereignBlade.GetVfxNode(player, card)` 是从
  `NCombatRoom.GetCreatureNode(player.Creature).GetChildren()` 里找同名牌的 `NSovereignBladeVfx`）。
  若确实找不到：改成从 `NCombatRoom.Instance` 全树搜，或直接 postfix `SovereignBlade.GetVfxNode` 在返回时翻。
- `[CharBoss] 君王之剑已镜像（剑尖朝左）：<节点名>.Scale=... （SpineSword=True/False）`
  → 已翻到东西，但用户说方向仍然不对 ⇒ 翻错了节点（可能要翻 `SwordBone`/`%Path` 轨道方向，而不是整体 Scale）。
- `[CharBoss] 随从诊断: pcs.Pets=N [...] creaturePets=M`
  → 士兵可能不在 `pcs.Pets`（原版 `PaelsLegion.SummonPet` 走 `PlayerCmd.AddPet<Monsters.PaelsLegion>`），
  也可能 `room.GetCreatureNode(pet)` 拿不到节点。若 Pets 为空 → 改从 `boss.Creature.Pets` 或战斗列表里找
  名字 `PAELS_LEGION` 的 creature。

## 3. ⚠️ 本会话造成的一处代码损伤（必须知道）

> **2026-10-20 补记：吞掉的那条语句已经找到了** —— `BossOsty.DropDeadPets()` 里的
> `pets.RemoveAt(i);` 被并进上一行注释，导致死掉的奥斯提永远留在 `Pets` → `GetPet` 永远返回尸体
> → 每回合重复召唤（右边多一只手）、不加血、复活后朝右且没血条。
> 已修复并由用户实机确认；其余相关修正（每帧确认朝向/血条、站位两侧都纠正、爪牙兜底）见
> **`docs/plans/osty-fix-2026-10-20.md`**。

`src/Hooks/Core/BossOsty.cs` 曾被我用 PowerShell `Get-Content`(PS 5.1 默认 ANSI) + `Set-Content` 改一处
`private→internal`，整个文件被当成 GBK 读入并写回 → **中文注释全变乱码、部分换行被吞**。
已按「反向转码 + 按 `?` 断行 + 用已安装 DLL 反编译出的字符串字面量回填日志文案」修复，并且：

- 编译 0 错误；方法集合与反编译结果一致（只差编译器生成的 `MoveNext`/`SetStateMachine`）；
- `Logger.Log(` 出现次数与 DLL 一致（33 次）；
- 残留问题：**注释里仍有乱码字符**（`?`/`\uFFFD`），少数注释行缩进错乱；代码与日志文案是对的。
  → 若用户在意，可让新会话按实机行为重写该文件的注释（别再猜代码）。

**教训（务必遵守）**：改 C# 源码只用 `read`/`edit`/`write` 工具；**禁止**用 PowerShell 做字符串手术。
没有 git 仓库，改坏了不可回滚。

## 4. 用户的设计约束（他本人是设计者，他说的算）

- 一次只改一处；先拿证据再改，不许「猜一种改法让他跑一次」。
- 不要自己造美术/字体/排版，用原版节点。
- 不能删他要求的功能。
- 第二幕 `ActTable[1] = {4,4,4}`（三个槽都是铸剑储君）；**涂毒猎（静默猎手毒流）代码保留但未接入**，之后要接。
- 储君剧本 9 回合，6～9 循环；能量逐回合写死 `2,2,2,2,4,2,2,2,3`。
- 佩尔的士兵是**玩家的召唤物**（不是她的）。
- 玩家有 999 HP 作弊档，替他打回合可以直接 `POST /api/v1/singleplayer {"action":"end_turn"}` 推进。
