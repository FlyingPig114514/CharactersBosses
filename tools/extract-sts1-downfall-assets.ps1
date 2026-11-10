# 从杀戮尖塔 1 的「崩坠 Downfall」mod（Downfall.jar）里提取**音乐 / 音效 / 美术**资源，
# 按类别放进几个文件夹，供人工浏览。
#
# 用法（PowerShell 7，即 pwsh；不要用 PS 5.1，它会把无 BOM 的 .ps1 当 ANSI 读、中文全乱）：
#   pwsh -File tools\extract-sts1-downfall-assets.ps1
#   pwsh -File tools\extract-sts1-downfall-assets.ps1 -Jar "D:\path\Downfall.jar" -Out "D:\out"
#
# 产物（默认）：
#   .assets-sts1-downfall\01-音乐\...
#                        \素材索引.csv
#                        \README.md
# 该目录已在 .gitignore 里（`/.assets-sts1-downfall/`），不会进仓库。
#
# ⚠ 只取「崩坠 mod 自己的」资源：jar 里没有塔 1 原版资源（那些在 desktop-1.0.jar 里），所以不用筛。

[CmdletBinding()]
param(
    [string]$Jar = 'D:\SteamLibrary\steamapps\workshop\content\646570\1610056683\Downfall.jar',
    [string]$Out = '',
    [switch]$KeepOld
)

# ⚠ PS 5.1 里 param 默认值求值时 $PSScriptRoot 还是空的，所以路径在这里补
if (-not $Out) { $Out = Join-Path (Split-Path -Parent $PSScriptRoot) '.assets-sts1-downfall' }

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path -LiteralPath $Jar)) { throw "找不到 Downfall.jar：$Jar" }

if (Test-Path -LiteralPath $Out) {
    if ($KeepOld) { Write-Host "保留已有目录：$Out" }
    else {
        # ⚠ 别整目录删：`14-角色Boss语音(取自塔1原版VO)` 是**另一个脚本**从塔 1 原版包里提的
        #   （tools\extract-sts1-charboss-voices.ps1），重跑本脚本不该把它一起清掉。
        Write-Host "清空旧目录（保留 14 语音目录）：$Out"
        Get-ChildItem -LiteralPath $Out -Force |
            Where-Object { $_.Name -notlike '14-*' } |
            Remove-Item -Recurse -Force
    }
}
New-Item -ItemType Directory -Force -Path $Out | Out-Null

# ── 类别目录名（中文 + 英文，方便在资源管理器里认） ────────────────────────────
$CatDirs = [ordered]@{
    '01' = '01-音乐 Music'
    '02' = '02-音效 Sound'
    '03' = '03-角色立绘与战斗动画 Characters'
    '04' = '04-卡牌图与卡框 Cards'
    '05' = '05-遗物图 Relics'
    '06' = '06-能力图标 Powers'
    '07' = '07-药水奖励与日常 Potions-Rewards'
    '08' = '08-事件场景剧情 Events-Scenes'
    '09' = '09-界面与地图 UI-Map'
    '10' = '10-怪物与Boss Monsters-Bosses'
    '11' = '11-特效与能量球 VFX-Orbs'
    '12' = '12-杂项与源文件 Misc-Source'
    '13' = '13-角色当Boss(把可玩角色做成boss) CharBosses'
}

function Get-CharLabel([string]$first) {
    # champResources / awakenedResources / downfallResources … → champ / awakened / downfall(通用)
    $n = $first
    if ($n -match '^(.*?)Resources$') { $n = $Matches[1] }
    if ($n -eq 'downfall') { return 'downfall-通用' }
    return $n
}

function Get-Category([string]$full, [string]$ext) {
    $p = '/' + $full.ToLowerInvariant()

    if ($ext -in @('.ogg', '.mp3', '.wav')) {
        if ($p -match '/music/' -or $p -match 'menumusic') { return '01' }
        return '02'
    }

    # 动画数据（.atlas / .json）、Spriter 工程（.scml）、可编辑源图（.pdn / .xcf）
    # 都跟着它们的图走 —— 只要目录能认出来就归到对应类别，认不出来才落到 12。
    $artish = $ext -in @('.png', '.jpg', '.jpeg', '.atlas', '.json', '.scml', '.pdn', '.xcf')

    if ($artish) {
        # ★ 第一优先：「角色当 boss」那几组专属立绘（expansioncontentResources/images/bosses/**）。
        #   反编译确认（class 常量池）：`charbosses/bosses/<角色>/(NewAge|Simpler)/ArchetypeAct*`
        #   直接引用这一批文件；其余幕数则**直接借塔1原版角色骨骼**（images/characters/…，在
        #   desktop-1.0.jar 里，按用户要求不提）。
        if ($p -match '/images/bosses/') { return '13' }
        # 顺序有讲究：先判「角色/立绘/动画」，再判卡牌/遗物/能力，最后才是通用 UI
        # ⚠ `mainChar` 里没有 `/char` 这一段（是 `/mainchar`），漏了它整份角色立绘会掉进 12 —— 踩过。
        if ($p -match '/(char|charselect|mainchar|characters|spriter|animation|static_character|portrait|corpse)') { return '03' }
        if ($p -match '/(cards|betacards|cardicons|cardui|cardmods|templates|1024|512)') { return '04' }
        if ($p -match '/relics' -or $p -match '/outline') { return '05' }
        if ($p -match '/powers') { return '06' }
        if ($p -match '/(potion|rewards|dailies)') { return '07' }
        if ($p -match '/(events|scenes|ending|tip)') { return '08' }
        if ($p -match '/(ui|menustuff|mainmenu|map|screens)') { return '09' }
        if ($p -match '/(monsters|bosses|charboss)') { return '10' }
        if ($p -match '/(vfx|orbs|orb|effects)') { return '11' }
        # 换装皮肤包下面几乎全是角色美术（肩甲 / 尸体 / 立绘 / 备用皮），别掉进 12
        if ($p -match '/reskincontent/') { return '03' }
        return '12'
    }

    return '12'   # .rar .zip .dbproj .lnk 等
}

# 需要连 .json 一起带走的目录（Spine 动画数据就在 .png/.atlas 旁边）
$zip = [System.IO.Compression.ZipFile]::OpenRead($Jar)
try {
    $atlasDirs = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($e in $zip.Entries) {
        if ([System.IO.Path]::GetExtension($e.Name) -ieq '.atlas') {
            # ⚠ 必须括号套住：方法实参里的 `-replace 'a','b'` 那个逗号会被当成**参数分隔符**
            [void]$atlasDirs.Add(([System.IO.Path]::GetDirectoryName($e.FullName) -replace '\\', '/'))
        }
    }

    $rows = [System.Collections.Generic.List[object]]::new()
    $skipExt = @('.class', '.mf', '.properties', '.dbproj', '.lnk', '.xml', '.maven', '.java', '.kt')
    $n = 0
    $bytes = 0L

    foreach ($e in $zip.Entries) {
        if ([string]::IsNullOrEmpty($e.Name)) { continue }      # 目录条目
        $full = ($e.FullName -replace '\\', '/')
        if ($full -like 'META-INF/*') { continue }
        if ($full -like 'saveData/*') { continue }
        if ($full -match '/localization/') { continue }         # 本地化 json 不是美术资源

        $ext = [System.IO.Path]::GetExtension($e.Name).ToLowerInvariant()
        if ($skipExt -contains $ext) { continue }

        $dir = [System.IO.Path]::GetDirectoryName($full) -replace '\\', '/'
        if ($ext -eq '.json' -and -not $atlasDirs.Contains($dir)) { continue }   # 只要动画用的 json

        $cat = Get-Category $full $ext

        # 拆成「角色标签 + 剩余相对路径」
        $segs = $full.Split('/')
        $first = $segs[0]
        $char = if ($segs.Length -gt 1) { Get-CharLabel $first } else { '_jar' }
        $rest = if ($segs.Length -gt 1) { $segs[1..($segs.Length - 1)] } else { $segs }
        if ($rest.Length -gt 0 -and $rest[0] -in @('images', 'GuardianImages', 'SlimeboundImages', 'audio', 'music', 'img', 'sound')) {
            $rest = if ($rest.Length -gt 1) { $rest[1..($rest.Length - 1)] } else { @() }
        }
        $rel = if ($rest.Length -gt 0) { $rest -join '/' } else { $e.Name }

        # 13 是专题目录：那一批立绘全来自 expansioncontentResources，再套一层角色名没意义 ⇒ 拍平
        if ($cat -eq '13') { $char = '' }
        $catDir = Join-Path $Out $CatDirs[$cat]
        $dest = if ($char) { Join-Path (Join-Path $catDir $char) $rel } else { Join-Path $catDir $rel }
        $destDir = Split-Path -Parent $dest
        if (-not (Test-Path -LiteralPath $destDir)) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }

        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, $dest, $true)
        $rows.Add([pscustomobject]@{
                类别   = $CatDirs[$cat]
                角色   = $char
                文件   = $rel
                KB     = [math]::Round($e.Length / 1KB, 1)
                原始路径 = $full
            })
        $n++
        $bytes += $e.Length
        if ($n % 500 -eq 0) { Write-Host ("  已提取 {0} 个文件…" -f $n) }
    }

    # ── 「角色当 boss」专题：把散落在各处的零碎图**再复制一份**到 13，方便一次看完 ──
    #    来源是反编译 class 常量池里实际引用的路径（见 Get-Category 里那段注释）。
    $curated = @(
        @{ Match = '^downfallResources/images/bgShrooms\.png$'; Sub = 'Ironclad蘑菇boss背景' }
        @{ Match = '^downfallResources/images/fgShrooms\.png$'; Sub = 'Ironclad蘑菇boss背景' }
        @{ Match = '^downfallResources/images/vfx/CultistPotion\.png$'; Sub = 'Hermit第3幕Doomsday' }
        @{ Match = '^downfallResources/images/powers/infinite_blades_marker\.png$'; Sub = '卡牌高亮标记' }
        @{ Match = '^downfallResources/images/powers/inverse_bias_marker\.png$'; Sub = '卡牌高亮标记' }
        # ⚠ 这几张（Blood / Strength / HeartOfIron / PowerPotion…）在 jar 里**不存在** ——
        #   那几条是塔 1 原版 images/vfx/… 的引用，所以干脆整个 vfx 目录一起复制
        @{ Match = '^downfallResources/images/vfx/'; Sub = '通用特效(boss与药水卡)' }
        @{ Match = '^hermitResources/images/char/hermit/'; Sub = '隐士Hermit立绘' }
        @{ Match = '^downfallResources/images/charBoss/Crowbot/'; Sub = 'Crowbot-boss立绘' }
    )
    foreach ($c in $curated) {
        foreach ($e in $zip.Entries) {
            if ([string]::IsNullOrEmpty($e.Name)) { continue }
            if ($e.FullName -notmatch $c.Match) { continue }
            $sub = $e.FullName -replace $c.Match, ''
            if ([string]::IsNullOrWhiteSpace($sub)) { $sub = $e.Name }   # 精确命中的单文件 ⇒ 用文件名
            $sub = $sub.TrimStart('/')
            $dest = Join-Path (Join-Path (Join-Path $Out $CatDirs['13']) $c.Sub) $sub
            $destDir = Split-Path -Parent $dest
            if (-not (Test-Path -LiteralPath $destDir)) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, $dest, $true)
        }
    }
}
finally { $zip.Dispose() }

# ── 索引 + 说明 ────────────────────────────────────────────────────────────
$rows | Sort-Object 类别, 角色, 文件 | Export-Csv -LiteralPath (Join-Path $Out '素材索引.csv') -NoTypeInformation -Encoding UTF8

$summary = $rows | Group-Object 类别 | Sort-Object Name | ForEach-Object {
    $sz = [math]::Round((($_.Group | Measure-Object KB -Sum).Sum) / 1024, 1)
    "| $($_.Name) | $($_.Count) | $sz MB |"
}

$readme = @"
# 杀戮尖塔 1「崩坠 Downfall」mod 素材提取

来源：``$Jar``（创意工坊物品 1610056683，459.7 MB 的 jar）
**只含崩坠 mod 自己的资源**，塔 1 原版资源在 ``desktop-1.0.jar`` 里，没有动。

共 **$n** 个文件 / **$([math]::Round($bytes/1MB,1)) MB**。

| 类别 | 文件数 | 体积 |
| --- | --- | --- |
$($summary -join "`r`n")

## 每个类别里还按角色分了一层

崩坠把多个角色的资源各自放在 ``<角色>Resources/`` 下，所以二级目录是角色名：
``champ``（勇士）、``collector``（收藏家）、``guardian``（守卫者）、``awakened``（觉醒者）、
``hermit``（隐士）、``hexamod``（六火亡魂）、``slimebound``（黏液）、``sneckomod``（蛇眼）、
``gremlin``（地精）、``bronze``（青铜）、``downfall-通用``（mod 自己的通用 UI / 音乐）、
``expansioncontent``（崩坠扩展内容）、``reskinContent``（皮肤/换装）。

## 几个要注意的点

- **角色立绘是图集（atlas）不是单张**：``03`` 里的 ``xxx.png`` 是 Spine 动画的**整张图集**，
  配套的 ``xxx.atlas`` / ``xxx.json`` 也在同一目录（没有它们图集无法对位）。要拆成逐帧图片需要额外工具。
- **卡图是单张 PNG**：``04`` 里每张卡一张，``xxx_p.png`` 是「升级后卡面」那张。
- **``1024`` / ``512`` 两个目录**是卡框/能量球/其他游戏卡框（``dimension`` 子目录），都归到 ``04``。
- ``12`` 里有作者留下的**源文件**：``.pdn``（Paint.NET）、``.xcf``（GIMP）、``.rar`` / ``.zip``、
  ``.scml``（Spriter 工程），以及少量零散 PNG。``hermitResources/images/images.zip``（16 MB）
  是作者打包的原始素材，可能需要再解一层。
- 全部路径对照表在 ``素材索引.csv``（可排序/筛选，含原始 jar 内路径）。

## ``13-角色当Boss`` 是专题目录

崩坠有两套完全相反的设计，别混：

| | 方向 | 例子 | 在哪 |
| --- | --- | --- | --- |
| A | **塔 1 原版 boss → 可玩角色** | 守卫者 / 六火亡魂 / 勇士 / 收藏家 / 觉醒者 / 黏液 / 蛇眼 / 青铜…… | ``03``、``04``（``bg_boss_*`` 卡框）、``reskinContent`` |
| B | **可玩角色 → boss**（charbosses 包） | 铁甲战士 / 静默猎手 / 故障机器人 / 观者 / 隐士 / 商人 | **``13``** |

``13`` 的内容 + 反编译结论见该目录里的 ``对照表.md``：charbosses 那套**只自带 5 组专属立绘**，
其余幕数直接借塔 1 原版角色的骨骼动画（``images/characters/…``，在 ``desktop-1.0.jar``，本次没提）。

## ``14-角色Boss语音`` 是另一个脚本产的

``13`` 里的 boss 立绘是 mod 自己的，但**它们的语音**（打攻击牌时喊的那一声）不在 mod 里 ——
崩坠只调用了塔 1 原版的 ``VO_IRONCLAD_*`` / ``VO_SILENT_*`` / ``VO_GREMLINCALM_*`` / ``VO_NEOW_2B`` / ``VO_CULTIST_*``。
那批文件由 ``tools\extract-sts1-charboss-voices.ps1`` 从 ``desktop-1.0.jar`` 提取到 ``14-角色Boss语音(取自塔1原版VO)\``，
重跑本脚本时**不会**删掉它。

重新生成：``powershell -ExecutionPolicy Bypass -File tools\extract-sts1-downfall-assets.ps1``
（语音另跑：``tools\extract-sts1-charboss-voices.ps1``）
"@
Set-Content -LiteralPath (Join-Path $Out 'README.md') -Value $readme -Encoding utf8

# ── 13 的对照表（内容来自对 class 常量池的实际扫描，不是猜的） ────────────────
$charbossMd = @"
# 角色当 Boss（`charbosses` 包）素材对照表

崩坠的 ``charbosses`` 包 = **把可玩角色做成 boss**（铁甲战士 / 静默猎手 / 故障机器人 / 观者 / 隐士 / 商人）。
它分幕、且每幕有 ``Simpler`` 与 ``NewAge`` 两套牌组（archetype）。

## 结论先说

**这个包几乎不自带美术。** 立绘/战斗动画都是直接加载**塔 1 原版角色的骨骼**：

| boss | 立绘来源（class 常量池实证） | 是不是 mod 自己的 |
| --- | --- | --- |
| 铁甲战士 | ``images/characters/ironclad/idle/skeleton.atlas/.json`` | ❌ 塔 1 原版 |
| 静默猎手 | ``images/characters/theSilent/idle/skeleton.*`` | ❌ 塔 1 原版 |
| 故障机器人 | ``images/characters/defect/idle/skeleton.*`` | ❌ 塔 1 原版 |
| 观者 | ``images/characters/watcher/idle/skeleton.*`` + ``eye_anim`` | ❌ 塔 1 原版 |
| 商人（第 3 幕） | ``images/monsters/merchant/noShadow/skeleton.*`` + ``skeletonGlow`` | ❌ 塔 1 原版 |
| 隐士 | ``hermitResources/images/char/hermit/Hermit.atlas/.json`` | ✅ mod 自己的（见 ``隐士Hermit立绘\``） |

## mod 自带的几组专属立绘（本目录 ``bosses\<角色>\<幕>\``）

| 幕 | archetype（Simpler 与 NewAge 共用同一张） | 立绘 |
| --- | --- | --- |
| 故障机器人 第 1 幕 | ``ArchetypeAct1VoidsSimple`` / ``ArchetypeAct1TurboNewAge`` | ``defect/1/Defect_thief.png`` |
| 故障机器人 第 2 幕 | ``ArchetypeAct2ClawSimple`` / ``ArchetypeAct2ClawNewAge`` | ``defect/2/clockworkDefect.png`` |
| 静默猎手 第 1 幕 | ``ArchetypeAct1PoisonSimple`` / ``ArchetypeAct1PoisonNewAge`` | ``silent/1/Poison_Silent.png`` |
| 隐士 第 1 幕 | ``ArchetypeAct1SharpshooterSimple`` / ``...NewAge`` | ``hermit/1/Hermit_Sharp.png`` |
| 铁甲战士 第 2 幕 | ``ArchetypeAct2MushroomsSimple`` / ``...NewAge`` | ``ironclad/2/Mushroomskeleton.png`` ⚠ 见下 |
| （小怪）虱子 Tangerine | ``charbosses/monsters/LouseTangerine`` | ``hermit/1/tangerine/skeleton_2.png`` |

⚠ ``Mushroomskeleton`` 在整个 jar 里**找不到任何引用它的 class**（只有它自己的 .atlas）⇒ 大概率是
没启用的残留资源；铁甲战士第 2 幕实际用的仍是塔 1 原版骨骼 + 下面那两个蘑菇图。

## 本目录其它子文件夹

| 子目录 | 内容 | 被谁用 |
| --- | --- | --- |
| ``Ironclad蘑菇boss背景\`` | ``bgShrooms.png`` / ``fgShrooms.png`` | ``CharBossIronclad`` 战斗场景的前景/背景蘑菇 |
| ``Hermit第3幕Doomsday\`` | ``CultistPotion.png`` | ``ArchetypeAct3DoomsdayNewAge`` |
| ``卡牌高亮标记\`` | ``infinite_blades_marker.png`` / ``inverse_bias_marker.png`` | ``charbosses/bosses/HighlightCards``（把手牌里关键牌点亮） |
| ``通用特效(boss与药水卡)\`` | 整个 ``downfallResources/images/vfx``（15 张） | ``charbosses/cards/colorless/potioncards/*`` 等 |
| ``隐士Hermit立绘\`` | ``Hermit.atlas/.json/.png`` + ``Spriter/`` + ``orb/`` | 隐士 boss 本体立绘（同时是隐士这个可玩角色的立绘） |
| ``Crowbot-boss立绘\`` | ``crowbot.atlas/.json/.png`` | 只在 boss 战出现的角色（``downfallResources/images/charBoss/Crowbot``） |

## 卡图：boss 出牌用的牌面

``charbosses/cards/`` 下的红/绿/蓝/紫牌（55/50/62/72 张）是**原版牌的 boss 版**，
牌面直接借塔 1 原版卡图（``images/cards/…``）⇒ 本次没提；隐士那 51 张（``En*``）用的是
``hermitResources/images/cards/*.png``，已经在 ``04-卡牌图与卡框 Cards\hermit\cards\`` 里，
部分是上面那两个 ``marker`` 图之外就没了。诅咒/无色/药水卡同理。
"@
Set-Content -LiteralPath (Join-Path $Out (Join-Path $CatDirs['13'] '对照表.md')) -Value $charbossMd -Encoding utf8

Write-Host ''
Write-Host "完成：$n 个文件 / $([math]::Round($bytes/1MB,1)) MB → $Out"
$rows | Group-Object 类别 | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0,-45} {1,5} 个" -f $_.Name, $_.Count)
}
