<#
.SYNOPSIS
  在**已有**的 CharacterBosses 多维表格里，再建一张「杂项记录」数据表（diag / consent）。

.DESCRIPTION
  为什么要这张表（用户 2026-10-30）：
    主表里除了对局数据（kind=boss_fight），还会混进 `diag`（问题诊断）和 `consent`（同意记录）——
    它们只有 `at/kind/mod/act/encounter/line` 几列有值，看起来像是「数据不全」，也影响统计。
    模组从 v0.2.246 起：**杂项默认不上传**（只留本机）；
    如果给 cfg 填了 `feishuDiagTableId`，杂项会写进这张单独的表，主表就只剩对局数据。

  用法：
    powershell -ExecutionPolicy Bypass -File tools\feishu-add-diag-table.ps1 `
        -AppId cli_xxx -AppSecret xxxx -BitableAppToken BITABLE_APP_TOKEN_REDACTED

  成功后它会打印一行 `"feishuDiagTableId": "tbl…",`，把它加到
  %APPDATA%\SlayTheSpire2\mod_configs\CharacterBosses\charbosses_config.cfg 里即可（**关着游戏改**）。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AppId,
    [Parameter(Mandatory = $true)][string]$AppSecret,
    [Parameter(Mandatory = $true)][string]$BitableAppToken,
    [string]$TableName = 'diag_log'
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$base = 'https://open.feishu.cn/open-apis'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Post-Json([string]$url, $body, [string]$token) {
    $json = $body | ConvertTo-Json -Depth 12 -Compress
    $tmp = [System.IO.Path]::GetTempFileName()
    try {
        [System.IO.File]::WriteAllText($tmp, $json, $Utf8NoBom)
        $curl = Join-Path $env:SystemRoot 'System32\curl.exe'
        if (-not (Test-Path $curl)) { $curl = 'curl.exe' }
        $args = @('-s', '-S', '-X', 'POST', $url,
                  '-H', 'Content-Type: application/json; charset=utf-8',
                  '--data-binary', "@$tmp")
        if ($token) { $args += @('-H', "Authorization: Bearer $token") }
        $out = & $curl @args 2>&1 | Out-String
    }
    finally { try { Remove-Item $tmp -Force -ErrorAction SilentlyContinue } catch { } }
    if ([string]::IsNullOrWhiteSpace($out)) { Write-Host '请求没有返回（网络/代理？）' -ForegroundColor Red; exit 1 }
    try { return ($out | ConvertFrom-Json) }
    catch { Write-Host ('飞书返回的不是 JSON：{0}' -f $out.Trim()) -ForegroundColor Red; exit 1 }
}

Write-Host '[1/3] 获取 tenant_access_token ...'
$tok = Post-Json "$base/auth/v3/tenant_access_token/internal" @{ app_id = $AppId.Trim(); app_secret = $AppSecret.Trim() } $null
if (-not $tok.tenant_access_token) {
    Write-Host ("失败：code={0} msg={1}" -f $tok.code, $tok.msg) -ForegroundColor Red
    exit 1
}
$token = [string]$tok.tenant_access_token
Write-Host '      ok'

# 字段：只要 diag/consent 用得到的这几列（1=多行文本 2=数字）
$fields = @(
    @{ field_name = 'at';        type = 1 },
    @{ field_name = 'kind';      type = 1 },
    @{ field_name = 'mod';       type = 1 },
    @{ field_name = 'act';       type = 2 },
    @{ field_name = 'encounter'; type = 1 },
    @{ field_name = 'line';      type = 1 }
)

Write-Host "[2/3] 在多维表格 $BitableAppToken 里创建数据表「$TableName」..."
$table = Post-Json "$base/bitable/v1/apps/$BitableAppToken/tables" @{
    table = @{
        name              = $TableName
        default_view_name = '全部杂项'
        fields            = $fields
    }
} $token
if ($table.code -ne 0 -or -not $table.data.table_id) {
    Write-Host ("失败：code={0} msg={1}" -f $table.code, $table.msg) -ForegroundColor Red
    Write-Host '  → 91403 / 1254302：应用没被加为该多维表格协作者（或是别人建的表）；1254004：app_token 不对。'
    exit 1
}
$tableId = [string]$table.data.table_id

Write-Host ''
Write-Host '====================== 完成了 ======================' -ForegroundColor Green
Write-Host ("diag 表 table_id = {0}" -f $tableId)
Write-Host ''
Write-Host '把下面这一行加到 %APPDATA%\SlayTheSpire2\mod_configs\CharacterBosses\charbosses_config.cfg（关着游戏改）：'
Write-Host ''
Write-Host ('  "feishuDiagTableId": "{0}",' -f $tableId) -ForegroundColor Green
Write-Host ''
Write-Host '之后：对局数据继续进主表；diag/consent 进这张表。' -ForegroundColor DarkGray
Write-Host '不填的话，diag/consent 就**只留本机、不上传**（主表一样干净）。' -ForegroundColor DarkGray
