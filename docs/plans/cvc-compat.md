# 与「CVC-跨版本兼容补丁」兼容（v0.2.588）

> 用户 2026-11-12：「本模组似乎和『CVC-跨版本兼容补丁』冲突。进行兼容」。
> 状态：**已实现 + 实机验证**（本文件记录证据与结论，便于下次回归对照）。

## 1. CVC 是什么，它怎么「看」我们

`CVC` = 创意工坊的 `CrossVersionCompat`（作者 Snowline，v0.9.50），**在模组 DLL 被加载之前**
用 Mono.Cecil 把游戏程序集与模组程序集过一遍 IL：修洞（broken call site）、重定向按名字查方法的调用、
以及给「按旧版本编译的模组」垫兼容层。它把结论写到

```
%APPDATA%\SlayTheSpire2\cross_version_compat_report.txt
```

关键点：**它会把 mod 目录里每一个 `.dll` 都当成一个模组实现去指纹识别**（"Scanning covers every
loaded mod whether or not it was selected for repair"）。

## 2. 冲突的真实形态（兼容前）

2026-11-12 14:30 那次的报告：

```
CharacterBosses  99/100 [clean]
  indeterminate refs 18; bridges: semantic=0, mechanical=0, reflection=0
  not inspected: calls into BaseLib x18; 25 res:// path(s)

Mod target-version evidence
  CharacterBosses
      declared minimum : 0.107.1
      API inference    : 0.107 [medium] (… strong, … competing signal(s))
      compatibility path: older→newer
        - … exclusive API member(s) match 0.107
```

同时 godot.log 里：

```
[CrossVersionCompat] repaired 0 broken call site(s) across 6 mod assembly(ies)
[CrossVersionCompat] 3 mod(s) were built for a different game build …
[CrossVersionCompat] startup cost: 15755ms … scanning 6673ms over 29 assembly(ies) (22 reused, 7 walked)
```

**结论**：CVC 的 IL 改写本身没有改坏我们（0 findings / 0 repairs / 0 will-throw），
问题是我们被它**误判成「按 0.107 编译的旧模组」**，于是它：

1. 把整个模组按 `older→newer` 的兼容路径处理（改写我们的 DLL，哪怕我们本来就对 0.111 编译）；
2. 走到一份**根本不会被加载**的 `CharacterBosses.Impl107.dll` 上。

根因：我们是**双版本外壳**，同目录同时放 `Impl107` + `Impl111` 两份实现，运行时只载入其中一份。
扫描器看不见「哪份生效」，只看到「有一份 107 专属 API」⇒ 推断 0.107。
（唯一的 107 专属成员：`MegaCrit.Sts2.Core.Modding.Mod::assembly`，在 `Telemetry.CharacterModOf`
的 `#if STS2_107` 分支里。）

次要冲突点（报告里的共用补丁点，不是错误但会互相顶图标）：

```
[postfix] PowerModel::get_PackedIconPath      — BaseLib; CharacterBosses; RitsuLib.framework-content-assets
[postfix] PowerModel::get_ResolvedBigIconPath — CharacterBosses; RitsuLib…; SteamRandomMatchMod
```

## 3. 改了什么

| # | 改动 | 文件 |
|---|------|------|
| 1 | 两份实现改以 **`.dll.bin`** 发布（扫描器看不见，`AssemblyLoadContext.LoadFromAssemblyPath` 不看扩展名） | `install-mod.bat` / `tools\pack-dist.ps1` / `loader\Loader.cs` |
| 2 | 外壳按「`.dll` → `.dll.bin`」顺序找实现 | `loader\Loader.cs` 新增 `FindImpl` |
| 3 | 安装与打包**主动删除**旧版留下的 `.dll`（否则升级后两份并存、又被扫到）；打包护栏两种扩展名都认，且缺一份就拒绝打包 | `install-mod.bat` / `tools\pack-dist.ps1` |
| 4 | 两个 Power 图标 postfix 改成**非破坏性**：别人已经解析出真实资源就不覆盖 | `src\Hooks\Patches\BossPowerIconPatch.cs` |
| 5 | 清单补 `min_game_version: 0.107.1`（版本过低的游戏直接拒载而不是崩） | `src\CharacterBosses.json` |
| 6 | 文档 / README 同步 `.dll.bin` 文件清单 | `docs\安装说明.txt`、`README.md`、`README_en.md` |

### 第 4 条为什么不能只写「Exists 就放过」

原版 `PowerModel.ResolvedBigIconPath` 自带兜底链：
`powers/<id>.png` → `powers/beta/<id>.png` → **`powers/missing_power.png`**，
而 `missing_power.png` 是**真实存在的资源** ⇒ 只判断「存在就别管」会把我们自己的 Power
永远留在「缺失图标」上。现在的判定是：

```csharp
foreignResolved = !string.IsNullOrEmpty(__result)
    && ResourceLoader.Exists(__result)
    && __result != missing          // 排除原版兜底
    && __result != vanillaDefault;  // 排除本 Power 自己的默认路径
```

## 4. 兼容后（2026-11-12 14:41，同一台机器）

```
CharacterBosses  99/100 [clean]
  CharacterBosses
      declared minimum : 0.107.1
      API inference    : ambiguous [none] (0 strong, 0 competing signal(s))   ← 不再是 0.107
      compatibility path: mixed/ambiguous
        - 0 exclusive API member(s) match 0.107
```

- 不再出现在「`3 mod(s) were built for a different game build`」名单里（现在只有 Loadout / Downfall / SilentFX），
  而且 `repaired … across 6 mod assembly(ies)` → **`across 3 mod assembly(ies)`**。
- 启动耗时 **15755ms → 7112ms**；扫描 6673ms / `7 walked` → **629ms / `1 walked`**
  （休眠的那份 107 不再需要被读一遍、改写一遍）。
- 模组本体行为不变：`[Loader]` 日志仍是
  `游戏=0.1.0+41cef1ea…（探测 beta=True）⇒ 加载实现 CharacterBosses.Impl111.dll`、
  `模型登记完成（127 个新登记 / 共 127 个 AbstractModel 实现）`、`打上的补丁目标 189 个`。

## 5. 顺带修掉的两个坑（不是 CVC 的锅）

1. **`install-mod.bat` 的 `if … ( … ) else ( … )` 被 echo 里的括号截断** ——
   `echo - Loader copied (CharacterBosses.dll)` 里的 `)` 被 cmd 当成 if 块结束符 ⇒
   `else` 分支被**无条件执行**（本次就多执行了「无外壳」那条 copy，只是源文件不存在才没出事）。
   现在 if 块里的 echo 一律不带括号。
2. **`tools\close_sts2.ps1` 用了 Windows 11 已移除的 `tskill`** ⇒
   `CommandNotFoundException`，游戏关不掉 ⇒ `install-mod.bat` 复制 DLL 被文件占用。
   改成 `taskkill` + `Stop-Process` 兜底，并等到进程真的退出（最多 10 秒）。

## 6. 遗留观察（本次未改，均**改动前就存在**）

- `godot.log` 每次启动有 **254 条**
  `Attempting to register type CharacterBosses.* in assembly CharacterBosses.Impl111, but it is not
  associated with any mod! You may need to call ModManager.AssociateAssemblyWithMod … Type sorting
  may break because of this, causing errors in multiplayer.`
  （今天 11:10 / 12:38 / 14:10 / 14:29 四次启动都是 254 条）。若能拿到
  `ModManager.AssociateAssemblyWithMod` 的签名，值得在 `loader\Loader.cs` 里补一次关联 ——
  它提示的风险正是「联机时类型排序不一致」。
- `[Loader] 双版本外壳：InitIds 失败: ModManager is not finished initializing!` ——
  外壳调用 `ModelDb.InitIds()` 的时机早于 `ModManager` 收尾，游戏随后自己会初始化 id，暂无副作用。
- `assets\phantom_anim` 目录不存在 ⇒ `install-mod.bat` 每次都提示「phantoms will not render」
  （该素材早已不使用，属于陈旧提示）。
