# 在 Godot 里浏览《杀戮尖塔 2》的工程（场景 / 节点 / 资源）

> 状态：**已跑通**（2026-11-11）。用途：不改游戏、不装 Mod，纯粹为了在 Godot 编辑器里展开
> `SlayTheSpire2.pck` 的场景树、看节点属性、看贴图。写下来是为了下次不用重踩一遍坑。

## 1. 结论先说

| 想看的东西 | 能看到吗 | 说明 |
| --- | --- | --- |
| 场景 / 节点树 / 节点属性 | ✅ | 1037 个 `.tscn` 全是文本格式，`res://scenes/**` 直接用 Scene 面板展开 |
| 贴图、字体、材质、动画 | ✅ | 3555 个 `.ctex` + 170 个 spine `.spatlas` 都抽出来了 |
| 布局 / 锚点 / 尺寸 | ✅ | 已实测 `combat_ui.tscn` 实例化出 **111 个节点**，`game.tscn` **69 个**，`creature.tscn` 34 个 |
| 游戏 C# 源码 | ❌ | **pck 里 3441 个 `.cs` 全是 1 字节空壳**，只用于让 `res://xxx.cs` 路径能解析。真源码要靠反编译 `sts2.dll` |
| FMOD 音频 / spine 骨骼预览 | ❌ | 见 §5，游戏只带 template_release 原生库，编辑器要 editor 版 |

## 2. 抽取 pck → 可直接打开的工程

脚本：`tools/art/extract_pck_project.gd`。宿主目录 `.recon/godot-host`（只有一个 118 字节的
`project.godot`，作用是提供 `--path`，脚本从 pck 里读文件）。

```powershell
$env:APPDATA      = "<工作区>\.recon\_profile\AppData\Roaming"   # 必须重定向，否则 headless 建 logs 目录时崩
$env:LOCALAPPDATA = "<工作区>\.recon\_profile\AppData\Local"
tools\godot\Godot_v4.5.1-stable_mono_win64_console.exe --headless `
  --path "<工作区>\.recon\godot-host" `
  --script "<工作区>\tools\art\extract_pck_project.gd" -- `
  "D:\SteamLibrary\steamapps\common\Slay the Spire 2\SlayTheSpire2.pck" `
  "<工作区>\.recon\project" all
```

结果：**19729 个文件 / 1.9 GB**。要点：

- `--script` 必须是**绝对路径**，否则按 `res://` 相对宿主工程找。
- `all` 第三个参数 = 不再按扩展名过滤。
- 抽取循环**必须保留 `.godot` 目录**（见 §4.1），并且**必须跳过 `project.godot`**（见 §4.2）。

## 3. 用编辑器打开

最省事：双击工作区根目录的 **`open-sts2-project.bat`**（它会自己设好 APPDATA/LOCALAPPDATA 和绝对路径）。
注意这个 `.bat` 必须保持**纯 ASCII**：cmd 按 ANSI 读 `.bat`，中文注释会被截成乱码并让整行命令错位。

等价的命令行写法（PowerShell）：

```powershell
$env:APPDATA      = "<工作区>\.recon\_profile\AppData\Roaming"
$env:LOCALAPPDATA = "<工作区>\.recon\_profile\AppData\Local"
tools\godot\Godot_v4.5.1-stable_mono_win64.exe --path "<工作区>\.recon\project" --editor
```

cmd 里则是（`$env:` 是 PowerShell 语法，cmd 会报「文件名、目录名或卷标语法不正确」）：

```bat
cd /d "D:\Sts2ModProject\Downfall Expansion Vanilla Characters as Bosses"
set "APPDATA=%CD%\.recon\_profile\AppData\Roaming"
set "LOCALAPPDATA=%CD%\.recon\_profile\AppData\Local"
tools\godot\Godot_v4.5.1-stable_mono_win64.exe --path "%CD%\.recon\project" --editor
```

注意 `--path` 后面要带引号（工作区路径含空格），不要用 `| Select-Object -First N` 接管道
（PowerShell 会在取够行后掐掉上游进程）。推荐 `Start-Process -ArgumentList '--path "…" --editor'`。
值得先看的场景：

| 场景 | 内容 |
| --- | --- |
| `scenes/game.tscn` | 全局根节点（69 节点）：场景容器、音频、光标、热键、转场 |
| `scenes/combat/combat_ui.tscn` | 战斗 UI（111 节点）：能量球、星位、结束回合、牌堆按钮、手牌容器 |
| `scenes/combat/intent.tscn` | 意图节点 |
| `scenes/combat/creature.tscn` | 生物（含血条、状态图标） |
| `scenes/rooms/*` | 各房间布局 |

## 4. 踩过的坑（都会表现为「加载失败 / 报错弹窗」）

### 4.1 必须抽 `.godot/imported`
第一版脚本跳过了所有 `.` 开头的目录，结果 `.godot/imported/**`（编译好的贴图
`.ctex`/`.image`，1.57 GB）全没抽。打开 `game.tscn` 时弹出
「由于缺少依赖项，加载失败：res://images/packed/common_ui/cursor_*.png」。
`.import` 文件里的 `path=` 指向 `res://.godot/imported/…`，**那才是真实贴图**。

### 4.2 `project.godot` 不能用 `res://project.godot` 拷
`res://project.godot` 读到的是**宿主工程自己的**那份（118 字节 → 目标工程变成空壳，
`config/name` 变成 `sts2-host`，`[autoload]`/场景全丢）。真正权威的是 pck 里的
`project.binary`。修复方式：保留 `project.binary` 让编辑器读，或先删掉错误的文本文件再
用 `ProjectSettings.save()` 重新落盘。

### 4.3 addons 的 `plugin.cfg` 被导出剥掉了
`project.godot` 的 `[editor_plugins] enabled` 列了 5 个 `res://addons/*/plugin.cfg`，
但**这些文件不在 pck 里**（Godot 导出会剔除编辑器专用配置），于是报
「无法启用位于 res://addons/megacontentcreator/plugin.cfg 的附加插件：配置解析失败」。
处理：把 `[editor_plugins] enabled` 改成 `PackedStringArray()`。

### 4.4 两个 autoload 也是空壳
`SentryBootstrap`（C# 空壳，不继承 Node）、`FmodManager`（引用原生 `FmodServer` 类），
都在启动时报错。处理：注释掉 `[autoload]` 里这两行（本副本只用于浏览）。

### 4.5 五个 `.gdc` 会刷解析报错（已换成空壳）

编辑器一打开就弹：

```
Failed to load script "res://src/gdscript/audio_manager_proxy.gdc" with error "Parse error".
Failed to load script "res://src/gdscript/music_controller_proxy.gdc" with error "Parse error".
Failed to load script "res://src/gdscript/testing/spine_sprite_auto_player.gdc" with error "Parse error".
Failed to load script "res://src/gdscript/testing/spine_sprite_copier.gdc" with error "Parse error".
Failed to load script "res://src/gdscript/testing/starry_impact_tester.gdc" with error "Parse error".
```

原因同上：前两个引用 FMOD 的 `FmodServer` / `FmodBus`，后三个引用 spine 类型，而这两个
GDExtension 在编辑器里加载失败 → 编译版脚本解析不出来。`.gdc` 是编译过的字节码，**没有
办法反编译回可读源码**，所以处理方式是换空壳：

- 原件（`.gc` + `.gdc.uid` + `.gd.remap`）整体备份到 `.recon/gdscript-disabled/`（同相对路径）；
- 原位写一个 `.gd` 空壳，基类对齐挂载的节点类型：
  `audio_manager_proxy` / `music_controller_proxy` → `extends Node`；
  `spine_sprite_auto_player` / `spine_sprite_copier` / `starry_impact_tester` → `extends Node2D`；
- 必须**同时删掉 `.gd.remap`**，否则 Godot 还是去加载 `.gdc`。

验证：`.recon/check_gdscript.gd` 逐个 `load()`，五个全部 `OK`、失败数 0。
改完在编辑器里 **Project → Reload Current Project**（或关掉重跑 `open-sts2-project.bat`）即可。

## 5. 消不掉的报错（可以无视）

```
GDExtension dynamic library not found: 'res://addons/fmod/fmod.gdextension'
GDExtension dynamic library not found: 'res://addons/sentry/sentry.gdextension'
GDExtension dynamic library not found: 'res://addons/spine/spine_godot_extension.gdextension'
ERROR: .NET Sdk not found. The required version is '9.0.8'.
ERROR: Cannot instantiate C# script because the associated class could not be found. Script: 'res://src/…/NCombatUi.cs'
```

- 原生库：`.gdextension` 在 pck 里，但库里只请求 `windows.editor.x86_64`，游戏目录只带
  `*.windows.template_release.x86_64.dll`（fmod / sentry / spine 都是），编辑器版没发布。
  后果：没有 FMOD 音频、spine 骨骼动画不预览。其它照常。
- `.NET Sdk 9.0.8`：游戏自己的 C# 工程不在 pck 里，编辑器找不到 SDK 也不影响看节点。
- `Cannot instantiate C# script`：`.cs` 是空壳（§1），节点照样建出来，只是挂不上脚本。

## 6. 真 C# 源码：已批量反编译到 `.recon/src-decompiled/`

`tools/ilspy-cli` 加了一个 `dump` 命令（`Program.cs`），一次进程跑完整个程序集：

```powershell
.\tools\dotnet\dotnet.exe build tools\ilspy-cli\ilspycli.csproj -c Release
.\tools\dotnet\dotnet.exe tools\ilspy-cli\bin\Release\net10.0\ilspycli.dll `
  dump .refs\sts2_111.dll .recon\src-decompiled all
```

产物（**3520 个文件 / 33 MB**，耗时三分钟级）：

| 目录 | 内容 | 来源 |
| --- | --- | --- |
| `src/`、`addons/`、`RiderTestRunner/`（704） | Godot 节点层脚本，**路径就是游戏原路径** | 类型上的 `[ScriptPath("res://…")]` |
| `_types/MegaCrit/…`（2809） | 卡牌 / 能力 / 怪物 / 遭遇 / 命令等逻辑类，按命名空间分目录 | 类型全名 |

例：`src/Core/Nodes/Combat/NIntent.cs`（687 行）、
`_types/MegaCrit/Sts2/Core/Models/Cards/Bash.cs`（152 行）。

注意：

- 反编译没有 PDB，`async` 方法会展开成 `<OnPlay>d__5` 这类状态机结构体（噪音大但逻辑完整）。
- 编译器生成的怪名字（`<Module>`、`<>f__AnonymousType0`、`<RegexGenerator_g>…`，共 21 个）在
  Windows 上是非法文件名，会写失败并打印 `!! write fail`，**无害**。
- 用的是 `.refs/sts2_111.dll`：与当前安装版 `v0.111.0` 的
  `data_sts2_windows_x86_64/sts2.dll` **字节数完全一致**（9757184）。

### 顺带修掉的两个工具问题

1. `tools/ilspy-cli/ilspycli.csproj` 的 `HintPath` 原本指向
   `D:\Sts2ModProject\SlaytheSpire2-ModScaffold\tools\ilspy\…`（该目录已不存在，导致**无法重新构建**）。
   改成 `$(MSBuildThisFileDirectory)..\ilspy\ICSharpCode.Decompiler.dll`。
2. `tools/art/extract_pck_project.gd` 不再拷 `project.godot`（§4.2），并保留 `.godot`（§4.1）。
