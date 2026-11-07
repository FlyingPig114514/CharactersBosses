<#
.SYNOPSIS
  给已有的 CharacterBosses 数据表补齐「一场战斗一行」需要的新列。

.DESCRIPTION
  v0.2.253 起，模组不再「每只 Boss 一行」，而是**一场战斗一行**，并新增这些列：

    bossTotal      本场 Boss 只数              数字
    bossesKilled   本场被打死的只数            数字
    maxHpTotal     本场所有 Boss 最大血量之和  数字
    hpTotal        本场所有 Boss 结束时剩余之和 数字
    bossList       参战 Boss 的 Key 列表       文本（SILENT_SHIV,NECROBINDER_ERADICATE,…）
    hpList         每只结束时剩余血量明细      文本（SILENT_BOSS:0;REGENT_BOSS:140）

  v0.2.264 起再加玩家侧的血量证据（回答「轻松赢还是险胜」）：

    pMaxHp         玩家最大生命合计            数字
    pHpStart       进 Boss 战时的血量合计      数字
    pHpEnd         Boss 战结束时的血量合计     数字
    pDmg           战斗中玩家掉的血合计        数字（**含自伤/献祭**，不含格挡吸收）
    pHeal          战斗中玩家回的血合计        数字（药水/遗物/卡牌，缩放到"这局挨了多少"已分开）
    pList          每人「开局/上限-结束/上限」  文本（72/80-21/80;60/75-0/75）

  `boss` / `bossMaxHp` / `bossHp` 仍然保留，含义改成「**主 Boss**（阵容第 0 位）」。

  本脚本**幂等**：已经存在的列会跳过，只补缺的。

  用法（不传参数时**直接从模组配置读** feishu 四项，不用手打 secret）：
    powershell -ExecutionPolicy Bypass -File tools\feishu-add-columns.ps1
    powershell -ExecutionPolicy Bypass -File tools\feishu-add-columns.ps1 `
        -AppId cli_xxx -AppSecret xxx `
        -BitableAppToken BITABLE_APP_TOKEN_REDACTED -TableId TABLE_ID_REDACTED
#>
[CmdletBinding()]
param(
    [string]$AppId = '',
    [string]$AppSecret = '',
    [string]$BitableAppToken = '',
    [string]$TableId = '',
    [string]$ConfigPath = ''
)

# 不传参数时**直接从模组配置里读**（%APPDATA%\SlayTheSpire2\mod_configs\CharacterBosses\charbosses_config.cfg），
# 这样不用手打 secret。
if (-not $ConfigPath) {
    $ConfigPath = Join-Path $env:APPDATA 'SlayTheSpire2\mod_configs\CharacterBosses\charbosses_config.cfg'
}
if ((-not $AppId -or -not $AppSecret -or -not $BitableAppToken -or -not $TableId) -and (Test-Path $ConfigPath)) {
    try {
        $cfg = [System.IO.File]::ReadAllText($ConfigPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
        if (-not $AppId) { $AppId = [string]$cfg.feishuAppId }
        if (-not $AppSecret) { $AppSecret = [string]$cfg.feishuAppSecret }
        if (-not $BitableAppToken) { $BitableAppToken = [string]$cfg.feishuBitableAppToken }
        if (-not $TableId) { $TableId = [string]$cfg.feishuTableId }
        Write-Host ("配置已从 {0} 读取" -f $ConfigPath) -ForegroundColor DarkGray
    }
    catch { Write-Host ("读配置失败（忽略）: {0}" -f $_.Exception.Message) -ForegroundColor Yellow }
}
foreach ($pair in @(@('AppId', $AppId), @('AppSecret', $AppSecret), @('BitableAppToken', $BitableAppToken), @('TableId', $TableId))) {
    if ([string]::IsNullOrWhiteSpace([string]$pair[1])) {
        Write-Host ("缺少 {0}：要么在 cfg 里配好 feishu* 四项，要么用参数显式传进来。" -f $pair[0]) -ForegroundColor Red
        exit 1
    }
}

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$base = 'https://open.feishu.cn/open-apis'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Invoke-Curl {
    param([ValidateSet('GET', 'POST')][string]$Method, [string]$Url, $Body = $null, [string]$Token = '')
    $curl = Join-Path $env:SystemRoot 'System32\curl.exe'
    if (-not (Test-Path $curl)) { $curl = 'curl.exe' }
    $args = @('-s', '-S', '-X', $Method, $Url, '-H', 'Content-Type: application/json; charset=utf-8')
    if ($Token) { $args += @('-H', "Authorization: Bearer $Token") }
    $tmp = $null
    try {
        if ($null -ne $Body) {
            $tmp = [System.IO.Path]::GetTempFileName()
            [System.IO.File]::WriteAllText($tmp, ($Body | ConvertTo-Json -Depth 10 -Compress), $Utf8NoBom)
            $args += @('--data-binary', "@$tmp")
        }
        $out = & $curl @args 2>&1 | Out-String
    }
    finally { if ($tmp) { Remove-Item $tmp -Force -ErrorAction SilentlyContinue } }
    if ([string]::IsNullOrWhiteSpace($out)) { throw '飞书没有返回内容（网络/代理？）' }
    return ($out | ConvertFrom-Json)
}

Write-Host '[1/3] 取 tenant_access_token ...'
$tok = Invoke-Curl -Method POST -Url "$base/auth/v3/tenant_access_token/internal" `
    -Body @{ app_id = $AppId.Trim(); app_secret = $AppSecret.Trim() }
if ($tok.code -ne 0 -or -not $tok.tenant_access_token) {
    Write-Host ("失败：code={0} msg={1}" -f $tok.code, $tok.msg) -ForegroundColor Red
    exit 1
}
$token = [string]$tok.tenant_access_token
Write-Host '      ok'

Write-Host '[2/3] 读现有字段 ...'
$res = Invoke-Curl -Method GET -Url "$base/bitable/v1/apps/$BitableAppToken/tables/$TableId/fields?page_size=200" -Token $token
if ($res.code -ne 0) {
    Write-Host ("读字段失败：code={0} msg={1}" -f $res.code, $res.msg) -ForegroundColor Red
    exit 1
}
$existing = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)
foreach ($f in @($res.data.items)) { if ($f -and $f.field_name) { [void]$existing.Add([string]$f.field_name) } }
Write-Host ("      现有 {0} 列：{1}" -f $existing.Count, (($existing | Sort-Object) -join ', '))

# 1 = 多行文本，2 = 数字
$want = @(
    @{ name = 'bossTotal';     type = 2 },
    @{ name = 'bossesKilled';  type = 2 },
    @{ name = 'maxHpTotal';    type = 2 },
    @{ name = 'hpTotal';       type = 2 },
    @{ name = 'bossList';      type = 1 },
    @{ name = 'hpList';        type = 1 },
    # v0.2.264：玩家侧血量证据
    @{ name = 'pMaxHp';        type = 2 },
    @{ name = 'pHpStart';      type = 2 },
    @{ name = 'pHpEnd';        type = 2 },
    @{ name = 'pDmg';          type = 2 },
    @{ name = 'pHeal';         type = 2 },
    @{ name = 'pList';         type = 1 },
    # v0.2.267：玩家操控的角色 + 角色所属模组（模组角色超模会影响强度数据）
    @{ name = 'pChar';         type = 1 },
    @{ name = 'pCharMod';      type = 1 },
    # 2026-11-01（用户：「为这个表格再增加一个筛选条件：进阶为10」）：
    # 模组一直在写 `ascension`，但表里没这一列 ⇒ 上传时被跳过。补上之后
    # `tools\feishu-stats.ps1 -Asc10` 才筛得出来。
    @{ name = 'ascension';     type = 2 }
    # 注：v0.2.277 曾加过 `mode`（solo/mp），用户 2026-10-31 说「直接看玩家人数就行」⇒ v0.2.278 起不再写
)

Write-Host '[3/3] 补缺失的列 ...'
$added = 0
foreach ($w in $want) {
    if ($existing.Contains($w.name)) {
        Write-Host ("      · {0} 已存在，跳过" -f $w.name) -ForegroundColor DarkGray
        continue
    }
    $r = Invoke-Curl -Method POST -Url "$base/bitable/v1/apps/$BitableAppToken/tables/$TableId/fields" `
        -Body @{ field_name = $w.name; type = $w.type } -Token $token
    if ($r.code -ne 0) {
        Write-Host ("      ✘ {0} 添加失败：code={1} msg={2}" -f $w.name, $r.code, $r.msg) -ForegroundColor Red
    }
    else {
        $added++
        Write-Host ("      ✔ {0}（type={1}）" -f $w.name, $w.type) -ForegroundColor Green
    }
}

Write-Host ''
Write-Host '====================== 完成 ======================' -ForegroundColor Green
Write-Host ("新增 {0} 列。" -f $added)
Write-Host '之后模组写数据时会自动填这些列（表里没有的列会被跳过，加上了就自动生效）。'
