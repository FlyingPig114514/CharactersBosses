# feishu-grant-permission.ps1 -- 让自己能编辑这张多维表格（不用邮箱、不用域名）
#
# WHY: 这张表格是用「应用（机器人）」的 tenant_access_token 通过 API 建的 ⇒ **所有者是那个应用**，
#      你的飞书账号默认只能"通过链接看"，网页上没有编辑按钮、也建不了视图。
#
# 三条路，任选：
#   ① **链接分享**（推荐，最省事、不需要邮箱）：把这张表的"链接分享"设成
#      「组织内获得链接的人**可编辑**」⇒ 用同一个租户的账号打开链接就能改。
#      -LinkShare tenant_editable（默认） / tenant_readable / anyone_editable（组织外也能改，慎用） / closed
#   ② 具体成员：-Email you@example.com 或 -MemberType openid -MemberId ou_xxxx
#   ③ 转让所有权：-Email … -TransferOwner（之后网页上什么都能改）
#
# ⚠ 飞书**不需要买域名**：免费版/团队版会自动分给你 `<你的团队>.feishu.cn` 这样的子域，
#   那就是你的租户域名，`xxx.feishu.cn/base/...` 就在它下面。要买域名的只有"企业自定义域名"那种高级功能。
#
# ⚠ 接口需要应用具备云文档权限 `drive:drive`；没开会返回 99991672
#   ⇒ 开放平台「权限管理」加上 →「创建版本并发布」→ 再跑本脚本。
#
# 用法（AppId/AppSecret/AppToken 不传时自动读 charbosses_config.cfg）：
#   powershell -ExecutionPolicy Bypass -File tools\feishu-grant-permission.ps1
#   powershell -ExecutionPolicy Bypass -File tools\feishu-grant-permission.ps1 -LinkShare anyone_editable
#   powershell -ExecutionPolicy Bypass -File tools\feishu-grant-permission.ps1 -Email you@example.com
#   powershell -ExecutionPolicy Bypass -File tools\feishu-grant-permission.ps1 -Email you@example.com -TransferOwner
[CmdletBinding()]
param(
    [string]$AppId = '',
    [string]$AppSecret = '',
    [string]$BitableAppToken = '',
    [string]$TableId = '',
    [string]$ConfigPath = '',

    [ValidateSet('tenant_editable', 'tenant_readable', 'anyone_editable', 'anyone_readable', 'closed', '')]
    [string]$LinkShare = 'tenant_editable',

    [ValidateSet('email', 'openid', 'userid')]
    [string]$MemberType = 'email',
    [string]$MemberId = '',
    [string]$Email = '',

    [ValidateSet('view', 'edit', 'full_access')]
    [string]$Perm = 'full_access',

    [switch]$TransferOwner,
    [switch]$NoNotify
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

$doMember = $false
if ($Email) { $MemberType = 'email'; $MemberId = $Email; $doMember = $true }
elseif ($MemberId) { $doMember = $true }

if ($TransferOwner -and -not $MemberId) {
    Write-Host '-TransferOwner 需要接收人：用 -Email you@example.com 或 -MemberType/-MemberId。' -ForegroundColor Red
    exit 1
}
if (-not $doMember -and -not $TransferOwner -and -not $LinkShare) {
    Write-Host '没有要做的事：给 -LinkShare（默认 tenant_editable）、-Email 或 -TransferOwner 之一。' -ForegroundColor Red
    exit 1
}

if (-not $ConfigPath) {
    $ConfigPath = Join-Path $env:APPDATA 'SlayTheSpire2\mod_configs\CharacterBosses\charbosses_config.cfg'
}
if ((-not $AppId -or -not $AppSecret -or -not $BitableAppToken) -and (Test-Path $ConfigPath)) {
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
foreach ($pair in @(@('AppId', $AppId), @('AppSecret', $AppSecret), @('BitableAppToken', $BitableAppToken))) {
    if ([string]::IsNullOrWhiteSpace([string]$pair[1])) {
        Write-Host ("缺少 {0}：在 cfg 里配好 feishu* 四项，或用参数传进来。" -f $pair[0]) -ForegroundColor Red
        exit 1
    }
}

$base = 'https://open.feishu.cn/open-apis'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Invoke-Curl {
    param([ValidateSet('GET', 'POST', 'PATCH')][string]$Method, [string]$Url, $Body = $null, [string]$Token = '')
    $curl = Join-Path $env:SystemRoot 'System32\curl.exe'
    if (-not (Test-Path $curl)) { $curl = 'curl.exe' }
    $a = @('-s', '-S', '-X', $Method, $Url, '-H', 'Content-Type: application/json; charset=utf-8')
    if ($Token) { $a += @('-H', "Authorization: Bearer $Token") }
    $tmp = $null
    try {
        if ($null -ne $Body) {
            $tmp = [System.IO.Path]::GetTempFileName()
            [System.IO.File]::WriteAllText($tmp, ($Body | ConvertTo-Json -Depth 10 -Compress), $Utf8NoBom)
            $a += @('--data-binary', "@$tmp")
        }
        $out = & $curl @a 2>&1 | Out-String
    }
    finally { if ($tmp) { Remove-Item $tmp -Force -ErrorAction SilentlyContinue } }
    if ([string]::IsNullOrWhiteSpace($out)) { throw '飞书没有返回内容（网络/代理？）' }
    try { return ($out | ConvertFrom-Json) }
    catch {
        Write-Host ('飞书返回的不是 JSON：{0}' -f $out.Trim()) -ForegroundColor Red
        exit 1
    }
}

function Show-Fail($res) {
    Write-Host ("失败：code={0} msg={1}" -f $res.code, $res.msg) -ForegroundColor Red
    switch ([string]$res.code) {
        '99991672' { Write-Host '  → 应用缺云文档权限：开放平台「权限管理」加 drive:drive，再「创建版本并发布」，然后重跑。' }
        '1063002'  { Write-Host '  → 文档不存在，或应用不是它的所有者（app_token 写对了吗？）' }
        '1064001'  { Write-Host '  → 参数不合法：member_id 必须是本企业成员（邮箱要属于本租户）。' }
        '1064006'  { Write-Host '  → 找不到这个成员：确认邮箱和你登录飞书用的一致；或者干脆用默认的链接分享，不需要邮箱。' }
        '1061045'  { Write-Host '  → 该设置不被允许（例如企业关闭了对外分享）⇒ 换 -LinkShare tenant_editable 试试。' }
        default    { }
    }
}

Write-Host '[1/2] 取 tenant_access_token ...'
$tok = Invoke-Curl -Method POST -Url "$base/auth/v3/tenant_access_token/internal" `
    -Body @{ app_id = $AppId.Trim(); app_secret = $AppSecret.Trim() }
if ($tok.code -ne 0 -or -not $tok.tenant_access_token) { Show-Fail $tok; exit 1 }
$token = [string]$tok.tenant_access_token
Write-Host '      ok'

# ① 链接分享：组织内获得链接的人可编辑 —— 不需要邮箱、不需要任何成员信息
if ($LinkShare) {
    Write-Host ("[2/2] 设置链接分享 = {0} ..." -f $LinkShare)
    $res = Invoke-Curl -Method PATCH -Url "$base/drive/v1/permissions/$BitableAppToken/public?type=bitable" `
        -Token $token -Body @{ link_share_entity = $LinkShare }
    if ($res.code -ne 0) { Show-Fail $res }
    else {
        $verb = if ($LinkShare -like '*editable*') { '编辑' } else { '阅读' }
        Write-Host ("      ok —— 现在「拿到链接的人」可以{0}（{1}）" -f $verb, $LinkShare) -ForegroundColor Green
        $now = Invoke-Curl -Method GET -Url "$base/drive/v1/permissions/$BitableAppToken/public?type=bitable" -Token $token
        if ($now.code -eq 0 -and $now.data) {
            Write-Host ("      当前 link_share_entity={0}" -f $now.data.link_share_entity) -ForegroundColor DarkGray
        }
    }
}
else {
    Write-Host '[2/2] 跳过链接分享（-LinkShare ""）'
}

$q = 'type=bitable'
if (-not $NoNotify) { $q += '&need_notification=true' }

if ($TransferOwner) {
    Write-Host ("[额外] 把所有者转让给 {0}:{1} ..." -f $MemberType, $MemberId)
    $url = "$base/drive/v1/permissions/$BitableAppToken/members/transfer_owner?$q" +
           '&remove_old_owner=false&stay_put=false&old_owner_perm=full_access'
    $res = Invoke-Curl -Method POST -Url $url -Body @{ member_type = $MemberType; member_id = $MemberId } -Token $token
    if ($res.code -ne 0) { Show-Fail $res }
    else { Write-Host '      ok —— 你现在是所有者，网页上可以随便改、也能建视图' -ForegroundColor Green }
}
elseif ($doMember) {
    Write-Host ("[额外] 添加协作者 {0}:{1}（perm={2}）..." -f $MemberType, $MemberId, $Perm)
    $res = Invoke-Curl -Method POST -Url "$base/drive/v1/permissions/$BitableAppToken/members?$q" -Token $token -Body @{
        member_type = $MemberType
        member_id   = $MemberId
        perm        = $Perm
        perm_type   = 'container'
        type        = 'user'
    }
    if ($res.code -ne 0) { Show-Fail $res }
    else { Write-Host ("      ok —— 已是协作者（{0}）" -f $Perm) -ForegroundColor Green }
}

Write-Host ''
Write-Host '打开方式（用你登录飞书的同一个账号）：' -ForegroundColor Cyan
Write-Host '  1) 网页版飞书 →「云文档 / 与我共享」里按名字找这张多维表格'
Write-Host ('  2) 或者直接开：https://www.feishu.cn/base/' + $BitableAppToken + $(if ($TableId) { '?table=' + $TableId } else { '' }))
Write-Host '     （若你的租户不是 www.feishu.cn，就用 tools\feishu-check.ps1 打印的真实链接）'
Write-Host '  想确认权限：表格右上「…」→ 权限设置，看"获得链接的人"是不是「可编辑」。'
