# 多人模式：多 Boss 同场 + 跟踪锁定（框架）

> 状态：**阶段 1 代码已落地，待实机验证**（2026-10-22 更新）。阶段 2～4 未开始。

## 0. 阶段 1 实施记录（代码位置）
| 条目 | 实现位置 | 备注 |
|---|---|---|
| 多 Boss 同场（按职业分配） | `Core/MultiBossPlan.cs` + `Encounters/CharBossEncounters.CharBossEncounterBase.GenerateMonsters` | `GenerateMonstersWithSlots` 是 non-virtual（不能 override），用 Harmony 前缀接 runState；多人 `Players.Count >= 2` 才追加第二只，第二只取 `ActTable` 该幕第二列（第三幕 `{3,4,2}` ⇒ 根除骨那场上无色储君），canonical 走 `ModelDb` 反射后 `ToMutable()` |
| `LockOnPower` | `Powers/LockOnPower.cs` | 挂在玩家身上、`StackType.Single`、`ShouldPlayVfx=false`，`AfterApplied` 回报给注册表 |
| 锁定注册 / 再平衡 | `Core/BossLockOn.cs` | `TargetFor` / `IsLocked` / `EnsurePower` / `OnBossDied` / `OnPlayerDied` / `Reset`；选「被锁次数最少的活着的玩家（并列随机）」；每场结束在 `BossHandUi.EndFight` 里 `Reset()` |
| 单体/随机选靶 | `Core/BossPlanner.ChooseTarget` | 先取「锁定这只 Boss 的玩家」，取不到才回落到「血最少」 |
| 群攻（AoE） | 原版行为，无需改动（**已读代码确认**） | `AttackCommand.TargetingAllOpponents` 只设 `TargetSide = 攻击者的对立面`，Boss（Enemy）⇒ 玩家侧全体；我们的 `HittableEnemies` 补丁只摘随从/奥斯提，不动玩家 |
| 死亡转锁 | `BossLockOn.TargetFor`（玩家死→重分）+ `BossFrameTick`（Boss 死→撤锁） | |
| 爪牙随主人死亡 | `Core/BossOsty.RemoveOnBossDeath` + `BossShadowPets.RemoveOnBossDeath` | 装饰随从原本就有；奥斯提用 `LoseHpInternal(CurrentHp, Unblockable|Unpowered)` 带走 |
| 多人血量缩放关闭 | `Patches/BossMultiplayerHpPatch.cs` | 只对本模组 Boss、且 `playerCount >= 2` |
| 手牌栏按锁定分流 | `UI/BossHandUi.Refresh` | 多人时只显示锁定本地玩家的那只 |
| 每帧维护覆盖所有 Boss | `Patches/BossFrameTickPatch.AllBosses` | 随从清理/奥斯提/剑补齐/撤锁逐只跑 |

### 待实机验证（有傀儡玩家后按此清单验）
1. 双人局 Boss 战是否真的上两只（日志 `多人(2 人)：这一幕同时上两只 Boss`）；
2. 两行 `跟踪锁定：… → <角色>（多人=2 人）`，且两只锁不同玩家；
3. `多 Boss 站位（2 只）：…` 坐标是否重叠 —— 若重叠，下一轮给第二只加水平偏移（改 UI，需坐标数据）；
4. 单体攻击只打锁定者、群攻打两人；
5. 打死一只 → 它的随从/奥斯提立刻消失、战斗不结束；两只都死才结束；
6. 打死一个玩家 → 那只 Boss 下一回合改锁另一人（日志会再出现一次 `跟踪锁定`）；
7. 单人局零回归（Boss 战表现与改动前一致）。

## 0.1 实机验证进度（2026-10-22，傀儡玩家双人局）

| 项 | 状态 | 证据 |
|---|---|---|
| 同场两只 Boss | ✅ | 日志 `多人(2 人)：这一幕同时上两只 Boss —— 我=RegentBoss，第 2 只=NECROBINDER_BOSS` |
| 血量不缩放 | ✅ | 日志 `多人(2 人)：跳过 … 的血量缩放`（两只各一行） |
| 单体/随机选靶走锁定者 | ⏳ 未观察到反例 | 两只 Boss 单体攻击都打在各自锁定者身上 |
| 两只各锁一人 | ⚠️ 曾两只锁同一人 → 已改判据（`max<=1 && min>=1` 才跳过）+ 实机待复验 |
| 站位（左上/右下、按职业左右） | ✅ 已不挤压 | 日志 `多 Boss 错位（2 只）：REGENT_BOSS(右下) → (…,810) | NECROBINDER_BOSS(左上) → (…,670)` |
| 特殊效果说明框（多人不显示） | ✅ | 拦截点挪到 `BossSpecialUi.Ensure` 内部（`ours >= 2` → `Hide()`） |
| 跟踪锁定图标 | ✅ 已换成塔1 图集裁的 lock-on（`res://CharacterBosses/images/powers/lock_on.png`） |
| 跟踪锁定文案 | ✅ Boss 名 + 人称（他/她/它）+「单一/随机/充能球」+ 对应型特殊效果附「只对你生效」 |
| Boss 死亡解锁 | ✅ 修过：新增 `BossLockOn.PurgeDeadBosses()` 每帧扫注册表（死掉的 Boss 可能已不在 `Enemies`/面板里） |
| 爪牙随主 | ⏳ 待验（`BossOsty.RemoveOnBossDeath`） |
| 两人的 UI 都显示 | ❌ **未做**（见下） |

## 0.2 剩余工作（按优先级）

> 2026-10-22 第二轮更新：**下面第 1、2 条已完成**（见 `docs/plans/multiplayer-cards.md` §5 与
> `docs/plans/handover-multiplayer.md` §1）。状态：0 ✅ / 1 ✅ / 2 ✅ / 3 已做（0.85）。

0. ✅ **第二只 Boss 的状态栏**：`BossStatusRow.Fill` 的能量球已改成按行各记一份（`SetEnergyOf`/`EnergyOf`），
   `UI/BossExtraStatusRow.cs` 曾经是过渡方案，**第二轮已停用** —— 现在每只 Boss 有自己整套面板（见 1）。
1. ✅ **双 Boss 各一套面板**（第二轮完成）：`UI/BossHandUi` 由单例改成
   `Dictionary<CharBossMonster, BossHandPanel>`（新增 `PanelFor` / `PanelBosses` / `ReleasePanel`），
   `Panel` / `CurrentBoss` 降级为「主面板」访问器（单人行为不变）；`BossFrameTick` 对每套面板各跑一次
   `RunFrame` + 各自 0.85 缩放；`BossOrbDisplay` 的激发高亮改成按 Boss 各记一份。
   **仍需实机确认**：两只 Boss 靠近时两套面板可能重叠（已加左右边界夹取，数值只有用户能看屏幕定）。
2. ✅ **意图按人清零**（第二轮完成）：做在**我们自己的手牌栏渲染侧**
   （`BossIntentBuilder.AimsElsewhere` → 显示 0），因为 `MoveState.Intents` 是每只生物一份、不分观看者。
3. ✅ **多 Boss UI 缩放**：面板整体 `Scale = 0.85`（每套面板各自缩），单人 1.0。

> 设计者：用户。下面 §1 的每条都已由用户确认，不要再改。

## 1. 已确认的设计（原话要点，冲突时以此为准）

1. **同一场战斗里同时存在 N 个 Boss**（N = 玩家数，目前最多 2）。玩家可以各自打各自的，
   也可以集火先杀一只减压；群攻牌能伤到**所有** Boss。
2. **Boss 血量不随人数缩放** —— 只关掉**本模组这几只 Boss** 的多人血量缩放，原版怪不动。
3. **跟踪锁定**（新 Debuff，无层数）：一名玩家 ↔ 一只 Boss 的绑定。
   描述：`__（对应 Boss）的攻击会优先攻击你，且 __（对应 Boss 的特殊效果）只对你生效。`
4. **特殊效果分两类**
   - **全局型**（影响所有玩家）：带毒刺击、时候未到、何人僭越。被锁定的玩家**不显示描述后半段**。
     - 「何人僭越」每有一名玩家，触发所需的失去血量 **+25**（2 人 = 50）。
   - **对应型**（只检测/只对锁定者生效）：偏差认知、袖里乾坤、共鸣，以及**预借时间**（根除骨 / 第三幕亡灵契约师）。
  （原计划里还有「灵魂绑定」，用户 2026-10-22 说明尚未制作 → **该需求暂时取消**。）
5. **特殊效果的可见性**：Boss 右侧的描述框**只默认显示对自己生效的那些**；
   想看别人的，把鼠标移到 Boss 状态栏的特殊效果图标上看（图标全都在）。
6. **玩家分配**：进入 Boss 战时所有玩家随机对应一只 Boss，**优先同职业**；同职业会重复时随机分配。
7. **目标选择**
   - Boss 的**单体攻击**与**随机攻击**（含充能球激发）**只以被锁定玩家为目标**；
   - Boss 的**群体攻击**打所有玩家；
   - **玩家不受限**，可以打任意 Boss。
   - **目标玩家死亡**后：该 Boss 转向「剩余玩家中跟踪锁定最少」的那个（并列则随机），并给其加一层跟踪锁定。
8. **跟踪锁定没有层数概念**，除上面两条（锁定攻击目标、限定对应型特殊效果）外**没有任何其它效果**。
9. **单人模式不加跟踪锁定**；多人中途掉到只剩 1 人时**不移除**已有锁定，只有 Boss 死亡才移除它对应的那个。
10. **胜利条件**：所有 Boss 都死亡才算赢。
11. **爪牙只随主人死亡而死**（奥斯提、佩尔的士兵都按这个来）。
12. **地图/顶栏**：目前 Boss 图标不全 → 优先显示已有的；将来齐了默认显示**房主**对应的那一个，
    顶栏那块也显示房主的头像，但文字说明要包含**所有**将要应对的 Boss。

## 2. 各幕的 Boss 编制（目前最多 2 人）

| 幕 | Boss 池 | 备注 |
|---|---|---|
| 1 | 灾厄骨（亡灵契约师）、**碎片储君** | 碎片储君待设计 |
| 2 | 铸剑储君、涂毒猎 | 都已有 |
| 3 | 刀猎、能力机 | 都已有 |
| 进阶 10 额外 | 无色储君、**根除骨** | 无色储君已有（第三幕定义），根除骨待设计 |

单人时的出场顺序**先不动**（沿用现在的 `ActTable` 行为）。

## 3. 现状（改造起点）

| 位置 | 现在是什么 | 要多人的地方 |
|---|---|---|
| `CharBossMonster` | 每只 Boss 自带一个**影子 Player**（`Shadow`），卡组/能量/手牌都挂在它身上 | 每个 Boss 都有自己的影子 Player —— 天然支持多只，但要确认 `BossRunStats.Ensure` 等按 Boss 分别登记 |
| `Encounters/CharBossEncounters.cs` | `ActTable` 每幕一行，`BossEncounter` / `SecondBossEncounter` 两个槽各换一只 | 2 人时**两个槽都要上场**（同一场战斗），并按「优先同职业」分配 |
| `Patches/BossSlotPatches.cs` | 两个槽分别替换（第二个用 `secondBoss: true` 取槽 1） | 分配逻辑要改成「按玩家职业选池内 Boss」 |
| `Core/BossPlanner.ChooseTarget` | 从可命中玩家生物里挑一个（血最少） | 改成「先找锁定这个 Boss 的玩家」 |
| `Patches/CombatSidePatches.cs` | `HittableEnemies` 按出手方相对解释；`GetOpponentsOf` 摘掉装饰随从 | 随机/群体选靶要按 §7 分流 |
| `Core/ShadowPlayerFactory` / `Patches/BossSlotPatches` | 无人数相关逻辑 | 关掉 `ScaleMonsterHpForMultiplayer`（只对本模组 Boss） |
| `Core/BossShadowPets`、`Core/BossOsty` | 爪牙标记 `MinionPower`、死亡后清理 | 加「主人死亡 → 爪牙跟着死」 |
| `CombatManager.IsCombatEnding` | 已经没有活着的 `IsPrimaryEnemy` 就结束 | 多 Boss 时天然要求全部死亡（它们都是 primary enemy），但要确认**装饰随从**不干扰 |

## 4. 实现分阶段

### 阶段 1：多 Boss 同场 + 跟踪锁定 + 选靶（框架）
1. **遭遇层**：多人时这一幕生成 N 个 `CharBossMonster`（N = 玩家数），按「优先同职业」随机分配（§11）。
2. **新 Power `LockOnPower`**：挂在玩家身上，记录 `对应 Boss`（存 Boss 的 creature id / 引用）。
   无层数；`PowerStackType.Single`；描述含 Boss 名，全局型特殊效果的被锁定者不显示后半段（§4）。
3. **选靶**：`BossPlanner.ChooseTarget` 与所有随机选靶（`AttackCommand` 的随机目标、球激发）优先取「锁定该 Boss 的玩家」。
4. **群攻**：保持原版 `TargetingAllOpponents`，多人时自然打到所有玩家。
5. **死亡转锁**：玩家死亡 → 该 Boss 重新选「锁定数最少」的玩家（并列随机）并加锁定；Boss 死亡 → 移除它对应的锁定。
6. **胜利条件**：所有 Boss 死亡；同时让爪牙随主人死亡（§11）。
7. **血量**：只对本模组 Boss 关掉多人血量缩放。

### 阶段 2：特殊效果按人分流
- 全局型（带毒刺击、时候未到、何人僭越）：对所有人生效；何人僭越阈值 `25 + 25×(人数-1)`。
- 对应型（偏差认知、袖里乾坤、共鸣）：只统计/只作用于锁定该 Boss 的玩家。
- 右侧描述框：只显示对本玩家生效的那些；状态栏图标全显、可悬浮查看。

### 阶段 3：地图与顶栏（✅ 2026-10-22 完成）
- 地图 Boss 图标：**继续用占位图**（用户：图标不着急）——`BossNodePath` 复用 TheKinBoss 美术；
  三张真图标（silent_poison / silent_shiv / defect_focus）照旧按遭遇覆盖。
- 顶栏该幕 Boss 区域：**用角色头像**（`CharacterModel.IconTexture` + `IconOutlineTexture` =
  `ui/top_panel/character_icon_<角色>.png`，就是玩家界面左上角那个头像）；原版 `DOUBLE_BOSS` 的
  悬浮文字本来就列出全部 Boss（`BossName1/BossName2`）。

### 阶段 4：新 Boss 与特殊效果（✅ 2026-10-22 完成）
- 碎片储君（第一幕，`Core/DebrisRegentScript.cs` + `Powers/CometBossPower.cs`）、
  根除骨（第三幕亡灵契约师变体）—— 上一轮会话做完，本轮核对确认。
- 预借时间：**对应型（只对锁定该 Boss 的玩家生效）**，且做成每玩家版累计。
- ~~灵魂绑定~~：用户 2026-10-22 说明尚未制作，**需求暂时取消**（代码里从来没有它的实现）。

## 5. 分批风险 / 需要实机确认的点

- 多 Boss 时原版**意图/回合调度**是否会对每只 Boss 各跑一次移动状态机（应该会，因为都是独立 `MonsterModel`）。
- `BossHandUi` 目前是**单例面板**（一只手牌栏）→ 多 Boss 时需要「按 Boss 各一个面板」或「只显示锁定自己那只」。
- `NCombatRoom.PositionPlayersAndPets` 的站位公式是按 1 只 Boss 算的，多 Boss 要确认不重叠。
- 多人时 `HittableEnemies` 的「相对出手方」语义在多 Boss 下是否仍成立。
