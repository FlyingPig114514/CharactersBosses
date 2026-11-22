# make-bosses-md.ps1 -- 生成 docs\bosses.md（「全部 Boss 一览」表）
#
# 为什么要脚本化：这张表以前是手写的，写着「27 个档位」，而实际设计早就涨到 40+ 套
# （抉择储 / 塔罗愚 / 彩虹机 / 蛇咬猎 / 血肉骨 … 全都没进表）⇒ 手抄必然过期。
# 现在和百科表同源：都读 tools\wiki\data.json（由 tools\wiki-dump 从模组 DLL 反射导出）。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File tools\wiki\make-bosses-md.ps1
#
# ⚠ 本文件必须存成 **UTF-8 with BOM**（Windows PowerShell 5.1 否则按 ANSI 读，中文变乱码且可能直接语法错）。

[CmdletBinding()]
param(
    [string]$DataPath = '',
    [string]$OutPath = ''
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptRoot)

if ([string]::IsNullOrEmpty($DataPath)) { $DataPath = Join-Path $scriptRoot 'data.json' }
if ([string]::IsNullOrEmpty($OutPath)) { $OutPath = Join-Path $repoRoot 'docs\bosses.md' }

if (-not (Test-Path -LiteralPath $DataPath)) { throw ('data.json 不存在: ' + $DataPath) }

$json = [System.IO.File]::ReadAllText($DataPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
if ($json -eq $null -or $json.bosses -eq $null) { throw 'data.json 里没有 bosses' }

# 角色出场顺序（沿用旧表的顺序：猎人 → 故障 → 亡灵 → 储君 → 铁甲；愚者 / 观者是后加的，排最后）
$charOrder = @('静默猎手', '故障机器人', '亡灵契约师', '储君', '铁甲战士', '愚者', '观者')
$charShort = @{
    '静默猎手'   = '猎人'
    '故障机器人' = '故障'
    '亡灵契约师' = '亡灵'
    '储君'       = '储君'
    '铁甲战士'   = '铁甲'
    '愚者'       = '愚者'
    '观者'       = '观者'
}
$actName = @{ 1 = '一层'; 2 = '二层'; 3 = '三层' }

function First-Line([string]$text) {
    if ([string]::IsNullOrEmpty($text)) { return '' }
    foreach ($raw in ($text -split "`n")) {
        $t = $raw.Trim()
        if ($t.Length -gt 0) { return $t }
    }
    return ''
}

# 表里只放「够认人」的短名：观者主设计在 data.json 里没有中文名（存的是 WATCHER_DIVINITY 第N幕 这种占位），
# 而观者那几个变体的特殊效果文案没有标题行（一上来就是整句）⇒ 只取前 18 个字。
function Short-Special([string]$text) {
    $t = First-Line $text
    if ([string]::IsNullOrEmpty($t)) { return '-' }
    if ($t.Length -gt 18) { return $t.Substring(0, 18) + '…' }
    return $t
}

function Design-Name([string]$name) {
    if ([string]::IsNullOrEmpty($name)) { return '-' }
    if ($name -match '^(WATCHER|FOOL|IRONCLAD|SILENT|DEFECT|NECROBINDER|REGENT)_(BOSS|DIVINITY)') {
        return '（本幕主设计）'
    }
    return $name
}

# 收集：act -> @( @{char; name; key; special} )，同一角色内保持 data.json 的顺序（主设计在前、变体在后）
$byAct = @{}
foreach ($b in $json.bosses) {
    $character = [string]$b.character
    if ($charOrder -notcontains $character) { continue }
    foreach ($a in $b.acts) {
        $act = 0
        try { $act = [int]$a.act } catch { continue }
        if ($act -lt 1 -or $act -gt 3) { continue }        # 四幕那些只是外壳，不进表
        if (-not $byAct.ContainsKey($act)) { $byAct[$act] = New-Object System.Collections.ArrayList }
        if (-not [string]::IsNullOrEmpty([string]$a.name)) {
            [void]$byAct[$act].Add([pscustomobject]@{
                character = $character
                name      = [string]$a.name
                key       = [string]$a.encounter
                special   = First-Line ([string]$a.special)
            })
        }
        foreach ($v in $a.variants) {
            if ([string]::IsNullOrEmpty([string]$v.name)) { continue }
            [void]$byAct[$act].Add([pscustomobject]@{
                character = $character
                name      = [string]$v.name
                key       = [string]$v.key
                special   = First-Line ([string]$v.special)
            })
        }
    }
}

$total = 0
foreach ($k in $byAct.Keys) { $total += $byAct[$k].Count }

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('')
[void]$sb.AppendLine('# 全部 Boss 一览（' + $total + ' 个档位）')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('> 登记规则：**「第几层 + 某角色的第几号」**。同一角色在同一场战斗里最多一只；主 Boss 的角色整局不重复。')
[void]$sb.AppendLine('> 控制台点名统一形状：`fight <KEY>_BOSS_ENCOUNTER`，**会把档位（幕数）钉死**，不管你现在在第几幕。')
[void]$sb.AppendLine('>')
[void]$sb.AppendLine('> 本文件由 `tools\wiki\make-bosses-md.ps1` 从 `tools\wiki\data.json` **自动生成，请勿手改**；')
[void]$sb.AppendLine('> 数据与百科页同源（`tools\wiki-dump` 从模组 DLL 反射导出），改完模组要重新导出 + 重新生成。')
[void]$sb.AppendLine('')

for ($act = 1; $act -le 3; $act++) {
    if (-not $byAct.ContainsKey($act)) { continue }
    $items = $byAct[$act]
    [void]$sb.AppendLine('## ' + $actName[$act] + '（' + $items.Count + ' 个）')
    [void]$sb.AppendLine('')
    [void]$sb.AppendLine('| 角色·号 | 设计 | Key | 特殊效果 | 对战指令 |')
    [void]$sb.AppendLine('|---|---|---|---|---|')
    foreach ($character in $charOrder) {
        $no = 0
        foreach ($d in ($items | Where-Object { $_.character -eq $character })) {
            $no++
            $who = $charShort[$character] + ' ' + $no + ' 号'
            [void]$sb.AppendLine('| ' + $who + ' | ' + (Design-Name $d.name) + ' | `' + $d.key + '` | ' + (Short-Special $d.special) +
                                 ' | `fight ' + $d.key + '_BOSS_ENCOUNTER` |')
        }
    }
    [void]$sb.AppendLine('')
}

[void]$sb.AppendLine('## 两类调试用遭遇（一般用不上）')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('**① 不钉档位的基础版**（强度按你**当前所在的幕**走，变体按房间遭遇/种子判）：')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('```')
[void]$sb.AppendLine('fight IRONCLAD_BOSS_ENCOUNTER      fight SILENT_BOSS_ENCOUNTER')
[void]$sb.AppendLine('fight DEFECT_BOSS_ENCOUNTER        fight NECROBINDER_BOSS_ENCOUNTER')
[void]$sb.AppendLine('fight REGENT_BOSS_ENCOUNTER')
[void]$sb.AppendLine('```')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('**② 「普通战斗房」版**（同一个 Boss，但生成在普通怪物房，不进 Boss 房，方便反复测）：')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('```')
[void]$sb.AppendLine('fight IRONCLAD_BOSS_NORMAL_ENCOUNTER        fight SILENT_BOSS_NORMAL_ENCOUNTER')
[void]$sb.AppendLine('fight SILENT_ENVENOM_BOSS_NORMAL_ENCOUNTER  fight SILENT_SLY_BOSS_NORMAL_ENCOUNTER')
[void]$sb.AppendLine('fight SILENT_SHIV_BOSS_NORMAL_ENCOUNTER     fight DEFECT_BOSS_NORMAL_ENCOUNTER')
[void]$sb.AppendLine('fight NECROBINDER_BOSS_NORMAL_ENCOUNTER     fight REGENT_BOSS_NORMAL_ENCOUNTER')
[void]$sb.AppendLine('```')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 注意')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('1. **`fight` 每一场只出 1 只 Boss**（走的是单怪遭遇，不套「按玩家数排阵容」那套）；想看多 Boss 联机阵容得正常爬塔。')
[void]$sb.AppendLine('2. 内部名字分 4 层，别混：')
[void]$sb.AppendLine('   - 怪物模型 id `MONSTER.*` —— **每个角色只有 1 个**，同角色所有设计共用（例如电球机和黑球机都是 `MONSTER.DEFECT_BOSS`）；')
[void]$sb.AppendLine('   - 阵容 Key（上表的 Key）—— 决定「用哪一套设计」，' + $total + ' 个；')
[void]$sb.AppendLine('   - 遭遇 id —— 上表的对战指令，=`<Key>_BOSS_ENCOUNTER`；')
[void]$sb.AppendLine('   - C# 类 —— `Monsters.DefectBoss` 之类，7 个（含愚者 / 观者）。')
[void]$sb.AppendLine('3. 缺本地化文案的遭遇会在战斗顶部/败北提示里直接显示 `encounters.XXX` 原文（发现了就补 `content/localization/<语言>/encounters.json`）。')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('## 特殊效果文案在哪')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('`content/localization/<语言>/monsters.json`，键名形如 `DEFECT_BOSS.thunder`、`REGENT_BOSS.minion`；')
[void]$sb.AppendLine('代码里由 `src/Hooks/Core/BossDefs.cs` 的 `SpecialLocByAct` / 变体的 `SpecialLoc` 指定，')
[void]$sb.AppendLine('真正的效果实现在 `src/Hooks/Powers/*.cs`。')

$dir = Split-Path -Parent $OutPath
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
[System.IO.File]::WriteAllText($OutPath, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))

Write-Output ('OK: ' + $OutPath)
Write-Output ('  档位 ' + $total + ' 个（一层 ' + $(if ($byAct.ContainsKey(1)) { $byAct[1].Count } else { 0 }) +
             ' / 二层 ' + $(if ($byAct.ContainsKey(2)) { $byAct[2].Count } else { 0 }) +
             ' / 三层 ' + $(if ($byAct.ContainsKey(3)) { $byAct[3].Count } else { 0 }) + '）')
