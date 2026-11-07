# 把 A2「混合匹配」换成「Boss 选择」（按地图逐只勾选）

> 用户 2026-11-06 的需求原话：
> 「把设置中的 A2『混合匹配』换成 boss 选择（因为已经把变体之类的区分删了），
>   每个在 A1 开启 boss 替换地图都可以单独选择你想遇到的 boss
>   （每个角色至少选择 1 个共至少 5 个，3 层则至少每个角色选择 2 个共至少 10 个）。」
>
> 状态：**设计稿，待确认 3 个问题后开工**（见 §9）。

## 1. 现状

- `src/Hooks/Config/BossConfigModConfig.cs` 的 `SetupConfigUI` 按 A1 → A2 → A3 → A4 → A5 → A6 顺序建区块；
  A2 现在是 `BuildMixedMatchingSection`，只显示一行**锁定不可关**的「同幕混合匹配」。
- 每层候补来自 **静态表** `BossKeys.RosterFor(act)`：一层 7 / 二层 9 / 三层 11
  （一层 `SILENT_SLY, DEFECT_DARK, DEFECT_THUNDER, NECROBINDER_DOOM, REGENT_DEBRIS, REGENT_BOMBARD, IRONCLAD_ANGER` …）。
- 用到这张表的 4 处：
  | 位置 | 用途 |
  |---|---|
  | `BossLineups.Build`（第 254 行） | 按角色分桶、抽本场阵容（主 Boss + 搭子） |
  | `BossLineups.Fallback`（377） | 抽不出来时的保底 |
  | `BossLineups.AlternativeMainKey`（433） | 换主 Boss 时的候选池 |
  | `CharBossEncounters.cs:243` | 档位遭遇表的兜底 |
- A1 的地图开关：`BossConfig.MapInfo(Entry, DisplayName, Enabled, IsVanilla)`、`MapsByAct()`、
  `IsMapEnabledById(entry)` / `SetMapEnabled(entry, on)`；**原版四张图默认开、模组加的图默认关**。

## 2. 目标（按地图逐只勾选）

- A2 区块改成「**Boss 选择**」：对 **A1 里勾上的每一张地图**，单独给出一份「本幕候补 Boss」的勾选表。
- 勾选表按**角色分组**（铁甲 / 猎人 / 故障 / 亡灵 / 储君），每个角色列出它在**本幕**的所有档位。
- 校验（硬约束）：
  | 幕 | 现役档位数 | 每角色至少 | 全图至少 |
  |---|---|---|---|
  | 第一幕 | 7（战1 猎1 机2 骨1 储2） | 1 | **5** |
  | 第二幕 | 9（战2 猎2 机1 骨1 储3） | 1 | **5** |
  | 第三幕 | 11（战2 猎2 机3 骨2 储2） | **2** | **10** |
- 未在 A1 勾上的地图：不显示它的 Boss 选择（或显示为灰色「该地图未替换」）。

## 3. 数据格式（`charbosses_config.cfg`）

```json
"bossPicks": {
  "OVERGROWTH":  ["SILENT_SLY","DEFECT_DARK","DEFECT_THUNDER","NECROBINDER_DOOM","REGENT_DEBRIS","REGENT_BOMBARD","IRONCLAD_ANGER"],
  "UNDERDOCKS":  ["..."]
}
```

- **键 = 地图 id 的 Entry**（和 A1 用同一套 id），值 = 允许出现的 Key 数组。
- **缺省 = 全选**（旧配置文件没有这个字段时行为完全不变）。
- 只存「用户显式改过的地图」即可；没改过的地图不写、按全选处理（配置更干净）。

## 4. UI（A2 区块）

```
A2 · Boss 选择
勾上的地图可以单独决定「这一幕会遇到哪些 Boss」。每个角色至少要留 1 个（第三幕要留 2 个）。

[第一幕]
  ▸ 密林（OVERGROWTH）            已选 7/7
      铁甲战士   ☑愤怒战
      静默猎手   ☑奇巧猎
      故障机器人 ☑黑球机 ☑电球机
      亡灵契约师 ☑灾厄骨
      储君       ☑碎屑储君 ☑轰击储
  ▸ 暗港（UNDERDOCKS）            已选 7/7
      …（同一套候补，独立勾选）
[第二幕] …
[第三幕] … 已选 11/11（每角色至少 2 个）
```

- 复用现成的 `CheckBoxButton` / `RowLabel` / `InfoLabel` / `AddInlineRow`。
- 每张地图先给一行「已选 N/M」；**取消到「某角色 0 个」时直接拒绝并闪一行提示**
  （第三幕取消到「某角色 1 个」也拒绝），不做「保存时才发现」。
- 顶部一句总说明 + 一句「多人联机时以房主为准」。

## 5. 取值逻辑

新增 `Core/BossSelection`（新文件，约 80 行）：

```csharp
internal static class BossSelection
{
    // 当前幕「实际可用的候补」= BossKeys.RosterFor(act) ∩ 该地图的勾选
    internal static string[] RosterForAct(int act);
    internal static bool IsPicked(string? mapEntry, string key);
    internal static void SetPicked(string? mapEntry, string key, bool on);
    internal static bool CanUnpick(int act, string? mapEntry, string key);  // 校验「每角色最少 N 个」
    internal static int MinPerCharacter(int act) => act >= 2 ? 2 : 1;
    internal static void ResetToAll(string? mapEntry);
}
```

- `RosterForAct` 保证**结果非空**且**保持 `BossKeys.RosterFor` 的顺序**（顺序参与 `RankOf` 排序之外的分桶，别乱）；
  勾选为空 / 全部非法（跨版本改过 key）时**回退整张表**并打一行日志。
- 把上面 §1 表格里的 4 处 `BossKeys.RosterFor(act)` 换成 `BossSelection.RosterForAct(act)`。
  `CharBossEncounters.cs:243` 那处要看上下文（多半是「档位遭遇表的兜底顺序」）—— 若它要求全表就保持不动。

## 6. 多人（这是主要风险）

- 本模组现有的规则：**A1 / A4 这类「影响怎么抽 Boss」的设置以房主为准**，
  客人端不看自己的配置（`BossConfig.GameplaySettingsFollowHost`），改由**同步的遭遇**认 Boss。
- 但**阵容条数（Boss 数 = 玩家数）与搭子**是客人**本地算**的 ⇒ 两端的选择必须一致，否则又是不不同步。
- 因此实现时必须：
  1. 联机文档写清「**同一份玩法设置**（A1/A2/A4 一致）」；
  2. 继续用现有的 `阵容指纹` 行做对表（两端不一致时会打出来）；
  3. 客人端**不要**再自作主张回退整表 —— 若本机勾选与同步遭遇矛盾，打一行显式警告
     `⚠ 本机 Boss 选择与房主不一致（本机缺 KEY，已按遭遇继续）`。

## 7. 兼容 / 迁移

- `mixedMatching` 字段：**保留在结构里**（写盘仍写 true），只是 UI 不再显示 —— 免得旧版本读到缺字段。
- 旧配置没有 `bossPicks` ⇒ 全选（等价于现在）。
- Key 校验：遇到不认识的 Key（别的版本删掉的）直接忽略＋日志，不让它把整张表搞空。

## 8. 影响面

| 文件 | 改动 |
|---|---|
| `src/Hooks/Core/BossSelection.cs` | **新增**（取值 + 校验 + 配置读写） |
| `src/Hooks/Core/BossConfig.cs` | 加 `BossPicks` 字段（`Dictionary<string, List<string>>`）+ 保存 |
| `src/Hooks/Config/BossConfigModConfig.cs` | A2 区块重写（`BuildMixedMatchingSection` → `BuildBossPickSection`） |
| `src/Hooks/Core/BossLineups.cs` | 3 处 `RosterFor` → `BossSelection.RosterForAct` |
| `src/Hooks/Encounters/CharBossEncounters.cs` | 视上下文决定是否替换（第 243 行） |
| `docs/安装说明.txt` / `README.md` | A2 说明改写 |

## 9. 待你确认的 3 件事

1. **「每个地图各配一套」确认吗？** 同一幕的两张地图（密林 / 暗港）候补完全相同，
   按地图配就是**同样的表填两遍**。要不要改成「**按幕配一次，本幕所有地图共用**」？
   （更省事；但你说的是「每个地图单独选」，我按字面理解 = 按地图。）
2. **取消到不满足下限时**：直接**拦住不让点**（推荐，配一句提示），还是允许取消、在**开局时自动补上**该角色的第一个？
3. **多人**：A2 按现有惯例算「以房主为准的玩法设置」（要求两端配置一致，指纹对表）—— 确认可以吗？

确认后我按 §8 一次做完（含配置界面），编好部署到游戏目录给你验。
