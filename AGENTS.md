# AGENTS.md - AI Agent 规范

本文件是 AI Agent（如 OpenCode）在此脚手架项目中的工作规范。

**在开始开发之前，必须先阅读 [`rules.md`](rules.md)。**

## 项目概览

这是一个 **Slay the Spire 2 Mod 脚手架**，使用 C# + Harmony 进行 Mod 开发。

- **开发规范**: `rules.md` / `rules_en.md` — 必读，约束所有开发行为
- **入口文件**: `src/ModEntry.cs` — 禁止修改
- **Hook 文件**: `src/Hooks/` 目录 — 按功能模块拆分文件
- **清单文件**: `src/com.vibecoding.sts2mod.json` — Mod 元数据（id、名称、版本等）
- **项目文件**: `src/Sts2ModScaffold.csproj` — .NET 10 类库

## 执行流程

### 0. 初始化 Mod 信息（首次必须执行）

**每个新 Mod 的第一步，必须先修改以下文件中的 Mod 身份信息：**

1. `src/com.vibecoding.sts2mod.json` — 修改 `id`（必须与文件夹名/DLL名一致）、`name`、`author`、`description`、`pck_name`
2. `src/ModEntry.cs` — 修改 `ModId`（与 manifest 的 `id` 一致）和 `ModName` 常量
3. 运行 `tools/rename-scaffold.ps1 -NewModName "你的Mod名"` 自动重命名 csproj、namespace、manifest 文件

**规则：**
- `id` 必须与文件夹名、DLL文件名、PCK文件名一致（详见 `rules.md`）
- `ModEntry.cs` 中的 `ModId`（Harmony 补丁 ID）必须与 manifest 的 `id` 一致
- 如果不执行此步骤，Mod 将以 "Sts2ModScaffold" 身份加载，可能与其他 Mod 冲突

### 1. 理解需求

当用户描述一个 Mod 需求时：
1. 使用 **ILSpy MCP** 反编译 `sts2.dll`，找到目标 Hook 的位置和签名
   - `decompile_type`: 查看类结构
   - `decompile_method`: 查看方法签名
   - **不要猜测签名，必须用 MCP 验证**
2. 查阅 `Modding-Tutorial/` 目录中的示例
3. 在 `docs/plans/` 下创建实现计划

### 2. 制定计划

**必须先在 `docs/plans/` 下创建计划文件，再编写代码。**

文件命名：`docs/plans/[功能名].md`

### 3. 编写代码

1. 编辑 `src/Hooks/` 下的 Hook 文件
2. 代码必须与计划一一对应
3. 使用 `Logger.Log()` 记录关键操作

### 4. 代码 Review

编写完代码后，逐条对照计划检查：
- [ ] 每个计划中的 Hook 都已实现
- [ ] 方法签名与 ILSpy MCP 反编译结果一致
- [ ] 代码逻辑与计划一致

### 5. 编译与安装

```powershell
install-mod.bat
```

### 6. 启动游戏并自动测试

**Agent 必须自行完成全部测试流程，使用脚本和 STS2MenuControl + STS2MCP 操作游戏。**

**前置条件：Steam 必须已启动并登录，否则游戏无法启动。启动前先检查 Steam 状态。**

#### 6.1 关闭已运行的游戏

```bash
powershell -ExecutionPolicy Bypass -File "tools\close_sts2.ps1"
```

#### 6.2 清除旧日志

```bash
powershell -Command "Remove-Item \"$env:APPDATA\SlayTheSpire2\logs\mod_log.txt\" -Force -ErrorAction SilentlyContinue; Remove-Item \"$env:APPDATA\SlayTheSpire2\logs\godot.log\" -Force -ErrorAction SilentlyContinue"
```

#### 6.3 启动游戏

**必须使用脚本启动游戏：**

```bash
powershell -ExecutionPolicy Bypass -File "tools\launch_sts2.ps1"
```

#### 6.4 等待游戏启动

```bash
powershell -ExecutionPolicy Bypass -File "tools\wait_sts2.ps1"
```

#### 6.5 使用 STS2MenuControl 控制主菜单

HTTP API on `localhost:8081`：

```bash
# 打开角色选择（单人）
curl -s -X POST http://localhost:8081/api/v1/menu -H "Content-Type: application/json" -d "{\"action\":\"open_character_select\"}"

# 选择角色
curl -s -X POST http://localhost:8081/api/v1/menu -H "Content-Type: application/json" -d "{\"action\":\"select_character\",\"option_index\":0}"

# 开始游戏
curl -s -X POST http://localhost:8081/api/v1/menu -H "Content-Type: application/json" -d "{\"action\":\"embark\"}"

# 创建多人房间（LAN）
curl -s -X POST http://localhost:8081/api/v1/menu -H "Content-Type: application/json" -d "{\"action\":\"open_multiplayer_host\",\"mode\":\"lan\",\"max_players\":4}"

# 标记准备就绪
curl -s -X POST http://localhost:8081/api/v1/menu -H "Content-Type: application/json" -d "{\"action\":\"set_ready\"}"
```

#### 6.6 使用 STS2MCP 操作游戏内

HTTP API on `localhost:15526`：

```bash
# 获取游戏状态
curl -s http://localhost:15526/api/v1/singleplayer

# 出牌
curl -s -X POST http://localhost:15526/api/v1/singleplayer -H "Content-Type: application/json" -d "{\"action\":\"combat_play_card\",\"card_index\":0,\"target\":\"ENEMY_ID\"}"

# 结束回合
curl -s -X POST http://localhost:15526/api/v1/singleplayer -H "Content-Type: application/json" -d "{\"action\":\"combat_end_turn\"}"

# 选择地图节点
curl -s -X POST http://localhost:15526/api/v1/singleplayer -H "Content-Type: application/json" -d "{\"action\":\"choose_map_node\",\"index\":0}"
```

#### 6.7 关闭游戏

```bash
powershell -ExecutionPolicy Bypass -File "tools\close_sts2.ps1"
```

等待进程完全退出（最多 10 秒）。

### 7. 检查日志确认 Mod 加载成功

```bash
powershell -ExecutionPolicy Bypass -File "tools\read_sts2_logs.ps1"
```

需要确认两点：
1. **godot.log** 中有 `Finished mod initialization for` — Mod 被加载
2. **mod_log.txt** 中有 `[Hook]` 开头的日志 — Hook 被触发

## MCP 工具使用

### ILSpy MCP — 代码反编译

用于理解游戏内部实现和验证 Hook 签名。**不要猜测签名，必须用 MCP 验证。**

所有工具都需要传 `assemblyPath` 参数，指向 `references/sts2.dll`。

- `decompile_type(assembly_path, type_name)` — 查看类结构
- `decompile_method(assembly_path, type_name, method_name)` — 查看具体方法签名
- `get_type_members(assembly_path, type_name)` — 快速查看成员列表
- `list_assembly_types(assembly_path)` — 列出程序集中的所有类型

### STS2MenuControl — 主菜单控制（端口 8081）

> 独立仓库：https://github.com/L4ntern0/STS2-MenuControl（install 时从 GitHub 克隆）

用于在主菜单中操作（角色选择、开始游戏、多人模式等）。**Agent 必须使用此 API 进行主菜单操作。**

| Action | 说明 |
|--------|------|
| `open_character_select` | 打开角色选择（单人模式） |
| `open_multiplayer_host` | 创建多人房间 |
| `select_character` | 选择角色 |
| `embark` | 开始新游戏 |
| `continue_run` | 继续存档 |
| `abandon_run` | 放弃存档 |
| `open_timeline` | 打开时间线 |
| `set_ready` / `set_unready` | 多人准备状态 |
| `get_lobby_status` | 查询房间信息 |
| `confirm_modal` / `dismiss_modal` | 弹窗交互 |
| `close_main_menu_submenu` | 关闭子菜单 |
| `return_to_main_menu` | 返回主菜单 |

### STS2MCP — 游戏内控制（端口 15526）

用于游戏中操作（战斗、地图、事件等）。**Agent 必须使用此 API 进行游戏内操作。**

| Action | 说明 |
|--------|------|
| `combat_play_card` | 出牌（需要 card_index，可能需要 target） |
| `combat_end_turn` | 结束回合 |
| `choose_map_node` | 选择地图节点 |
| `choose_event_option` | 选择事件选项 |
| `proceed` | 继续前进（奖励/事件后） |
| `select_card` / `confirm_selection` | 卡牌选择 |
| `skip_card_reward` | 跳过卡牌奖励 |
| `cancel_selection` | 取消选择 |
| `rewards_claim` | 领取奖励 |
| `rest_choose_option` | 休息站选项 |
| `shop_purchase` | 商店购买 |
| `deck_select_card` / `deck_confirm_selection` | 卡组编辑 |

## 文件规则

- **只修改** `src/Hooks/` 目录（和新建的计划文件）
- **首次使用时修改** `src/com.vibecoding.sts2mod.json`、`src/ModEntry.cs` 中的 Mod 身份信息
- **禁止修改** `src/ModEntry.cs` 的代码逻辑（Initialize 方法和 Logger）
- **禁止修改** `src/Sts2ModScaffold.csproj`
- **Mod 清单** 在 `src/*.json`
- **项目代码** 全部在 `src/` 目录下

## 编译与安装

### 首次安装（环境配置）

```powershell
.\install.bat
```

自动安装 .NET 10 SDK、.NET 8 运行时、Godot、ILSpy、STS2MCP、STS2MenuControl，配置 MCP 服务器。只需运行一次。

### 日常开发

```powershell
install-mod.bat
```

重新构建所有 Mod（脚手架 + STS2MenuControl）并安装到游戏。可加参数：`install-mod.bat "游戏路径"`

### 卸载

```powershell
uninstall-mod.bat
```

从游戏目录移除脚手架 Mod、STS2MCP 和 STS2MenuControl。

### 辅助脚本

| 脚本 | 用途 |
|------|------|
| `tools/rename-scaffold.ps1` | 重命名脚手架（自动修改 csproj、namespace、manifest） |
| `tools/launch_sts2.ps1` | 启动游戏 |
| `tools/close_sts2.ps1` | 关闭游戏 |
| `tools/wait_sts2.ps1` | 等待游戏启动（轮询检测） |
| `tools/read_sts2_logs.ps1` | 查看日志 |

## Mod 文件结构

游戏加载 Mod 时的结构要求见 [`rules.md`](rules.md) 规则4。

## 错误排查

### Mod 未加载
- 检查 `godot.log` 中的 `ERROR` 和 `WARNING`
- 确认清单中的 `id` 与文件夹名、DLL文件名一致
- 确认 `has_dll: true` 和 `has_pck: true` 已设置
- 确认 `mod_manifest.json` 包含 `pck_name` 字段
- 确认 DLL 和 PCK 文件都在游戏 `mods/` 目录的子文件夹下
- 注意：首次启动新 Mod 时游戏可能提示 "mods warning"，需在游戏中确认后才加载

### Hook 不生效
- 用 ILSpy MCP 验证方法签名是否匹配
- 检查 `[HarmonyPatch]` 属性是否正确
- 检查 `Logger.Log()` 是否在 `mod_log.txt` 中出现（确认 Hook 被调用）

### 编译失败
- 确认已运行过 `install.bat`（自动安装 .NET 10 SDK）
- 检查 `references/` 下是否有 `sts2.dll`、`0Harmony.dll`、`GodotSharp.dll`
- 检查 csproj 中的 `GameDir` 路径是否指向正确的游戏目录

<!-- dsh-openwolf:start -->
# Code Map
Generated 2026-10-22T09:53:49.174Z · 231 files · 40065 lines · 0.16s

## ./
- `HOWTO-new-mod.md` — 107 lines · 这个目录是**复用资产库**，不是可以直接开工的项目根 —— 里面的 `src/` 是原始脚手架
- `LICENSE` — 21 lines · MIT License
- `README.md` — 251 lines · 零配置杀戮尖塔 2 Mod 开发脚手架，支持 AI 辅助的 Vibe Coding。克隆、运行 `install.bat`、开始编码。
- `README_en.md` — 250 lines · A zero-config scaffold for Slay the Spire 2 mod development with AI-assisted vibe coding. Clone, run `install.bat`, start coding.
- `install-mod.bat` — 142 lines · @echo off
- `install.bat` — 2 lines · @echo off
- `install.ps1` — 972 lines · [CmdletBinding()]
- `rules.md` — 245 lines · ```
- `rules_en.md` — 245 lines · ```
- `uninstall-mod.bat` — 96 lines · @echo off
- `屏幕录制 2026-10-18 112548.mp4` — 0 lines · [file too large]
- `屏幕录制 2026-10-18 134739.mp4` — 0 lines · [file too large]
- `屏幕录制 2026-10-18 161703.mp4` — 0 lines · [file too large]
- `屏幕录制 2026-10-18 162713.mp4` — 0 lines · [file too large]
- `屏幕录制 2026-10-18 165829.mp4` — 0 lines · [file too large]
- `屏幕录制 2026-10-18 180914.mp4` — 0 lines · [file too large]
- `屏幕录制 2026-10-18 182005.mp4` — 0 lines · [file too large]
- `屏幕录制 2026-10-18 220604.mp4` — 0 lines · [file too large]
- `屏幕录制 2026-10-18 221041.mp4` — 0 lines · [file too large]
- `屏幕录制 2026-10-19 094626.mp4` — 0 lines · [file too large]
- `屏幕录制 2026-10-19 221748.mp4` — 0 lines · [file too large]
- `屏幕录制 2026-10-19 224149.mp4` — 0 lines · [file too large]
- `屏幕录制 2026-10-22 124807.mp4` — 0 lines · [file too large]

## content/localization/eng
- `content/localization/eng/encounters.json` — 21 lines · {
- `content/localization/eng/intents.json` — 4 lines · {
- `content/localization/eng/monsters.json` — 19 lines · {
- `content/localization/eng/powers.json` — 20 lines · {
- `content/localization/eng/relics.json` — 6 lines · {

## content/localization/zhs
- `content/localization/zhs/encounters.json` — 22 lines · {
- `content/localization/zhs/intents.json` — 4 lines · {
- `content/localization/zhs/monsters.json` — 19 lines · {
- `content/localization/zhs/powers.json` — 20 lines · {
- `content/localization/zhs/relics.json` — 6 lines · {

## docs/plans
- `docs/plans/act3-boss-eradicate-bone.md` — 82 lines · > 状态：**待实现**。用户设计，逐字要点见 §1；ID 已核对（§2）。
- `docs/plans/boss-silent-bladedancer.md` — 109 lines · > 用户设计。前两个 Boss：灾厄骨（亡灵契约师，第一幕）、能力机（故障机器人，第三幕）。
- `docs/plans/boss-silent-poisoner.md` — 100 lines · > 用户设计。当前**只做特殊效果**，卡组 / 遗物 / 意图尚未设计（先用占位卡组）。
- `docs/plans/cards-colorless.md` — 84 lines · > 自动生成。任何角色都能往卡组里塞无色牌。
- `docs/plans/cards-defect.md` — 146 lines · > 自动生成（`tools\gen-card-lists.ps1`）：费用/类型/稀有度/数值来自 `sts2.dll` 反编译，名字/效果来自游戏官方中文本地化。
- `docs/plans/cards-ironclad.md` — 144 lines · > 自动生成（`tools\gen-card-lists.ps1`）：费用/类型/稀有度/数值来自 `sts2.dll` 反编译，名字/效果来自游戏官方中文本地化。
- `docs/plans/cards-necrobinder.md` — 146 lines · > 自动生成（`tools\gen-card-lists.ps1`）：费用/类型/稀有度/数值来自 `sts2.dll` 反编译，名字/效果来自游戏官方中文本地化。
- `docs/plans/cards-regent.md` — 146 lines · > 自动生成（`tools\gen-card-lists.ps1`）：费用/类型/稀有度/数值来自 `sts2.dll` 反编译，名字/效果来自游戏官方中文本地化。
- `docs/plans/cards-silent.md` — 146 lines · > 自动生成（`tools\gen-card-lists.ps1`）：费用/类型/稀有度/数值来自 `sts2.dll` 反编译，名字/效果来自游戏官方中文本地化。
- `docs/plans/charbosses.md` — 0 lines · [file too large]
- `docs/plans/game-object-lookup.tsv` — 1074 lines · kind	id	name	owner	mod
- `docs/plans/handover-2026-10-17.md` — 234 lines · > 写于 2026-10-17 深夜。作者交接给下一个会话/下一个人。
- `docs/plans/handover-multiplayer.md` — 75 lines · > 用途：换新对话时先读本文件 + `docs/plans/multiplayer-lockon.md` + `docs/plans/multiplayer-cards.md`。
- `docs/plans/handover-osty-facing.md` — 196 lines · > 写给**下一个会话**。这份文件是为了让接手者**不要再重走已经试过的六条死路**。
- `docs/plans/handover-regent-boss.LATEST.md` — 78 lines · > 给下一个会话：先读完这一页再动手。所有结论都是本会话**实机验证过**的（`mod_log.txt` 有日志行）。
- `docs/plans/handover-regent-boss.md` — 137 lines · > 给下一个会话：先读完这一页再动手。所有结论都是本会话**实机验证过**的（`mod_log.txt` 有日志行）。
- `docs/plans/multiplayer-cards.md` — 74 lines · > 状态：**设计已定，代码未实现**。用户 2026-10-22 口头给出三条，本文件是唯一权威记录。
- `docs/plans/multiplayer-lockon.md` — 147 lines · > 状态：**阶段 1 代码已落地，待实机验证**（2026-10-22 更新）。阶段 2～4 未开始。
- `docs/plans/osty-facing-body.md` — 115 lines · > 承接 `docs/plans/handover-osty-facing.md`。目标是**先把证据摆齐，再一次只改一处**。
- `docs/plans/osty-fix-2026-10-20.md` — 41 lines · > 这一整串症状的**唯一根因**是：`src/Hooks/Core/BossOsty.cs` 的 `DropDeadPets()` 里
- `docs/plans/recon-card-play.md` — 1251 lines · presentation delay AFTER the player commits a card but BEFORE effects (damage/powers) resolve**.
- `docs/plans/recon-charboss-sts2.md` — 0 lines · [file too large]
- `docs/plans/recon-combat-ui.md` — 1211 lines · 目标 Mod 需求：Darkest Dungeon 风格「行动演出」——找到每个 `Creature` 的屏幕节点、读取屏幕坐标/尺寸、遮挡/高亮任意生物、在角落画 HUD 横幅与完整卡牌、可选缩放/平移镜头。
- `docs/plans/recon-damage-powers.md` — 0 lines · [file too large]
- `docs/plans/recon-downfall-charbosses.md` — 0 lines · [file too large]
- `docs/plans/recon-monster-intents.md` — 1264 lines · 目标：为 Harmony Mod 定位「特定 BOSS 使用特定大招时、在效果结算**之前**播放过场动画」的 Hook 点，
- `docs/plans/recon-tooling.md` — 33 lines · 因为当前会话没有 ILSpy MCP 工具绑定，用 `tools/ilspy-cli` 代替。
- `docs/plans/relics-shared.md` — 125 lines · > 自动生成。这些遗物不属于某个角色，原版里任何角色开箱/商店都可能拿到，Boss 也可以配。
- `docs/plans/workshop-upload.md` — 98 lines · 上传器：`sts2-mod-uploader-main/`（独立仓库，`dotnet run -- upload <工作区目录>`）。

## docs/plans/_recon
- `docs/plans/_recon/NGame-members.txt` — 141 lines · M public System.Void _EnterTree()
- `docs/plans/_recon/NRun-members.txt` — 36 lines · M public static MegaCrit.Sts2.Core.Nodes.NRun Create(MegaCrit.Sts2.Core.Runs.RunState state)
- `docs/plans/_recon/TMegaCrit__Sts2__Core__Nodes__Cards__NCard.txt` — 0 lines · [file too large]
- `docs/plans/_recon/TMegaCrit__Sts2__Core__Nodes__Combat__NCombatUi.txt` — 1480 lines · namespace MegaCrit.Sts2.Core.Nodes.Combat;
- `docs/plans/_recon/TMegaCrit__Sts2__Core__Nodes__Combat__NCreature.txt` — 0 lines · [file too large]
- `docs/plans/_recon/TMegaCrit__Sts2__Core__Nodes__Combat__NCreatureStateDisplay.txt` — 726 lines · namespace MegaCrit.Sts2.Core.Nodes.Combat;
- `docs/plans/_recon/TMegaCrit__Sts2__Core__Nodes__Combat__NCreatureVisuals.txt` — 879 lines · namespace MegaCrit.Sts2.Core.Nodes.Combat;
- `docs/plans/_recon/TMegaCrit__Sts2__Core__Nodes__Combat__NHealthBar.txt` — 1143 lines · namespace MegaCrit.Sts2.Core.Nodes.Combat;
- `docs/plans/_recon/TMegaCrit__Sts2__Core__Nodes__Combat__NIntent.txt` — 594 lines · namespace MegaCrit.Sts2.Core.Nodes.Combat;
- `docs/plans/_recon/TMegaCrit__Sts2__Core__Nodes__Combat__NPlayerHand.txt` — 0 lines · [file too large]
- `docs/plans/_recon/TMegaCrit__Sts2__Core__Nodes__Combat__NSelectionReticle.txt` — 225 lines · namespace MegaCrit.Sts2.Core.Nodes.Combat;
- `docs/plans/_recon/TMegaCrit__Sts2__Core__Nodes__Combat__NTargetManager.txt` — 1127 lines · namespace MegaCrit.Sts2.Core.Nodes.Combat;
- `docs/plans/_recon/TMegaCrit__Sts2__Core__Nodes__Rooms__NCombatRoom.txt` — 1671 lines · namespace MegaCrit.Sts2.Core.Nodes.Rooms;
- `docs/plans/_recon/TMegaCrit__Sts2__Core__Nodes__Screens__Overlays__IOverlayScreen.txt` — 21 lines · namespace MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
- `docs/plans/_recon/TMegaCrit__Sts2__Core__Nodes__Screens__Overlays__NOverlayStack.txt` — 498 lines · namespace MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
- `docs/plans/_recon/TMegaCrit__Sts2__Core__Nodes__Vfx__NDamageNumVfx.txt` — 413 lines · namespace MegaCrit.Sts2.Core.Nodes.Vfx;
- `docs/plans/_recon/Tanimator.txt` — 225 lines · namespace MegaCrit.Sts2.Core.Animation;
- `docs/plans/_recon/Tanimstate.txt` — 193 lines · namespace MegaCrit.Sts2.Core.Animation;
- `docs/plans/_recon/Tattackcommand.txt` — 1143 lines · namespace MegaCrit.Sts2.Core.Commands.Builders;
- `docs/plans/_recon/Tattackcontext.txt` — 216 lines · namespace MegaCrit.Sts2.Core.Commands.Builders;
- `docs/plans/_recon/Tcombatmanager.txt` — 0 lines · [file too large]
- `docs/plans/_recon/Tcommonbanner.txt` — 311 lines · namespace MegaCrit.Sts2.Core.Nodes.CommonUi;
- `docs/plans/_recon/Tcreaturecmd.txt` — 0 lines · [file too large]
- `docs/plans/_recon/Tglobalui.txt` — 628 lines · namespace MegaCrit.Sts2.Core.Nodes.CommonUi;
- `docs/plans/_recon/Tngame.txt` — 0 lines · [file too large]
- `docs/plans/_recon/Tplayerbanner.txt` — 342 lines · namespace MegaCrit.Sts2.Core.Nodes.Combat;
- `docs/plans/_recon/anim-types.txt` — 5 lines · MegaCrit.Sts2.Core.Animation.AnimState
- `docs/plans/_recon/camera-types.txt` — 0 lines
- `docs/plans/_recon/cards-types.txt` — 8 lines · MegaCrit.Sts2.Core.Nodes.Cards.NCard
- `docs/plans/_recon/combat-core-types.txt` — 13 lines · MegaCrit.Sts2.Core.Combat.CombatId
- `docs/plans/_recon/combat-types.txt` — 34 lines · MegaCrit.Sts2.Core.Nodes.Combat.NCardPlay
- `docs/plans/_recon/nodes-all.txt` — 0 lines · [file too large]
- `docs/plans/_recon/overlays-types.txt` — 6 lines · MegaCrit.Sts2.Core.Nodes.Screens.Overlays.IOverlayScreen
- `docs/plans/_recon/rooms-types.txt` — 10 lines · MegaCrit.Sts2.Core.Nodes.Rooms.IRoomWithProceedButton
- `docs/plans/_recon/search-cam-lower.txt` — 21 lines · F static System.Text.Json.JsonEncodedText PropName_campfireUpgrades
- `docs/plans/_recon/search-camera.txt` — 24 lines · M public virtual System.Single GetCameraScaling()

## src
- `src/CharacterBosses.csproj` — 30 lines · <Project Sdk="Microsoft.NET.Sdk">
- `src/CharacterBosses.json` — 11 lines · Replaces each act boss with a vanilla playable character that fights you with its own deck and relics.
- `src/ModEntry.cs` — 38 lines · ModEntry, Logger · namespace CharacterBosses;

## src/Hooks/Core
- `src/Hooks/Core/BladesGrowth.cs` — 222 lines · BladesGrowth · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossActIndex.cs` — 62 lines · BossActIndex · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossCardSelector.cs` — 105 lines · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossDamagePredictor.cs` — 255 lines · BossDamagePredictor · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossDefValidator.cs` — 121 lines · BossDefValidator · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossDefinition.cs` — 150 lines · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossDefs.cs` — 330 lines · BossDefs · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossHookBridge.SideTurn.cs` — 370 lines · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossHookBridge.cs` — 165 lines · GetTurnNumber, Snapshot · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossIntentBuilder.cs` — 409 lines · BossIntentBuilder · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossLoadout.cs` — 170 lines · BossLoadout · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossLockOn.cs` — 334 lines · BossLockOn · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossOrbMath.cs` — 126 lines · BossOrbMath · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossOrbSim.cs` — 249 lines · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossOsty.cs` — 1115 lines · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossPiles.cs` — 64 lines · BossPiles · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossPlanner.cs` — 201 lines · BossPlanner · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossRunStats.cs` — 41 lines · BossRunStats · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossScript.cs` — 425 lines · BossScriptRunner · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossScriptPlanner.cs` — 358 lines · BossScriptPlanner, Matches · namespace CharacterBosses.Core;
- `src/Hooks/Core/BossShadowPets.cs` — 468 lines · BossShadowPets · namespace CharacterBosses.Core;
- `src/Hooks/Core/DebrisRegentScript.cs` — 98 lines · DebrisRegentScript · namespace CharacterBosses.Core;
- `src/Hooks/Core/DebugMode.cs` — 123 lines · DebugMode · namespace CharacterBosses.Core;
- `src/Hooks/Core/DefectScript.cs` — 102 lines · DefectScript · namespace CharacterBosses.Core;
- `src/Hooks/Core/MultiBossPlan.cs` — 153 lines · MultiBossPlan, EncounterRunStateCapturePatch · namespace CharacterBosses.Core;
- `src/Hooks/Core/MultiplayerCardSwap.cs` — 64 lines · MultiplayerCardSwap · namespace CharacterBosses.Core;
- `src/Hooks/Core/NecrobinderAct3Script.cs` — 91 lines · NecrobinderAct3Script · namespace CharacterBosses.Core;
- `src/Hooks/Core/NecrobinderScript.cs` — 107 lines · NecrobinderScript · namespace CharacterBosses.Core;
- `src/Hooks/Core/NeowBlessingToggle.cs` — 39 lines · NeowBlessingToggle · namespace CharacterBosses.Core;
- `src/Hooks/Core/RawAttackIntent.cs` — 81 lines · namespace CharacterBosses.Core;
- `src/Hooks/Core/RegentScript.cs` — 219 lines · RegentScript · namespace CharacterBosses.Core;
- `src/Hooks/Core/SacrificeIntent.cs` — 33 lines · namespace CharacterBosses.Core;
- `src/Hooks/Core/ShadowPlayerFactory.cs` — 199 lines · ShadowPlayerFactory · namespace CharacterBosses.Core;
- `src/Hooks/Core/ShivPlays.cs` — 96 lines · ShivPlays · namespace CharacterBosses.Core;
- `src/Hooks/Core/SilentBlades.cs` — 118 lines · SilentBlades · namespace CharacterBosses.Core;
- `src/Hooks/Core/SilentPoisonScript.cs` — 107 lines · SilentPoisonScript · namespace CharacterBosses.Core;
- `src/Hooks/Core/SilentScript.cs` — 112 lines · SilentScript · namespace CharacterBosses.Core;

## src/Hooks/Encounters
- `src/Hooks/Encounters/CharBossEncounters.cs` — 331 lines · IroncladBossEncounter, SilentBossEncounter, SilentPoisonBossEncounter, SilentShivBossEncounter, DefectBossEncounter, NecrobinderBossEncounter · namespace CharacterBosses.Encounters;

## src/Hooks/Monsters
- `src/Hooks/Monsters/BossThinkState.cs` — 30 lines · namespace CharacterBosses.Monsters;
- `src/Hooks/Monsters/BossVisuals.cs` — 85 lines · BossVisuals · namespace CharacterBosses.Monsters;
- `src/Hooks/Monsters/CharBossMonster.Descriptor.cs` — 90 lines · BeforeRemovedFromRoom · namespace CharacterBosses.Monsters;
- `src/Hooks/Monsters/CharBossMonster.Turn.cs` — 350 lines · IsChargeBattery · namespace CharacterBosses.Monsters;
- `src/Hooks/Monsters/CharBossMonster.cs` — 626 lines · namespace CharacterBosses.Monsters;
- `src/Hooks/Monsters/CharacterBosses.cs` — 37 lines · namespace CharacterBosses.Monsters;

## src/Hooks/Patches
- `src/Hooks/Patches/BossEnemySpreadPatch.cs` — 80 lines · BossEnemySpreadPatch · namespace CharacterBosses.Patches;
- `src/Hooks/Patches/BossFrameTickPatch.cs` — 376 lines · BossFrameTickStarPatch, BossFrameTickEnergyPatch, BossBladeHoverPositionPatch, BossFrameTickBladePatch, BossFrameTick · namespace CharacterBosses.Patches;
- `src/Hooks/Patches/BossHandUiPatches.cs` — 34 lines · ClearHandUiOnCombatEndPatch, ClearHandUiOnCombatVictoryPatch · namespace CharacterBosses.Patches;
- `src/Hooks/Patches/BossMultiplayerHpPatch.cs` — 39 lines · BossNoMultiplayerHpScalePatch · namespace CharacterBosses.Patches;
- `src/Hooks/Patches/BossReplacementPatches.cs` — 85 lines · PullNextEncounterPatch · namespace CharacterBosses.Patches;
- `src/Hooks/Patches/BossSlotPatches.cs` — 131 lines · BossSlotAssignmentPatch, BossSlotGenerateRoomsPatch, BossSlotValidateAfterLoadPatch, SecondBossEncounterGetPatch · namespace CharacterBosses.Patches;
- `src/Hooks/Patches/BossSpecialPowerIconPatch.cs` — 53 lines · BossSpecialPowerIconPatch · namespace CharacterBosses.Patches;
- `src/Hooks/Patches/CharbossIsolationPatches.cs` — 37 lines · ShadowPlayerIsolationPatch · namespace CharacterBosses.Patches;
- `src/Hooks/Patches/CombatContainsSafePatch.cs` — 47 lines · CombatContainsSafePatch · namespace CharacterBosses.Patches;
- `src/Hooks/Patches/CombatSidePatches.cs` — 286 lines · HittableEnemiesSidePatch, ShadowPetNotHittablePatch, ShadowPetNeverHittablePatch, ShadowPetIsSecondaryEnemyPatch, ShadowPetNotPrimaryEnemyPatch, ShadowPetNotAnOpponentPatch · namespace CharacterBosses.Patches;
- `src/Hooks/Patches/CometDebrisPatch.cs` — 142 lines · CometDebrisRedirect, MakeItSoNoReturnPatch · namespace CharacterBosses.Patches;
- `src/Hooks/Patches/CrackedCoreOncePatch.cs` — 53 lines · CrackedCoreOncePatch · namespace CharacterBosses.Patches;
- `src/Hooks/Patches/FencingManualOncePatch.cs` — 52 lines · FencingManualForgeOncePatch · namespace CharacterBosses.Patches;
- `src/Hooks/Patches/ModRelicPoolPatch.cs` — 43 lines · ModRelicPoolPatch · namespace CharacterBosses.Patches;
- `src/Hooks/Patches/OrbLocalVisualPatch.cs` — 64 lines · OrbAddSlotLocalPatch, OrbAddOrbLocalPatch, OrbEvokeLocalPatch, OrbStateChangedLocalPatch · namespace CharacterBosses.Patches;
- `src/Hooks/Patches/OstyNoCombatStartSummonPatch.cs` — 65 lines · OstyNoCombatStartSummonPatch · namespace CharacterBosses.Patches;
- `src/Hooks/Patches/OstyPoisonImmuneP…
(rendered map truncated at 16384 bytes)
<!-- dsh-openwolf:end -->
