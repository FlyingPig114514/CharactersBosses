# 变体级 Boss 归属 + 多人每场 Boss 数（2026-10-26 晚，v0.2.26）

> 用户原话（本次改动的唯一依据）：
> 「杀戮尖塔2每一层都有变体，比如暗港和密林就是1层的两个变体。两个变体的小怪、精英、boss 都是不同的。
> 所以本模组设计的 boss 也是有自己对应的变体的。截止目前，官方还没有更新二层和三层的变体，
> 但我们打算先把对应的 boss 做出来。目前，所有 boss 都还没有区分变体，默认属于密林、巢穴、荣耀，
> 单纯因为 boss 数量太少了。而为了支持更多人联机，我们需要更多的 boss，尤其是三层 boss，
> 因为进阶 10 三层会有两个 boss，需要的 boss 数量变成了人数*2。因此，我们先制作了三层另一个变体的 boss，
> 目前炼药猎就归属其中。由于 boss 数量太少，目前不同变体的 boss 混合匹配，即不管是哪一个变体的 boss
> 都有可能出现在当层的任意一个变体。所以，炼药猎也可能会作为三层荣耀的 boss，不过同样享受主 boss
> 角色不重复这个设置。在多人时，她不会与刀猎出现在同一场战斗。另外修改一下，让进阶 10 三层的两场
> boss 战中所有 boss 都不能相同，包括所有主 boss 和副 boss，但是可以出现同一角色 boss，
> 例如第一场中有刀猎，第二场中有炼药猎。目前三层的 6 个 boss 足以支持三人联机，适配一下三人联机。
> 炼药猎在联机时的排序和刀猎是一样的。3 个 boss 按左上、中下、右上排列，尽量错开。」

---

## 1. 核心概念：**Boss 标识 = 角色 + 变体**

以前阵容里的单位是「角色下标 0～4」，于是「刀猎 / 炼药猎」只能靠种子 50/50 二选一（同一个槽）。
现在改成 **Boss 标识（`BossKeys`）**：

| 幕 | 候补 Boss 标识 |
|---|---|
| 1 层（密林 / 暗港） | `IRONCLAD_ANGER`（愤怒战）、`NECROBINDER_DOOM`（灾厄骨）、`REGENT_DEBRIS`（碎屑储君） |
| 2 层（巢穴） | `SILENT_ENVENOM`（涂毒猎）、`DEFECT_CLAW`（物理机）、`NECROBINDER_SOUL`（灵魂骨）、`REGENT_FORGE`（铸剑储君） |
| 3 层（荣耀 + **另一个变体**） | `SILENT_SHIV`（刀猎）、`DEFECT_AI`（能力机）、`NECROBINDER_ERADICATE`（根除骨）、`REGENT_COLORLESS`（无色储君）、`IRONCLAD_VULN`（易伤战）、**`SILENT_BREWER`（炼药猎）** |

- Key 的命名规则 = 档位遭遇 id 去掉 `_ENCOUNTER` 与 `_BOSS`：
  `SILENT_BREWER_BOSS_ENCOUNTER` → `SILENT_BREWER`。所以**以后新增 Boss 只要加一条遭遇就自动对得上**
  （`BossKeys.KeyOfEncounter` 反查同一套规则）。
- 刀猎与炼药猎是**两个不同的 Boss**（同角色、不同变体），但都排在第三幕候补里
  ⇒ 混合匹配下炼药猎也可能当三层「荣耀」的 Boss（用户明确要的）。
- **等官方补齐变体**：把 `BossKeys.RosterFor` 按变体拆成两组即可，其余逻辑不用动。

## 2. 每场 Boss 数 = 玩家数（1～3）

`BossLineups.PlayersPerFight() = clamp(PlayerCount, 1, 3)`；阵容长度就是它。

- 单人：1 只（与改动前一样）；
- 双人：2 只（与改动前一样）；
- **三人：3 只**。第三幕 6 个 Boss ⇒ 3 只 × 2 场 = 6，刚好用满（用户算的正是这个）。
- 4 人暂不支持（按 3 处理），等 Boss 再多加。

## 3. 去重规则（`BossLineups.Build`）

1. **同一场里不出现同一角色的两个 Boss** —— 这条顺带保证了
   **刀猎与炼药猎不会同场**（用户要求）；也保证不会出现「两个亡灵」「两个储君」。
2. **进阶 10 第三幕的两场之间：Boss 标识完全不重复**（主/副都算），
   **但同一角色可以跨场出现**（第一场刀猎 + 第二场炼药猎 ✓）。
   实现：第二场从「第一场没用过的 Key」里挑。
3. **主 Boss 角色逐幕不重复**（旧规则，尽量保留）：能避开前几幕主 Boss 的角色就避开；
   人多了候补不够时**主动让步**给规则 1、2（否则三人两场凑不满 6 只）。
4. 全部只用「本局种子 + 幕 + 场 + Key」派生（`RankOf`）⇒ 同种子结果固定、**联机两端一致**。

## 4. 炼药猎的「哪套设计」判定（`BossLineups.BrewerActive`）

刀猎与炼药猎的怪物类型都是 `SilentBoss`，所以「这一只是哪套设计」必须显式判断。优先级：

1. 当前**遭遇** id（`SILENT_BREWER_BOSS_ENCOUNTER` / `SILENT_SHIV_BOSS_ENCOUNTER`）—— 主 Boss 自身最准；
2. 否则看**这一场的阵容**（`NoteCurrentFight` 记录的）：含 `SILENT_BREWER` ⇒ 炼药猎，含 `SILENT_SHIV` ⇒ 刀猎。
   这样「静默猎手作为**伙伴**上场」时也能判对（同场不会同时出现刀猎和炼药猎，答案唯一）；
3. 都拿不到才退回旧的种子 50/50。

## 5. 站位（`BossEnemySpreadPatch`）

- **2 只**：沿用原逻辑（左上 / 右下），一个像素没动；
- **≥3 只**（三人）：按用户要求 **左上 / 中下 / 右上** ——
  x = `minX - 150 / 中心 / maxX + 150`（`SpreadX*0.75`），
  y = 上排 `baseY - 91`、中下 `baseY + 91`（`SpreadY*0.65`），第 4 只起循环。
- 排序仍按「站位偏好 + 角色固定次序」，**炼药猎作为静默猎手与刀猎同序**（用户要求）。

## 6. 复测要点

```
[CharBoss] 第 3 层第 1 场阵容：（3 人局，进阶 10）[DEFECT_AI,SILENT_BREWER,REGENT_COLORLESS]
[CharBoss] 第 3 层第 2 场阵容：（3 人局，进阶 10）[SILENT_SHIV,NECROBINDER_ERADICATE,IRONCLAD_VULN]
[CharBoss] 多人(3 人)：这一幕同时上 3 只 Boss —— 我=DEFECT_AI，伙伴=SILENT_BREWER=… | REGENT_COLORLESS=…
[CharBoss] 多 Boss 错位（3 只，左上/中下/右上）：…(左上) → (x,y) | …(中下) → … | …(右上) → …
```

- 两场之间**没有任何 Boss 重复**；同角色跨场可以出现；
- 同一场里**不会出现两个静默猎手**（刀猎 vs 炼药猎）；
- 单人时阵容只有 1 只、站位逻辑完全不变（回归测试重点）。

## 7. 已知未验证 / 待补

- **三人联机没实机验证过**（本会话无法启动游戏）：重点看「两端阵容是否一致」「3 只是否都正常行动/选靶」；
- 4 人（Boss 数量不足）暂不支持；
- 变体拆分：等官方给出第二、三层变体后，把 `BossKeys.RosterFor` 按变体分组即可。

## 8. v0.2.27：炼药猎的进阶加成

用户 2026-10-26：「炼药猎进阶 8：升级中和。进阶 9：升级精确切击和拳斗」。

实现：`Core/SilentBrewerScript.cs` 新增小工具

```csharp
/// 进阶 minAscension 起这张牌是升级版
private static ScriptCard A<T>(int minAscension) where T : CardModel
    => new() { Card = typeof(T), From = "deck", UpgradeMinAscension = minAscension };
```

用法（`BossScriptRunner.BuildHand` 里 `entry.UpgradeMinAscension > 0 && AscensionOf(shadow) >= entry.UpgradeMinAscension` 生效）：

| 回合 | 牌 | 改成 |
|---|---|---|
| 1 | 精确切击 | `A<PreciseCut>(9)` |
| 2 | 中和（炼药后才喝瓶中船那张） | `A<Neutralize>(8)` |
| 4 | 拳斗 | `A<Fisticuffs>(9)` |
| 5 | 精确切击 | `A<PreciseCut>(9)` |
| 7 | 中和 | `A<Neutralize>(8)` |
| 8 | 拳斗 | `A<Fisticuffs>(9)` |

⚠ 为什么写在**剧本卡**上而不是 `BossDefinition.AscensionUpgrades`：后者是**按角色**的
（刀猎与炼药猎共用 `BossDefs.Silent`），写进去会连刀猎一起升级；剧本卡上的
`UpgradeMinAscension` 只作用于**她自己这一场的手牌**（手牌本来每回合都是剧本重建的）。
