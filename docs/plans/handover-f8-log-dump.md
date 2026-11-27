> ★★ **本文件的任务已在 v0.2.977 完成（2026-11-27，实机验证通过）**，结论摘要：
> ① §5.1 的按键探针跑出来 = **游戏的** `NGame._Input` / `NInputManager._UnhandledKeyInput` 两条通道都活着，
>    **我们自己 Node 的三条通道（`_Input` / `_UnhandledInput` / `_UnhandledKeyInput`）0 行**；
> ② 真因由挂载自检证明：`has_method(_input)=False`（节点在 `/root` 下、`script=有`、`processInput=True`）
>    ⇒ Godot 不认为这个节点实现了输入回调（与本模组早记过的「自绘 UI `_Process`/信号不触发」同一个病）；
> ③ 落地 = `src/Hooks/Core/HotkeyRouter.cs` + `HotkeyPatches.cs`（Harmony 后缀挂游戏输入入口，400ms 去重）
>    + §3.2 的「磁盘读尾巴 + zip」全部做了。
> ④ 实测：主菜单 F8 → 55 行；战斗内 F8 → 同文件覆盖成 219 行；F9 → 场景树 4381 行 + 战斗房 580 行。
> 下面保留原文，作为**排查过程**与反例存档（尤其「别再给自己的 Node 写虚回调」这条）。

# 交接：游戏内 F8「导出最近日志」没生效，把它做出来

> 写于 2026-11-27（对话已到 280 轮 / 5209 步，交接给新会话）。
> 先读完这一页，再动手。所有「实测」都是本机（Windows，游戏装在 `D:\SteamLibrary\steamapps\common\Slay the Spire 2`）跑出来的。

---

## 0. 一句话任务

**玩家在游戏里按 F8，应该得到一份「最近的模组日志」文件，现在按了没有任何反应。把它做成真的能用。**

完成标准（缺一不可）：

1. 主菜单按 F8 → 生成文件；进一场 Boss 战按 F8 → 生成文件（同一份，覆盖）；
2. 文件里有**真实日志行**（不是只有表头），且 `mod_log.txt` 里出现两行：
   `… 最近日志已导出：<绝对路径>（N 行…）`（N > 0）；
3. 在游戏里**亲手按一次 F8 并确认**（不能只靠读代码；验证方法见 §5）；
4. 3 份构建 0 错误 + `install-mod.bat`，版本双改（见 §6）。

---

## 1. 现状（代码在，但从没成功过 —— 这是实测结论，不是猜）

已经有实现，位置：

- `src/Hooks/Core/RecentLogDumper.cs`（v0.2.900 加，v0.2.901 改成「最近 60 分钟」）
- 数据源：`src/Hooks/Patches/SafeLogPatch.cs` 里的 `Recent` / `RecentAt` 环形缓冲 + `SnapshotRecent(out first, out last)`
- 挂载点：`src/Hooks/Patches/BossFrameTickPatch.cs` 第 ~231/233 行
  （`Core.SceneTreeDumper.Ensure()` / `Core.RecentLogDumper.Ensure()`，每帧幂等调用）
- 输出：`%APPDATA%\SlayTheSpire2\logs\characterbosses_recent.txt`
- 实现方式：新建一个 `Node`，`tree.Root.AddChild(_inst)`，`SetProcessUnhandledKeyInput(true)`，
  然后在 `public override void _UnhandledKeyInput(InputEvent @event)` 里判 `Key.F8`。

### 本机日志里的铁证（`%APPDATA%\SlayTheSpire2\logs\mod_log.txt`，54 MB）

| 证据 | 结果 |
| --- | --- |
| `最近日志导出：按 **F8** 导出到 …`（挂载成功行） | **58 行**（每次启动都挂上了）|
| `最近日志已导出：…`（真正导出成功的行） | **0 行** |
| `最近日志导出失败：…`（`Dump()` 里 catch 到的失败） | **0 行** |
| `characterbosses_recent.txt` 文件 | **不存在** |
| 同构的 F9 场景树导出（`SceneTreeDumper`，同一套挂载写法） | `scene_tree.txt` 也**不存在**，`场景树已导出：…` 真命中 **0 行** |

⇒ 结论：**不是 Dump 写文件失败，而是按键根本没进到我们那个 `_UnhandledKeyInput`**。
（`Dump()` 内部每一步都有 try/catch + 日志，真跑进去失败了会留痕，实际一条都没有。）
注意：日志里搜「场景树已导出」会命中十几行「假命中」——那是旧 `BuildInfo.Tag` 长文本里含这几个字，别被骗。

---

## 2. 为什么按键进不去（按可能性排序）

1. **事件在到 `_UnhandledKeyInput` 之前就被吃掉了。**
   Godot 4 的分发顺序是 `_Input` → GUI → `_UnhandledInput` → `_UnhandledKeyInput`。
   游戏自己的 `NInputManager._UnhandledKeyInput`、设置面板的 `NInputSettingsPanel._UnhandledKeyInput`
   都会看键；只要上游有人 `AcceptEvent()` / `SetInputAsHandled()`，`_UnhandledKeyInput` 就再也不会被调用。
   F9 同构实现也不работает（见上表）⇒ 更像是「整条 unhandled 通道在我们的节点上不通」，而不是 F8 这个键特殊。
2. **`_UnhandledKeyInput` 在这个 C# / Godot 组合下没被派发到我们手动 `AddChild` 的裸 `Node`**
   （可能需要在 `_Ready` 里 `SetProcessUnhandledKeyInput(true)`，或节点被 `Root` 的某个 `ProcessMode` 影响）。
3. F8 被某个 mod 绑定成动作键（本机装了很多 mod：DanmakuSpire / AiTeammate / STS2_MCP / 薄荷尖塔…）。
4. 环形缓冲为空（`SafeLogPatch` 没挂上）—— 但这**不会**让日志里出现「0 行导出成功」，只会产出空文件，
   所以它不是本次「完全没反应」的原因；不过**顺手把它去掉更好**（见 §3.3）。

**第一步要做的事：用 5 分钟把「按键到底有没有到我们的节点」测出来**（见 §5.1 的探针做法），
不要先动结构。测完再按结论选 §3 里的方案。

---

## 3. 建议实现方案（按优先级，可叠加）

### 3.1 换触发通道：用 `_Input` 或直接 Harmony 挂游戏自己的输入入口

- 先试最省事的：把两个 dumper 的监听改成 `public override void _Input(InputEvent @event)`
  （`_Input` 在 GUI 之前，最不容易被吃），保留 `_UnhandledKeyInput` 作为第二通道，
  两处都判同一个键，用一个 `bool _firedThisFrame` 防重复；**不要**在 `_Input` 里 `SetInputAsHandled`。
- 若 `_Input` 也不通：用 Harmony 直接挂**游戏自己的**输入方法（比裸 Node 可靠得多）。
  用 ilspy 先确认有哪些 override：
  ```
  .\tools\dotnet\dotnet.exe tools\ilspy-cli\bin\Release\net10.0\ilspycli.dll search .refs\sts2_113.dll '_Input'
  .\tools\dotnet\dotnet.exe tools\ilspy-cli\bin\Release\net10.0\ilspycli.dll search .refs\sts2_113.dll '_UnhandledKeyInput'
  ```
  已知存在：`MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager._UnhandledKeyInput(InputEvent)`、
  `…Screens.Settings.NInputSettingsPanel._UnhandledKeyInput(InputEvent)`；
  再找 `NGame` / `NSceneContainer` / `NRun` 上有没有 `_Input`。
  Harmony 用 `[HarmonyPatch(typeof(NGame), "_Input")]` 这种按名字挂（私有/虚方法都能挂），
  Postfix 里判 `InputEventKey` 的 `Keycode`/`PhysicalKeycode`。
- 备用触发（强烈建议一起做，反正只有几行）：在 BaseLib 的模组设置页加一个「导出最近日志」按钮，
  直接调 `RecentLogDumper.Dump()`；这样即使键盘通道再出问题，玩家也有路。

### 3.2 导出内容别只靠内存环：**从磁盘读尾巴**

`mod_log.txt` 是逐行落盘的（`SafeLogPatch` 后台线程 200 ms 一批），磁盘上那份永远是最全的。
建议 `Dump()` 改成：

1. 打开 `%APPDATA%\SlayTheSpire2\logs\mod_log.txt`，`FileShare.ReadWrite`，**只读最后 ~8 MB**
   （`FileStream.Seek` 到 `Length - 8MB`，丢掉第一行半截），按行切，取最后 60000 行；
2. 表头保留现在这套（构建串 / 导出时间 / 覆盖时段 / 行数 / 脱敏说明）；
   脱敏沿用现有 `Redact`（`SafeLogPatch` 那边已有对应的脱敏工具，确认一下是哪一个）；
3. 顺手把同目录的 `characterbosses_trace.txt`、`characterbosses_stall.txt`、
   `characterbosses_desync.txt`（存在才取）一起打成一个 zip：
   `%APPDATA%\SlayTheSpire2\logs\characterbosses_report_yyyyMMdd_HHmmss.zip`，
   文本另存 `characterbosses_recent.txt`（保持玩家熟悉的名字）；
4. 打完**必须** `Logger.Log`：「最近日志已导出：<绝对路径>（N 行，覆盖 …→…）」，失败打清原因
   （包括「F8 按到了但 N=0」这种可疑情况）。
5. 手机（`LogLoudness`/`PhoneBuild`）没有 F8：键位那条静默跳过，但设置页按钮保留。

### 3.3 顺手清理

- `SnapshotRecent` 保留但降级为「磁盘读失败时的兜底」，别再当唯一数据源；
- `_UnhandledKeyInput` 里加一行「按到 F8 了」的高优先级日志（`LogLoudness.Diag` 会被手机的
  loud 规则吞掉，用普通 `Logger.Log`），这样下次能一眼分出「键没到」还是「导出炸了」。

---

## 4. 必须知道的工程约束（用户定的硬规矩）

1. **每改一次代码 → 3 份构建必须 0 错误 + `install-mod.bat`**：
   ```
   .\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111
   .\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=113
   .\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111 -p:PhoneBuild=1
   cmd /c install-mod.bat
   ```
   `install-mod.bat` 会打印 `[verify] 两份实现的版本串都是 vX.Y.Z OK` —— 必须是 OK。
   （「0 错误 / 近 200 个警告」是常态，警告不用管。）
2. **版本号两处一起改**：`src/CharacterBosses.json` 的 `version` + `src/Hooks/Core/BuildInfo.cs` 的 `Tag`。
   `Tag` 里**不能有反斜杠**（构建脚本会失败），改完可以这样核：
   `((Select-String -Path src\Hooks\Core\BuildInfo.cs -Pattern 'string Tag = ').Line)` 里 `\` 数量必须是 0。
3. 每次改动都要在 `docs/plans/changelog-history.md` **最上方**加一条（`## vX.Y.Z（日期）：一句话`，
   再写用户原话 + 铁证 + 根因 + 改法）。
4. **打包 / 上传创意工坊 / 推 GitHub 只在用户明说时做**（他有固定口令：「打包，上传创意工坊和github」）。
   步骤见 `tools/pack-dist.ps1`、`docs/plans/workshop-upload.md`、本仓库历史里的发布记录。
5. `src/ModEntry.cs`、`src/Sts2ModScaffold.csproj` 之类脚手架文件不要动；改动尽量落在 `src/Hooks/`。

---

## 5. 验证怎么做（必须自己跑，别只看编译过）

### 5.1 先做「按键探针」（10 分钟）

临时在 `RecentLogDumper` 里加：

```csharp
public override void _Input(InputEvent @event)
{
    if (@event is InputEventKey k && k.Pressed && !k.Echo)
        Logger.Log($"[CharBoss] 按键探针：keycode={k.Keycode} physical={k.PhysicalKeycode}");
}
```

启动游戏 → 随便按 F8 / F9 / A / Esc → 看 `mod_log.txt` 有没有「按键探针」。
- 一条都没有 ⇒ 我们的节点收不到任何输入 ⇒ 走 §3.1 的 Harmony 方案；
- 有、但 F8 那条不存在 ⇒ 键被上游吃了或 keycode 不是 F8 ⇒ 打印里看实际 keycode；
- 有 F8 ⇒ 那就是 `Dump()` 的问题，按 §3.2 重写并看失败日志。

### 5.2 游戏内实测（脚本都在 `tools\`）

```powershell
# 前置：Steam 必须已登录
powershell -ExecutionPolicy Bypass -File tools\close_sts2.ps1
Remove-Item "$env:APPDATA\SlayTheSpire2\logs\mod_log.txt" -Force -ErrorAction SilentlyContinue
powershell -ExecutionPolicy Bypass -File tools\launch_sts2.ps1
powershell -ExecutionPolicy Bypass -File tools\wait_sts2.ps1
# 主菜单：STS2MenuControl HTTP :8081（open_character_select / select_character / embark / open_multiplayer_host …）
# 战斗内：STS2MCP HTTP :15526（combat_play_card / combat_end_turn / choose_map_node …）
```

主菜单按 F8 → 检查文件；打到一场 Boss 战再按 F8 → 检查文件被覆盖且行数变多。
**注意**：这些脚本、adb、`install-mod.bat`、`git push` 在沙箱里需要 `danger-full-access`
（工具调用时带 `sandbox_permissions`）。

### 5.3 回归面（别踩坏）

- F9（`SceneTreeDumper`）现在同样没生效 —— 顺手一起修可以，但**别为了修 F8 把 F9 的落盘格式改了**；
- `SafeLogPatch` 是本模组所有日志的唯一出口，改它要保证「永不抛异常给游戏逻辑」这条不破；
- 崩溃面包屑 `characterbosses_trace.txt`（`CrashTrace`）与停摆报告 `characterbosses_stall.txt`
  （`StallWatchdog`）是另一套机制，别混进这个任务的改动里。

---

## 6. 当前版本与工作区状态（2026-11-27 21:40）

- **已安装到游戏**：`v0.2.975`（`[verify]` OK）。
- **已发布**：创意工坊条目 **3830639796** 与 GitHub Release 都还是 **v0.2.974**；
  `main` = `9b79822`（v0.2.974 发布记录）。
- **未提交的改动**（就是 0.2.975 的内容，`git status` 可见）：
  `src/CharacterBosses.json`、`src/Hooks/Core/BuildInfo.cs`、`src/Hooks/Core/ShadowPlayerFactory.cs`、
  `src/Hooks/Patches/BossFrameTickPatch.cs`、`src/Hooks/Patches/HookIterateNullCombatStatePatch.cs`、
  `docs/plans/changelog-history.md`、`.dshwolf/memory.md`。
- **v0.2.975 修了什么（务必别回退）**：0.2.974 为了解决联机「Boss 被毒死后整场卡死」
  曾经在 Boss 死亡时调 `Player.DeactivateHooks()`；那会让**尸体身上的 Power 被钩子广播过滤掉**，
  于是「愚者的箭」（`ImpaledPower.AfterDeath` → `DislodgeAllArrows`）不再返还 ⇒ 箭永久失踪。
  已回滚该做法，卡死改由 `Patches/HookIterateNullCombatStatePatch` 两处纯兜底承担。
  `BossFrameTickPatch` 与 `HookIterateNullCombatStatePatch` 里都写了「别再走这条近路」的反编译注释
  —— **不要再拿 `IsActiveForHooks` 去治「死掉 Boss 的遗物继续发效果」**。
- 因此：**0.2.975 还没打包/上传**。等用户说「打包，上传创意工坊和github」再发
  （发之前记得：`git add -A && commit`、tag、推、`install-mod.bat pack`、上传、Release）。

---

## 7. 其它交接要点（省得重新踩）

- 用户口径：**中文回复、结论先行、带铁证（日志原文 / 反编译原文）、别只说"应该"**；
  没验证过的不要说验证过。
- 反编译工具（没有 ILSpy MCP，用 CLI）：
  ```
  .\tools\dotnet\dotnet.exe tools\ilspy-cli\bin\Release\net10.0\ilspycli.dll <types|type|method|members|search> <dll> [pattern]
  ```
  dll：`.refs\sts2_113.dll`（游戏 0.111 beta，主参照）、`.refs\sts2_111.dll`（正式版，签名可能不同）。
  注意命名空间陷阱：球是 `MegaCrit.Sts2.Core.Models.Orbs.*`，`OrbModel` 在 `Models`（不是 `Models.Orbs`）。
- PowerShell 坑：含中文的 `.ps1` 必须 **UTF-8 带 BOM**（用 edit/write 工具改过之后要补 BOM）；
  这是 Windows PowerShell 5.1，`Set-Content -Encoding utf8NoBOM` 不存在（用
  `[System.IO.File]::WriteAllText($p,$t,(New-Object System.Text.UTF8Encoding($false)))`）。
- 本机装了很多 mod（BaseLib 依赖是硬依赖 `min_version 3.3.5`、AiTeammate、DanmakuSpire、
  薄荷尖塔、Downfall 收藏家…），日志里大量 `!is_inside_tree()` 之类的噪声是已知的无害噪声。
- 想快速看日志尾部：`mod_log.txt` 有几十 MB，用 `Get-Content -Tail N` 而不是整份读。
- 已知与本任务无关但常见的坑：`git credential fill` 起不来（用 `.tmp/get-token.ps1` 的 CredRead），
  `Invoke-RestMethod`/`curl` 因 schannel 失败（用 `node --use-system-ca`），
  上传器是 net8 应用（`$env:DOTNET_ROLL_FORWARD='Major'`）。
