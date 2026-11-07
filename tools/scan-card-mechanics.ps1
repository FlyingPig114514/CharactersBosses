# 扫描卡牌的「角色专属机制依赖」，找出不适合当 Boss 牌的卡
#
#   powershell -ExecutionPolicy Bypass -File tools\scan-card-mechanics.ps1 [-Chars Necrobinder,Regent]
#
# 为什么：Boss 的卡是用「影子 Player」打出的，凡是依赖**玩家侧专属实体/UI**的效果都可能出问题。
# 目前确认的一条：`PlayerCmd.AddPet(player)` 召唤的随从（亡灵契约师的 Osty）会被算成**玩家侧盟友**，
# 于是 Boss 打这张牌等于给玩家送一个帮手 —— 这类牌必须排除。
#
# 输出：.recon/card-mechanics.tsv（字符<TAB>类名<TAB>命中的机制关键字）
# 只读 references\sts2.dll，不碰游戏。每张牌一次反编译，约 1 张/秒。

param(
    [string]$Assembly = 'references\sts2.dll',
    [string[]]$Chars = @('Necrobinder', 'Regent', 'Defect', 'Silent'),
    [string]$OutDir = '.recon'
)

$ErrorActionPreference = 'Continue'
$dotnet = Join-Path $PSScriptRoot 'dotnet\dotnet.exe'
$cli    = Join-Path $PSScriptRoot 'ilspy-cli\bin\Release\net10.0\ilspycli.dll'

# 关心的机制关键字：命中的会写进结果
$tokens = @(
    'AddPet',            # 召唤随从 -> 随从会站到玩家一侧，Boss 不能用
    'OrbCmd',            # 充能球（参数是 Player，理论可用）
    'OrbQueue',
    'PlayerCmd.GainStars',
    'PlayerCmd.LoseStars',
    'Osty',              # 亡灵契约师的随从
    'NCombatRoom',       # 直接摸 UI 节点
    'NGame.',
    'NCard',
    'NMapScreen',
    'Input.',            # 读输入
    'Settings.'
)

$cardPools = [ordered]@{
    Ironclad   = 'MegaCrit.Sts2.Core.Models.CardPools.IroncladCardPool'
    Silent     = 'MegaCrit.Sts2.Core.Models.CardPools.SilentCardPool'
    Defect     = 'MegaCrit.Sts2.Core.Models.CardPools.DefectCardPool'
    Necrobinder= 'MegaCrit.Sts2.Core.Models.CardPools.NecrobinderCardPool'
    Regent     = 'MegaCrit.Sts2.Core.Models.CardPools.RegentCardPool'
    Colorless  = 'MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool'
}

$rows = New-Object System.Collections.Generic.List[string]
foreach ($char in $Chars) {
    if (-not $cardPools.Contains($char)) { Write-Host "跳过未知角色 $char"; continue }

    $dump = & $dotnet $cli type $Assembly $cardPools[$char] 2>&1
    $types = $dump | Select-String -Pattern 'ModelDb\.Card<' |
        ForEach-Object { ($_.Line -replace '.*ModelDb\.Card<', '' -replace '>\(\).*', '') } | Sort-Object -Unique
    Write-Host "[scan] $char : $($types.Count) 张"

    $i = 0
    foreach ($t in $types) {
        $i++
        if ($i % 20 -eq 0) { Write-Host "  ... $i/$($types.Count)" }
        $body = & $dotnet $cli type $Assembly "MegaCrit.Sts2.Core.Models.Cards.$t" 2>&1
        $text = $body -join "`n"
        $hits = @()
        foreach ($token in $tokens) {
            if ($text.Contains($token)) { $hits += $token }
        }
        if ($hits.Count -gt 0) { $rows.Add(("{0}`t{1}`t{2}" -f $char, $t, ($hits -join ','))) }
    }
}

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }
[IO.File]::WriteAllLines((Join-Path $OutDir 'card-mechanics.tsv'), $rows, (New-Object Text.UTF8Encoding($false)))
Write-Host "[scan] 写入 $($rows.Count) 行 -> card-mechanics.tsv"
Write-Host '完成'
