# 交接报告：多人联机（2026-10-22 第二轮收尾）

> 用途：换新对话时先读本文件 + `docs/plans/multiplayer-lockon.md` + `docs/plans/multiplayer-cards.md`。
> 本文件只写**已经确认的事实**与**下一步该动哪里**，不重复设计条文。

## 1. 当前代码状态（**源码已到这一版，DLL 还没装进游戏**）

- 已构建/已安装 DLL 哈希：`BCB1F664A53FB568ECF47E2DBAC38330AAD9B47AE0BD1BF95A5075041B66B997`（**2026-10-23 10:4x 已装**；构建指纹 `v0.1.8`，日志第一帧打 `===== CharacterBosses 构建：v0.1.8 …（二层 Boss = 灵魂骨 + 灵魂绑定）`；这一版加了「拖延+ = 下回合 +2 能量真的到账」、「X 费牌（挽歌/根除）按剩余能量记账」、「拦掉她的原版抽牌（计数照加）」和「巨镰第 3 回合伤害=23」；本轮**只装 DLL + PCK，没打包 zip** —— 用户说暂时不发朋友测试）
- PCK 也已重打并装进游戏（改了 `content/localization` 的 `monsters.json` / `powers.json`）：`CharacterBosses.pck` 694016 字节
- 上一版**已安装**的哈希（用户实测过的那版，含跟踪锁定 Single / 格挡不翻倍 / 灾厄不被吃）：`034240FD5AA646F2C892391A40C810C6AB1BBD5847F840335B489085DA5874C6`
- 上一版**已安装**的哈希（用户实测过的那版）：`4A8E0A8070BE476657CA3998A1E3F03BF7BB9C9BB763F7877A8201FFB8A5E6DE`
- 构建命令：`& tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -v m`（必须 0 错误）
- 安装：复制 `src\bin\Release\CharacterBosses.dll` → `D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\CharacterBosses\CharacterBosses.dll`
  （**该路径在工作区外，必须带 `sandbox_permissions: danger-full-access` + justification；子代理无此权限，只有主代理能装**）
- 本地化改动后必须重打 PCK（脚本路径要绝对路径）：
  `& tools\godot\Godot_v4.5.1-stable_mono_win64.exe --headless --path "<repo>\tools\pck_builder" --script "<repo>\tools\pck_builder\build_pck.gd" -- "<mod>\mod_manifest.json" "<mod>\CharacterBosses.pck" "<repo>\content" "CharacterBosses" "<repo>\tools\art\pck_extras.txt"`
  （**本轮改了** `content/localization`（新增 `NECROBINDER_BOSS.soul_bound`、`BOSS_SOUL_BOUND_POWER.*`），所以**必须**重打 PCK —— 已重打并装好。）

### 已实现并已安装（上一版就在的多人相关）

| 内容 | 文件 |
|---|---|
| 多 Boss 同场（按用户给定配对：骨储 / 猎储 / 猎机 / 骨储） | `Core/MultiBossPlan.cs`（Harmony 前缀接 `EncounterModel.GenerateMonstersWithSlots` 的 runState）+ `Encounters/CharBossEncounters.CharBossEncounterBase.GenerateMonsters` |
| `LockOnPower`（玩家身上，仅多人挂载）+ 本地化 | `Powers/LockOnPower.cs` |
| 锁定注册 / 均衡 / 死亡转锁 / 死主清理 | `Core/BossLockOn.cs` |
| 单体/随机选靶优先锁定者 | `Core/BossPlanner.ChooseTarget` |
| 站位（左上/右下） | `Patches/BossEnemySpreadPatch.cs` |
| 爪牙随主人死亡 | `Core/BossOsty.RemoveOnBossDeath` |
| 多人血量不缩放 | `Patches/BossMultiplayerHpPatch.cs` |
| 第二只 Boss 的状态栏（遗物/能量/辉星） | `UI/BossExtraStatusRow.cs`（**本轮已停用** —— 见下，每只 Boss 现在有自己的整套面板） |
| 意图发布给外部 mod | `CharBossMonster.PublishVanillaIntents()` |
| 彗星新规则 | `Powers/CometBossPower.cs` |

### 本轮（2026-10-22 第二轮）新增，**已在源码里、已构建 0 错误、尚未安装**

| 块 | 文件 | 一句话 |
|---|---|---|
| A1 冰川→休眠 效果 | `Patches/HibernateFrostBlockPatch.cs` | 冰球格挡（回合结束 `2+集中`/激发 `5+集中`，冰球几颗结几次）照原版发给所有 Boss，**只拦掉发给真人玩家那一笔** |
| A2 吸引仇恨→骸骨军团 效果 | `Patches/LegionOfBoneSummonPatch.cs` + `BossOsty.LegionSwapActive` + `CharBossMonster.EffectiveDrainOrder` | 只给 Boss 召奥斯提（归属该 Boss、补爪牙标记）；多人换牌生效时**死了不再自动补建**，只有这张牌再打出才复活；能量汲取第 1 组 → `{DEFY, LEGION_OF_BONE}` |
| A3 斗篷与匕首→刀刃交响曲 效果 | `Patches/BladeSymphonyPatch.cs` + `BossIntentBuilder`（`BLADE_SYMPHONY` 分支、`TeammateBossesOf`） | 队友 Boss 各拿 n 把小刀、当场朝**自己锁定的玩家**扔，意图提前显示 |
| B 双 Boss 两套面板 | `UI/BossHandUi.cs`（多实例）+ `BossFrameTick`（每套各跑 `RunFrame`）+ `BossOrbDisplay.Highlight`（高亮按 Boss 各记一份）+ `BossStatusRow(.Icons)`（能量球/辉星**按行按 Boss 各记一份**） | 每只 Boss 一套完整的（手牌 + 遗物/能量/辉星 + 意图）；状态条里三个静态单例（`EnergyCounterNode` / `_energyCounter` / `_starShadow`）都改成了按行/按 Boss 的字典 |
| C 特殊效果按人分流 | `BossLockOn.RealPlayers/RealPlayerCount/SpecialEffectAppliesTo` + 4 个对应型 Power + `KnowThyPlacePower` 阈值 | 对应型只认锁定者；全局型对所有真人玩家；何人僭越阈值 `25 × 人数` |
| F 意图按人清零 | `BossIntentBuilder.AimsElsewhere` | 「打的是别人」的攻势意图对本地玩家显示 0 |
| D 顶栏图标 | `Patches/TopBarBossIconFallbackPatch.cs` | 我们的遭遇 → **角色头像**（`CharacterModel.IconTexture`/`IconOutlineTexture` = `ui/top_panel/character_icon_<角色>.png`，就是玩家界面左上角那个头像，`KeepAspectCentered` 防拉扁）；别人的遭遇不动；取不到退回占位图。地图节点图标仍用占位图（用户：不着急） |
| 性能 | `BossOsty.HerLiveOstyNode` / `BossFrameTick.Tick` / `BossSovereignBlade.MirrorAllInScene` / `PublishVanillaIntents` | 见 `multiplayer-cards.md` §5 末尾 |
| 多 Boss 收尾（顺手修的真 bug） | `Core/BossShadowPets.cs` | 原来「挂死亡监听」「死亡清理」各用一个静态字段记状态 ⇒ 两只 Boss 同场时**每帧重复挂监听**（死亡时几千个回调）、**打死一只会把队友的士兵一起摘掉**。改成按生物 hash 去重 + `OwnedBy` 记录随从归属；`BossShadowPets.Update` 也改成对每只 Boss 都跑（以前只跑面板上那只，队友的士兵不朝左、没爪牙标记） |
| 多 Boss 状态条 / 集中监控 | `UI/BossStatusRow(.Icons).cs`、`BossFrameTick.RefreshIntentsIfFocusChanged` | 三处静态单例（`EnergyCounterNode` 判销毁、`_energyCounter/_energyPlayer`、`_starShadow`）改成按行/按影武者各记一份；集中监控改成 `Dictionary<Boss,int>`（队友 Boss 拿到集中时意图也会重算），并在战斗结束时清账 |
| E 预借时间每玩家版 | `Powers/BorrowedTimeBossPower.cs` | 累计改成 `Dictionary<Player,int>`（各花各的），图标显示离触发最近的那个玩家还差多少 |

### 未实现 / 待确认（下一轮的活）

1. **E 阶段 4：已完成**（2026-10-22 用户确认）——碎片储君＝`Core/DebrisRegentScript.cs` + `Powers/CometBossPower.cs`
   （上一轮会话就做完了，本轮只核对）；根除骨＝第三幕亡灵契约师，「预借时间」是**对应型（单体）**，已并入 C 的闸门并做成每玩家版；
   **灵魂绑定用户说还没做 → 需求暂时取消**（代码里从来没有它的实现）。
2. **A3 的观感细节待实机确认**：队友出刀时没有「卡面摆出来」的过场（直接 `CardCmd.AutoPlay`），
   如果用户想看到那把刀先亮一下，需要在 `BladeSymphonyPatch` 里照 `SilentBlades.FrontloadRealShivs` 那套做。
3. **B 的版面待实机确认**：两套面板各 0.85 缩放、各自贴自己的 Boss（左上/右下），
   已加左右边界夹取，但**两只 Boss 靠得近时两块仍可能重叠** —— 只有用户能看屏幕定数值
   （`BossHandPanel.Layout.cs` 的 `PanelOffsetX` / `BossFrameTick` 里那个 `0.85`）。
4. **地图节点图标仍是占位图**（用户 2026-10-22：图标不着急，先用占位的）。顶栏那颗已经换成角色头像。


## 2. 这一路踩过的坑（务必别再踩）

- **⭐⭐ 影子 Player 不在 `CombatState.PlayerCreatures` / `CombatState.Players` 里**（2026-10-22 日志实证：
  `LegionOfBone` 的 `foreach (c in PlayerCreatures)` 只跑了 2 次，都是真人）。
  ⇒ 任何「遍历玩家」的原版逻辑**都不会带上我们的 Boss**，所以
  ①「只拦真人那一路」= 谁都不生效（骸骨军团就此变成谁都没召唤到）；
  ② `BossIntentBuilder.TeammateBossesOf` 里 `c.IsPlayer` 的过滤永远为假（A3 队友意图/出刀全废）。
  正确写法：按 `Creature.Monster is CharBossMonster` 判定队友，需要给每只 Boss 做事就自己遍历
  `CombatState.Enemies`（`LegionOfBonePlayPatch` / `BladeSymphonyPatch` 都是整段接管 OnPlay）。
- **⭐ 多人换牌的三张牌曾经永远打不出去**（2026-10-22 用户实测：三层根除骨全程没有奥斯提）：
  `BossScript.BuildHand` 造的是**换上的类型**（`LegionOfBone`），而 `BossScriptPlanner.Matches`
  只拿**原牌类型**（`entry.Card` = `PullAggro`）去比 ⇒ 日志 `跳过 [PullAggro(不在手牌)]` ⇒ 那张牌不进 Plan。
  修法：`Matches(card, entry, boss)` 里再用 `MultiplayerCardSwap.ResolveQuiet(entry.Card, boss)` 比一次
  （用 Quiet 版，否则逐张比对会把日志刷满）。
  **同一条 bug 让 A1（休眠）/ A3（刀刃交响曲）也一直没真的打出去过** —— 看到「跳过 [××× (不在手牌)]」先怀疑这里。

- **`GenerateMonstersWithSlots` 是 non-virtual** → 只能 Harmony 前缀拿 runState。
- **`RunManager.Instance.State` 不存在**、**`CombatStateTracker.CurrentState` 不存在** → 本地玩家用 `LocalContext.GetMe(combatState)`。
  ⚠ 修正：`RunManager.State` **存在，但是 private**（C# 直接访问会 CS1061）。要「任何时刻都能拿到本局人数」
  （例如在 `ActModel.PullNextEncounter` 里）必须反射读它 —— 见 `MultiBossPlan.CurrentRunState()`。
- **`MoveState.Intents` 的 setter 对外只读**（`CS0200`）→ 反射写 `<Intents>k__BackingField`。
- **`CardModel.HasEnergyCostX` 是 protected** → 用 `EnergyCost.CostsX`；`ResolveEnergyXValue()` 在打出前是 0。
- **`Creature` 没有 `Id`** → 用 `RuntimeHelpers.GetHashCode` 做键；`Creature.LoseHpInternal(amount, ValueProp.Unblockable|Unpowered)` 才是「失去生命」。
- **`ValueProp` 没有 None**（flags：Unblockable/Unpowered/Move/SkipHurtAnim）。
- **跨 owner 塞牌会抛 `Tried to add cards with different owners to the same pile`** → 战斗直接卡死（用户实测「洗不了牌」）。要复制成玩家拥有的新卡再 `AddGeneratedCardToCombat`。
- **原版展示塞牌用 `CardCmd.Preview(card, 0.75f)` + `Cmd.Wait(1f)`**（照抄 `BiiigHug`）。
- **每帧硬写敌方位移会被原版布局拉回去** → 挂在 `PositionEnemies` 的 postfix。
- **`EnergyCounterNode` 曾是静态单例** → 两条状态栏会互抢（已改按行各记一份）。
- **Harmony 前缀返回 false 时必须自己赋 `__result`**，否则调用方 await null → NRE。
- 意图节点渲染那条链路**不计算她的虚弱**（`WeakPower.ModifyDamageMultiplicative` 里 `dealer != Owner` 直接返回 1），所以我们在 `BossIntentBuilder.ApplyOwnerDamageModifiers` 里只补虚弱、**不补力量**（力量节点会算）；奥斯提牌走 `OstysDamage`/`ExtrDamage` 分支，不吃她的力量/虚弱、只吃钙化。
- **⭐ 本轮最重要的新事实：影子 Player 也在 `CombatState.Players` 里**（`Creature.IsPlayer => Player != null`，而影子 Player 的 Creature 就是 Boss 本体）。
  所以凡是「玩家」相关判定都**必须**用 `BossLockOn.RealPlayers / RealPlayerCount`（按 `Creature.Monster == null` 滤），
  **不能**用 `CombatState.Players.Count`。反过来说：`MultiBossPlan.WantsSecondBoss` 读的是 `RunState.Players`（只有真人），
  那个才是判断「是不是多人」的可靠口径。
- **原版顶栏本来就支持两只 Boss**：`NTopBarBossIcon` 有 `_secondBossIcon` + `DOUBLE_BOSS` 悬浮说明（填 `BossName1/BossName2`），
  只要 `Act.SecondBossEncounter != null` 就会同时显示两颗图标 + 两个名字。**缺口只是 `ui/run_history/<id>.png` 贴图**。
- **原版「休眠」的格挡分发写在 `FrostOrb.Passive/Evoke` 里**（`foreach (玩家 p in CombatState.Players) if (p != Owner) GainBlock`），
  **不是**由 `HibernatePower` 自己做的；`HibernatePower` 只有「自己回合开始 −1」这一件事。
  ⭐ 「−1」这条**对影子 Player 是生效的**：`BossHookBridge.FireTurnStart` 每个 Boss 回合都会调
  `Hook.AfterPlayerTurnStart(combat, ctx, shadow)`，而 `HibernatePower.AfterPlayerTurnStart` 判的正是
  `player.Creature == Owner` ⇒ 休眠确实只持续她一个回合，不需要我们额外补计数。
- **原版激发是「先 Remove 再 Evoke」**（`OrbCmd.Evoke` 里 `if (dequeue) queue.Remove(orb)` 在 `await orb.Evoke()` 之前）。
  所以「目标队列里还有没有那颗冰球」**不能**用来判断「这笔格挡是不是他自己的」——
  A1 的拦截为此额外加了一条「真人自己的冰球正在结算中（时间窗 1.5s）」的放行通道
  （`FrostOrb.Passive/Evoke` 上只记录、不改行为的前缀）。宁可不拦，也不许吞玩家的格挡。
- **`OstyCmd.Summon` 里的 `combatState.Allies` 查找对敌方阵营的 Boss 永远找不到**（影子玩家的奥斯提在 Enemies 侧），
  所以「死掉的奥斯提」是靠 `IsOstyAlive` 先判、再新建；这也是为什么「再打一次骸骨军团 = 重新召一只」天然成立。
- `OstyCmd` / `NTopBarBossIcon` 所在命名空间小写：`MegaCrit.sts2.Core.Nodes.TopBar`、`MegaCrit.Sts2.Core.Assets.PreloadManager`。

## 3. 工作方式约定（用户明确要求）

- 一次只改一处；先拿证据（反编译 `sts2.dll` 或读 `%APPDATA%\SlayTheSpire2\logs\mod_log.txt`）再改，不许猜着改。
- 改 C# 源码只用 read/edit/write，**禁止** PowerShell 做字符串手术。
- 不留半成品：做不动的宁可跳过并写清原因（半成品会破坏正在跑的战斗）。
- **单人模式零回归**：新逻辑一律用 `MultiBossPlan.WantsSecondBoss` / `BossLockOn.RealPlayerCount >= 2` 包住。
- 多人行为**只有用户能实机验证**（他用「傀儡玩家」mod 一人控两人）；我们只能保证构建通过 + 日志取证。
- `src/Hooks/Core/BossOsty.cs` 里有约 210 个 U+FFFD（历史遗留的乱码注释，**不影响编译**）——改这个文件时别大面积重写。

## 4. 实机验证清单（用户下次测时看）

1. 双人局第三幕：日志出现 `多人(2 人)：这一幕同时上两只 Boss`、两行 `跟踪锁定：… → …（多人=2 人）`、`多人换牌（2 人）：…`；
2. 两只站位：一只左上、一只右下（`多 Boss 错位（2 只）` 日志里坐标应相差约 ±200/±140）；
3. **两套面板**：日志出现两行 `hand ui: 挂到 … boss=XXX（本场共 2 套面板）`，屏幕上有两份手牌/遗物/能量；
4. 打死一只：`跟踪锁定解除` / `爪牙随主人死亡` 是否触发、另一只是否继续正常打；
5. 单人局：Boss 战与改动前一致（无锁定图标、无换牌、只有一套面板 `本场共 1 套面板`）。
6. **A1**（能力机第三幕）：她打出「休眠」后，日志 `休眠：冰球格挡 N 不发给真人玩家 …`，两只 Boss 各自涨格挡、玩家不涨；
7. **A2**（根除骨第三幕）：`骸骨军团：… 给 … 召唤奥斯提` 每次打出都有一行、且**没有**发给真人玩家的那一行；
   她的奥斯提被打死后日志是 `骸骨军团（多人换牌）：场上没有活着的奥斯提 → 不自动补建，等这张牌再打出`；
8. **A3**（刀猎第三幕）：她打出「刀刃交响曲」时，另一只 Boss 的意图上出现小刀伤害，且**实际伤害与意图一致**；
9. **F**：不是锁定你的那只 Boss，它的单体攻势意图在你的界面上显示 **0**。
10. **性能**：boss 战是否比上一版流畅（重点看两只同场时）。
