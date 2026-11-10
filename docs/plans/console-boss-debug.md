# 控制台直接打某一只 Boss（一 Boss 一个 id）

> 状态：**代码已落地、已构建 0 错误、已装进游戏（v0.1.12）**，但**还没实机验证过**
> （`mod_log.txt` 里还没有 `遭遇钉死档位=` 这一行）。实机第一次用到时先确认两件事：
> ① `fight <id>` 不报 `Encounter '<id>' not found`；
> ② 日志出现 `[CharBoss] <怪物> 遭遇钉死档位=N（<encounter id>）`，且血量/卡组是该档位的。

## 1. 怎么开控制台

跑中一局（地图界面就行）按 **`** 或 **'** 或 **^** 或 **\***（Shift+8），Esc 关。

debug 命令默认只给开发版，但源码 `NDevConsole._Ready` 是这么判的：

```csharp
bool shouldAllowDebugCommands = OS.HasFeature("editor") || TestMode.IsOn
    || ModManager.IsRunningModded() || SaveManager.Instance.SettingsSave.FullConsole;
```

**装了 mod 就满足 `IsRunningModded()`** ⇒ `fight` / `act` / `godmode` 全部可用，不用改设置。

## 2. `fight <遭遇ID>`

`MegaCrit.Sts2.Core.DevConsole.ConsoleCommands.FightConsoleCmd`，`CmdName = "fight"`：

```csharp
var modelId = new ModelId(ModelId.SlugifyCategory<EncounterModel>(), args[0].ToUpperInvariant());
RunManager.Instance.EnterRoomDebug(RoomType.Monster, MapPointType.Unassigned,
    ModelDb.GetById<EncounterModel>(modelId).ToMutable());
```

- 参数 = 遭遇 id，大小写不敏感（内部 `ToUpperInvariant`），Tab 可补全。
- 必须**有一局进行中**（`RunManager.Instance.IsInProgress`）。
- ⚠ `EnterRoomDebug` 里有 `if (model is EncounterModel e) roomType = e.RoomType;` ——
  房间类型**由遭遇自己决定**，传进去的 `RoomType.Monster` 会被覆盖。我们的遭遇都是
  `RoomType.Boss`，所以 `fight` 出来的是**真 Boss 房**：打完走 Boss 结算并进下一幕。
  想原地反复测就 `act N` 再 `fight`。

## 3. 每只 Boss 的 id（**正常流程与控制台是同一批**）

id = 类名按 `StringHelper.Slugify`（驼峰断词 → 全大写）算出来的，和本地化 key 同一套。
这些遭遇**把强度档位钉死**（`Core/IBossTierPin.cs` 的 `TierAct`），所以不管当前第几幕都是那一套。

> 2026-10-23 用户要求「把正常流程的 boss id 和控制台的统一一下（以控制台为准）」：
> `CharBossEncounters.ForVanillaBoss` 现在**直接返回下面这批遭遇**
> （`TierByCharacterAct` 表：(角色, 幕) → 遭遇）。所以正常打到的那只和 `fight` 打到的
> 是**同一个遭遇 id** —— 地图上的名字、顶栏 Boss 图标、死亡提示（`*.loss`）从此都显示设计名
> （愤怒战 / 灵魂骨 / 无色储君…），不再是「亡灵契约师」这种角色名。
> 没有设计的档位（铁甲战士一层以外、故障机器人一二层、猎手一层）仍退回按角色的基础遭遇
> （起始卡组占位，标题是角色名）。

| 命令 | Boss | 档位 | 剧本 / 卡组 |
|---|---|---|---|
| `fight IRONCLAD_ANGER_BOSS_ENCOUNTER` | 愤怒战 | 一层 | `Core/IroncladAngerScript.cs` |
| `fight NECROBINDER_DOOM_BOSS_ENCOUNTER` | 灾厄骨 | 一层 | `Core/NecrobinderScript.cs` |
| `fight NECROBINDER_FLESH_BOSS_ENCOUNTER` | **血肉骨** | 一层 | `Core/FleshBoneScript.cs`（用户 2026-11-10） |
| `fight REGENT_DEBRIS_BOSS_ENCOUNTER` | 碎屑储君 | 一层 | `Core/DebrisRegentScript.cs` |
| `fight IRONCLAD_VULN_BOSS_ENCOUNTER` | 易伤战 | 三层 | `Core/VulnIroncladScript.cs` |
| `fight DEFECT_CLAW_BOSS_ENCOUNTER` | 物理机 | 二层 | `Core/ClawDefectScript.cs` |
| `fight SILENT_SLY_BOSS_ENCOUNTER` | **奇巧猎** | 一层 | `Core/SilentSlyScript.cs` |
| `fight SILENT_BREWER_BOSS_ENCOUNTER` | 炼药猎 | 三层 | `Core/SilentBrewerScript.cs` |
| `fight SILENT_ENVENOM_BOSS_ENCOUNTER` | 涂毒猎 | 二层 | `Core/SilentPoisonScript.cs` |
| `fight NECROBINDER_SOUL_BOSS_ENCOUNTER` | 灵魂骨（灵魂绑定） | 二层 | `Core/SoulBoneScript.cs` |
| `fight REGENT_FORGE_BOSS_ENCOUNTER` | 铸剑储君 | 二层 | `Core/RegentScript.cs` |
| `fight SILENT_SHIV_BOSS_ENCOUNTER` | 刀猎 | 三层 | `Core/SilentScript.cs` |
| `fight DEFECT_AI_BOSS_ENCOUNTER` | 能力机 | 三层 | `Core/DefectScript.cs` |
| `fight NECROBINDER_ERADICATE_BOSS_ENCOUNTER` | 根除骨 | 三层 | `Core/NecrobinderAct3Script.cs` |
| `fight REGENT_COLORLESS_BOSS_ENCOUNTER` | 无色储君 | 三层 | `RegentScript.BuildAct3` |
| `fight REGENT_STAR_BOSS_ENCOUNTER` | 辉星储 | 三层 | `Core/RegentStarScript.cs` |
| `fight REGENT_KING_BOSS_ENCOUNTER` | **王储**（用户 2026-11-02；原名「征服者储君」） | 二层 | `Core/KingRegentScript.cs` |
| `fight IRONCLAD_BLOOD_BOSS_ENCOUNTER` | 放血战 | 二层 | `Core/BleedIroncladScript.cs` |
| `fight IRONCLAD_JUGGLER_BOSS_ENCOUNTER` | 杂耍战 | 二层 | `Core/JugglerIroncladScript.cs` |
| `fight IRONCLAD_BOULDER_BOSS_ENCOUNTER` | 巨石战 | 三层 | `Core/BoulderIroncladScript.cs` |
| `fight DEFECT_DARK_BOSS_ENCOUNTER` | 黑球机 | 一层 | `Core/DarkOrbBossScript.cs` |
| `fight SILENT_SNECKO_BOSS_ENCOUNTER` | **蛇眼猎**（用户 2026-11-03） | 二层 | `Core/SnakeEyesSilentScript.cs` |
| `fight NECROBINDER_OSTY_BOSS_ENCOUNTER` | **养手骨**（用户 2026-11-03） | 三层 | `Core/OstyKeeperScript.cs` |
| `fight DEFECT_EVENT_BOSS_ENCOUNTER` | **事件机**（用户 2026-11-03） | 三层 | `Core/EventMachineScript.cs` |

用词按用户口径（2026-10-23）：**灾厄 = Doom**、**涂毒 = Envenom**（见 `docs/plans/cards-silent.md`
里 `Envenom` 的中文就是「涂毒」）、灵魂骨只留 **Soul**（不加 Bone）。

按角色走、档位跟着当前幕的原版入口仍然可用（**现在只在一幕没有设计时才会被正常流程用到**）：

| 命令 | 打谁 |
|---|---|
| `fight IRONCLAD_BOSS_ENCOUNTER` | 铁甲战士（一层愤怒战；二/三层是占位起始卡组） |
| `fight SILENT_BOSS_ENCOUNTER` | 静默猎手（一层占位） |
| `fight DEFECT_BOSS_ENCOUNTER` | 故障机器人（三层能力机；一/二层占位） |
| `fight NECROBINDER_BOSS_ENCOUNTER` | 一二三层分别是灾厄骨 / 灵魂骨 / 根除骨 |
| `fight REGENT_BOSS_ENCOUNTER` | 一二三层分别是碎屑 / 铸剑 / 无色储君 |
| `fight *_NORMAL_ENCOUNTER` | 同上的「普通战斗房」版（调试开关用的那批，现在只有 5 个角色版） |

## 4. 改动清单（本轮）

| 文件 | 改了什么 |
|---|---|
| `src/Hooks/Core/BossTierPin.cs`（新） | 接口 `IBossTierPin { int? TierAct }` + 为什么需要它 |
| `src/Hooks/Encounters/TierBossEncounters.cs`（新） | 8 个钉死档位的遭遇类（铁甲/故障/亡灵×3/储君×3） |
| `src/Hooks/Encounters/CharBossEncounters.cs` | 基类实现 `IBossTierPin`（默认 `null`）；涂毒猎 `TierAct => 1`、刀猎 `=> 2`；`TierEncounters` 名册 + `IsOurs` 认它们 |
| `src/Hooks/Monsters/CharBossMonster.Descriptor.cs` | `ActIndex` 三档优先级：调试文件 → 遭遇钉子 → 真实幕数；`PinnedTier()` 缓存（问到「没有钉子」**不缓存**，因为生物构造期间 `CombatState` 会抛异常） |
| `content/localization/{zhs,eng}/encounters.json` | 8 个新 id 的 `.title` / `.loss` |
| `src/Hooks/Core/BuildInfo.cs` | 指纹 → **v0.1.12** |

**零回归**：这些遭遇**不进** `BossLineups` / `ForVanillaBoss`，正常流程仍然按幕数取不带钉子的
`*BossEncounter`；基类默认 `TierAct => null`，所以 `ActIndex` 对原有遭遇的行为一个字没变
（单人/多人都是）。

## 5. 备选：`charbosses_debug.txt`

`...\mods\CharacterBosses\`（和 DLL 同级），第一行 `角色[@幕数]`：

- 角色：`ironclad` / `silent` / `defect` / `necrobinder` / `regent` / `cycle`
- `@幕数`（1 起）：强制档位，例 `necrobinder@2`
- 效果：**第一幕的普通战斗**全换成该 Boss（普通战斗房，不推进剧情）

优先级：**调试文件的强制档位 > 遭遇钉子 > 真实幕数**。也就是说留着
`charbosses_debug.txt`（含 `@N`）时，`fight` 也会跟着用那个 N。
