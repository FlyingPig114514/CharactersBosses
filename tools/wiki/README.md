# 百科页（GitHub Pages）与数据导出工具

这个目录放的是**从模组本体自动导出数据**、再生成「Boss 百科页 / Excel 表」的工具链。
页面地址（启用 GitHub Pages 后）：<https://flyingpig114514.github.io/CharactersBosses/>

## 一、这套东西解决什么问题

模组里有 34+ 套 Boss 设计，每套都有卡组、遗物、特殊效果、逐回合剧本。
以前这些只存在于代码和配置界面的悬浮说明里，玩家（和做视频的 up 主）看不到全貌。
现在从**编译好的模组 DLL** 里把运行时数据反射出来 —— 和游戏实际跑的那一份**完全同源**，不存在手抄过期的问题。

## 二、目录里的东西

| 文件 | 作用 |
|---|---|
| `tools/wiki-dump/` | C# 小工具：加载 `CharacterBosses.Impl113.dll`，反射 `BossDefs.All` / `BossVariant` / `BossScript` / `BossInfoTable`，导出 `tools/wiki/data.json` |
| `tools/wiki/build-site.ps1` | 读 `data.json` + `template.html`，生成 `docs/wiki/index.html`（数据内联，双击也能看） |
| `tools/wiki/template.html` | 页面模板（HTML + CSS + 渲染 JS，全部内联、不依赖 CDN） |
| `tools/wiki/make-markdown.ps1` | 读 `data.json`，生成 `docs/wiki/boss-table.md`（**GitHub 原生渲染的表格**，不用开 Pages 就能看） |
| `tools/wiki/make-xlsx.ps1` | 读 `data.json`，生成 `玩家角色当Boss——Boss意图表.v2.xlsx`（总表 + 三层剧本 + 说明） |
| `tools/wiki/data.json` | 导出的原始数据（唯一数据源，两个生成器都读它） |
| `docs/wiki/index.html` | 生成出来的页面（GitHub Pages 的入口） |
| `docs/wiki/boss-table.md` | Markdown 版意图表（GitHub 上直接以表格形式阅读 / 可按 Ctrl+F 搜） |

## 三、改完模组后怎么重新生成

```powershell
cd "D:\Sts2ModProject\Downfall Expansion Vanilla Characters as Bosses"

# 1) 先构建 113 版实现（页面里的数据都来自它）
tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=113

# 2) 导出数据（需要游戏本体目录里的托管 DLL 来解析 sts2 类型）
$game = "D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64"
tools\dotnet\dotnet.exe run --project tools\wiki-dump -c Release -- `
    src\bin\Release-113\CharacterBosses.Impl113.dll $game tools\wiki\data.json $PWD

# 3) 生成页面 + Excel
powershell -ExecutionPolicy Bypass -File tools\wiki\build-site.ps1
powershell -ExecutionPolicy Bypass -File tools\wiki\make-markdown.ps1
powershell -ExecutionPolicy Bypass -File tools\wiki\make-xlsx.ps1
```

> `make-xlsx.ps1` / `build-site.ps1` 都是 **Windows PowerShell 5.1** 脚本，文件必须存成 **UTF-8 with BOM**（否则中文会乱码）。

## 四、数据里都有什么（`data.json`）

```
bosses[]                      每个「角色定义」（7 个：铁甲战士/静默猎手/故障机器人/亡灵契约师/储君/愚者/观者）
  key / character / info      定义 key、角色中文名、配置界面那张「能力统计」表（循环/单体能力/群攻能力/高压回合/特殊效果类型…）
  acts[]                      该角色的第 1/2/3 幕
    act / name / encounter    幕号、设计中文名（愤怒战…）、遭遇 id（IRONCLAD_ANGER…）
    special / specialDread    特殊效果正文（普通 / 恐惧模式加强版）
    deck / relics / potions   卡组、遗物、药水（中文名，按出现次数重复）
    dreadUpgrades             恐惧模式额外升级的牌
    script                    剧本：loopFrom / loopLength / turns[{ note, cards[] }]
    variants[]                同一角色的其它设计（打击战、电球机、王储…），字段同上
```

`cards[]` 里每张牌带标记：`Upgraded`（升级）、`Burned`/`HoldInstead`/`NotDrawn`/`DiscardInstead*`/`DisplayOnly`（**这些算「不出」**）、
`Sly`/`Retain`/`AutoPlayed`/`FreeThisTurn`…，以及 `CostOverride`/`DamageOverride`/`BlockOverride`/`ExtraAoe` 等数值覆盖。

## 五、血量怎么算

游戏内血量 = **角色基础血量** + 该幕额外 + **涅奥的祝福 120** + **每进一幕 180×(幕−1)** + 变体 `HpBonus`

| 角色 | 基础血量 |
|---|---|
| 铁甲战士 | 80 |
| 静默猎手 | 70 |
| 故障机器人 | 75 |
| 亡灵契约师 | 66 |
| 储君 | 75 |
| 愚者 | 75 |
| 观者 | 72 |

例：愤怒战（一层）= 80+120 = **200**；易伤战（三层）= 80+120+180+180 = **560**；
根除骨（三层，带布质果实 +31）= 66+31+120+180+180 = **577**；养手骨（三层变体，不继承那 +31）= **546**。

## 六、发布（GitHub Pages）

页面是纯静态的 `docs/wiki/index.html`，仓库设置里把 Pages 指到 **main 分支 / `docs` 目录**即可：

> Settings → Pages → Build and deployment → Source: *Deploy from a branch* → Branch: `main` / folder: `/docs`

启用后地址是 `https://flyingpig114514.github.io/CharactersBosses/`，可以把这条链接贴到创意工坊的介绍里。

## 七、已知取舍

- 「打出 / 不出」是按剧本里的标记分的：**`打出` = 剧本排队要打的牌（顺序即真实出牌顺序）**，
  `不出` = 被标记为「被她自己烧掉 / 丢弃 / 保留没打 / 只显示意图」的牌。
  牌打到最后**没费**的那几张仍列在「打出」里（剧本会尝试并按顺序停下），每回合的 `note` 备注里写明了是哪几张没费。
- 数据不含卡牌的具体数值（伤害/格挡/费用），那部分仍以游戏内卡面为准；表里只有剧本显式覆盖过的地方（如「遗传算法 16 格挡」）才带数字。
