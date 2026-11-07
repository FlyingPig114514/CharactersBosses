# CharacterBosses — 把每一幕 Boss 换成原版角色（崩坠式）

> 侦察依据：`docs/plans/recon-charboss-sts2.md`（StS2 侧，含全部反编译证据）、
> `docs/plans/recon-downfall-charbosses.md`（崩坠侧，含 `javap` 证据）。
> 本文件是实施计划，**先于代码**。

## 0. 快速开始（看这里就够了）

```powershell
# ① 联机前 / 联机后 开关模组（会重启才生效）
powershell -ExecutionPolicy Bypass -File tools\charbosses-mod.ps1 -Action disable   # 关
powershell -ExecutionPolicy Bypass -File tools\charbosses-mod.ps1 -Action enable    # 开

# ② 构建 + 安装 + 实机冒烟测试（自动开车到 Boss 并打几个回合，最后打印关键日志）
powershell -ExecutionPolicy Bypass -File tools\charbosses-smoke-test.ps1 -Boss cycle

# ③ 设计 Boss：改这两个文件，然后重跑 ②
#    src/Hooks/Core/BossDefs.cs                  ← 卡组 / 遗物 / 每幕 HP·能量·手牌数 / 出牌优先级 / 禁用牌
#    src/Hooks/Encounters/CharBossEncounters.cs  ← 哪一幕的哪个 Boss 槽换成哪个角色（ActTable）

# ④ 挑牌：卡池清单（费用/稀有度/中文名/效果/类名）
#    docs/plans/cards-{ironclad,silent,defect,necrobinder,regent,colorless}.md
```

| 我想知道… | 看哪节 |
|---|---|
| 怎么定义/改一个 Boss | §9（★主要看这节） |
| 哪个角色能配上哪些机制（充能球/星星/随从） | §9.6 |
| 卡组里某张牌不想让它打出来 | §9.7 `SkipCards` |
| 卡池清单怎么重新生成（游戏更新后） | §9.5 |
| 实机怎么验证、看哪些日志 | §6 / §11.2 |
| 现在已经验证到哪一步了 | §7 实测记录 / §11.2 |
| 哪些地方超出了 `rules.md` 的「只改 src/Hooks」 | §10 |

## 1. 目标

每一幕的 Boss 被替换成一名原版可玩角色（铁甲战士 / 静默猎手 / 故障机器人 / 死灵缚者 / 摄政王）：

- **有自己的卡组**：从该角色自己的卡池里选牌，按幕分强度档（Act1 弱 / Act2 中 / Act3 强），每局独立洗牌。
- **有自己的遗物**：真实触发（走 `Player.Relics` → `IterateHookListeners`）。
- **像玩家一样打牌**：抽手牌 → 花能量 → 出牌 → 弃牌，攻击/能力/格挡/易伤等全部走原版卡牌效果。
- **Boss 手牌 UI**：玩家回合就能看到 Boss 的手牌；**本回合确定要打出的牌高亮、下方显示该牌对应意图；不打的牌变暗**。
- 卡组 / 遗物 / 每幕档位由**数据定义文件**描述，便于用户自行设计 Boss。

### 与崩坠的关键差异（本设计主动选择的）

| | 崩坠（StS1） | 本项目（StS2） |
|---|---|---|
| 卡组 | **没有卡组**，按回合号写死的出牌剧本 | **真卡组**（抽牌堆/弃牌堆/手牌） |
| 卡牌效果 | 重写 ~400 张 `En*` 卡（`use(p, m)`） | **复用原版卡牌**（`CardCmd.AutoPlay`） |
| 意图 | 靠整回合模拟猜 | **回合开始时计划已确定**，意图即实际计划 |
| 遗物 | 手工转发 30 个钩子 | 挂在影子 Player 上，`IterateHookListeners` 自动触发 |

因为「计划在玩家回合前就锁定」，抽牌的随机性不会破坏意图预告——这是 StS2 侧最大的结构性优势。

## 2. 架构（影子 Player，Architecture A）

```
      真实玩家 Player#1..N  (Creature.Side = Player)
                  ▲  卡牌效果只认 Creature（DamageCmd / PowerCmd）
                  │
   ┌──────── 影子 Player（每个 Boss 一个）────────┐
   │ Character = Ironclad（拿它的 CardPool/遗物池）│
   │ PlayerCombatState：Hand/Draw/Discard/Exhaust │
   │ Relics：Boss 遗物（真实触发）                 │
   │ Creature ←──反射──┐                          │
   └───────────────────┼──────────────────────────┘
                       │
   ┌──── Boss Creature（唯一战斗实体）────────────┐
   │ Monster = CharBossMonster（mod 类型，自动注册）│
   │ Side    = Enemy                             │
   │ Player  ←──反射── 影子 Player（让卡牌系统可用）│
   └─────────────────────────────────────────────┘
```

**唯一非官方操作**：反射互绑 `Player.<Creature>k__BackingField` 与 `Creature.<Player>k__BackingField`。

### 2.1 回合循环（无 Harmony 的意图钩子）

`MonsterState.GetNextState(Creature owner, Rng rng)` 是 `virtual`，`MonsterModel.RollMove` 会调它，
`MonsterMoveStateMachine.States` 是 public 字典 → **动态换招式不需要任何 Harmony patch**：

```
GenerateMoveStateMachine() → machine{ "THINK": BossThinkState, "PLAY": 占位 MoveState }
玩家回合前 RollMove：
  THINK.GetNextState()  → ① 抽手牌 ② 按优先级+能量预算选牌 ③ 建计划
                         ④ States["PLAY"] = new MoveState("PLAY", onPerform, 计划生成的意图…)
                         ⑤ 返回 "PLAY"
  → NextMove = 计划对应的 MoveState → NCreature.RefreshIntents() 显示意图
玩家回合：手牌 UI 显示手牌（高亮计划内 / 变暗计划外）
敌人回合：PerformMove → onPerform → 逐张 await CardCmd.AutoPlay(...) → 扣能量 → 弃手牌
下一次 RollMove：PLAY.GetNextState() = "THINK" → 回到 ①
```

### 2.2 必须的隔离补丁（否则 Boss 会被当成玩家）

| # | 目标 | 作用 |
|---|---|---|
| P1 | `CombatState.get_PlayerCreatures` Postfix | 滤掉 charboss creature，使 `Players` 不含影子 Player（否则回合开始抽 5 张/重置能量/弃手牌全砸到 Boss 身上） |
| P1b | `NCombatRoom.AddCreature` Prefix | 强制走 `_enemyContainer`（`IsPlayer` 为真会被画到玩家侧） |
| P2 | `MonsterModel.RollMove` Postfix | 仅对 charboss：确保 `NextMove` 是我们的计划（兜底） |

### 2.3 注入点（换 Boss）

- P0（主）：`ActModel.PullNextEncounter(RoomType)` Postfix → Boss 房返回我们的 **canonical** encounter。
- P0b：`RunManager.GenerateRooms()` Postfix → `act.SetBossEncounter(...)`（新游戏确定性）。
- P0c：`ActModel.FromSave` / `ValidateRoomsAfterLoad` Postfix（读档）。
- **开发开关**：`charbosses_debug.txt`（放在 mod 目录）存在时，把 **Act1 第一个普通战斗遭遇**也换成 Boss 遭遇 → 1~2 个房间即可测试，省掉打通整幕的成本。

## 3. 文件布局（全部在 `src/Hooks/`，单文件 ≤200 行）

```
src/Hooks/
  Core/    BossDefinition.cs      角色 Boss 的数据定义（卡组/遗物/HP/能量/档位）
           BossArchetypes.cs      5 角色 × 3 幕的定义表 ★用户主要编辑这里
           ShadowPlayer.cs        影子 Player 的创建与反射互绑
           BossCombat.cs          战斗内持久状态（影子 Player/手牌/牌堆/计划）
  AI/      BossPlan.cs            本回合计划（要打的牌 + 目标 + 意图）
           BossPlanner.cs         优先级 + 能量预算选牌
           BossIntentBuilder.cs   由计划生成 AbstractIntent 列表
  Monsters/CharBossMonster.cs     MonsterModel 基类（VisualsPath/HP/状态机）
           BossThinkState.cs      GetNextState 里做计划
           IroncladBoss.cs        样板：易伤流铁甲战士
  Encounters/CharBossEncounter.cs EncounterModel（每个 Boss 一个子类）
  Patches/  BossReplacementPatches.cs
           IsolationPatches.cs
  UI/       BossHandUi.cs         手牌栏容器（NCard + 意图 + 高亮/变暗）
content/CharacterBosses/localization/{eng,zhs}/{monsters,encounters}.json   （pck 内）
```

## 4. 里程碑

- **M1 换 Boss**：`IroncladBoss` 立绘出现在 Boss 房（先用普通攻击占位，不碰卡牌系统）。
- **M2 会打牌（Go/No-Go）**：影子 Player + `CardCmd.AutoPlay` 打出第一张 `StrikeIronclad`。
  - 验证点：伤害落到玩家身上、日志无异常、**打死 Boss 进胜利结算**（不是 run 失败）。
- **M3 AI + 意图**：真卡组抽牌、能量预算选牌、意图 = 实际计划。
- **M4 手牌 UI**：高亮/变暗 + 每张牌下方意图。
- **M5 遗物 + 数据化**：真实遗物触发；5 角色 × 3 幕档位的编辑接口。
- **M6 样板**：易伤流铁甲战士（Bash/Thunderclap 上易伤 + 重击收尾）。

## 5. 风险与对策

| 风险 | 对策 |
|---|---|
| 影子 Player 污染 `Players` | P1/P1b；日志打印 `Players.Count` 断言 |
| 多人 checksum 不一致 | MVP 只支持单人；`affects_gameplay` 已标 true |
| `OnPlayWrapper` 在敌方回合内的额外 await | 全部串行在 `MoveState.onPerform` 的 await 链里；异常全 catch 并记日志 |
| 需要玩家选择的原版卡不适用于敌人 | 计划器跳过 `TargetType` 非单敌/全体/自身的牌 |
| 卡牌 UI 在影子 Player 名下无节点 | `NCard.FindOnTable` 有 null 检查；手牌 UI 自己建节点 |

## 6. 验证方式

`install-mod.bat` → 启动游戏 → 用 `charbosses_debug.txt` 开关把 Act1 第一场普通战斗换成 Boss 遭遇 →
`tools/read_sts2_logs.ps1` 检查 `[Hook]` 与 `[CharBoss]` 日志 → 记录实机结论到本文件的「实测记录」。

## 7. 实测记录（2026-10-15，游戏内）

**已通过的验证**

| 项 | 结果 |
|---|---|
| 替换一幕 Boss | `[Hook] act 0 boss replaced ... -> ENCOUNTER.IRONCLAD_VULNERABLE_BOSS_ENCOUNTER` |
| 调试开关 | `charbosses_debug.txt` 生效，第一场普通战斗即刷出 Boss（省掉打通整幕） |
| 影子 Player | `shadow player ready: deck=15 drawPile=15 relics=2 hp=140 act=0` |
| 真卡组循环 | 每回合抽 5 张、打完进弃牌堆、抽牌堆空时自动洗回 |
| 真遗物 | `relics=2`（燃烧之血 + 纸蛙），由 `IterateHookListeners` 触发 |
| 真卡牌效果 | `PLAY TREMBLE → TAUNT → THUNDERCLAP`；玩家吃到 4 层易伤、掉了血；Boss 得到 6 格挡 |
| 意图=计划 | 第 2 回合计划 `[CRUELTY,DOMINATE,BREAK,BULLY]`，意图出现 **35 点攻击**（欺凌按易伤层数滚起来） |
| 中英文 | Boss 名显示「铁甲战士」 |
| 手牌 UI | 贴 Boss 立绘包围盒上方 20px；要打的牌在左、不打的压暗排右；意图只在卡牌下方（Boss 头顶不再重复） |

**踩过的坑（都已在代码里修掉）**

1. `pck` 里本地化路径重复前缀（`CharacterBosses/CharacterBosses/...`）→ `content/` 下的目录层级必须与 `res_prefix` 配合。
2. `NCard.Create` 只是从对象池取节点并设 `Model`，**不会刷视觉**，且回收后尺寸被重置成 `0`；
   卡面内部节点按锚点撑满父级 → 父级 0 尺寸整张卡塌成看不见。必须显式 `Size = (280,392)` + `UpdateVisuals`。
3. `NIntent` 必须在**入树之后**再调 `UpdateIntent`，否则内部字段还没建好会空引用。
4. mod 程序集的 Godot 类没走 `ScriptManagerBridge` 注册，`_Process` 不保证被调用 → 关键布局要同步执行，不能只靠帧回调。
5. `ActModel.PullNextEncounter` 的 Postfix **必须返回 canonical 实例**（调用方 `RunManager.CreateRoom` 自己会 `.ToMutable()`）。
6. `NCreature` 的矩形只是交互框，比立绘矮得多；定位要用 `NCreature.Visuals.Bounds`（立绘包围盒 Control）。

**已知未解决**

1. **兼容性噪音**：影子 Player 的 `netId` 不在 run history 里，其它 mod（实测 RitsuLib 的 `CardOnPlayHook`）按 netId 查玩家统计时会抛异常并打进日志；不影响战斗（牌的效果照常结算），但联机/统计类 mod 会刷错误日志。**不打算用「往 run history 塞假玩家」来消音** —— 那会污染存档里的爬塔记录。
2. ~~地图上仍显示原版 Boss 名/图标~~ → **已修**（R2）：改成在 `ActModel.ApplyDiscoveryOrderModifications` 这个「选 Boss 的最终收口」就替换，地图屏读的 `ActModel.BossEncounter` 因此直接是我们的遭遇。**待实机验证**。
3. 部分卡牌的意图是近似值（只读 `DynamicVars.Damage`），如「欺凌」这类按易伤加伤的牌数字可能不准。
4. 读档续战：R2 已加兜底（`PlanReady` 标记 + 没计划就现算），不会白站一回合；但**手牌栏在续战那一回合不会提前显示**。
5. 多人联机未做适配（`affects_gameplay: true` 会被拦）。
6. X 费牌（如旋风斩）与需要玩家做选择的牌已被 AI 主动排除，避免「白嫖全部能量」和空转。

## 8. 开/关模组（联机用）

```powershell
powershell -ExecutionPolicy Bypass -File tools\charbosses-mod.ps1 -Action disable   # 联机前
powershell -ExecutionPolicy Bypass -File tools\charbosses-mod.ps1 -Action enable    # 联机后
```

把整个 mod 文件夹在 `mods\` 与 `mods_disabled\` 之间来回搬；游戏只在启动时扫 `mods\`，**切换后要重启游戏**。

## 9. 用户设计 Boss 的接口（★主要看这一节）

### 9.1 你要改的地方：只有两个文件

| 文件 | 改什么 |
|---|---|
| `src/Hooks/Core/BossDefs.cs` | **卡组 / 遗物 / 每幕 HP、能量、手牌数 / 出牌优先级** |
| `src/Hooks/Encounters/CharBossEncounters.cs` | **哪一幕的哪个 Boss 槽换成哪个角色**（`ActTable`） |

数值和卡表全部数据驱动：改完 `install-mod.bat` 重启游戏就生效，不需要碰任何逻辑代码。

### 9.2 一个 Boss 定义长什么样

```csharp
public static readonly BossDefinition Regent = new()
{
    Key = "REGENT_BOSS",                 // 日志标签，对应 IRONCLAD_BOSS.name 那套 key
    VisualScene = "regent",              // 复用哪个角色的战斗立绘（5 个都能用）
    CharacterType = typeof(...Regent),   // 决定卡池/遗物池/起始卡组
    Hp       = new[] { 145, 200, 255, 295 },   // 下标 = ActModel.Index（0=一幕,1=二幕,2=三幕）
    Energy   = new[] { 3, 3, 4, 4 },
    HandSize = new[] { 5, 5, 6, 6 },
    DeckByAct = new List<Type>?[]
    {
        new List<Type> { typeof(StrikeIronclad), ... },   // 一幕卡组
        new List<Type> { ... },                           // 二幕卡组（更强）
        new List<Type> { ... },                           // 三幕卡组（最强）
        null,                                             // 备用幕
    },
    RelicsByAct = new List<Type>?[] { new List<Type>{ typeof(PaperPhrog) }, ... },
    Priority = new List<Type> { /* 出牌优先级，靠前的先打 */ },
};
```

要点：

- **卡组里写的类型重复几次就是几张牌**（例如 `typeof(Bash), typeof(Bash)` = 2 张痛击）。
- `DeckByAct[幕]` 非空就用它；为空回落到 `Deck`；两者都为空时走**占位卡组**（见下）。
- `RelicsByAct` 同理；为空回落到 `Relics`，再为空用角色起始遗物。
  与起始遗物重名的会自动跳过，不会重复。遗物是**真实触发**的（挂在影子 Player 上，走 `IterateHookListeners`）。

**占位卡组规则**（你还没设计的角色会走这条，`BossLoadout.AutoDeckFromPool`）：
从该角色**自己的卡池**里按「类型 × 费用」分档挑 16 张 —— 攻击 1 费×4 / 2 费×4 / 3 费×1、
技能 1 费×2 / 2 费×2 / 0 费×1、能力 1 费×1 / 2 费×1，不够再按 1→2→3→0 费补满。
挑之前会滤掉 AI 用不了的牌（状态/诅咒、`Unplayable`、X 费、需要玩家选择目标的）。
结果是每个角色都有 1 费×7 / 2 费×7 / 3 费×1 / 0 费×1 的曲线和一张 3 费大招，例如：

| 角色 | 自动占位卡组里的招牌牌 |
|---|---|
| 静默猎手 | 匕首雨、回响斩击、终结技、涂毒、刀刃交响曲、**谋杀**(3费) |
| 故障机器人 | 球状闪电、寒流、弹幕齐射、混沌、冰川、**冰之长枪**(3费) |
| 亡灵契约师 | 荒疫打击、碎骨、护卫、来生、死亡使者、**收割**(3费) |
| 储君 | 新月长矛、迫降、七星、天穹之力、创世纪、**轰击**(3费) |

> 这条规则只是**为了让未设计的角色也能打出一套像样的牌**，不是替代你的设计 ——
> 你在 `DeckByAct` 里写了什么就用什么。自检日志会明确标出「未定义 → 按卡池自动配的占位卡组」。
- 出牌优先级：不在 `Priority` 里的牌按卡牌类型兜底（能力 > 攻击 > 技能）。
  当前选牌是「按优先级排序后贪心塞满能量」，所以同一套卡组每回合的行为是可预测的。
- HP/能量/手牌数只受**幕**影响，不受「是哪个角色」影响 —— 即同一角色在一幕/二幕/三幕会自动变强。

### 9.3 幕与 Boss 槽的映射（`ActTable`）

```
ActTable[ActModel.Index][该幕第几个原版 Boss 槽] = 角色下标（0铁甲 1静默 2故障 3亡灵 4储君）

Index 0 = 第一幕（密林 或 暗港，两者 Index 都是 0）→ { 铁甲, 静默, 故障 }
Index 1 = 第二幕（巢穴）                          → { 亡灵, 储君, 铁甲 }
Index 2 = 第三幕（荣耀）                          → { 静默, 故障, 亡灵 }
```

映射是**按原版 Boss 槽位固定**的（不是随机）：同一幕的三个候选 Boss 会变成三个不同角色，
且与地图上显示的候选一致；走完三幕 5 个角色都会出场。

### 9.4 想加一个「同一角色、不同流派」的 Boss

在 `BossDefs` 里加一个新定义 + 在 `CharacterBosses.cs` 加一个怪物类 + 在 `CharBossEncounters.cs` 加一个遭遇类
（三行），再把角色下标塞进 `ActTable`。三种类都只有几行样板，照抄最近的即可。

### 9.5 挑牌用的辅助工具（生成「卡池清单」）

卡牌的费用/类型/稀有度/数值**都不在**游戏的本地化文件里（那里只有名字和效果文案，而且文案里写的是
`{Damage:diff()}` 这种占位符）。它们只写在卡牌类里：

```csharp
protected Bash() : base(2, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
protected override IEnumerable<DynamicVar> CanonicalVars => ... new DamageVar(8m, ValueProp.Move)
```

两个脚本把它们挖出来，替换掉描述里的占位符，再和官方中文合并成可读清单：

```powershell
powershell -ExecutionPolicy Bypass -File tools\extract-card-meta.ps1   # 反编译 ~520 个卡牌类 -> .recon\card-metadata.tsv（约 5~10 分钟）
powershell -ExecutionPolicy Bypass -File tools\gen-card-lists.ps1      # 合并官方中文 + 填数值 -> docs\plans\cards-*.md
```

产出：

| 文件 | 内容 |
|---|---|
| `docs/plans/cards-ironclad.md` … `cards-regent.md` | 每个角色一份：按类型分组的卡牌表（**费用 / 稀有度 / 中文名 / 效果（已填真实数值）/ C# 类名**）+ 遗物表 + 起始卡组与起始生命 + 复制即用的 `DeckByAct` 骨架 |
| `docs/plans/cards-colorless.md` | 无色牌（5 个角色都能用，65 张） |
| `docs/plans/relics-shared.md` | 共享遗物池（118 个，任何角色都可能拿到） |

「类名」就是填进 `BossDefs.cs` 里 `typeof(类名)` 用的那个名字。效果列里的数值来自 `CanonicalVars`，
是**未升级**的基础值；描述里仍有 `[Xxx]` 形式残留的，说明那个数字是运行时算出来的（如「完美打击」按打击牌数量加伤）。

> 两个脚本共同的坑（都已在代码里修掉，改脚本时别踩回去）：
> ① PS 5.1 的 `-replace` 会把 `'$1_$2'` 这种替换串弄坏（`Assassinate` → `A_ss_as_si_na_te`），
>    生成 slug 必须用 .NET 的 `[regex]::Replace`；
> ② ILSpy 的输出把**构造函数排在属性之后**，而 `CanonicalVars` 是属性 —— 解析时若在 `CanonicalVars`
>    语句处 `break`，就永远读不到后面的 `: base(费用, 类型, 稀有度…)`（踩过：整列变空）。

### 9.6 角色专属机制能不能用在 Boss 身上（已核实，2026-10-15 R4）

Boss 的牌是用「影子 Player」打出的，所以凡是只有真人玩家才有的东西都可能出问题。逐个查了
4 个角色的专属机制（扫描脚本 `tools\scan-card-mechanics.ps1`，结果在 `.recon/card-mechanics.tsv`）：

| 机制 | 用到的 API（反编译证据） | 结论 |
|---|---|---|
| **充能球**（故障机器人，30 张牌；8 张直接用 `OrbQueue`） | `OrbCmd.Channel/EvokeNext/EvokeLast/AddSlots(..., Player player)` | ✅ **可用**。这些命令的参数就是 `Player`，影子 Player 满足；`PlayerCombatState.OrbQueue` 在构造时就建好了 |
| **星星**（储君，9 张牌） | `PlayerCombatState.Stars` / `PlayerCmd.GainStars(Player)` | ✅ **可用**（唯一差别：没有星星计数器 UI） |
| **随从 Osty**（亡灵契约师，22 张牌） | `OstyCmd.Summon(choiceContext, summoner, amount, source)` → 内部 `PlayerCmd.AddPet<Osty>(summoner)` → `CreateCreature(..., summoner.Creature.Side, null)` | ✅ **可用**：随从按 **`summoner.Creature.Side`** 创建，而 Boss 的 Creature 在敌方 ⇒ Osty 会站在 **Boss 一侧**、为 Boss 作战。两点偏差见下 |
| **直接摸 UI 的牌**（19 张，含 `Neutralize`/`Slice`/`DaggerSpray`/`Comet`/`Hyperbeam` 等） | `NCombatRoom`（多半是打点/动画定位） | ⚠️ **基本安全**：原版这些调用都带 null 检查；真出错也被「一张牌一个 try/catch」兜住，不会打断整回合 |
| **需要玩家选择**（弃牌 / 消耗 / 从抽牌堆挑牌…） | `CardSelectCmd.FromHand` / `FromHandForDiscard` / `FromCombatPile` / `FromDeckFor*` / `FromChooseACardScreen` | ✅ **R8 起可用**：这些入口都是「有推入的选择器就用它，没有才弹 UI」，Boss 出牌期间推入自动选择器即可自动结算（见 §11.8）。**不这样做会挂死回合**：`BlockingPlayerChoiceContext` 不等玩家输入，牌会卡在选择上 |
| **需要选目标（生物/非生物）** | `TargetType.TargetedNoCreature` / `Osty` | ❌ 仍由 AI 过滤：这走的是目标选择而不是卡牌选择，没有验证过，先不放进卡组 |

**Osty 的两点已知偏差**（不影响能否玩，影响手感）：

1. Osty 死亡后再召唤：原版是「在**己方盟友**里找到那只 Osty 复活」，而 Boss 的 Osty 不在 `Allies` 里
   → 会改成**新召一只**而不是复活原来那只。
2. `NCombatRoom.AddCreature` 把带 `PetOwner` 的单位放进**玩家侧容器**
   → Boss 的 Osty 会被**画在玩家那半边屏幕**（纯视觉：它的阵营仍是敌方，会打玩家、也会被玩家打）。

**没有任何卡牌直接召唤随从以外的越界行为**（`AddPet` 在 364 张非铁甲牌里零命中，随从统一走 `OstyCmd`）。

### 9.7 禁用某张牌：`SkipCards`

```csharp
public static readonly BossDefinition Necrobinder = new()
{
    ...
    // AI 永远不会打出这些牌（在排序前就被过滤，日志里会以 skip=[...] 显示）
    SkipCards = new List<Type> { typeof(Nightmare) },
};
```

用途：① 你试出某张牌对 Boss 手感很差或行为异常；② 需要玩家做选择的牌；③ 召唤/复活类你不想让它出现时。

## 10. rules.md 规范对照（自查，2026-10-15 R6）

| 规范条目 | 状态 | 证据 / 说明 |
|---|---|---|
| `ModEntry.cs` 的 `Initialize()` 与 `Logger` 逻辑不得修改 | ✅ | 只有 `ModId`/`ModName` 被 `rename-scaffold.ps1` 改成 `CharacterBosses`，方法体与日志实现原样 |
| 只修改 `src/Hooks/`（+计划文件） | ⚠️ **有 4 处例外** | 见下表，都是「不这样做就做不出功能」的必要改动 |
| Hook 文件按功能拆分、单文件 ≤200 行 | ✅ | 15 个文件，最大 167 行（`CharBossMonster.cs`）；UI 因超 200 行已从 `BossHandPanel` 拆出 `BossHandCard` |
| Hook 签名必须用反编译验证，禁止猜测 | ✅ | 3 个补丁目标全部来自 `ilspycli` 反编译原文；其中 2 个已在实机日志中确认生效（见下） |
| 数值修改类 Hook 用 `ref __result` | ✅ | `PullNextEncounterPatch.Postfix(..., ref EncounterModel __result)` |
| 每个 Hook 用 `Logger.Log()` 记录 | ✅ | 所有补丁与核心路径都有 `[Hook]`/`[CharBoss]` 前缀日志 |
| manifest `id` = 文件夹名 = DLL 名 = pck 名 | ✅ | 实测安装目录：`CharacterBosses\CharacterBosses.dll` / `.json` / `.pck`，manifest `id=CharacterBosses` |
| 不引入 NuGet 的 Harmony | ✅ | 走 `references\0Harmony.dll`（csproj 未改动） |
| 禁止恶意逻辑 | ✅ | 无网络、无文件写入（除日志），所有补丁失败都降级为非致命 |

### 10.1 4 处 `src/Hooks/` 之外的改动（必要性与代价）

| 文件 | 为什么必须改 |
|---|---|
| `tools/pck_builder/build_pck.gd` | 原脚手架只把 `mod_manifest.json` 打进 pck —— 而 mod 的本地化必须落在 `res://<modId>/localization/<lang>/<表名>.json` 才能被 `LocManager` 合并。改成支持「递归打包一个 content 目录」 |
| `install-mod.bat` | 配合上一条：把 `content\` 目录内容传进 pck 打包器（多了 1 个参数） |
| `content/localization/{eng,zhs}/{monsters,encounters}.json` | 5 个角色 Boss 的名字/招式名/败北文案（key 形如 `IRONCLAD_BOSS.name`）。**不放这里就没有中文名** |
| `tools/*.ps1`（5 个） | 开发/测试工具：mod 开关、一键冒烟测试、卡池元数据提取、卡池清单生成、机制扫描。不影响游戏运行 |

### 10.2 补丁与实机验证状态

| 补丁 | 目标签名（反编译原文） | 实机验证 |
|---|---|---|
| `PullNextEncounterPatch` | `ActModel.PullNextEncounter(RoomType roomType) → EncounterModel` | ✅ R1 日志：`[Hook] act 0 boss replaced: … -> ENCOUNTER.IRONCLAD_VULNERABLE_BOSS_ENCOUNTER`，战斗里刷出铁甲战士 |
| `ShadowPlayerIsolationPatch` | `Creature.get_IsPlayer`（属性 getter） | ✅ R1 日志：Boss 在敌方（`players=1 enemies=1`）、按敌人侧绘制、影子 Player 未混入玩家回合 |
| `BossSlotAssignmentPatch` | `ActModel.ApplyDiscoveryOrderModifications(UnlockState unlockState)` | ⏳ **R2 新增，待实机**（让地图显示的 Boss 与实际一致）。静态去风险已完成：`RunManager.GenerateRooms` 里 `act.GenerateRooms(...)` 与 `act.ApplyDiscoveryOrderModifications(...)` 是**紧挨着的两行**，中间没有 `Freeze`/转 canonical ⇒ Postfix 里调 `SetBossEncounter`（内部有 `AssertMutable`）不会抛；即便抛了也只打日志、不影响战斗（`PullNextEncounter` 那层仍会换） |

## 11. 当前状态（2026-10-15 晚）

- 框架：**5 角色 × 每幕强度档 全部就位**；铁甲战士三档已设计，其余 4 角色卡组待用户设计（现为起始卡组占位）。
- 本地化：5 个角色的中英文名与遭遇文案已配好（含怪物名 `IRONCLAD_BOSS.name` 等）。
- **mod 当前处于关闭状态**（用户要联机爬塔）：文件夹在 `mods_disabled\CharacterBosses`，用
  `tools\charbosses-mod.ps1 -Action enable` 打开并重启游戏。
- 尚未实机验证的新增内容：另外 4 个角色的立绘/卡组/遗物在游戏里的表现、`ActTable` 的实际命中、
  以及 R2 的 Boss 槽位替换（地图名同步）。下次实测看 `[CharBoss] shadow player ready: <KEY> act=N ...` 与
  `[Hook] act N boss slot fixed: ...` 两行日志即可确认。

### 11.1 R2 新增（2026-10-15 第二轮，联机期间离线完成）

| 改动 | 说明 |
|---|---|
| `Patches/BossSlotPatches.cs` | 在 `ActModel.ApplyDiscoveryOrderModifications` 就换 Boss → **地图上显示的名字/图标与真正打到的 Boss 一致**。选这个切点的原因：它是选 Boss 的最终收口，而且是基类上的普通方法（抽象的是它末尾调用的 `ApplyActDiscoveryOrderModifications`），一个 Postfix 覆盖全部幕 |
| `PullNextEncounter` 加 `IsOurs` 守卫 | 防止「已经是我们的 Boss」被二次映射成另一个角色 |
| `CharBossMonster.PlanReady` | 读档续战等没走过 `RollMove` 的情况会在出招时现算计划，不再白站一回合；用 `PlanReady` 区分「算过了但没牌可打」，避免每回合重复弃牌重抽 |
| `BossPlanner` 过滤 | 排除 X 费牌（`AutoPlay` 会把 X 按当前全部能量 capture，我们只按 `CostOf` 记账 → 会白嫖）、以及需要玩家选择目标的 `TargetedNoCreature`/`Osty` 牌 |
| `DebugMode` 支持内容 | `charbosses_debug.txt` 里写 `cycle` 会**每场普通战斗轮换一个角色**（一次会话看全 5 个）；也可以写 `silent`/`defect` 等指定。调试模式下第一幕的普通战斗**全部**替换（不再只换第一场） |
| 卡组日志 | 影子 Player 就绪时打印实际卡组：`deck(16): BASH,BASH,TREMBLE,...` —— 你设计完卡组看这一行就能确认生效 |

### 11.2 一键冒烟测试
```powershell
powershell -ExecutionPolicy Bypass -File tools\charbosses-smoke-test.ps1                 # 轮换 5 角色，自动打 4 回合
powershell -ExecutionPolicy Bypass -File tools\charbosses-smoke-test.ps1 -Boss silent -Turns 3
powershell -ExecutionPolicy Bypass -File tools\charbosses-smoke-test.ps1 -NoLaunch       # 游戏已开着，只做开局+驱动
```

它会：启用 mod → 构建安装 → 写调试开关 → 启动游戏 → 开新局 → 驱动到第一场战斗 →
自动出牌并结束回合若干次 → 最后把 `[CharBoss]`/`[Hook]`/`hand ui` 日志打出来，并列出检查要点。
（动作名自适应：STS2MCP 的 `combat_end_turn` 已失效，脚本会自己试到 `end_turn`。）

### 11.3 R3 新增（第三轮，联机期间离线完成）

给用户「挑牌」准备菜单：把 5 个角色 + 无色牌的全部卡牌整理成可读清单（见 §9.5）。
生成物共 7 份：`docs/plans/cards-{ironclad,silent,defect,necrobinder,regent,colorless}.md` 与 `relics-shared.md`，
含 **费用 / 稀有度 / 中文名 / 效果（占位符已填成真实数值）/ C# 类名**，另附各角色起始卡组、起始生命与遗物表。

交叉验证：清单里「闪电霹雳 = 1 费，对全体造成 4 点伤害 + 1 层易伤」「挑衅 = 1 费，6 格挡 + 1 易伤」
与 R1 实机日志里 Boss 头顶显示的意图完全一致（当时就是 Attack 4 / Defend 6），说明提取链路可信。

仍有 `[Xxx]` 残留的少数卡牌：数值是运行时算出来的（如「完美打击」按名字含"打击"的牌数加伤、
「欺凌」按易伤层数加伤），或文案用了通用占位名（「上勾拳」的 `{Power:diff()}` 对应两个不同的 PowerVar，无法按名匹配）。

### 11.4 R4 新增（第四轮，联机期间离线完成）

| 改动 | 说明 |
|---|---|
| 规范修正 | `UI/BossHandPanel.cs` 超了 rules.md 的「单文件 ≤200 行」（原 271 行）→ 拆出 `UI/BossHandCard.cs`；现在最大文件 165 行，全部达标 |
| **`SkipCards`** | `BossDefinition.SkipCards`：数据驱动地禁用某些牌，AI 排序前就过滤，日志里显示 `skip=[...]`。配合下面的机制核查使用 |
| **角色机制核查** | 写了 `tools\scan-card-mechanics.ps1`，扫 364 张（4 个新角色）卡牌对「影子 Player 专属系统」的依赖，结论见 §9.6：充能球/星星/随从 Osty **都可用**，只有 2 处 Osty 手感偏差 + 19 张摸 UI 的牌（有 null 检查兜底） |

核查的价值：**你设计静默猎手/故障机器人/亡灵契约师/储君的卡组时可以放心用充能球、星星、Osty 牌**，
不必绕着它们走；唯一要留意的是「需要玩家做选择」的牌（如 `Nightmare`）要丢进 `SkipCards`。

### 11.5 R5 新增（第五轮，联机期间离线完成）

| 改动 | 说明 |
|---|---|
| **多意图** | 原先一张牌只显示一个意图（痛击只显示攻击 8）。现在按 `DynamicVars` 逐个读出来，**攻击 / 格挡 / 易伤 / 虚弱 / 多段** 都显示：痛击 = 攻击 8 + 策略，闪电霹雳 = 攻击 4 + 策略，双重打击 = 攻击 5。UI 侧改成卡面下方一排图标（居中 `HBoxContainer`），新建 `Core/BossIntentBuilder.cs` |
| **修 bug：星星没扣** | `AutoPlay` 与能量一样**不扣星星**，而储君的牌是要花星星的 → 原实现等于白嫖。现在计划阶段按 `GetStarCostWithModifiers()` 预算、出牌后用 `pcs.LoseStars()` 扣账（能量也改成走 `LoseEnergy`，比直接赋值更接近原版，会广播事件） |
| **变量名日志** | 每回合的计划日志现在附带每张牌的动态变量：`vars=[BASH[Damage=8,VulnerablePower=2] …]` —— 设计卡组时对着看就知道这张牌到底给几层、打多少 |

读动态变量用的是 `DynamicVarSet.TryGetValue(key)`，并且每个概念都试多个可能的 key
（例如易伤试 `VulnerablePower`/`Vulnerable`，多段试 `Repeat`/`Repeats`/`HitCount`/`Hits`/`Times`），
读不到就退回「按卡牌类型给个兜底图标」，不会因为某个 key 改名而整条意图链断掉。

### 11.6 R6 新增（第六轮，联机期间离线完成）

| 改动 | 说明 |
|---|---|
| **定义自检** | 新增 `Core/BossDefValidator.cs`：第一次拉房间时（早于任何战斗）把「5 角色 × 3 幕」解析一遍写进日志 —— HP/能量/手牌数、每一幕实际拿到的牌名、遗物、以及**卡名写错会标 `(!)`**。你填完卡组启动游戏就能核对，不用进战斗才发现少几张牌 |
| 规范自查 | 补上 `rules.md` 逐条对照表（§10）：含「`src/Hooks/` 之外的 4 处必要改动」与「3 个补丁的签名来源 + 实机验证状态」 |
| 静态去风险 | `BossSlotAssignmentPatch` 唯一的不确定点（`SetBossEncounter` 会不会撞 `AssertMutable`）已排除：`RunManager.GenerateRooms` 里两个调用紧挨着，中间无冻结 |
| 文档 | 顶部加「§0 快速开始」+ 导航表；章节号整理连贯 |

自检日志长这样（每局开始一次）：

```
[CharBoss] ===== 定义自检开始 =====
[CharBoss] #0 IRONCLAD_BOSS: 角色=Ironclad 立绘=ironclad HP=140/200/260/300 能量=3/4/5/5 手牌=5/5/6/6
[CharBoss]   act1: 15 张牌组=[BASH,BASH,TREMBLE,THUNDERCLAP,TAUNT,UPPERCUT,BREAK,BULLY,CRUELTY,DOMINATE,COLOSSUS,STRIKE_IRONCLAD,STRIKE_IRONCLAD,DEFEND_IRONCLAD,DEFEND_IRONCLAD]
[CharBoss]   act1: 遗物 1 个 [PAPER_PHROG]
[CharBoss] #1 SILENT_BOSS: 角色=Silent 立绘=silent HP=130/185/240/280 能量=3/3/4/4 手牌=5/5/6/6
[CharBoss]   act1: 16 张（未定义 → 按卡池自动配的占位卡组） 牌组=[STRIKE_SILENT,...]
```

### 11.7 R7 新增（第七轮，联机期间离线完成）

| 改动 | 说明 |
|---|---|
| **占位卡组升级** | 之前未设计的角色用「起始卡组循环复制」＝ 5 打击 + 5 防御 ×1.6，Boss 会打得像个傻子。现在改成 `BossLoadout.AutoDeckFromPool`：从该角色**自己的卡池**按「类型 × 费用」分档挑 16 张（见 §9.2 的规则与示例），并滤掉 AI 用不了的牌 |
| 规则修正（踩过坑） | 第一版实现是「便宜优先」，**模拟发现每个卡池都能用 1 费牌填满 16 张** → 全是 1 费小牌、没有大伤害。改成按费用档位配额后才得到 1/2/3 费的曲线 |
| 离线验证方式 | 用 R3 的 `.recon/card-metadata.tsv` 把同一条挑选规则**离线重跑**了一遍，逐个角色核对结果（4 个角色都是 `0费×1 / 1费×7 / 2费×7 / 3费×1`，且自动落到了各角色主题牌上：故障机器人拿到球状闪电·寒流·冰川，亡灵契约师拿到护卫·来生） |

> 这条离线验证值得保留：**只要挑选规则是确定性的，就能在没开游戏的情况下用反编译数据核对结果**，
> 不必等实机才知道给的是什么卡组。

### 11.8 R8 新增（第八轮，联机期间离线完成）

**① 自动选择器 —— 修掉一个会挂死回合的隐患（本轮最重要的改动）**

R7 的自动占位卡组里带进了「杂技（抽 3 弃 1）」「投掷匕首（弃 1）」这类牌，而 Boss 出牌用的是
`BlockingPlayerChoiceContext` —— 它**不会等玩家输入**。原版的卡牌选择入口长这样：

```csharp
// CardSelectCmd 里每一条 From* 路径都是这个形状（FromHand / FromHandForDiscard /
// FromCombatPile / FromDeckForUpgrade / FromChooseACardScreen … 共 20+ 处）
if (Selector != null) { 用选择器 } else { 弹 UI 等玩家点 }
```

也就是说：没有选择器时会去弹「让玩家选牌」的界面，而我们的上下文又不等它 → **整个 Boss 回合会挂住**。
解决办法是用原版自带的测试钩子：`CardSelectCmd.PushSelector(ICardSelector)`（栈式，`IDisposable` 出栈；
栈空时 `Selector` 返回 null，所以只在 push 期间生效，不影响原版 UI）。

新增 `Core/BossCardSelector.cs` 并在 Boss 出牌全程用 `using` 推入：候选里**只选「必须选的最少张数」，
且优先挑最便宜的**（尽量把好牌留在手里），并把每次自动选择写进日志。收益：

- 「弃 1 / 消耗 1 / 抽牌堆挑 1」类牌**从「可能挂死」变成「正常结算」**；
- 这些牌不再需要进 `SkipCards`，可用牌池变大。

**② 行数审计纠错**

之前用 `Measure-Object -Line` 数行数 —— 它**跳过空行**，所以「全部 ≤200 行」是虚的：
`CharBossMonster.cs` 实际 205 行（超了）。改用 `(Get-Content).Count` 后把「抽牌 / 弃牌 / 洗回」三个
纯工具方法拆到新的 `Core/BossPiles.cs`，现在最大 167 行。顺带在 `BossPiles` 注释里说明了
为什么不用原版 `CardPileCmd.Draw/Shuffle`（那会走本地玩家动画与 UI 路径，而 Boss 的牌堆没有界面节点）。

**③ 仍待实机**：`BossCardSelector` 本身、以及 R2 起的全部改动。

### 11.9 R9 新增（第九轮，联机期间离线完成）

**① 把「会挂住/会误触发」这一类风险收口了（含反编译证据）**

| 检查项 | 结论 |
|---|---|
| `CombatManager.CheckForEmptyHand`（Boss 把手牌打空时必然走到） | ✅ **安全**。私有实现只做一件事：`if (turnState.IsInProgress && !IsExecutingCardOrPotionEffect(player) && 该玩家手牌为空) await Hook.AfterHandEmptied(...)` —— **不会结束回合、不碰 UI**，只是发个通知；而监听者是影子 Player 自己的遗物/牌 |
| 除卡牌选择外还有没有别的「等玩家输入」入口 | ✅ **没有了**。`CreatureCmd` 里**没有任何** Select/Choose 方法（只有 Damage/Block/LoseBlock…）；全程序集里的选择器机制只有 `ICardSelector`（TestSupport）与一个遗物内部的 `VakuuCardSelector`。所以 R8 的自动选择器把这一类风险**收口**了 |

**② 手牌 UI 改成增量同步**

早先每打出一张牌都 `Rebuild` 整排（重建 5 个 `NCard` 节点），视觉会闪、对象池被反复取用。
现在 `BossHandPanel.Rebuild` 先判断「目标列表是不是现有格子按同一顺序过滤掉打出去的牌」
（子序列判定），是就只摘掉那一格、其余节点原样保留；新回合/计划变了才整排重建。

> 踩过的坑：第一版写成了**前缀比对** —— 打掉第 1 张后现有格子是 5 个、目标是 4 个，
> `_cells.Count > wanted.Count` 直接判定不能同步，优化完全没生效。子序列判定才对。

**③ 新的已知限制（设计卡组时会碰到）**

Boss 的抽牌/洗回是**直接操作牌堆**（`BossPiles`，`silent: true`），没走原版 `CardPileCmd.Draw/Shuffle`
（那些会走本地玩家的动画与 UI 路径，而 Boss 的牌堆没有界面节点）。后果：
**Boss 自己的「抽到时/洗牌时」类触发不会响应**（例如「抽到这张牌时……」或依赖 `AfterCardDrawn` 的能力）。
给 Boss 配卡组时，尽量避免把收益完全建立在「抽牌触发」上的牌。

### 11.10 R10 新增（第十轮，联机期间离线完成）

**① 幕数档位统一到同一个来源（潜在不一致）**

之前两处「第几幕」取的来源不同，理论上可能不一致：

| 位置 | 原来源 | 现来源 |
|---|---|---|
| `CharBossEncounters.ActTable` 的行号（哪一幕换哪个角色） | `ActModel.Index` | 不变 |
| `CharBossMonster.ActIndex`（用哪一档 HP/能量/手牌数/卡组） | `RunState.CurrentActIndex`（幕数位置） | **改成 `RunState.Act.Index`**（当前这一幕），拿不到才退回原值 |

`ActModel.Index` 才是「幕的档位」（密林与暗港都是第一幕、Index 都是 0）。两处统一后，
**「选中的角色」与「生效的强度档」必然来自同一个数字**，不会出现「按第一幕选的 Boss、却用了第二幕的卡组」。

**② 本地化 key 一致性检查（新工具 `tools\check-loc-keys.ps1`）**

本地化 key 是由**类名**推出来的（`IroncladBoss` → `IRONCLAD_BOSS.name`）。一旦重命名类名、或加第 6 个 Boss
而忘了补 key，编译不会报错、游戏里只会**静默显示成生 key**（`monsters.IRONCLAD_BOSS.name` 这种）。
这个脚本扫代码里的类名 → 算 slug → 检查 `content/localization/{eng,zhs}` 里的 `monsters.json` /
`encounters.json` 是否有对应条目，并反向提示「content 里有、代码里没人用」的残留 key。

当前结果：**5 个 Boss × 2 个字段 × 2 种语言 + 5 个遭遇 × 2 个字段 × 2 种语言 = 40 个 key 全部齐全，无残留。**

### 11.11 R11 新增（第十一轮：状态条与回合开始钩子）

**① 手牌栏挂到战斗房 UI 层**（`BossHandUi.EnsurePanel`）

以前挂在自己的 `CanvasLayer`（Layer 80）上，层级比游戏 UI 高 → 地图界面还挂着牌、遗物悬浮说明被卡牌盖住。
现在优先挂到 `NCombatRoom.Ui` 的 `%PlayContainer`：坐标系与屏幕一致、和游戏 UI 同树（弹窗/说明画在我们上面）、
战斗房销毁时一起消失；只在拿不到战斗房时才退回 `CanvasLayer`。面板还加了一条 `Visible = CombatState.IsLiveCombat()`。

**② Boss 状态条**（`BossStatusRow`）：全部用原版节点 —— 遗物 `NRelicBasicHolder.Create`（带悬浮说明）、
能量 `NEnergyCounter.Create`（数字在球内、字体一致，需要 `AnimIn()` 才显示球体）；
遗物**从右往左**摆（列表首位的初始遗物落在最右），辉星 > 0 才显示。状态条基线跟着卡面下移量 `cardDrop` 一起让。

**③ 影子 Player 的「玩家回合开始」钩子**（`BossHookBridge`）

影子 Player 不在 `CombatState.Players` 里，原版永远不会替它跑 `Hook.AfterPlayerTurnStart` ——
所以召 Osty、给辉星、「下回合加抽/加能量」这类挂在遗物/能力上的效果全都不生效。
现在 Boss 出牌前手动 `Hook.AfterPlayerTurnStart(state, ctx, shadow)` 补一次。

### 11.12 R12 新增（第十二轮：充能球）

目标：故障机器人的 Boss 要**真的用球**，而且显示、结算时机都对。**所有球位都用原版节点**，不自己画。

**① 显示：复用 `NOrbManager` + `NOrb`**（`UI/BossOrbDisplay.cs`）

原版只在 `NCreature` 认为 `Entity.IsPlayer` 时才 `NOrbManager.Create` ——
我们的 Boss 是敌方怪物（`IsPlayer` 被强制 false），所以攻击力/球位都得自己挂：

| 需求 | 做法 |
|---|---|
| 球位外观/数值/描边 | `NOrbManager.Create(creature, isLocal:false)` + `NCreature.SetOrbManagerPosition`（原版同样的弧线半径 0.75 折、上抬 50px） |
| 球的数字要可见 | `NOrb` 用 `isLocal:true` 建（原版对「别人的球」是压暗且不显示数字） |
| 槽位排布 | 直接调私有 `NOrbManager.TweenLayout()`，不重写那套三角函数 |
| 增删球 | 改私有 `_orbs` 列表 + `%Orbs` 容器（直接改子节点会让 `TweenLayout` 按下标越界） |
| 高亮即将被激发的球 | `NOrbManager.UpdateVisuals(OrbEvokeType.Front/All)`，类型取自本回合计划里 `CardModel.OrbEvokeType` |

**② 结算时机：Boss 自己回合结束时**（`BossOrbDisplay.ResolveTurnEnd`）

玩家侧的球被动由 `CombatManager` 的回合结束流程调 `OrbQueue.BeforeTurnEnd` 触发；Boss 不在那条流程里，
所以出完牌、弃手牌之前手动 `await pcs.OrbQueue.BeforeTurnEnd(ctx)` —— 闪电打人、冰霜给格挡、黑暗攒伤害都在这一刻。
球的伤害目标走 `CombatState.GetOpponentsOf(owner)`，天然是玩家侧，不需要侧向补丁。

**③ 等离子球的能量**（`Core/BossOrbMath.PlasmaEnergyFor`）

等离子球的被动是「回合开始 +1 能量」（原版 `AfterTurnStartOrbTrigger`）。Boss 的计划在玩家回合就得定死
（意图必须精确），所以这笔能量在 `PrepareTurn` 里**提前算进本回合能量预算**，而不是等出牌时才加。

**④ 手牌栏里的「(激发 N)」**（`Core/BossOrbMath.EvokeDamageFor` + `BossHandCard.AddEvokeLabel`）

激发球的牌（双重释放/四重释放/碎片）本身往往没有伤害意图，所以：
按 `CardModel.OrbEvokeType` 取「队首」或「全部」球，伤害只算真正造成伤害的球
（**闪电/黑暗/玻璃**；冰霜激发是格挡、等离子是能量，不算伤害），乘以每颗球的激发次数
（双重释放 2 次、碎片每颗 2 次、四重释放吃 `Repeat` 变量，其余 1 次），
在意图行末尾用蓝色括号显示，例：`(激发 16)`。`PlannedPlay.EvokeDamage` 只用于显示，不进意图。

**还没做的**：冰霜球的激发格挡没在意图里标数字；`Cracked Core` 这类「战斗开始引导球」的遗物钩子
（`BeforeSideTurnStart`）仍未桥接，所以 Boss 开局是空球位。

### 11.13 R13（2026-10-17：进战斗黑屏事故）

**现象**：装完 R12 后一进战斗就黑屏没反应。

**根因**（不是充能球的问题，是 R12 顺手把 `ActIndex` 抽成 `BossActIndex` 时把 try/catch 挪错了层级）：

```
Creature..ctor → MonsterModel.MinInitialHp → CharBossMonster.ActIndex
   → BossActIndex.Resolve(CombatState)   ← 参数在“进方法之前”求值
   → MonsterModel.CombatState → InvalidOperationException("Creature was accessed before it was set.")
```

`MonsterModel.CombatState` 依赖 `Creature`，而 `Creature` 的构造函数恰好在这个阶段问 HP —— 异常在
实参求值时抛出，落在 `Resolve` 的 try 之外，于是 `CombatRoom.StartCombat` 整体失败 → 黑屏。

**修法**（`Core/BossActIndex.cs`）：

1. `Resolve` 改成接收**委托** `Func<ICombatState?>`，把取值动作塞进 try 里；
2. 取不到时退回全局 `RunManager.Instance.DebugOnlyGetState()`（`RunManager.State` 在引用程序集里是
   internal，对外只有这个取值方法；两者返回同一个 `RunState`），再取 `Act.Index` / `CurrentActIndex`。

**顺带的收获**：以前这个异常被吞掉、`ActIndex` 恒为 0，所以**第二/三幕的 Boss 一直在用第一幕的 HP**；
现在创建动物时也能拿到真实幕数，HP 档位才对上（能量/手牌数/卡组本来就是出牌时才取，一直是对的）。

### 11.14 R14（2026-10-17：战场拉远 + 版式重排）

用户实机反馈：Boss 头顶太挤 —— 中间那颗球被卡面挡住、遗物压到卡面上、能量球比遗物大一圈，
而且「塔1 里 Boss 就站在意图正下方，是因为角色尺寸和卡牌尺寸都不一样」。

**① 战场拉远（原版就有的开关）**

`EncounterModel.GetCameraScaling()` / `GetCameraOffset()` 是原版给大怪用的：战斗房会拿它缩放
整个 `SceneContainer`（双方角色 + 地面 + 特效）。现在 5 个角色 Boss 遭遇统一：

| 值 | 含义 |
|---|---|
| `ArenaScale = 0.80` | 双方一起缩小到 80%，战场看起来更大；原版还会把「可用宽度」按 `960 / scaling` 放宽 |
| `ArenaOffsetY = -20` | 场景整体上移 20px，给下方玩家手牌让位 |
| `CardScale = 0.40 → 0.34` | 展示的卡面 ≈ 95×133 |

角色立绘本来就是按「玩家侧尺寸」画的，直接当敌人会占满整个竖屏 —— 这是拉远的根本原因。

**② 手牌栏改成显式竖排**（`BossHandPanel.Layout.cs`）

不再用「卡面整体下移 0.5 卡宽」这种相对量（那是前几轮为躲意图叠压临时加的），改成自顶向下逐行算：

| 行 | 位置 | 说明 |
|---|---|---|
| 状态条（遗物 + 能量 + 辉星） | `y = 0`，右对齐 | 贴着手牌栏最上沿 |
| 卡面 | `y = statusH + 8` | 状态条实测高度 + 8 |
| 意图行 | `y = cardTop + cardH + 2` | 顶端贴住卡面底端，左侧与卡牌对齐 |
| 整块 | `anchorTop - panelH - 28` | 意图行底边离立绘顶部 28px |

好处：改卡面缩放或任一行高度都不会再互相压住（之前 `cardDrop` 与意图行是两套独立坐标，容易叠）。

**③ 状态条**：能量球是原版 `NEnergyCounter`，但它是按 HUD 角标尺寸做的，比遗物图标大一圈 → 缩放 `0.75`。
遗物仍然是「列表首位（角色初始遗物）在最右」，能量球在最右端之后。

**④ 球位摆位**（`BossOrbDisplay.Place`）：原版球位正对角色**头顶正中**，而 Boss 头正上方是意图行 + 卡面，
弧线中间那颗球必然被挡。现在复刻原版的 `SetOrbManagerPosition` 算法后，再叠加偏移：

- 右移 `0.34 × 立绘包围盒宽`、下移 `0.06 × 高`（整圈球挪到立绘右上侧，像多人模式看别人）
- 球位缩放 `0.85`

`Place` 每次 `Sync` 都按绝对值重算（不是累加），所以被原版的缩放动画改掉后会自动纠正。

**⑤ 顺手记录的失误**：拆分文件时用 `Select-String` 定位失败，脚本把 `BossHandPanel.cs` 截断成 5 行；
仓库没有 git，只能按之前读到的内容逐段重建（逻辑未改）。教训：改写文件优先用编辑工具，别用脚本做结构化裁剪。

### 11.15 R15（2026-10-17：状态条重做 + 悬停放大 + 点击遗物 + 锚点锁定）

用户第二次实机反馈的四个问题 + 两个新需求，全部在这轮处理。

**① 能量球「只有 2/2、没有球体」的真凶**

`NEnergyCounter.AnimIn()` 的实现是：

```csharp
_animInTween = CreateTween();
base.Position = new Vector2(-480f, 128f);      // 场景里的隐藏位
_animInTween.TweenProperty(this, "position", Vector2.Zero, 0.6f);
```

也就是说它**靠补间自己的 Position 入场**。放进 `HBoxContainer` 后：
容器每帧把子节点 Position 摆回槽位 → 和补间互相打架 → 数字留在容器槽位、球体被拖到别处；
另外场景根的球体图层是按锚点撑满父级的，`HBox` 给它的尺寸又是 0 → 图层整片塌掉，只剩数字。

修法：`BossStatusRow` 从 `HBoxContainer` 改成**普通 Control + 手动从右往左摆**：
不调 `AnimIn`，直接给 `Position = Zero`；并显式 `Size = 量到的固有尺寸`（量不到就给 112×112）再缩放到最长边 ≤ 56。
这样球体图层有尺寸可依、位置也不再被容器抢。

**② 遗物遮住卡牌**：`statusH` 原来用 `_statusRow.GetCombinedMinimumSize().Y`，遗物节点是锚点撑的、
固有高度可能算小了 → 卡面排上去压在遗物上。现在改成 `BossStatusRow.Height(row)`：
逐个量子节点（控件自己的 CustomMinimumSize / 组合最小尺寸取大者），最小 36。

**③ 遗物改成可点击 + 原版详情页**

节点从 `NRelicBasicHolder` 换成 `NRelicInventoryHolder`（就是玩家自己遗物栏用的那个，尺寸/悬浮说明一致），
并照抄原版 `NRelicInventory` 的接法：

```csharp
holder.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>(_ => ...));
NGame.Instance.GetInspectRelicScreen().Open(全部遗物, 被点的那件);
```

所以点 Boss 的遗物 = 点自己遗物，同一个界面，左右还能翻。

**④ 球位不再偏右**：`OrbOffsetX = 0`（水平与 Boss 正中对齐）、`OrbOffsetY = 0.40`（整体往下压）、
`OrbScale = 0.80` —— 让球弧落在头顶偏下，既不被卡面挡、也不会浮在立绘外侧。

**⑤ 意图行**：按用户指定，额外「上移 0.4 卡宽 + 左移 0.4 卡宽」（卡面不动）：
`IntentShiftX/Y = -0.40`（× 卡宽）。

**⑥ 新增：卡牌悬停放大 2×**（`BossHandCard.WireHover`）

`NCard.MouseFilter` 从 `Ignore` 改成 `Stop`（否则收不到鼠标），`MouseEntered/Exited` 里把**这张卡的 Scale**
改成 `CardScale × 2`（或改回），同时把所在格子的 `ZIndex` 抬到 20 以免被右边的卡盖住。
只动卡面节点自己的缩放，所以意图行与其他卡的位置完全不变。

**⑦ 新增：锁定相对位置**（`BossHandAnchor.ResetLock` / 锁定逻辑）

第一次量到真实立绘时，把「立绘左上角 → 锚点」的偏移记下来，之后每帧只用
「立绘当前位置 + 这个固定偏移」：

- 受击抖动、立绘缩放、包围盒重算都不会再让手牌栏漂移（以前每次都重新量，会跟着抖）；
- Boss 自己移动时手牌栏仍然贴着走；
- `BossHandUi.Clear()`（战斗结束/离开房间）时清掉锁，下一场重新量。

### 11.16 R16（2026-10-17：状态条对齐 + 球位高度 + 每帧跟随 Boss）

用户第三次实机反馈，四个点：

**① 遗物仍然压卡面** → 两处叠加的留白：`StatusGap 8 → 14`，并且 `BossStatusRow.Height()` 末尾再加
`RowBottomPad = 10`（遗物图标若比排版盒高，靠这块留白吸收）。

**② 能量球要和遗物同一行、且在其正右方** → 两个原因一起处理：

- 遗物/能量都走 `Fit()` 统一塞进 `IconSide = 56` 见方的视觉盒（超过就按比例缩放并写回
  `CustomMinimumSize`，排版按缩放后的尺寸算）；
- 排版从「垂直居中」改成**顶部对齐** —— 两个盒子高度不同时，居中会让能量球看起来比遗物低一截。

另外 `NEnergyCounter` 建好后显式 `Size = 固有尺寸`（量不到就给 112×112），
否则内部按锚点撑的球体图层会塌掉、只剩数字。

**③ 中间那颗球压在脑袋上** → `OrbOffsetY 0.40 → 0.22`：上一轮为了躲卡面压得太狠，
现在落在「卡面底边」和「头顶」之间；`OrbScale` 保持 0.80。

**④ 用模组挪动 Boss，只有球位跟着动** → 手牌栏以前只在**刷新时**（换手牌/出牌）算一次位置，
所以 Boss 被位移后不会跟。现在把排版拆成两档：

| 方法 | 时机 | 干什么 |
|---|---|---|
| `LayoutCells` | 内容变化时（`_layoutDirty`） | 重算所有行高、卡宽、状态条宽度 |
| `FollowBoss` | **每帧** | 只按锚点重算整块 `Position`（卡宽用上次缓存的值） |

球位是 `NCreature` 的子节点，本来就跟着生物走；现在卡牌/意图/遗物/能量也一起跟，
和普通怪物的意图行为一致。

### 11.17 R17（2026-10-17：卡牌卡在半空的真凶 + 跟随时机 + 状态条再上移）

**① 「打出一张打击，牌卡在半空不进弃牌堆」—— 不是动画问题，是异常打断**

`godot.log` 里抓到了：

```
[ERROR] GameAction PlayCardAction card: CARD.STRIKE_IRONCLAD completed with exception:
System.AggregateException: (Player with ID 18446744069414607733 not found in player stats for this run history! We have 1)
   at CreatureCmd.Damage(...) at StrikeIronclad.OnPlay(...)
```

那个 ID 就是影子 Player 的虚构 NetId（`0xFFFFFFFF_0000_xxxx`）。伤害结算会按**受击方所属玩家**去
当前地图点的 `PlayerStats` 里查记录，而影子 Player 不在 `RunState.Players` 里 → 抛异常 →
整次 `PlayCardAction` 中断 → 卡牌的「飞向弃牌堆」那一步没执行（但伤害已经结算了，所以 Boss 掉血正常）。

修法：`Core/BossRunStats.Ensure` —— 建影子 Player 时（`AfterAddedToRoom`）和每回合（`PrepareTurn`）
都确认一条 `PlayerMapPointHistoryEntry { PlayerId = shadow.NetId }` 存在于
`RunState.CurrentMapPointHistoryEntry.PlayerStats`。最小侵入，不污染真实玩家统计。

**② 「拖动 Boss 时卡牌/遗物不动」—— `_Process` 没被调用**

上一轮加的是 `_Process` 里的每帧跟随，但本项目早先就踩过「mod 程序集里的 Godot 节点收不到 `_Process`」，
所以那段代码从来没跑过。改成在 `_Ready` 里挂场景树帧信号：

```csharp
GetTree().ProcessFrame += OnFrame;   // _ExitTree 里解绑
```

`OnFrame` 里：`_layoutDirty` 时整排重排，否则只重算整块 Position。

**③ 状态条再往上抬**：`StatusGap 14 → 22`（状态条与卡面之间的空隙），
并在排版日志里直接打出「状态条 y=0 h=… / 卡面顶 y=… / 意图顶 y=… / 面板高 …」，
下轮出问题可以只看日志定位，不必靠截图猜。

### 11.18 R18（2026-10-17：战斗开始类遗物 + 跟随/遮挡的诊断）

**① 「战斗开始」类遗物在 Boss 第一个回合才生效（裂核引导球最明显）**

根因：这类遗物全都挂在 `BeforeSideTurnStart` 上（反编译确认）：

```csharp
public override async Task BeforeSideTurnStart(PlayerChoiceContext ctx, CombatSide side,
                                               IReadOnlyList<Creature> participants, ICombatState combatState)
{
    if (participants.Contains(base.Owner.Creature) && base.Owner.PlayerCombatState.TurnNumber <= 1) { … }
}
```

裂核 = 引导闪电球、锚 = 给格挡、弹珠袋 = 上易伤、红面具 = 上虚弱、扭曲漏斗 = 上中毒，全是这个形状。
它们靠 `Hook.BeforeSideTurnStart` 的广播跑到玩家头上，而**影子 Player 的遗物不是监听者**
（它不在 `CombatState.Players` 里），所以一个都不触发 —— 直到我们出牌前手动补了
`AfterPlayerTurnStart`，裂核才在 Boss 回合顺手把球引导出来（于是看起来「晚了一个回合」）。

修法（`BossHookBridge.FireCombatStart`）：在**玩家第 1 回合开始**（= 玩家眼里的战斗开始，
也是 Boss 算计划的时刻）逐个遗物直接调：

```csharp
relic.BeforeSideTurnStart(ctx, CombatSide.Player, participants: [Boss 本体], combat);
relic.AfterSideTurnStart(CombatSide.Player, participants: [Boss 本体], combat);   // 注意这个没有 ctx
```

要点：

| 点 | 为什么 |
|---|---|
| `participants` 里必须放 **Boss 本体** | 遗物的条件是 `participants.Contains(owner.Creature)`，而影子 Player 的 Creature 就是 Boss |
| `side` 传 `Player` | 这批遗物不看 side，但语义上这是「影子玩家的回合开始」 |
| 只触发一次（`CombatStartFired`） | 遗物里还带 `TurnNumber <= 1` 的条件，而影子 Player 的回合数不会自己递增 |
| 期间打开 `HittableEnemiesSidePatch.ForceRelativeSide` | 此刻当前阵营还是 Player，不强制侧向的话弹珠袋会给 **Boss 自己**上易伤 |
| 结束后 `BossHandUi.Refresh` | 可能在战斗开始就引导出球，球位要跟着刷 |

`AfterSideTurnStart` 的签名与 `BeforeSideTurnStart` 不同（少一个 `PlayerChoiceContext`），
这是编译器抓出来的（`CS1501`），反编译确认后按 3 参数版本调用。

**② 「遗物压卡面」「拖动不跟随」—— 布局数字与画面不一致，先加诊断**

日志显示的状态条高度 66、卡面顶 88（也就是遗物在 0–56、卡面从 88 开始，两者本该差 32px），
但截图里遗物仍压在卡面容上、且拖动 Boss 时整块不动 —— 说明**实际绘制位置和排版数字不一致**，
靠猜改数值没意义。这轮先做三件事：

1. 状态条整体再上抬 `RowLift = 12`（`row.Position.Y = -12`，画在手牌栏上边界之外），并把 `StatusGap` 加到 26 —— 症状先缓解；
2. `LayoutCells` 里打印状态条与**每个子节点**的真实屏幕坐标/尺寸/缩放（`LogStatusRow`）；
3. 给帧回调加心跳日志（每秒 1 条、共 3 条），并给「手牌栏跟随」加位移日志 —— 用来确认
   `ProcessFrame` 到底有没有跑、跟随算出来的目标位置是多少。

另外 `CharBossMonster` 拆成两个 partial 文件（`CharBossMonster.cs` 行为 +
`CharBossMonster.Descriptor.cs` 幕数/血量/名字/立绘/动画器），守住 200 行规则。

### 11.19 R19（2026-10-17：按日志实测定案 —— 能量球、跟随、mod 遗物池）

**① 诊断日志给出的硬数据**

```
手牌栏跟随: (0, 0) → (1347.5199, 188.72) (anchor=1465,472)
状态条屏幕=(1347.5199, 176.72) 尺寸=(301.6, 66)
  子节点 NRelicContainerHolder-RELIC_NEOW_BLESSING 屏幕=(1469, 176.72) 尺寸=(68,68) 缩放=0.82
  子节点 NRelicContainerHolder-RELIC_CRACKED_CORE  屏幕=(1531, 176.72) 尺寸=(68,68) 缩放=0.82
  子节点 DefectEnergyCounter                       屏幕=(1593, 176.72) 尺寸=(112,112) 缩放=0.5
hand ui: 状态条 y=0 h=66 / 卡面顶 y=88
```

三件事一次说清：

1. **布局数字是对的**：遗物占屏幕 y 176.7–232.7，卡面从 276.7 开始（相差 44px）。
   也就是说「遗物压卡面」不是排版错位；
2. **帧回调是跑着的**（`手牌栏跟随` 那条就是它打的），所以「拖动不跟随」不是没 tick；
3. 真正的元凶是 **`NEnergyCounter` 画出来的球比排版盒大**：它的场景把球体图层**绝对定位**在固定偏置上
   （不是按父级撑满），我们按 56px 的盒子去摆，它却画成一个更大的球并向下溢出到卡面上。
   之前几轮「遗物挡卡」「能量位置不对」其实是同一件事。

**② 能量显示改成自己摆（原版图标 + 数字）**

不再用 `NEnergyCounter`，改成：

```csharp
EnergyIconHelper.GetPath(shadow.Character.CardPool)   // 游戏自己的能量图标（.tres）
→ TextureRect(44×44, KeepAspectCentered) + Label("2/3")
```

尺寸完全由我们决定（`EnergyIconSide = 44`），数字在 0 能量时变红，和原版提示一致。
遗物仍是原版 `NRelicInventoryHolder`（56 见方、可点击看详情）。

**③ 跟随：删掉「锁定偏移」**

上一轮为防受击抖动，把「立绘左上角 → 锚点」的偏移锁死了 —— 日志证明帧回调在跑，
那么不动的原因只能是**锁定把位移吃掉了**。现在 `BossHandAnchor.Resolve` 每帧实时按
`Visuals.Bounds` 量取（包围盒跟着立绘/生物节点走，所以模组怎么拖都会跟）。

**④ 顺带修掉一个崩溃：mod 遗物没有遗物池**

```
InvalidOperationException: Sequence contains no matching element
  at RelicModel.get_Pool()   ← EnergyIconHelper.GetPool ← RelicModel.get_HoverTip_Patch1
```

原版是 `ModelDb.AllRelicPools.First(p => p.AllRelicIds.Contains(Id))`，而「涅奥的祝福」不在任何池子里
→ 生成遗物说明/悬浮提示时抛异常（说明文本里的能量图标要问 `Pool`）。
新增 `ModRelicPoolPatch`：只在「确实找不到池子」时兜底返回第一个池，原版遗物完全走原逻辑。

**⑤ 战斗开始钩子已确认触发**：日志里有 `战斗开始：替 2 件遗物触发 BeforeSideTurnStart`，
并新增一条 `战斗开始钩子结束: 球位 capacity=3 orbs=N`，用来确认裂核是否当场引导出球。

### 11.20 R20（2026-10-17：第一个真实设计 —— 三层故障机器人「能力机」）

用户给出了完整设计：525 血、7 件遗物、21 张卡组、**按回合写死的 12 回合剧本**（9～12 循环）。
这一轮先把「剧本」这套东西做出来并落地。

**① 新增剧本引擎**（`Core/BossScript.cs` + `Core/BossScriptPlanner.cs`）

贪心 AI 打不出设计好的节奏（先堆集中、再激发球、某些回合故意没费打不出牌），所以加了一层剧本：

```csharp
ScriptByAct = new BossScript?[] { null, null, DefectScript.Build(), null }
```

| 结构 | 作用 |
|---|---|
| `ScriptCard` | 一张牌：类型 + 是否升级 + 来源标记（deck / ai / hologram / status，只用于日志） |
| `BossTurn` | 一回合：**按顺序**尝试打出的牌列表 + 设计备注 |
| `BossScript` | 整场：回合列表 + 循环规则（`LoopFrom = 9`、`LoopLength = 4` → 9～12 循环） |

出牌流程变成：`BossScriptRunner.BuildHand` 用 `CombatState.CreateCard` **按剧本造出这一回合的手牌**
（不抽牌堆、不随机）→ `BossScriptPlanner.Plan` 按剧本顺序逐个尝试，**能量不够就跳过**
（这正是设计里的「这张牌没费打不了」）→ 意图因此完全固定可复现。

「卡组里的消耗牌/能力牌打出后不再出现」「第 4 回合之后的能力牌是创造性AI印的」
「第 5 回合的第二张冰川是全息影像捞的」这些规则，全都直接写进剧本本身，不需要模拟牌堆流转。

**② 血量口径修正**：涅奥的祝福按幕累积

玩家侧是「拾取 +150，之后每进入新的一幕再 +150」，所以三层时已经是 +150×3。
`BlessingHpBonus` 从固定 150 改成 `150 × (幕序号 + 1)` —— 于是用户设计的
**三层故障机器人 = 75 + 150×3 = 525** 正好对上（定义里写 `Hp[2] = 75`）。

> 副作用：所有 Boss 的二/三层血量也会跟着变成 +300 / +450。若不想这样，把 `BlessingHpBonus`
> 改回固定 150、把三层故障机器人的定义值写成 375 即可。

**③ 中文名 → 类名对照（已全部核对存在）**

| 卡 | 类名 | 卡 | 类名 |
|---|---|---|---|
| 双重释放 | `Dualcast` | 电击 | `Zap` |
| 打击 | `StrikeDefect` | 防御 | `DefendDefect` |
| 究极防御 | `UltimateDefend`（**无色牌**） | 充电 | `ChargeBattery` |
| 高速脱离 | `BoostAway` | 飞跃 | `Leap` |
| 偏差认知+ | `BiasedCognition`（`Upgraded = true`） | 冰川 | `Glacier` |
| 弹幕齐射 | `Barrage` | 子程序 | `Subroutine` |
| 冰雹风暴 | `Hailstorm` | 雷暴 | `Storm` |
| 扩容 | `Capacitor` | 暴涨 | `BulkUp` |
| 全息影像 | `Hologram` | 创造性AI | `CreativeAi` |
| 寒流 | `ColdSnap` | 缓冲 | `Buffer` |
| 眩晕 | `Dazed`（状态牌） | | |

遗物：破损核心 `CrackedCore`、红面具 `RedMask`、永冻冰晶 `Permafrost`、尘封魔典 `DustyTome`、
坚固钳子 `SturdyClamp`、皮草大衣 `FurCoat`、钨合金棍 `TungstenRod`。

**④ 从用户给的伤害/格挡数字反推出的能量模型（已在 12 回合上逐条验算一致）**

- 基础能量 3（定义 4 − 涅奥祝福 1）；
- **子程序** = 每打出一张能力牌 **+1 能量**（卡面文档里这个数值是空的，靠剧本反推出来）；
- **充电** = 下回合 +1 能量。

验算样例：第 3 回合 雷暴(1)+扩容(1)+暴涨(2) 需要 4 费 —— 只有「子程序每次 +1」才打得出来；
第 5 回合 冰雹风暴(1,能力)→+1，全息影像(1)、冰川(2) 之后打击正好没费；其余回合同理。

### 11.21 R21（2026-10-17：能量口径 + 特殊效果「偏差认知」）

**① 能量改成「定义值 + 遗物增减」**

用户补充：涅奥祝福是 −1 能量，设计时却按 3 费算，所以把「皮草大衣」换成**低语耳环**
（`WhisperingEarring`，每回合 +1 能量，对意图固定的 Boss 是无损加费）。于是每回合 3 − 1 + 1 = 3。

实现上不再手动减祝福那 1 点，改为直接问影子 Player：

```csharp
int relicEnergy = pcs.MaxEnergy - Shadow.Character.MaxEnergy;   // 遗物带来的净增减
int energy = Def.EnergyForAct(act) + relicEnergy + plasma;
```

`PlayerCombatState.MaxEnergy` 是 `Hook.ModifyMaxEnergy(...)` 的结果，所以**任何加/减费遗物都自动生效**；
三层故障机器人的定义能量因此写成 3（3 + 0 = 3）。少抽 2 张牌仍然是手动记账（Boss 不走原版抽牌管线）。

**② 特殊效果「偏差认知」**（`Powers/DeviationPower.cs`，与同名卡牌无关）

挂在 Boss 身上的一层 Power，战斗开始时由 `BossHookBridge.FireCombatStart` 施加
（定义里 `SpecialPowerType = typeof(DeviationPower)`）：

| 效果 | 实现 |
|---|---|
| 玩家每打出 1 张能力牌 → Boss +1 集中 | `AfterCardPlayed`：`cardPlay.Card.Type == Power` 且**不是** Boss 自己出的牌 → `PowerCmd.Apply<FocusPower>(+1)` |
| 力量与集中数值始终相同 | `ReconcileAsync`：记录上次的力量/集中，判断**哪一边**变了，把差值补给另一边。调用点：每次出牌后（`CharBossMonster.PlayPlannedCards`）、回合开始（`AfterSideTurnStart`）、回合结束（`BeforeSideTurnStart`/`BeforeSideTurnEnd`） |

所以「暴涨」的 +2 力量会同步 +2 集中；而「偏差认知」卡牌每回合掉 1 点集中时，力量也掉 1 点。
同步时用 `silent: true` 施加，避免动画/日志抖动，并用 `_syncing` 防止重入。

**③ 状态栏上的特殊效果条目**（`UI/BossSpecialChip.cs`）

定义里的 `SpecialEffectLocKey` 非空时，状态条最左侧加一个「图标 + 说明文本框」：

- 图标：原版「集中」(`FocusPower`) 的贴图，不额外造美术；
- 文字：`monsters.json` 里的 `DEFECT_BOSS.special`（中英各一份，3 行，自动换行）；
- 整个条目 44 高、图标 44 + 文本 250，尺寸由我们给死，不会溢出。

新增本地化文件 `content/localization/{eng,zhs}/powers.json`（`DEVIATION_POWER.title/description`）。

### 11.22 R22（2026-10-17：特殊效果按幕区分 + 说明框改成贴 Boss）

用户实测提出的三个问题，逐条修：

**① 特殊效果不能所有幕都一样** —— 同一角色每幕的特殊效果不同。
`BossDefinition` 里把它改成按幕数组：

```csharp
SpecialLocByAct   = new string?[] { null, null, "DEFECT_BOSS.special", null };
SpecialPowerByAct = new Type?[]   { null, null, typeof(Powers.DeviationPower), null };
```

于是只有第三幕的故障机器人才有「偏差认知」；一/二幕既不施加 Power 也不显示说明。

**② 施加 Power 时 NRE**

日志里：`特殊效果施加失败: NullReferenceException at PowerModel.SetAmount ← ToMutable`。
自己 `ModelDb.GetById<PowerModel>(...).ToMutable(1)` 会在未初始化的规范模型上炸，
改成**反射调用泛型** `PowerCmd.Apply<TPower>(ctx, creature, 1, creature, null, true)`，让游戏自己建模型。

**③ 说明框改成贴在 Boss 旁边**（`UI/BossSpecialUi.cs`，替换掉原来的状态栏小条）

参考塔1 Downfall 的做法：说明框挂在 Boss 立绘**正右方**（右边放不下就贴左侧），
每帧跟着 Boss 走（复用与手牌栏相同的跟随逻辑），标题+正文自动换行，宽度上限 320。
用游戏自己的 `PanelContainer` 主题，不额外造美术。

**④ 地图上 Boss 节点不显示（用户："第一幕的 boss 不见了"）**

我们的遭遇类拼的 `BossNodePath` 是 `.../{自己的 id}_icon`，而 mod 没有那份贴图 →
`Failed to load resource` + 地图上 Boss 节点空白。改成**复用原版 Boss 的占位图标**
（`ModelDb.Encounter<TheKinBoss>().BossNodePath`）。

**⑤ 顺带确认**：`charbosses_debug.txt` 里写着 `defect` 时，**第一幕的普通战斗**都会被换成故障机器人
（那是调试开关的用途），所以"随便一场普通战斗遇到它"是开关造成的，不是 Boss 槽位错乱；
已把该文件清空，改回正常的「每幕 Boss 槽」逻辑。

### 11.23 R23（2026-10-17：三层固定故障机器人 + 崩坠的地图图标）

**① 第三幕固定是故障机器人**

`CharBossEncounters.ActTable` 的第三幕行从 `{1,2,3}`（静默/故障/亡灵）改成 `{2,2,2}` ——
荣耀的三个 Boss 槽位全是故障机器人「能力机」，这样才和用户"三层 = 能力机"的设计对上。
（第 3 个槽位原本是亡灵契约师，它的三层设计以后要出场时再改。）

**② 地图 Boss 图标：从塔 1 崩坠里取**

崩坠的地图图标就在它的 jar 里，正好是按「角色 + 流派」命名的：

```
downfallResources/images/ui/map/icon/defect_focus.png          ← 三层"能力机（专注流）"
downfallResources/images/ui/map/outline/defect_focus_outline.png
downfallResources/images/ui/map/icon/defect_bronze_orbs.png    ← 另两种流派（备用）
downfallResources/images/ui/map/icon/defect_void.png
```

取图脚本见 `tools/art/`（用 .NET ZipFile 直接从 jar 抽出，同时导出到 `tools/art/downfall/`）。
`DefectBossEncounter.BossNodePath` 指向 `res://CharacterBosses/images/map/defect_focus`
（游戏会自动再找 `_outline` 那张）。

**③ 贴图怎么进 pck（这次新加的能力）**

Godot 运行时加载 `res://x.png` 走的是 `x.png.import` 里的 remap → `res://.godot/imported/*.ctex`，
所以三个文件必须**各自落在精确的 res 路径**上，而普通 content 打包做不到（`.import` 会被跳过、
`.godot/imported` 也不在 content 里）。做法：

1. 用一个临时 Godot 项目 `tools/art/import_proj/`（目录名就叫 `CharacterBosses`，正好映射成
   `res://CharacterBosses/...`）跑 `godot --headless --import`，生成 `.import` 与 `.ctex`；
2. `tools/art/pck_extras.txt` 记录 `res路径|本地路径` 六行（png / png.import / ctex 各两份）；
3. `tools/pck_builder/build_pck.gd` 新增第 5 个参数解析这份清单，`install-mod.bat` 传进去。

踩坑：第一次用 PowerShell 往 gd 文件尾部追加函数，缩进用了空格而原文件是 Tab，
Godot 报 `Parse Error: Unexpected "Indent"`，整个打包静默失败（pck 还是旧尺寸 5680 字节）。
重写整个 gd 文件（统一 Tab）后正常：`Packed 6 extra file(s)`，pck 从 5.7KB 变成 234KB。

### 11.24 R24（2026-10-17：实机反馈八条修复）

用户一次性报了 8 个问题，逐条处理：

| # | 问题 | 原因与修法 |
|---|---|---|
| 1 | 特殊效果说明跑到左上角、样式不对 | 我自己画的 `PanelContainer + Label` 定位算错。**改成用游戏自己的说明面板** `NHoverTipSet.CreateAndShow(anchor, power.HoverTips, Right)`，锚点放在立绘右侧、每帧跟随，且**不调 Remove** → 整个战斗期间常驻 |
| 2 | 特殊效果没有图标 | `PowerModel.PackedIconPath` 不是 virtual，用 Harmony 前缀补丁让它返回原版**好奇（CuriousPower）的灯泡图标**；以后所有特殊效果 Power 都走这个补丁 |
| 3 | 集中「左脚踩右脚」翻倍 | `SyncAsync` 把差值**同时**加给了力量和集中：获得 6 集中 → 同步 6 力量 → 又同步 6 集中。改成**只补另一边**（`focusDelta` 参数决定给谁） |
| 4 | 球上数字要常亮 | `NOrb` 用私有 `_isLocal` 决定「显示数字 + 是否压暗」，而 `NOrbManager.AddSlotAnim`（战斗开始的 CombatSetUp 回调）会再造一批非本地槽位混进来。新增 `ForceLocalVisuals`：反射置 `_isLocal = true`、`Modulate = White`、再刷 `UpdateVisuals` |
| 5 | 「(激发 40)」文字会过时、且不该自己画字 | 去掉自绘文字，改成**原版攻击意图**（`SingleAttackIntent(EvokeDamage)`）；数值在计划时按当前集中/球实时算 |
| 6 | 红面具每回合给虚弱 | 遗物的「本场第一次」条件用 `PlayerCombatState.TurnNumber <= 1`，而影子玩家的回合数从不递增 → 原版每次阵营回合开始都会再触发一次。修法：`PrepareTurn` 里 `pcs.IncrementTurnNumber()` |
| 7 | 弹幕齐射等没被识别成多段 | 命中数大多在**计算变量**里（`CalculatedHits`：弹幕齐射=球数、终结技=本回合攻击牌数、散射炮=消耗的状态牌数、螺旋钻机=打出的牌数），以前只读 `Repeat`。新增 `PreviewVar` 走预览通道读 `CalculatedHits`，再回退 `Repeat` 与写死的例外表 |
| 8 | 规划没算「子程序」加费 | `BossScriptPlanner` 现在检查 Boss 是否有 `SubroutinePower`，每计划一张能力牌就 +1 能量；「充电」的「下回合 +1 能量」也记到 `NextTurnEnergyBonus`，下个回合补进预算 |

顺带：`CharBossMonster` 再拆出 `CharBossMonster.Turn.cs`（回合准备 + 招式构造），三个分部文件都在 200 行以内。

### 11.25 R25（2026-10-17：六条反馈）

| # | 问题 | 根因 / 修法 |
|---|---|---|
| 1 | 破损核心的电球没了 | 上一轮加的「回合数递增」抢跑了：`PrepareTurn` 里先 `IncrementTurnNumber()`，战斗开始钩子（异步）随后才执行，于是裂核看到 `TurnNumber = 2`、`<= 1` 不成立 → 不引导球。修法：`FireCombatStart` 在触发遗物钩子期间用反射把 `TurnNumber` 掰回 1，触发完恢复；另外容量为 0 时补一次 `AddCapacity(角色球位数)`（容量 0 时 `OrbQueue.TryEnqueue` 直接返回 false，球会静默消失） |
| 2 | 涅奥的祝福应该在破损核心左边 | `BossStatusRow.Fill` 现在先拼「期望顺序」（起始遗物、涅奥的祝福、其余按定义顺序），再反向添加 |
| 3 | 球数值还是不常亮 | 上一轮的 `ForceLocalVisuals` 没生效，且左侧疑似多出来一个「隐身」空槽。这一轮先加了**每帧强制**＋诊断日志（manager 位置 / 槽位数 / 首槽屏幕坐标 / Modulate，每 2 秒一条），下轮按日志定位 |
| 4 | 能量数字是自绘字体 | 换成**原版文字节点** `MegaLabel`（原版伤害数字/能量数字都用它，`SetTextAutoSize`），失败才退回 `Label` |
| 5 | 说明框还在左上角 | 上一轮只挪了锚点，`NHoverTipSet` 自己在创建时就定了位、不会跟着锚点走。现在**每帧直接移动说明面板本身**，并用面板实际尺寸做边界钳制 |
| 6 | 左侧有个「隐身」球位 | 与 #3 同一模块，靠 #3 的诊断日志定位（怀疑是 `CombatSetUp` 时 `NOrbManager.AddSlotAnim` 补造的槽位没被清干净） |

### 11.26 R26（2026-10-17：日志定位到的两个真 bug）

用户反馈"依旧修寂寞"，日志给出了硬证据：

```
[CharBoss] shadow player ready: DEFECT_BOSS act=3 … hp=290 energy=3
[CharBoss] 战斗开始钩子结束: 球位 capacity=3 orbs=0→0
```

**① 三层 Boss 只有 290 血（应该是 525）—— 血量档位算错**

生物的 HP 在 `Creature` 构造时由 `MinInitialHp` 算出来，而那一刻 `MonsterModel.CombatState` 不可用
（"Creature was accessed before it was set"），`BossActIndex` 退回 RunManager 也拿不到幕数 → 得到 0 →
用了第一幕的 140 + 150 = 290。**而剧本/能量/卡组是在回合里取幕数的，一直是对的**，所以只有血量错。

修法：`CharBossMonster.AfterAddedToRoom` 里调 `FixActHp()` —— 进房后 CombatState 一定可用，
按真实幕数重算 `HpForAct + 祝福加成`，用反射改 `Creature` 的 `<MaxHp>` / `<CurrentHp>` 后备字段
（这两个属性在引用程序集里只读），并按原比例保留已损失的血量。

**② 裂核的闪电球始终为 0**

`orbs=0→0` 说明遗物的 `BeforeSideTurnStart` 跑完也没引导出球（既没有异常日志，也没有球 ——
说明它内部的数量变量/条件在我们这种「影子玩家 + 手动触发」场景下不成立）。
修法：兜底 —— 触发完遗物钩子后，若球数没变且影子玩家身上有 `CRACKED_CORE`，
就手动 `OrbCmd.Channel<LightningOrb>(ctx, shadow)` 补一颗，并打一条日志。

**③ 球位改由我们自己摆**

`NOrbManager.TweenLayout` 按「容量」算弧线半径，且 `CombatSetUp` 时 `AddSlotAnim` 还会补造槽位 ——
屏幕上因此出现一个「飞到玩家那边」的空槽。现在 `Rebuild` 之后直接 `LayoutSlots`：
在自己算的弧线上摆（半径 82、张角 118°）、强制 `Visible`、强制白 Modulate + 数字可见，
不再调用 `TweenLayout`。

### 11.27 R27（2026-10-18 00:0x：无主充能球 —— Contains 空引用的根因）

**① 日志说清了是谁没有 Owner：不是遗物，是球**

```
[CharBoss] Contains 空引用已忽略（模型 LIGHTNING_ORB，多半是没有 Owner）   × 16983 条（整份日志只有这一种模型）
```

反编译 `CombatState.Contains`（私有，`IterateHookListeners` 逐模型调用）原文：

```csharp
return !orbModel.HasBeenRemovedFromState && orbModel.Owner.IsActiveForHooks;   // ← Owner 为 null 就 NRE
```

**② 根因：我们手动入队的球没有 Owner**

`OrbCmd.Channel`（反编译 `<Channel>d__3.MoveNext`）的顺序是：

```csharp
orb.AssertMutable();
orb.Owner = player;                       // ★先设 Owner
if (count >= capacity) await EvokeNext(...);
if (!await orbQueue.TryEnqueue(orb)) return;   // TryEnqueue 自己**不设** Owner
```

而 R26 的「裂核兜底」（`BossHookBridge.FireCombatStart`）是 `canonical.ToMutable(1)` →
`orbs.TryEnqueue(orb)`，**跳过了 `orb.Owner`** → 那颗闪电球永远无主 → 每次
`Hook.ModifyMaxEnergy` / `CanModifyDamage` / `CanReceivePowers` 展开监听者时都空引用一次。
（RitsuLib 只是放大器：它逐个问 `Contains`，于是把单个坏模型变成上万条 NRE。）

修法：入队前补 `orb.Owner = shadow;`（一行，`Owner` 的 setter 只是 `AssertMutable` + 赋值，安全）。

**③ 为什么它可能有关系的不止日志**

`OrbModel.CombatState => Owner.Creature.CombatState`、`ModifyOrbValue`、`TriggerPassive`
全都经 `Owner`，所以无主球既污染日志，也让这颗球的数值/被动不可信。

**④ 本轮同时首次实机的东西（注意别混在一起判断）**

上一轮的构建（`D69AD053…`，23:58）**从未启动过游戏**（日志最后写入 23:55:31），所以
`BossOrbDisplay` 的「补建槽位 + 调原版 `SetOrbManagerPosition`」、`BossStatusRow` 的能量节点缓存、
`CardCmd.AutoPlay` 5 秒超时，都是这次才第一次真跑。本轮只改了上面那一行 Owner，
其余行为与上一轮构建完全一致。

**⑤ 顺带记下一个已知硬规矩违规（未处理）**：`Core/BossHookBridge.cs` 现 251 行，
超出「单文件 ≤200 行」（R18 之后一路长大的）。下次动这个文件时应拆成
`BossHookBridge.cs`（回合钩子）+ `BossHookBridge.CombatStart.cs`（战斗开始 + 裂核兜底）。

### 11.28 R28（2026-10-18 09:0x：空球位看不见 + 球的数值不常显）

**实机回执（R27 的效果）**：`Contains 空引用已忽略` 一条没再新增（仍停在历史 16983 条），
`裂核兜底 → 球位 UI 已同步（AddOrbAnim）` 正常，`补建 3 个槽位` 也打出来了。
用户实测：球能生成、生成球的牌也不卡了；剩下「**空槽看不见**」与「**球的数值要悬停才显示**」。

**根因（反编译 `NOrb`，两件事同一个开关）**：`NOrb` 的私有字段 `_isLocal` 决定三件事：

```csharp
// UpdateVisuals 里，空槽分支
_sprite?.QueueFreeSafely();
_passiveLabel.Visible = false; _evokeLabel.Visible = false;
_outline.Visible = _isLocal;                       // ← 空槽的圈只在本地样式下可见
// 有球分支
_labelContainer.Visible = _isLocal;                // ← 数值不常显
if (!_isLocal) base.Modulate = Model.DarkenedColor; // ← 整颗球压暗
```

而槽是 `NOrbManager.AddSlotAnim` 造的，它写死 `NOrb.Create(LocalContext.IsMe(Player))`；`LocalContext.IsMe` 是
`player.NetId == LocalContext.NetId`，影子 Player 的 NetId 是我们虚构的 → 永远 false → 所有槽都是「非本地」。

**修法**：新增 `UI/OrbLocalVisuals.cs`，把每个槽的 `_isLocal` 掰成 true（并补上原版 `_Ready` 里本地槽的
`Scale *= 0.85f`），再让原版 `UpdateVisuals(OrbEvokeType.None)` 自己重画。幂等，所以三处都调：

| 调用点 | 为什么 |
|---|---|
| `BossOrbDisplay.Attach` | 补建的槽一造出来就是非本地 |
| `BossOrbDisplay.Follow`（每帧） | `AddSlotAnim` 补槽 / `AddOrbAnim` 新造的球节点同样是非本地 |
| `BossOrbDisplay.AfterChannel` | 手动入队后立刻显示，不等下一帧 |

**R28b（同日 09:2x，用户实测「只修了一半」的收尾）**

用户回执：**刚进战斗时**生成的 1 个闪电球 + 2 个空槽是对的，之后生成的球与空出来的槽都不对。

日志给了答案：本轮会话里 `帧回调 ok`（心跳）和 `球位 UI 同步：球数 X→Y` **一条都没有**，
而球数明明从 1 涨到 5 → **手牌栏那条 `GetTree().ProcessFrame` 每帧回调根本没跑到**
（`BossHandPanel._Ready` 里的订阅形同虚设）。所以只有 `Attach` / `AfterChannel` 这两条**非帧路径**
掰过的槽是对的 —— 正好就是「战斗开始那一刻」的状态。

原版只有三处会造 `NOrb` 节点，全部写死 `NOrb.Create(LocalContext.IsMe(Player))`：

| 原版方法 | 造出来的节点 | 用户看到的现象 |
|---|---|---|
| `AddSlotAnim` | 空槽（战斗开始 / 扩容） | 空球位看不见 |
| `AddOrbAnim` | 一颗**有模型的球** | 新球数值不显示、还压暗 |
| `EvokeOrbAnim` | 球激发后**新建一个空槽**（旧节点 0.25s 淡出后 `QueueFreeSafely`） | 空出来的球位看不见 |

修法：新增 `Patches/OrbLocalVisualPatch.cs`，给这三个方法各挂一个 **Harmony 后置补丁**，
调 `OrbLocalVisuals.ForceIfOurBoss(__instance)`；它用管理器私有的 `_creatureNode` 判断
`Entity.Monster is CharBossMonster`，只对我们的 Boss 生效（玩家自己的球位一个字段都不碰）。
于是**节点一造出来就是本地样式**，不再依赖任何帧回调。`BossOrbDisplay` 里原来的三处调用保留（无害）。

### 11.29 R29（2026-10-18 09:4x：球变亮 + 力量↔集中改成即时镜像）

**① 球有数值但还是暗的**

上一轮只掰了 `_isLocal`，没把 `Modulate` 改回来。原版 `NOrb.UpdateVisuals` 只在 `!_isLocal` 时
`Modulate = Model.DarkenedColor`，**没有 else 分支设回白**；只有 `OnFocus`（鼠标碰一下）会设白 ——
所以现象是「数值有、但暗，鼠标一碰才亮」。修法：`OrbLocalVisuals.Force` 掰字段时顺手
`orb.Modulate = Colors.White`（就是原版 `OnFocus` 那一句）。

**② 力量↔集中同步：从「轮询对账」改成「即时镜像」**

轮询版的 bug（用户实测）：`BiasedCognitionPower.AfterSideTurnStart` 调
`PowerCmd.Apply<FocusPower>(ctx, owner, -Amount, owner, null)` 掉 1 集中，而我们在「每次出牌后」才比对基线；
「暴涨」若是本回合第一张牌，两个变化就落进同一窗口 → 对账只看到「两边都变了」→ 按「以力量为准」补齐
→ 那 -1 被吃掉，偏差认知当回合失效。

正解不需要新补丁：`PowerCmd.Apply` 改完数值会广播
`AbstractModel.AfterPowerAmountChanged(choiceContext, power, amount, applier, cardSource)`
（反编译确认；注意这个虚方法比静态 `Hook.AfterPowerAmountChanged` **少一个 combatState 参数**）。
新增 `Powers/DeviationPower.Mirror.cs`（`DeviationPower` 改成 `sealed partial`）override 它：
`power.Owner == Owner` 且是 `FocusPower`/`StrengthPower` 时，把同一个差值 `PowerCmd.Apply` 给另一边
（silent），`_syncing` 防重入。轮询对账保留作兜底 —— 镜像生效后两边恒相等，它自然退化成「只更新基线」。

已核对的两条来源都走这个广播：`BulkUp.OnPlay → PowerCmd.Apply<StrengthPower>`、
`BiasedCognitionPower → PowerCmd.Apply<FocusPower>`。

**下一步（未做）**：

1. **激发数值（蓝色）**：原版是 `NOrbManager.UpdateVisuals(OrbEvokeType.Front/All)` →
   `NOrb.UpdateVisuals(isEvoking:true)` → `_passiveLabel.Visible = !isEvoking; _evokeLabel.Visible = isEvoking`。
   我们已有 `HighlightEvokes`（读 `boss.Plan` 里的 `CardModel.OrbEvokeType`），但**只在
   `ResolveTurnEnd` 里被调用过一次**，时机太晚（球已经激发完了）。要改成「手牌/意图刷新时」调。
2. **特殊效果说明框**：`UI/BossSpecialUi.cs` 在上几轮被整段删除了（`SpecialLocByAct` 目前没人读）。
   重建时照抄原版 `NOrb.OnFocus` 的写法：
   `NHoverTipSet.CreateAndShow(anchor, power.HoverTips, HoverTip.GetHoverTipAlignment(anchor))?.SetFollowOwner();`
   —— `SetFollowOwner()` 让面板自己跟着锚点走，正好解决「跑到左上角」。
3. **死掉的帧回调**：`BossHandPanel._Ready` 里的 `GetTree().ProcessFrame += OnFrame` 实测**没有生效**
   （心跳 0 条、`球位 UI 同步` 0 条，而球数确实在涨）。球位已经不依赖它了，但「手牌栏跟随 Boss 位移」
   仍然依赖 → 要换成 `_Process`（本项目实测 mod 节点收不到，所以曾改走帧信号）之外的可靠来源，
   例如在 `NCombatRoom` 的帧信号上挂，或干脆每次刷新/出牌时重算锚点。

### 11.30 R30（2026-10-18 10:0x：激发数值 + 特殊效果说明框）

**① 激发数值（蓝色）**

原版玩家侧：手里提着「双重释放/碎片」时，**将要被激发的那个球**右下角数值从被动值换成激发值并变蓝
（`NOrb.UpdateVisuals(isEvoking:true)` 负责画，`NOrbManager.UpdateVisuals(OrbEvokeType.Front/All)` 决定谁进入该状态）。
Boss 没有「提着牌」这个动作，所以用**本回合计划**里的 `CardModel.OrbEvokeType` 当替代。

- `HighlightEvokes(boss)` 改成先记下 `CurrentEvoke` 再调 `manager.UpdateVisuals(type)`；
- 调用点从「回合结束后」挪到 **`BossHandUi.Refresh`**（玩家看 Boss 手牌/意图的时机就是它）；
- `OrbLocalVisuals.Force` 重画时改用 `BossOrbDisplay.CurrentEvoke`，否则刚建出来的槽会把蓝色激发值又重置回被动值；
- `ResolveTurnEnd` 里 `ClearHighlight(boss)` 恢复被动值。

**② 特殊效果说明框（原版 `NHoverTipSet`）**

新增 `UI/BossSpecialUi.cs`，内容直接用 `PowerModel.HoverTips`（即 `powers.json` 里的
`DEVIATION_POWER.title/description`），**不自绘任何节点**：

```csharp
_tipSet = NHoverTipSet.CreateAndShow(_anchor, power.HoverTips.ToList(), HoverTipAlignment.Right);
_tipSet?.SetFollowOwner();
```

`SetFollowOwner()` 的实现是把面板钉在「锚点当前全局位置」上，并在面板自己的 `_Process` 里跟着锚点走 ——
所以不需要我们写每帧逻辑（正好绕开 §11.28b 那个死掉的帧回调）。锚点是挂在战斗房 UI 层
`%PlayContainer`（屏幕坐标系，与手牌栏同一个父级）上的 1×1 `Control`，位置按立绘包围盒
`bounds.GetScreenPosition() + (w*0.98, h*0.26)`（立绘右侧）算，每次刷新重对一次（Boss 被拖走也能跟上）。

生命周期：`BossHandUi.Refresh` 里 `Ensure`（幂等）；`BossHandUi.Clear` 里 `Hide`
（`NHoverTipSet.Remove(anchor)` + 释放锚点），否则战斗结束后面板会留在全局悬浮说明层上。

**③ 文件行数**：`BossOrbDisplay.cs` 又要超 200 行 → 把「激发显示 + 每回合被动结算」拆到
`UI/BossOrbDisplay.Highlight.cs`（`BossOrbDisplay` 改 `static partial`）。当前最大文件 190 行。

### 11.31 R31（2026-10-18 10:2x：激发高亮被原版清掉 + 说明框挡人 + 意图双重加成）

**实机回执**：说明框出来了、球常亮了。新报三条，全部有反编译证据。

**① 「一直看不到球的待激发状态」——原版自己会把它清掉**

```csharp
// NOrbManager
public override void _EnterTree() {
    CombatManager.Instance.StateTracker.CombatStateChanged += OnCombatStateChanged;
}
private void OnCombatStateChanged(CombatState _) => UpdateVisuals(OrbEvokeType.None);   // ← 清激发高亮
```

原版玩家侧不怕这个：鼠标提着激发牌时每帧都会重新调 `UpdateVisuals(Front/All)`。
我们的「提着牌」是**本回合计划**（稳定不动），所以高亮刚设上就被任意一次战斗状态变化清成被动值。

修法：`Patches/OrbLocalVisualPatch.cs` 加第三个后置补丁
`[HarmonyPatch(typeof(NOrbManager), "OnCombatStateChanged")]` → `OrbLocalVisuals.ForceIfOurBoss`；
并把 `Force` 里的 `UpdateVisuals(CurrentEvoke)` 改成**无条件调用**（原来只在真掰过字段时才调）。

**② 说明框挡住 Boss**

说明框是从锚点向右展开的，而立绘是 `scale` 为负的镜像节点，`Visuals.Bounds` 的中心并不等于
可见立绘中心 → 只按包围盒右缘摆就会压在身上。按用户指示再右移**一个说明框宽度**：
`target.X += BoxWidth()`，宽度直接问原版节点（`GetCombinedMinimumSize().X`，量不到回落 320）。
每次刷新都会按真实宽度重对一次位置。

**③ 意图数值被「力量 + 集中」双重加成 —— `AttackIntent.GetSingleDamage` 自己还会再过一次钩子**

```csharp
public int GetSingleDamage(IEnumerable<Creature> targets, Creature owner) {
    decimal num = DamageCalc.Invoke();
    Player me = LocalContext.GetMe(owner.CombatState);
    if (me != null)
        num = Hook.ModifyDamage(me.RunState, combatState, me.Creature, owner, DamageCalc.Invoke(),
                                ValueProp.Move, null, null, ModifyDamageHookType.All, CardPreviewMode.None, out _);
    return Math.Max(0, (int)num);
}
```

注意 `Hook.ModifyDamage` 的参数顺序是 **(target, dealer)**，所以这里是「目标 = 本地玩家、攻击者 = Boss」——
**意图节点会在我们给它的数字上再跑一次力量/易伤**。而早先我们喂的是已经加成过的 `PreviewValue`：

| 意图 | 旧算法（双重） | 新算法（单次） |
|---|---|---|
| 普通攻击 | 基础 + 力量（预览）**+ 力量**（意图节点） | 喂基础值 → 意图节点 + 一次力量 |
| 激发球 | 基础 + 集中（`EvokeVal` 过 `FocusPower.ModifyOrbValue`）**+ 力量**（意图节点） | 喂「基础 − 集中」→ 意图节点 + 一次力量 |

因为本 mod 的力量与集中恒等，第二种正好等于用户要的「**都只受力量加成**」。
实现：`BossDamagePredictor.ReadBaseDamage` 用
`DynamicVar.UpdateCardPreview(..., runGlobalHooks: false)` 取未加成基础值（先跑一遍完整预览保证
`CalculatedDamage` 被算出来，再逐个变量按不跑钩子重算）；`BossOrbMath.EvokeDamageOf(orb, focus)`
减去 `BossOrbMath.FocusOf(boss.Creature)`。

**④ 顺带**：`BossDamagePredictor` 里那份「已加成」的读法只剩 `DealsDamage` 用，改成就看基础值后删除，
文件回到 184 行。

### 11.32 R32（2026-10-18 10:4x：球位模拟器 —— 激发/生成球的意图全算对）

用户要求：**电球激发 → 攻势意图；冰球激发 → 守势意图**；而且不只是「双重释放」这种直接激发的牌，
**电击/寒流/冰川这类生成球的牌在球位满时也会激发**，**用了雷暴之后每张能力牌都会生成电球**也可能激发；
另外**双重释放是两段伤害**，要显示成 `伤害×2`，且两段各自过一次加成。

**① 为什么必须做「按出牌顺序的模拟」**

反编译 `OrbCmd.Channel`（`<Channel>d__3.MoveNext`）的顺序：

```csharp
if (Orbs.Count >= Capacity) await EvokeNext(ctx, player);      // ← 满了就先激发队首
if (!await orbQueue.TryEnqueue(orb)) return;                   // 再入队
```

也就是说「这张牌会不会激发」取决于**前面的牌把队列变成了什么样**，只看牌自己的 `OrbEvokeType` 必然漏。
新增 `Core/BossOrbSim.cs`：复制当前队列，按计划顺序推进，逐张产出激发结果。

| 输入 | 模拟内容 | 反编译依据 |
|---|---|---|
| 「生成什么球」 | 卡名 → (球类型, 数量) 表 | `Zap/ColdSnap=1 冰或电`、`Glacier` 里 `for (i<2)`、`IceLance` 用 `DynamicVars.Repeat`、`Rainbow` 三种各 1、`MeteorStrike` 等离子 3 … |
| 牌自己的激发 | `OrbEvokeType` × `EvokesPerOrb` | `Dualcast.OnPlay` = `EvokeNext(dequeue:false)` → 等待 → `EvokeNext()`，**同一颗球两次** |
| 雷暴 | 每张能力牌生成 `StormPower.Amount` 个电球 | `StormPower.AfterCardPlayed`（`card.Type == Power`） |
| 扩容 | `CAPACITOR` 让容量 +2 | `Capacitor` 调 `OrbCmd.AddSlots` |

**② 意图映射（用户定的规则）**

| 激发的球 | 意图 |
|---|---|
| 电球 / 黑暗球 / 玻璃球 | **攻势**（`SingleAttackIntent` / `MultiAttackIntent`） |
| 冰球 | **守势**（原版 `DefendIntent`，不带数字） |
| 等离子球 | 只给能量，不出意图 |

伤害按「每次激发的伤害」分组：同一份伤害出现 N 次 → `MultiAttackIntent(伤害, N)`，于是
**双重释放显示成 `伤害×2`，而且两段各自过一次加成**（`GetSingleDamage` 对单段加成后再 × 次数）。
不同球类型的伤害不同就各自成一个意图 —— 不会把两个数值硬拼在一起。

**③ 手牌栏不再自己拼激发意图**：`BossHandCard.BuildIntentRow` 只渲染 `planned.Intents`；
激发意图在计划阶段就由 `BossOrbSim.AppendIntents` 追加进去了，顺序天然是「牌本身的效果 → 激发效果」。
`BossOrbMath.EvokeDamageFor` 随之删除（被模拟器取代），`EvokesPerOrb` 转公开给模拟器用。

**④ 行数**：`BossHandCard.cs` 破 200 → 悬停放大 + `ReleaseCell` 拆到 `UI/BossHandCard.Hover.cs`
（`BossHandCard` 改 `static partial`）。当前最大文件 195 行（`BossOrbSim.cs`）。

### 11.33 R33（2026-10-18 11:0x：所有将被激发的球都显示待激发状态）

用户要求：不只是队首，**本回合所有会被激发的球**都要显示成「待激发」（蓝色激发值）。

原版只给两档：`NOrbManager.UpdateVisuals(OrbEvokeType.Front/All)` = 队首 / 全部。
但 Boss 这边会出现「队首之外的球被激发」（球位满时生成球激发队首、碎片激发全部后队首又换了…），
两档不够。好在 **`NOrb.UpdateVisuals(bool)` 本身是 public** → 改成逐球点亮：

1. `BossOrbSim` 新增 `HashSet<OrbModel> Evoked` —— 记录模拟过程中**真实队列实例**被激发的那些
   （模拟里新生成的球不记录，它们此刻还没画在屏幕上）；
2. `BossOrbDisplay.HighlightEvokes` 用**和意图完全相同的模拟器**跑一遍本回合计划 →
   `EvokingOrbs` = 会被激发的球；于是「亮起的球」与「显示了攻势/守势意图的球」永远是同一批；
3. `ReapplyHighlight(manager)`：先 `UpdateVisuals(None)` 全部恢复被动，再按
   「`_orbs[i]` ↔ 队列第 i 颗」逐球 `node.UpdateVisuals(true)`；
4. `OrbLocalVisuals.Force` 改调 `ReapplyHighlight` —— 原版 `OnCombatStateChanged` 每次都清零，靠它恢复。

注意 `NOrbManager.Player` 不是 public，取玩家要走私有字段 `_creatureNode.Entity.Player`
（`QueueOf` 里就是这个写法）。

### 11.34 R34（2026-10-18 11:2x：冰雹风暴的独立意图）

用户要求：故障机器人**同时有冰雹风暴（`HailstormPower`）和冰球**时，在**能量栏正下方、
与其他意图同一高度**显示一个攻击意图，数字 = **冰雹风暴层数**，且**不吃力量/集中**。

**① 数值口径（反编译 `HailstormPower.BeforeSideTurnEnd`）**

```csharp
int frost = Owner.Player.PlayerCombatState.OrbQueue.Orbs.Count(o => o is FrostOrb);
if (frost >= DynamicVars["FrostOrbs"].IntValue)
    await CreatureCmd.Damage(ctx, CombatState.HittableEnemies, Amount, ValueProp.Unpowered, Owner);
```

即「冰球够数 → 对敌方造成**层数**点伤害，`ValueProp.Unpowered`（不吃力量/集中）」。

**② 为什么需要 `RawAttackIntent`**

原版 `AttackIntent.GetSingleDamage` 会对喂进去的数字**再跑一次** `Hook.ModifyDamage`（力量/易伤），
所以「必须原样显示」的数字躲不开加成。`SingleAttackIntent.GetTotalDamage` 是 `override`（可再覆盖），
而 `NIntent.UpdateVisuals` 的数字取自 `attackIntent.GetIntentLabel(...)`、图标取自 `GetTexture(...)`，
两者内部都用 `GetTotalDamage` → 覆盖它就同时修好数字与图标档位，节点仍是原版 `NIntent`。
新增 `Core/RawAttackIntent.cs`。

**③ 位置**：`BossHandPanel.Extra.cs` 里的独立意图容器（`ExtraIntents`，名字不冲突于卡片格子），
在 `LayoutCells` 里由 `PlaceExtraIntents` 摆位：

| 轴 | 取值 |
|---|---|
| x | `BossStatusRow.EnergyCenterX(row)` 居中（能量球中心，新增的公开方法） |
| y | 与卡片意图行**同一个公式**：`intentTop + IntentShiftY * cardW` |

重建时机：`BossHandPanel.Rebuild` 里排完状态条之后调 `BuildExtraIntents(boss)`（先取能量球位置再排意图），
`Reset()` 里一起释放。

### 11.35 R35（2026-10-18 12:0x：录像复核后的四条修正）

用户提供录像（`屏幕录制 2026-10-18 112548.mp4`）。读帧方式从上一个模组 CriticalCinematic 抄：
`imageio_ffmpeg` 里那份 `ffmpeg-win-x86_64-v7.1.exe` + `crop=…,scale=…` 取帧，
再配合日志里的排版数字定位（比纯目测可靠）：

```
hand ui: 4 card(s), cell 95x133, 状态条 y=0 h=66 / 卡面顶 y=88 / 意图顶 y=223 / 面板高 255,
         pos=(1295.92, 188.72), size=(404.8, 255.28)
```

**① 意图行：收紧 + 左上移**

| 常量 | 旧 | 新 | 依据 |
|---|---|---|---|
| `IntentSeparation`（新增） | 主题默认 4 | **-8** | 同一张牌多个意图的间距太大，会压到右邻卡 |
| `IntentShiftX` | -0.40 | **-0.58** | 视频里最左意图（蓝色上箭头）的**可见左缘**比卡面左缘偏右约 17 逻辑像素；17/95 ≈ 0.18 → -0.40-0.18 |
| `IntentShiftY` | -0.40 | **-0.52** | 用户「整体往左上挪」 |

（`IntentSeparation` 用 `row.AddThemeConstantOverride("separation", …)` 落地。）

**② 待激发高亮「不是实时的」——已出的牌还留在 `Plan` 里**

`PlayPlannedCards` 遍历 `Plan.ToList()` 但从不摘牌，而 `HighlightEvokes` 是拿 `boss.Plan`
重新模拟一遍的 → 出完「双重释放」之后那一条还在计划里，于是模拟去激发**当时**的队首
（= 刚顶上来的那颗），把它误标成待激发。修法：每打出一张就 `Plan.Remove(play)`。

**③ 冰雹风暴意图要把「本回合要打的那张」算进去**

`BuildExtraIntents` 现在 = `HailstormPower.Amount + 计划里的 HAILSTORM 张数 × 6`
（每张的 `PowerVar<HailstormPower>` 基数，读不到回落 6）。所以「本回合要打冰雹风暴」时，
回合一开始意图就已经是叠加后的数字。

**④ 意图数字偏高一点点：偏差认知的集中还没扣**

`BiasedCognitionPower.AfterSideTurnStart` 在 **Boss 自己回合开始**（敌方回合）掉 `Amount` 点集中，
而计划和意图是在**玩家回合开始**算死的 → 那时这笔还没扣，所有受力量加成的意图都偏高。
新增 `BossIntentBuilder.PendingDrop(creature)`（= 偏差认知层数），规划时先从基础值里减掉
（意图节点之后会再补一次力量，正好抵消）。球上显示的数值按用户要求**先不动**。

### 11.36 R36（2026-10-18 12:4x：意图回补 / 状态条不漂移 / 冰雹风暴首回合）

**① 意图「预扣一点」的副作用：行动时反而偏低**

原版 `NIntent` 自己挂 `OnCombatStateChanged → UpdateVisuals()`，数字是**每次战斗状态变化都重算**的。
所以规划时喂 `基础-1` 是对的（此刻力量还没掉），但等 Boss 回合开始、集中/力量真的掉了 1 点之后，
节点会用「已经掉过的力量」再加上一次 `-1` → 行动时显示偏低一点（用户实测）。

修法：Boss 回合开始（`FireTurnStart` 之后）调 `CharBossMonster.RefreshPlannedIntents()`，
用 `pendingDrop = 0` 把**还没打出的牌**的意图重算一遍（`PlannedPlay.Intents` 改成可写）。
含义正是用户要的「在它回合开始掉集中的时候把那一点加回去」。

**② 状态条不再随出牌左移 + 遗物统一高度**

两个原因都修掉了：

| 症状 | 原因 | 修法 |
|---|---|---|
| 出牌后能量/遗物往左滑 | `total = _cells.Count * cardW …` → 面板宽度随出牌缩小，整块重新居中 | 新增 `_turnCards`（本回合最大手牌数，`Rebuild` 整排重建时更新），排版用 `Math.Max(_turnCards, _cells.Count)` 算宽度 |
| 遗物高矮不一 | `Fit()` 只「超过就缩」，68×68 缩成 56 而 48 的还是 48 | `Fit()` 改成**一律按 `IconSide`（56）统一高度**（等比缩放宽） |
| 能量球比遗物大一圈 | `NEnergyCounter` 是 112 见方，且从未归一到遗物尺寸 | 建节点的**那一次**调 `Fit()`（只在新建时调，否则 Scale 会叠乘成 0.25） |

注意状态条仍挂在手牌栏里（不钉屏幕绝对坐标）——冰雹风暴意图要贴着能量球下方，两者必须一起走。

**③ 冰雹风暴「第一次打」那回合也该显示**

原先要求 `HailstormPower` 已存在 → 第一张冰雹风暴打出去之前没有意图。现在：
`当前层数 + 计划里 HAILSTORM 张数 × 6`；冰球条件也改成看**回合结束**时会不会有冰球
（`BossOrbSim.FrostOrbsAfter` 把整份计划模拟一遍，所以本回合要打冰川也会成立）。

### 11.37 R37（2026-10-18 13:0x：状态条改成**绝对钉屏**）

用户否掉了「跟着面板走」的做法，要求**钉屏幕**，并且明确「没让你把能量栏变小」→ 能量球的
`Fit()` 已撤回（仍是原版 112 见方），遗物的统一高度保留。

新增 `UI/BossStatusRow.Pin.cs`：位置不再来自手牌栏宽度，而是**从玩家 HUD 的遗物栏算出来**：

| 元素 | 参照（用户指定） | 取值来源 |
|---|---|---|
| 能量栏最右侧 | 玩家遗物栏**第一排**最右边那件的右缘 | `NRun.Instance.GlobalUi.RelicInventory` 的子节点：取 y 最小那一排里 `x+w` 的最大值 |
| 遗物那一行的高度 | 玩家遗物栏**第二排**的顶边 | 同上，取 y 严格大于第一排的最小 y |
| 能量栏 | 与遗物同一条基线 | `child.Position.Y = (IconSide - size.Y) * 0.5f`（高的往上抬半格，中线对齐） |

坐标换算：`row.Position = new Vector2(rightEdgeX - width, rowTopY) - panel.GlobalPosition`，
所以**必须排在 `FollowBoss`（设定面板位置）之后**执行。拿不到玩家遗物栏时退回「贴着面板右缘」。

顺带：手牌栏整块宽度用 `_turnCards`（本回合最大手牌数）固定，出牌不再缩宽、不再左滑。

### 11.38 R38（2026-10-18 13:4x：状态条改成**以 Boss 为坐标**）

R37 的「钉玩家遗物栏」实测跑到了屏幕左上角 —— 因为参照点是**逐件遗物**算的
「第一排最右缘」，玩家遗物一少参照点就贴到左上角去了（不可靠）。用户要求改成**以 Boss 为坐标**，
理由也很明确：以后可能有**多个角色 Boss 同场**，每条状态条必须只跟着自己的 Boss。

现在的规则：

| 轴 | 依据 |
|---|---|
| 横向 | 状态条右缘 = **Boss 立绘包围盒右缘**（`BossHandAnchor.ResolveRect`，新增；微调旋钮 `PinRightOffsetX`（× 包围盒宽）） |
| 纵向 | 仍由手牌栏整块决定（那块本来就贴着头顶上方，天然是 Boss 相对） |

于是横向**完全与手牌张数无关** → 出牌不漂移；多 Boss 同场时各自归位。
`PlacePinned`/玩家遗物栏参照那套已删除，只保留 `PlaceBossRelative` + 兜底的 `Place`。
计算必须排在 `FollowBoss`（设定面板位置）之后。

### 11.39 R39（2026-10-18 14:0x：状态条贴到卡牌右上角）

「以 Boss 包围盒右缘」还是不理想（8 件遗物时状态条宽 608，会比包围盒宽出一大截，看着偏左）。
用户给了明确的相对约束，改成按它算，不用猜像素：

| 约束 | 实现 |
|---|---|
| **能量栏左缘** = 最后一张卡的右缘 | `rowRight = cardsRight + energyWidth`，再 `row.Position.X = rowRight - width` |
| **遗物下端** = 卡牌上端 | `row.Position.Y = cardsTop - IconSide`（遗物那一行在行内 y=0） |

`cardsRight` 用**本回合最大手牌数**（`sizing`）算：`(sizing-1)*(cardW+Gap)+cardW`，
所以打出卡牌时这条不缩、不左滑。卡面/意图的相对位置完全没动（面板 `Size.Y` 变大只是上方多了透明留白，
`anchorTop - Size.Y - GapAboveBoss` 把整块钉在头顶上方，卡面底边与头顶的距离不变）。

### 11.40 R40（2026-10-18 14:0x：新意图「牺牲」+ 状态条微调落地）

**① 「牺牲」意图**（用户定义：这名敌人将付出代价换取强力效果，图标 = 强化的蓝色箭头**向下翻转**）

原版意图只有 攻击/格挡/强化(↑红蓝?)/减益(↓红)/召唤/治疗/状态/眩晕…，**没有「牺牲」**。做法：

| 步骤 | 实现 |
|---|---|
| 类型 | 新增 `Core/SacrificeIntent.cs`，继承**原版 `BuffIntent`**（图标/尺寸/动画全是强制化那一套），只把 `IntentPrefix` 换成 `SACRIFICE`（本地化文案用我们自己的）、`GetAnimation` 固定回 `buff`（否则查不到 `sacrifice` 的动画帧） |
| 图标向下 | `NIntent` 的图标是私有 `Sprite2D _intentSprite` → UI 层反射翻 `FlipV = true`。不碰数字标签、不自造美术 |
| 文案 | 新增 `content/localization/{zhs,eng}/intents.json`：`SACRIFICE.title / .description` |

**② 什么牌算「牺牲」**（`BossIntentBuilder.ClassifyPowers`）

关键在于区分「给自己上负面」（牺牲）与「给玩家上负面」（原版红色 ↓）。规则：

| 信号 | 判定 |
|---|---|
| `HpLoss > 0` | 牺牲（血墙 / 御血术） |
| 能力变量为**负** 且 `card.TargetType == Self` | 牺牲（友谊 -2 力量） |
| 能力变量是**减益** 且 `TargetType == Self` | 牺牲（偏差认知的偏差认知 debuff、精神过载的灾厄），**不再**是红色 ↓ |
| 其余减益 | 原版红色 ↓（给玩家上负面，例如「下砸」减敌人力量） |
| 表内卡名 | 牺牲：`HYPERBEAM`（全体伤害+失去集中，目标不是自己）、`BULLET_TIME`/`BATTLE_TRANCE`（代价是不能再抽牌，无变量）、`BULK_UP`（`OrbCmd.RemoveSlots` —— 注意扩容是 **Add**Slots、变量名同为 `OrbSlots`，不能按变量判断）、`WRAITH_FORM`、`BIASED_COGNITION`、`NEUROSURGE`、`FRIENDSHIP` |

于是「偏差认知」= 蓝色↑（+5 集中）+ 蓝色↓（牺牲），「暴涨」= 蓝色↑（+2 力量/敏捷）+ 蓝色↓（失去球位）。

**③ 状态条微调默认值落地**：`BossUiTuning` 的默认值改成实机认可的
`relicGapY=-80`、`energyGapX=-10`、`energyGapY=0`；`charbosses_ui.txt` 缺失时按这组生成。
（另修掉一个坑：原来用 `AppContext.BaseDirectory` 定位调参文件，那是**游戏 exe 目录**，
用户改 `mods\CharacterBosses\` 里那份永远读不到 —— 改成用程序集自身路径。）

















### 11.41 R41（2026-10-18 深夜：第一幕「亡灵契约师」+ 奥斯提全套）

**① 第一幕 = 亡灵契约师**（`Core/NecrobinderScript.cs`，11 回合、7–11 循环）

- `ActTable` 第一幕改成 `{3,3,3}`；
- 血 66 + 涅奥祝福 150 = **216**；能量定义 3、祝福 −1 → 实际 2（剧本另有逐回合覆盖：`BossTurn.Energy`，这是新加的数据字段）；
- 遗物：缚魂命匣（起始）、涅奥的祝福、闪亮口红 `SparklingRouge`、**船夹板 `HornCleat`**（用户口述的"船甲板"实际叫船夹板）；
- 循环进阶：`BossScript.UpgradeByCycle` —— 第 1 轮循环升级出击+护卫，第 2 轮碎骨+死亡之门，之后每次打击+防御（`BossScript.CycleAt`）。

**② 特殊效果「时候未到」**（`Powers/NotYetPower.cs`）

| 效果 | 实现 |
|---|---|
| 格挡将因回合开始消失 → 抵消等量灾厄 | `BeforeSideTurnStart(side=Player)`：读玩家格挡 B，灾厄 −B，并**主动扣掉 B 格挡**（有残影/壁垒时原版不清格挡，不扣会变成双倍收益） |
| 回合将结束若会因灾厄死亡 → 用格挡（再用奥斯提生命）续命 | `BeforeSideTurnEnd(side=Player)`：需要减 `灾厄−生命+1`，先扣格挡、不够再扣 Boss 侧奥斯提生命（`ValueProp.Unpowered` 伤害） |

**③ 意图**：灾厄=向下红箭头（自动，因 Doom 是 Debuff 型 PowerVar）；`BONE_SHARDS`/`FRIENDSHIP` 带**牺牲**（`SacrificeIntent` = 继承原版 `BuffIntent`，UI 层反射 `NIntent._intentSprite.FlipV` 翻成向下）；`DRAIN_POWER` 强制 攻势+强化+红↓；**奥斯提攻击牌**（`UNLEASH`/`BONE_SHARDS`）用 `RawAttackIntent`，避免她的负力量污染出击伤害。

**④ 奥斯提（踩坑最多的一块）**

| 症状 | 根因 | 修法 |
|---|---|---|
| 血条/状态栏完全不可见 | 外层 `NCreatureStateDisplay` 被整块 `visible=False / alpha=0`（"非本地玩家的随从"处理，与联机看不到队友随从血条同理） | 只点亮外层容器 + `NameplateContainer`；**不要整棵子树拉 alpha**，否则血条背景被漂白 |
| 一直是死的、碎骨/出击空转 | `player.Osty => PlayerCombatState.GetPet<Osty>()` 取 Pets 里**第一只**；召唤出的那只没登记 → 永远拿到尸体 | 召唤后用 `SummonResult.Creature` **补登记进 `_pets`** |
| 越召唤越多 | 未登记的球同理：每回合判定"没有"→ 再召一只 | 补登记 + `_lastSummonTurn` 每回合只一次 |
| 尸骨堆积如山 | 碎骨每轮杀一只，尸体节点留在场上 | 摘出 `Pets` 时一并 `Visible=false` |
| **回合越久越卡** | 死掉的奥斯提仍是战斗里的 Creature → 每次钩子广播都要遍历，RumiLib 再放大 | **把死宠物从 `CombatState._allies/_enemies` 摘掉**（这条一修，延迟消失） |
| 位置不稳定/叠在她身上 | 曾用"扫战场找 OSTY"判定归属 → 把玩家的奥斯提当成她的（`Player` 常为 null） | 只用 `shadow.Osty` 且必须 `PetOwner == shadow`；位置**校准一次后固定偏移**（相对 Boss 节点），不再依赖玩家的奥斯提 |
| 血条文字镜像 | 翻了整个 `NCreature` | 只翻 `Visuals`（不能翻节点，也不能翻 `Body`——奥斯提是 Spine 网格） |

**⑤ 性能诊断留下的通用手法**（以后遇到"卡一下"直接照做）

`Stopwatch` 包住可疑段 + 只在超阈值时打日志。本次依次排除了：球结算 0ms、手牌 UI 刷新 <20ms、遗物触发 0ms、`PowerCmd.Apply<DoomPower>(-N)` **115ms**（扣到 0 会走整套"移除能力"链路 → 改成 `SetAmount(silent:true)` 直接设层数）。

**⑥ 已知限制（纯视觉，未修）**

1. 复活/召唤瞬间奥斯提**先叠在她身上**，随后才被摆到固定位置；
2. 每次复活**先出现在她右侧、再瞬移**到固定位置（原版 `OstyCmd.Summon` 内部有一次自己的排布）；
3. **朝向**：原版在召唤完成后会重设朝向（随从挂在玩家侧容器、"正面"朝右），我们翻 `Visuals` 会被覆盖 → 目前**站着与攻击都朝右**。要真正修需把随从移到敌方容器并改阵营，风险高（抢宠物/血条错位），暂缓；
4. 复活瞬间血条会闪一帧 `∞`（`MaxHp=0` 的中间态）。
