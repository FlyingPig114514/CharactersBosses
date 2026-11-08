# 把 30 档 Boss 暴露给「斗蛐蛐」等外部工具

> 状态：**实现中**（2026-11-08）。来源：用户需求「不只是隐藏，还要能暴露出来、能自由操控档位；
> 斗蛐蛐（CricketFightMod）里要能按名字选中，并且选中的档位/设计必须精确生效」。

## 1. 问题

斗蛐蛐选人界面按 **怪物类名**（`MonsterModel` 的具体子类名 → `ModelId.Entry`）列出怪物，
并用类名作为 `last_teams.json` 里的 `MonsterId`。本模组每个角色只有 1 个怪物类
（`IroncladBoss` / `SilentBoss` / `DefectBoss` / `NecrobinderBoss` / `RegentBoss`），
所以斗蛐蛐里只能看到 5 个条目，30 档设计全部藏在「遭遇 + 幕数」的判定里。

即使把 5 个基类拆成 30 个子类，还有两个问题：

1. **幕数**：`CharBossMonster.ActIndex` 先问遭遇（`IBossTierPin`），斗蛐蛐的遭遇恒为
   `CricketEncounter`（不实现该接口）⇒ 只能退到「当前真实幕数」，第一幕就永远打一层配置。
2. **设计（变体）**：`BossDefinition.ActiveVariant` 靠 `BossVariant.Enabled()` 判定，
   而这些判定读的是**遭遇 entry**（`BossLineups.CurrentEncounterEntry`）；斗蛐蛐里遭遇不匹配
   ⇒ 退化成「本局种子 + 幕 + salt」随机 ⇒ 同一只怪每次进去可能是另一套设计。

## 2. 方案（改在本仓库里，提 PR）

新增 **30 个薄壳怪物类**（`src/Hooks/Monsters/TierBossMonsters.cs`），每个类：

- 继承对应角色的基类（`SilentBoss` 等），因此立绘 / 动画 / 出牌逻辑全部复用；
- `TierAct` 钉死幕数（0/1/2）；
- `PinnedVariantKey` 钉死设计 Key（`BossKeys.All` 里的那 30 个）；
- 构造时同时写 `SpawnVariantKey`（给脚本 / 统计 / 崩坠兼容沿用同一身份）。

配套改动：

| 文件 | 改动 |
|------|------|
| `src/Hooks/Core/BossDefinition.cs` | 新增 `PinnedVariantKey` + `WithVariantPin(key)`（`MemberwiseClone` 出副本，不动全局单例）；`ActiveVariant` 优先按 pin 选 |
| `src/Hooks/Monsters/CharBossMonster.Descriptor.cs` | `Def` 由抽象改为「`DefSource` + 按 pin 克隆并缓存」；新增 `virtual int? TierAct`、`virtual string? PinnedVariantKey`；`ActIndex` 先用 `TierAct` |
| `src/Hooks/Monsters/CharacterBosses.cs` | 5 个基类去 `sealed`、`Def` → `DefSource` |
| `content/localization/{zhs,eng}/monsters.json` | 30 个新 id 的 `.name` / `.title` / `.moves.CHARBOSS_PLAY.title` |

## 3. 对原版游戏的影响

- `ModelDb.Init` 会把 mod 程序集里所有具体 `AbstractModel` 子类实例化并注册，
  但 `ModelDb.Monsters` 只由 `Acts` / `EventEncounters` 反推 ⇒ **新类不挂遭遇就不进怪物库**，
  不会出现在正常流程、图鉴、Boss 池里。
- 斗蛐蛐自己遍历 `ModelDb.AllAbstractModelSubtypes`，所以照样看得见 30 个。
- 5 个基类行为不变（`DefSource` 直接返回原定义，pin 为 null）。

## 4. 验收

1. 编译通过（本地用 `-p:TargetFramework=net9.0`，游戏运行时是 .NET 9）。
2. 进游戏后 `mod_log.txt` 无异常，图鉴 / 正常流程 Boss 数量与改动前一致。
3. 斗蛐蛐选人界面能列出 35 个条目（5 基类 + 30 档），中文名正确。
4. 选 `SilentShivBoss` 开打 ⇒ 三层刀猎配置（不是一层）。
5. 选 `DefectEventBoss` 开打 ⇒ 事件机（不是能力机），与种子无关，重复开局结果一致。
