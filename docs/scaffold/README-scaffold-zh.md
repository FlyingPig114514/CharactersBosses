# 杀戮尖塔 2 Mod 开发脚手架

零配置杀戮尖塔 2 Mod 开发脚手架，支持 AI 辅助的 Vibe Coding。克隆、运行 `install.bat`、开始编码。

## 致谢

- **[STS2MCP](https://github.com/Gennadiyev/STS2MCP)** - 游戏控制 MCP（出牌、状态读取、菜单导航）
- **[ILSpy-Mcp](https://github.com/maces/ILSpy-Mcp)** - .NET 程序集反编译 MCP
- **[Modding-Tutorial](https://github.com/fresh-milkshake/Modding-Tutorial)** - Mod 开发参考文档和示例

## 快速开始

```powershell
# 双击或在 PowerShell 中运行：
.\install.bat
```

这一条命令会：

1. 安装 .NET 10 SDK + .NET 8 运行时（隔离到 `tools/dotnet/`）
2. 下载 Godot 4.5.1 Mono、ILSpy
3. 构建 ILSpy MCP Server 和 STS2MCP
4. 在 `opencode.jsonc` 中配置 MCP 服务器
5. 构建 STS2MenuControl Mod 并安装
6. 构建脚手架 Mod 并安装
7. **启动游戏并验证 Mod 加载**（深度验证）

**注意**：深度验证需要 Steam 已启动并登录。

## 项目结构

```
SlaytheSpire2ModVibeCoding/
├── src/                            # 脚手架 Mod（你的代码在这里）
│   ├── ModEntry.cs                  # Mod 入口点 — 禁止修改逻辑
│   ├── Hooks/                       # 在这里写你的代码（按功能拆分）
│   │   └── .gitkeep
│   ├── Sts2ModScaffold.csproj       # .NET 10 项目 — 禁止修改
│   └── com.vibecoding.sts2mod.json  # Mod 清单
├── tools/
│   ├── STS2MenuControl/             # 主菜单控制 Mod（install 时从 GitHub 克隆）
│   ├── STS2Mcp/                     # STS2MCP 游戏控制 Mod（自动安装）
│   ├── pck_builder/                 # PCK 构建脚本
│   ├── dotnet/                      # 隔离的 .NET SDK/运行时
│   ├── godot/                       # Godot 4.5.1 Mono
│   ├── ilspy/                       # ILSpy 反编译器
│   └── launch_sts2.ps1 等           # 辅助脚本
├── references/                      # 游戏 DLL（gitignored）
├── docs/plans/                      # 实现计划目录
├── rules.md                         # 开发规范（必读）
├── AGENTS.md                        # AI Agent 工作流指南
├── install.bat                      # 一键安装
├── install-mod.bat                  # 重新构建并安装
└── uninstall-mod.bat                # 卸载所有 Mod
```

## 日常工作流

```powershell
# 1. 编辑 src/Hooks/ 下的文件

# 2. 重新构建并安装
.\install-mod.bat

# 3. 启动游戏
.\tools\launch_sts2.ps1

# 4. 查看日志
.\tools\read_sts2_logs.ps1
```

## MCP 服务器

| 服务器 | 端口 | 用途 |
|--------|------|------|
| **ILSpy MCP** | - | 反编译 `sts2.dll` 探索游戏内部结构、验证 Hook 签名 |
| **STS2MCP** | 15526 | 游戏内控制（出牌、结束回合、地图导航、事件选择等） |
| **STS2MenuControl** | 8081 | 主菜单控制（新游戏、角色选择、多人模式、时间线等） |

### STS2MenuControl API

> 独立仓库：[https://github.com/L4ntern0/STS2-MenuControl](https://github.com/L4ntern0/STS2-MenuControl)

HTTP API 在 `localhost:8081` 上控制主菜单（补充 STS2MCP 的功能）：

```
GET  /api/v1/menu                    # 获取当前菜单状态
POST /api/v1/menu                    # 执行菜单操作
GET  /health                         # 健康检查
```

#### 单人游戏操作

| 操作 | 参数 | 说明 |
|------|------|------|
| `open_character_select` | - | 打开角色选择界面（单人模式） |
| `select_character` | `option_index` | 选择角色 |
| `embark` | - | 开始新游戏 |
| `continue_run` | - | 继续存档 |
| `abandon_run` | - | 放弃存档（弹出确认框） |
| `open_timeline` | - | 打开时间线 |
| `choose_timeline_epoch` | `option_index` | 选择时间线时代 |
| `close_main_menu_submenu` | - | 关闭子菜单 |
| `return_to_main_menu` | - | 返回主菜单（从 Game Over） |

#### 多人游戏操作

| 操作 | 参数 | 说明 |
|------|------|------|
| `open_multiplayer_host` | `mode`, `max_players`, `port` | 创建多人房间（LAN/Steam） |
| `set_ready` | - | 标记准备就绪 |
| `set_unready` | - | 取消准备就绪 |
| `get_lobby_status` | - | 查询房间状态和玩家列表 |

#### 通用操作

| 操作 | 参数 | 说明 |
|------|------|------|
| `confirm_modal` | - | 确认弹窗 |
| `dismiss_modal` | - | 关闭弹窗 |

#### 状态返回

`GET /api/v1/menu` 在角色选择界面会额外返回：

```json
{
  "is_multiplayer": true,
  "lobby_type": "host",
  "max_players": 4,
  "net_type": "Host",
  "players": [
    {"id": 1, "character": "IRONCLAD", "is_ready": false, "is_local": true}
  ]
}
```

### STS2MCP API

HTTP API 在 `localhost:15526` 上控制游戏内操作：

```
GET  /api/v1/singleplayer         # 获取游戏状态
POST /api/v1/singleplayer         # 执行游戏操作
```

常用操作：`combat_play_card`、`combat_end_turn`、`choose_map_node`、`choose_event_option`、`proceed`、`select_card`、`confirm_selection`、`skip_card_reward`、`end_turn`。

## 完整测试流程（自动）

以下是 Agent 从零到第一场战斗胜利的完整自动化流程：

```
1. STS2MenuControl: open_character_select
2. STS2MenuControl: select_character(option_index=0)
3. STS2MenuControl: embark
4. STS2MCP:        choose_event_option(index=0)     # 涅奥
5. STS2MCP:        proceed / select_card + confirm   # 处理卡牌选择
6. STS2MCP:        choose_map_node(index=0)         # 选择地图节点
7. STS2MCP:        combat_play_card + combat_end_turn  # 战斗循环
8. STS2MCP:        proceed                         # 领取奖励
```

## Vibe Coding with AI

本脚手架专为 AI 辅助开发设计（OpenCode、Claude 等）：

1. 用自然语言描述你想要的 Mod
2. AI 使用 ILSpy MCP 找到正确的 Hook 并验证签名
3. AI 在 `src/Hooks/` 中编写代码
4. AI 通过 `install-mod.bat` 构建并安装
5. AI 使用 STS2MenuControl + STS2MCP 进行全自动测试
6. 你在游戏中验证

**重要**：AI Agent 必须在开发前阅读 `rules.md` 和 `AGENTS.md`。

## Hook 参考

所有 Hook 定义在 `MegaCrit.Sts2.Core.Hooks.Hook` 上。

### Hook 类型

1. **事件 Hook**（返回 `Task`，使用 Postfix）— `AfterCardPlayed`、`BeforeCombatStart` 等
2. **数值修改 Hook**（有返回值，使用 `ref __result`）— `ModifyDamage`、`ModifyBlock` 等
3. **布尔门控 Hook**（返回 `bool`，允许/拒绝）— `ShouldDie`、`ShouldPlay` 等

### Hook 示例

```csharp
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), nameof(MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage))]
public static class ModifyDamagePatch
{
    public static void Postfix(ref decimal __result)
    {
        __result *= 2;
    }
}
```

完整 Hook 列表请参考 `rules.md` 中的分类表。

## 脚本参考

| 脚本 | 用途 |
|------|------|
| `install.bat` | 完整环境安装（仅首次运行） |
| `install-mod.bat` | 重新构建并安装所有 Mod（日常使用） |
| `uninstall-mod.bat` | 从游戏卸载所有 Mod |
| `tools/launch_sts2.ps1` | 通过 Steam 启动游戏 |
| `tools/close_sts2.ps1` | 强制关闭游戏 |
| `tools/wait_sts2.ps1` | 等待游戏进程启动 |
| `tools/read_sts2_logs.ps1` | 查看游戏日志 |
| `tools/rename-scaffold.ps1` | 将脚手架重命名为你的 Mod 名称 |

## 环境要求

- **Windows**（Godot 和游戏仅支持 Windows）
- **Git**（需在 PATH 中）
- **杀戮尖塔 2** Steam 版
- **Steam** 已登录（用于启动游戏和深度验证）

其他所有依赖（.NET、Godot、ILSpy）都会自动安装。

## 故障排查

### Mod 未加载

1. 检查 `godot.log` 中的 `ERROR` 和 `WARNING`
2. 确认清单中的 `id` 与文件夹/DLL/PCK 名称一致
3. 确认 `has_dll: true` 和 `has_pck: true` 已设置

### Hook 不生效

1. 用 ILSpy MCP 验证签名 — **永远不要猜测**
2. 检查 `[HarmonyPatch]` 属性是否正确
3. 通过 `Logger.Log()` 输出确认 Hook 被触发

### 编译失败

1. 先运行 `install.bat`
2. 确认 `references/` 包含 `sts2.dll`、`0Harmony.dll`、`GodotSharp.dll`

## 文档

- **`rules.md`** / **`rules_en.md`** - 开发规范和约束（必读）
- **`AGENTS.md`** - AI Agent 工作流指南
- **`docs/plans/`** - 实现计划目录

## 许可证

MIT
