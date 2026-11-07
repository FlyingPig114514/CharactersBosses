# 用这个模板开新模组

这个目录是**复用资产库**，不是可以直接开工的项目根 —— 里面的 `src/` 是原始脚手架
（`Sts2ModScaffold` + `com.vibecoding.sts2mod`）。**别在这里直接开发**，否则下次没得复制。

## 1. 复制一份出去

```powershell
robocopy D:\Sts2ModProject\_template D:\Sts2ModProject\你的模组名 /E
```

## 2. 改绝对路径

模板里有三处写死了 `_template` 的绝对路径，复制后要改（在**新目录**里跑）：

```powershell
$old='_template'; $new=Split-Path -Leaf (Get-Location)
Get-ChildItem -Recurse -File -Include *.jsonc,*.ps1,*.md,*.json,*.bat |
  ForEach-Object {
    $t=[IO.File]::ReadAllText($_.FullName,[Text.Encoding]::UTF8)
    $n=$t.Replace("D:\Sts2ModProject\$old","D:\Sts2ModProject\$new")
    if($n -ne $t){ [IO.File]::WriteAllText($_.FullName,$n,(New-Object Text.UTF8Encoding($false))) }
  }
```

要改的三处：`opencode.jsonc`（ILSpy MCP / STS2MCP 的命令行路径）、`.recon/dc.ps1`、`docs/plans/recon-*.md`。

> **必须用 `[IO.File]::ReadAllText/WriteAllText` 显式 UTF-8。**
> PS 5.1 的 `Get-Content`/`Set-Content` 默认按 ANSI(GBK) 读写，会把源码里的中文写坏 ——
> 上一个模组真踩过，一次毁掉 3 个文件的中文注释。改任何带中文的文件都别用 `Get-Content -Raw | Set-Content`。

## 3. 改模组身份（**必须在写任何中文代码之前**）

```powershell
powershell -ExecutionPolicy Bypass -File tools\rename-scaffold.ps1 -NewModName "你的模组名"
```

**顺序很关键**：这个脚本用 `Get-Content -Raw` / `Set-Content` 去改 `ModEntry.cs` 和
`Hooks/*.cs`，源码里一旦有中文就会被它写坏。所以**先改名、后写代码**。

它还有两个坑：

- 写死了旧名 `Sts2ModScaffold` / `com.vibecoding.sts2mod` ——
  **在已经改过名的目录上跑会全部 skip**（只打印一句 "already renamed" 然后什么都不做）。
  所以别指望"复制一个成品模组再改名"。
- 清单它会改成 `<com.vibecoding.xxx>.json`；本项目的成品模组用的是 `<模组名>.json`，
  两种都行 —— 游戏是在模组目录里**扫 `.json`** 找清单的（见 `docs/plans/workshop-upload.md`）。

改名后记得核对：`id` = 文件夹名 = DLL 名 = pck 名（`rules.md` 的硬约束），
以及 `src/*.csproj` 里的 `<GameDir>` 指向本机游戏目录
（本机是 `D:\SteamLibrary\steamapps\common\Slay the Spire 2`）。

## 4. 环境已经是好的，不要再跑 install.bat

`tools/` 里带的是本机装好的整套：dotnet SDK、Godot 4.5.1 mono、ILSpy + ilspy-cli、
pck_builder、STS2MCP、STS2MenuControl、uv。
`install.bat` 会尝试联网下载 —— **本机没有外网，跑了只会失败**。

`references/` 里有 `sts2.dll`、`0Harmony.dll`、`GodotSharp.dll`、`BaseLib.dll`。

## 5. 已经带过来的侦察成果（省掉重查的功夫）

| 位置 | 内容 |
|---|---|
| `.recon/pck/localization/{eng,zhs,zht,...}` | **游戏官方本地化**。查任何术语的官方中英名都靠它，例如 `monsters.json` 里 `VANTOM.name` = 墨影幻灵 / "Vantom"，`GIANT_HEAD` 一类同理 |
| `.recon/*.txt` | 反编译转储：`MonsterModel-PerformMove-body.txt`、`t-NCard.txt`、`M-NPlayerHand.txt`、`types-intent.txt`、`monsters-all.txt` 等 |
| `docs/plans/recon-*.md` | 成体系的侦察笔记：战斗房节点树与绘制层级、`NCard`/`NCombatUi` 结构、怪物意图系统、卡牌结算时序 |
| `docs/plans/game-object-lookup.tsv` | 游戏对象名对照表 |
| `docs/plans/workshop-upload.md` | 上个模组整理的工坊上传硬约束（上传器怎么读 `workshop.json`、`image.png` <1MB、`mod_id.txt` 的作用、`content/` 放什么） |

## 6. 反编译工具链（本项目主力工具）

```powershell
$dotnet = "tools\dotnet\dotnet.exe"
$cli    = "tools\ilspy-cli\bin\Release\net10.0\ilspycli.dll"
& $dotnet $cli types   references\sts2.dll                 # 列类型
& $dotnet $cli type    references\sts2.dll MechaKnight      # 反编译整个类
& $dotnet $cli method  references\sts2.dll IceLance OnPlay  # 只看一个方法
& $dotnet $cli members references\sts2.dll NCard            # 只看成员签名
& $dotnet $cli search  references\sts2.dll "RepeatVar"      # 搜成员名
```

注意 `type` 是**子串匹配**：查 `NCard` 会连带出一堆 `NCard*` 类型；
要精确就写全名 `MegaCrit.Sts2.Core.Nodes.Cards.NCard`。

要读**游戏自己的资源**（本地化、场景 `.tscn`、`.tres`）时，`tools/pck_builder` 那套 Godot
加 `.recon/extract_pck.gd` 能从 `SlayTheSpire2.pck` 抽出文件（脚本里的 PCK 路径按需改）。

**几条踩过的坑，能省你几个小时**：

- 字符串常量搜不到 ≠ 不存在：自定义特性的字符串参数存在 **Blob 堆（UTF-8）**，
  普通字面量在 **#US 堆（UTF-16）**，两种编码都要搜一遍（比如 `"{0:0.00}"` 只在 UTF-8 里能搜到）。
- 想知道游戏读哪个清单文件？搜 `sts2.dll` 的字符串堆就行 —— `mod_manifest.json` 压根不在里面，
  实际是**扫模组目录里的 `.json`**。
- 反编译的输出**别用 PowerShell 管道重定向到文件**（二进制/大文本会被拧坏），
  用 `ilspycli ... > out.txt` 这种 cmd 级重定向，或先 `Out-String` 再写。

## 7. 构建与安装

```powershell
install-mod.bat        # 构建 + 装到游戏 mods/ 目录
```

- **游戏必须先关掉**：DLL 被占用时 `copy` 会静默失败，而脚本**无条件**打印 `- DLL copied`。
  所以每次都要**核对目标文件的时间戳**，别信那行输出。
- 脚本用 `xcopy /Y` 拷 `frames/` 一类素材，**不会删改名前的旧文件** ——
  重命名过素材之后要手动清掉游戏目录里的残留。
