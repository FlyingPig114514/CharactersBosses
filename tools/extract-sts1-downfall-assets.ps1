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
    else { Write-Host "清空旧目录：$Out"; Remove-Item -LiteralPath $Out -Recurse -Force }
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

        $dest = Join-Path (Join-Path (Join-Path $Out $CatDirs[$cat]) $char) $rel
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

重新生成：``pwsh -File tools\extract-sts1-downfall-assets.ps1``
"@
Set-Content -LiteralPath (Join-Path $Out 'README.md') -Value $readme -Encoding utf8

Write-Host ''
Write-Host "完成：$n 个文件 / $([math]::Round($bytes/1MB,1)) MB → $Out"
$rows | Group-Object 类别 | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0,-45} {1,5} 个" -f $_.Name, $_.Count)
}
