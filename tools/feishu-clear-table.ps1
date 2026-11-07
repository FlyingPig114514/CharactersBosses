<#
.SYNOPSIS
  清空（或按条件删除）CharacterBosses 多维表格里的记录。

.DESCRIPTION
  为什么需要脚本（用户 2026-10-31：「先把表格清空吧，里面现在全是我自己的对局数据，还有脏数据」）：
    · 表格是用 API 建的，记录一条条点删很烦；
    · 我们自己测试期产生的数据（含早期判定错误的 lose）要一次性清掉，之后只留真实玩家数据。

  做法：先分页列出所有记录拿 `record_id`，再按 500 条一批 `batch_delete`。
  `-DryRun` 只列不删，先看一眼要删多少条最稳。

  用法：
    # 先看看有多少条（不删）
    powershell -ExecutionPolicy Bypass -File tools\feishu-clear-table.ps1 `
        -AppId cli_xxx -AppSecret xxx `
        -BitableAppToken BITABLE_APP_TOKEN_REDACTED -TableId TABLE_ID_REDACTED -DryRun

    # 真删
    powershell -ExecutionPolicy Bypass -File tools\feishu-clear-table.ps1 `
        -AppId cli_xxx -AppSecret xxx `
        -BitableAppToken BITABLE_APP_TOKEN_REDACTED -TableId TABLE_ID_REDACTED

  可选 -KeepKind boss_fight：**只保留** kind=boss_fight 的记录，其它（diag 等）删掉。
  可选 -MinVersion 0.2.276：**只保留** mod 版本 >= 这个值的记录；版本更低的全部删除
    （用户 2026-10-31：v0.2.276 之前的版本因为战败局没记录，表里只有“赢”，数据有偏 ⇒ 全删）。
    读不到版本号的记录**不删**（会单独报数），避免误伤。
  两个过滤可以同时给：命中任一「保留」条件就留下。
#>
[CmdletBinding()]
param(
    [string]$AppId = '',
    [string]$AppSecret = '',
    [string]$BitableAppToken = '',
    [string]$TableId = '',
    [string]$ConfigPath = '',
    [string]$KeepKind = '',
    [string]$MinVersion = '',
    [switch]$DryRun
)

# 把 mod 字段（形如 `v0.2.276 / 2026-10-31（…）`）里的版本号取出来
function Get-ModVersion([string]$mod) {
    if ([string]::IsNullOrWhiteSpace($mod)) { return $null }
    $m = [regex]::Match($mod, 'v?(\d+)\.(\d+)\.(\d+)')
    if (-not $m.Success) { return $null }
    try { return [version]("{0}.{1}.{2}" -f $m.Groups[1].Value, $m.Groups[2].Value, $m.Groups[3].Value) }
    catch { return $null }
}

$minVer = $null
if ($MinVersion) {
    $minVer = Get-ModVersion $MinVersion
    if ($null -eq $minVer) {
        Write-Host ("-MinVersion 看不懂：{0}（要写成 0.2.276 这样）" -f $MinVersion) -ForegroundColor Red
        exit 1
    }
}

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
    try { return ($out | ConvertFrom-Json) }
    catch {
        Write-Host ('飞书返回的不是 JSON（多半是网络/证书问题）：{0}' -f $out.Trim()) -ForegroundColor Red
        exit 1
    }
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

Write-Host '[2/3] 列出记录 ...'
$toDelete = New-Object System.Collections.ArrayList
$kept = 0
$keptByKind = 0
$keptByVersion = 0
$keptUnknownVersion = 0
$pageToken = ''
$page = 0
while ($true) {
    $page++
    $url = "$base/bitable/v1/apps/$BitableAppToken/tables/$TableId/records?page_size=500"
    if ($pageToken) { $url += "&page_token=$([uri]::EscapeDataString($pageToken))" }
    $res = Invoke-Curl -Method GET -Url $url -Token $token
    if ($res.code -ne 0) {
        Write-Host ("读记录失败：code={0} msg={1}" -f $res.code, $res.msg) -ForegroundColor Red
        exit 1
    }
    foreach ($it in @($res.data.items)) {
        if (-not $it) { continue }

        $kind = ''
        $modText = ''
        try {
            $names = $it.fields.PSObject.Properties.Name
            if ($names -contains 'kind') { $kind = [string]$it.fields.kind }
            if ($names -contains 'mod') { $modText = [string]$it.fields.mod }
        }
        catch { }

        # ── 保留判定：给了几个过滤条件就**同时满足**（AND）──────────────
        #   · 只给 -KeepKind      ⇒ 只按 kind 判
        #   · 只给 -MinVersion    ⇒ 只按版本判
        #   · 两个都给            ⇒ 必须「kind 对 且 版本够」才留（这就是
        #     `-MinVersion 0.2.276 -KeepKind boss_fight` = 只留下新版本的对局行）
        $recVer = $null
        if ($null -ne $minVer) { $recVer = Get-ModVersion $modText }

        $keepKindOk = $true
        if ($KeepKind) { $keepKindOk = ($kind -eq $KeepKind) }

        $keepVerOk = $true
        if ($null -ne $minVer) {
            if ($null -eq $recVer) {
                # 读不到版本号：**不删**（单独报数，免得误伤）
                $keepVerOk = $true
                $keptUnknownVersion++
            }
            else { $keepVerOk = ($recVer -ge $minVer) }
        }

        if ($keepKindOk -and $keepVerOk) {
            $kept++
            if ($KeepKind -and $kind -eq $KeepKind) { $keptByKind++ }
            if ($null -ne $minVer -and $null -ne $recVer -and $recVer -ge $minVer) { $keptByVersion++ }
            continue
        }

        [void]$toDelete.Add([string]$it.record_id)
    }
    Write-Host ("      第 {0} 页：{1} 条（待删累计 {2}，保留 {3}）" -f $page, @($res.data.items).Count, $toDelete.Count, $kept)
    if ($res.data.has_more -and $res.data.page_token) { $pageToken = [string]$res.data.page_token } else { break }
    if ($page -ge 60) { Write-Host '      （已取 60 页，先停）' -ForegroundColor Yellow; break }
}

if ($MinVersion) {
    Write-Host ("      版本过滤：>= {0} 保留 {1} 条；无版本号保留 {2} 条；将要删除的是低于 {0} 的 {3} 条" -f `
        $minVer, $keptByVersion, $keptUnknownVersion, $toDelete.Count) -ForegroundColor DarkGray
}
if ($KeepKind) {
    Write-Host ("      kind 过滤：= {0} 保留 {1} 条" -f $KeepKind, $keptByKind) -ForegroundColor DarkGray
}

if ($toDelete.Count -eq 0) {
    Write-Host '没有需要删除的记录（表格已经是干净的）。' -ForegroundColor Green
    exit 0
}

if ($DryRun) {
    Write-Host ''
    Write-Host ("-DryRun：本会删除 {0} 条；保留 {1} 条。去掉 -DryRun 再跑一次才会真删。" -f $toDelete.Count, $kept) -ForegroundColor Yellow
    exit 0
}

Write-Host '[3/3] 分批删除 ...'
$deleted = 0
for ($start = 0; $start -lt $toDelete.Count; $start += 500) {
    $chunk = @()
    for ($i = $start; $i -lt [Math]::Min($start + 500, $toDelete.Count); $i++) { $chunk += $toDelete[$i] }
    $res = Invoke-Curl -Method POST `
        -Url "$base/bitable/v1/apps/$BitableAppToken/tables/$TableId/records/batch_delete" `
        -Body @{ records = $chunk } -Token $token
    if ($res.code -ne 0) {
        Write-Host ("删除失败：code={0} msg={1}" -f $res.code, $res.msg) -ForegroundColor Red
        Write-Host '  → 91403/1254302：应用没有这张表的编辑权限；1254004：table_id 不对。'
        exit 1
    }
    $deleted += $chunk.Count
    Write-Host ("      已删 {0} 条" -f $deleted)
}

Write-Host ''
Write-Host '====================== 完成 ======================' -ForegroundColor Green
Write-Host ("删除 {0} 条；保留 {1} 条。" -f $deleted, $kept)
Write-Host '（本地日志文件不受影响：%APPDATA%\SlayTheSpire2\mod_configs\CharacterBosses\telemetry\）'
