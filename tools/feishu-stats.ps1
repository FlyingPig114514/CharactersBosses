# feishu-stats.ps1 -- 把 CharacterBosses 的多维表格数据直接算成一张「难度/胜率」表
#
# 只统计**补全的对局行**：kind=boss_fight、result 非空、lineup 不带 console:/aborted: 前缀。
# 默认按 Boss 分组；可以再按玩家人数或「玩家角色所属模组」切一刀。
#
# 用法（参数全部可选，app_id/secret/app_token/table_id 自动从 cfg 读）：
#   powershell -ExecutionPolicy Bypass -File tools\feishu-stats.ps1
#   ... -ExplodeMulti                   # 多人局：每场记到它场上的每只 Boss 名下（默认只记阵容第 0 位）
#   ... -By boss                        # 只按角色（5 个，会把奇巧猎/炼药猎/刀猎混在一起）
#   ... -By variant+players             # 34 只 Boss 各自再按人数拆
#   ... -CleanSolo                      # 只算「单人 + 玩家用原版角色」的对局（= -SoloOnly -VanillaChars）
#   ... -SoloOnly                       # 只算单人局
#   ... -Players 2,3,4                  # 只算指定人数的对局（多人胜率用这个；单人写 -Players 1）
#   ... -VanillaChars                   # 只算玩家用原版角色的对局（pCharMod 全是 vanilla）
#   ... -Asc10                          # 只算**进阶 10** 的对局（用户 2026-11-01 要求）
#   ... -Ascension 9,10                 # 进阶筛选的通用写法（单个也行：-Ascension 10）
#   ... -By charmod                     # 按「玩家角色所属模组」分组（看模组角色有没有把数据拉偏）
#
# 默认 -By variant：分组键取自 lineup（第 0 位 = 主 Boss）/ bossList（多人局全部），
# 也就是**当前 34 只 Boss 的变体键**（SILENT_SLY / SILENT_BREWER / IRONCLAD_ANGER / DEFECT_DARK…）。
# 注意 `boss` 列只有**角色**（SILENT_BOSS / DEFECT_BOSS…，5 个），别用它数 Boss 数量。
#   ... -MinVersion 0.2.276             # 只算这个版本及以后的数据
#   ... -Csv "$env:USERPROFILE\Desktop\charbosses-stats.csv"
#   ... -NoCsv                          # 不导 CSV
#
# 列含义：
#   对局      参与统计的场次
#   胜率      win / (win + lose)
#   回合      平均回合数
#   承伤      pDmg / pMaxHp 的平均值（玩家一共掉了多少比例的血，越高越难）
#   残血      pHpEnd / pMaxHp 的平均值（结束时还剩多少血，越低越险）
#   Boss剩    hpTotal / maxHpTotal 的平均值（Boss 还剩多少比例，输的局会很高）
#   打死      bossesKilled / bossTotal 的平均值（多人局看打死几只）
[CmdletBinding()]
param(
    [string]$AppId = '',
    [string]$AppSecret = '',
    [string]$BitableAppToken = '',
    [string]$TableId = '',
    [string]$ConfigPath = '',

    # variant = 34 只 Boss（`lineup`/`bossList` 里的变体键，如 SILENT_SLY / IRONCLAD_ANGER）；
    # boss    = 只按角色（5 个：SILENT_BOSS…），会把奇巧猎/炼药猎/刀猎混在一起
    [ValidateSet('variant', 'variant+players', 'variant+charmod',
                 'boss', 'players', 'charmod', 'boss+players', 'boss+charmod')]
    [string]$By = 'variant',

    # 多人局：把每一场记到它场上的**每只** Boss 名下（默认只记阵容第 0 位那只）
    [switch]$ExplodeMulti,

    # 只要单人局（players = 1；读不到人数的旧行也会被排除）
    [switch]$SoloOnly,
    # 只要**玩家用的是原版角色**的对局（pCharMod 全是 vanilla；读不到 pCharMod 的旧行排除）
    [switch]$VanillaChars,
    # 一次到位：单人 + 原版角色（= -SoloOnly -VanillaChars）
    [switch]$CleanSolo,

    # 只要**进阶 10** 的对局（用户 2026-11-01：「为这个表格再增加一个筛选条件：进阶为10」）
    [switch]$Asc10,
    # 进阶筛选的通用写法：单个（9）或多个（9,10）都行；行里读不到进阶的会被排除
    [string]$Ascension = '',

    # 只要**指定人数**的对局（用户 2026-11-11：「计算多人胜率，分 2/3/4 人」）：
    #   写 `-Players 2,3,4` / `-Players 2` / `-Players 2 3 4` 都行；
    #   读不到 players 的旧行会被排除（和 -SoloOnly 同一套口径）。
    [string]$Players = '',

    [string]$MinVersion = '',
    [string]$Csv = '',
    [switch]$NoCsv,

    # 难度分档（玩家胜率）。默认按原版基准：原版 Boss 的玩家胜率大致在 75%～88%
    #   （最狠的原版 Boss 击杀率约 25% ⇒ 玩家胜率 75%；最温和的约 12% ⇒ 88%）
    [double]$HardBelow = 0.75,
    [double]$EasyAbove = 0.88,
    # 自检：不连飞书，用几行假数据验证统计与排版
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }

# ── 参数：不传就从 cfg 读 ────────────────────────────────────────────────
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
        Write-Host ("缺少 {0}：在 cfg 里配好 feishu* 四项，或用参数传进来。" -f $pair[0]) -ForegroundColor Red
        exit 1
    }
}

function Get-ModVersion([string]$mod) {
    if ([string]::IsNullOrWhiteSpace($mod)) { return $null }
    $m = [regex]::Match($mod, 'v?(\d+)\.(\d+)\.(\d+)')
    if (-not $m.Success) { return $null }
    try { return [version]("{0}.{1}.{2}" -f $m.Groups[1].Value, $m.Groups[2].Value, $m.Groups[3].Value) }
    catch { return $null }
}
$minVer = $null
if ($MinVersion) { $minVer = Get-ModVersion $MinVersion }

# -CleanSolo = 单人 + 原版角色（用户 2026-10-31：「去掉多人，只囊括使用原版角色的单人战斗」）
if ($CleanSolo) { $SoloOnly = $true; $VanillaChars = $true }

# -Asc10 是 -Ascension 10 的简写；两个都给时以 -Ascension 为准（并集）
$ascWanted = @()
if ($Ascension) {
    foreach ($part in ($Ascension -split '[,;\s]+')) {
        $p = $part.Trim()
        if (-not $p) { continue }
        $n = 0
        if ([int]::TryParse($p, [ref]$n)) { $ascWanted += $n }
        else { Write-Host ("-Ascension 里的「{0}」不是数字，已忽略" -f $p) -ForegroundColor Yellow }
    }
}
if ($Asc10 -and -not ($ascWanted -contains 10)) { $ascWanted += 10 }
$ascWanted = @($ascWanted | Select-Object -Unique)
$useAscFilter = $ascWanted.Count -gt 0

# -Players 2,3,4 —— 只要这些人数（含单人时也可以写 1）
$playersWanted = @()
if ($Players) {
    foreach ($part in ($Players -split '[,;\s]+')) {
        $p = $part.Trim()
        if (-not $p) { continue }
        $n = 0
        if ([int]::TryParse($p, [ref]$n) -and $n -gt 0) { $playersWanted += $n }
        else { Write-Host ("-Players 里的「{0}」不是正整数，已忽略" -f $p) -ForegroundColor Yellow }
    }
}
$playersWanted = @($playersWanted | Select-Object -Unique)
$usePlayersFilter = $playersWanted.Count -gt 0

# ── HTTP（和其它 feishu-*.ps1 一样走 curl，避免 PS 的 TLS 老问题）───────────
$base = 'https://open.feishu.cn/open-apis'
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Invoke-Curl {
    param([ValidateSet('GET', 'POST')][string]$Method, [string]$Url, $Body = $null, [string]$Token = '')
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

$rows = New-Object System.Collections.ArrayList
$skipped = @{ diag = 0; aborted = 0; console = 0; old = 0; noresult = 0; multi = 0; modchar = 0; nocharmod = 0 }
if ($SelfTest) {
    Write-Host '[自检] 不连飞书，用假数据验证统计与排版 ...' -ForegroundColor Yellow
    $skipped = @{ diag = 2; aborted = 1; console = 1; old = 3; noresult = 0; multi = 0; modchar = 0; nocharmod = 0; noasc = 0 }
    $fakeRows = @(
        @{ boss = 'SILENT_BOSS';   variant = 'SILENT_SLY';      variants = @('SILENT_SLY');      players = 1; charMod = 'vanilla'; result = 'win';  turns = 7;  pMaxHp = 70; pDmg = 18; pHpEnd = 52; hpTotal = 0;   maxHpTotal = 220; killed = 1; bossTotal = 1; asc = 10 },
        @{ boss = 'SILENT_BOSS';   variant = 'SILENT_SLY';      variants = @('SILENT_SLY');      players = 1; charMod = 'vanilla'; result = 'win';  turns = 9;  pMaxHp = 70; pDmg = 34; pHpEnd = 21; hpTotal = 0;   maxHpTotal = 220; killed = 1; bossTotal = 1; asc = 10.0 },
        @{ boss = 'SILENT_BOSS';   variant = 'SILENT_SLY';      variants = @('SILENT_SLY');      players = 1; charMod = 'vanilla'; result = 'lose'; turns = 12; pMaxHp = 70; pDmg = 71; pHpEnd = 0;  hpTotal = 96;  maxHpTotal = 220; killed = 0; bossTotal = 1; asc = 10 },
        @{ boss = 'SILENT_BOSS';   variant = 'SILENT_BREWER';   variants = @('SILENT_BREWER');   players = 1; charMod = 'vanilla'; result = 'win';  turns = 6;  pMaxHp = 70; pDmg = 12; pHpEnd = 58; hpTotal = 0;   maxHpTotal = 220; killed = 1; bossTotal = 1; asc = 5 },
        @{ boss = 'DEFECT_BOSS';   variant = 'DEFECT_DARK';     variants = @('DEFECT_DARK','IRONCLAD_ANGER','REGENT_STAR','NECROBINDER_DOOM'); players = 4; charMod = 'Downfall#3737335127'; result = 'win'; turns = 5; pMaxHp = 300; pDmg = 210; pHpEnd = 180; hpTotal = 0; maxHpTotal = 900; killed = 4; bossTotal = 4; asc = 10 },
        @{ boss = 'DEFECT_BOSS';   variant = 'DEFECT_DARK';     variants = @('DEFECT_DARK','IRONCLAD_VULN','REGENT_FORGE','NECROBINDER_SOUL'); players = 4; charMod = 'vanilla,vanilla,vanilla,vanilla'; result = 'lose'; turns = 8; pMaxHp = 300; pDmg = 320; pHpEnd = 0; hpTotal = 240; maxHpTotal = 900; killed = 2; bossTotal = 4; asc = 10 }
    )
    foreach ($r in $fakeRows) {
        [void]$rows.Add([pscustomobject]@{
            boss = $r.boss; variant = $r.variant; variants = $r.variants
            bossList = ($r.variants -join ','); players = $r.players; charMod = $r.charMod
            result = $r.result; turns = $r.turns; pMaxHp = $r.pMaxHp; pDmg = $r.pDmg; pHpEnd = $r.pHpEnd
            hpTotal = $r.hpTotal; maxHpTotal = $r.maxHpTotal; killed = $r.killed; bossTotal = $r.bossTotal
            ascension = $r.asc
        })
    }
}
else {Write-Host '[1/3] 取 tenant_access_token ...'
$tok = Invoke-Curl -Method POST -Url "$base/auth/v3/tenant_access_token/internal" `
    -Body @{ app_id = $AppId.Trim(); app_secret = $AppSecret.Trim() }
if ($tok.code -ne 0 -or -not $tok.tenant_access_token) {
    Write-Host ("失败：code={0} msg={1}" -f $tok.code, $tok.msg) -ForegroundColor Red
    exit 1
}
$token = [string]$tok.tenant_access_token
Write-Host '      ok'

# ── 字段取值（文本可能是字符串 / 富文本数组；数字可能是字符串）────────────
function AsText($v) {
    if ($null -eq $v) { return '' }
    if ($v -is [string]) { return $v }
    if ($v -is [System.Array]) {
        $sb = ''
        foreach ($seg in $v) {
            if ($null -eq $seg) { continue }
            if ($seg -is [string]) { $sb += $seg }
            elseif ($seg.PSObject.Properties.Name -contains 'text') { $sb += [string]$seg.text }
            else { $sb += [string]$seg }
        }
        return $sb
    }
    if ($v.PSObject.Properties.Name -contains 'text') { return [string]$v.text }
    return [string]$v
}
function AsNum($v) {
    try {
        if ($null -eq $v) { return $null }
        $t = AsText $v
        if ([string]::IsNullOrWhiteSpace($t)) { return $null }
        $d = 0.0
        if ([double]::TryParse($t, [ref]$d)) { return $d }
        return $null
    }
    catch { return $null }
}
function Fld($fields, $name) {
    if ($null -eq $fields) { return $null }
    if ($fields.PSObject.Properties.Name -contains $name) { return $fields.$name }
    return $null
}

Write-Host '[2/3] 拉取记录 ...'
$rows = New-Object System.Collections.ArrayList
$skipped = @{ diag = 0; aborted = 0; console = 0; old = 0; noresult = 0; multi = 0; modchar = 0; nocharmod = 0; noasc = 0 }
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
        $f = $it.fields

        $kind = (AsText (Fld $f 'kind')).Trim()
        if ($kind -ne 'boss_fight') { $skipped.diag++; continue }

        $lineup = AsText (Fld $f 'lineup')
        if ($lineup -like 'aborted:*') { $skipped.aborted++; continue }
        if ($lineup -like 'console:*') { $skipped.console++; continue }

        $result = (AsText (Fld $f 'result')).Trim().ToLowerInvariant()
        if ($result -ne 'win' -and $result -ne 'lose') { $skipped.noresult++; continue }

        if ($null -ne $minVer) {
            $v = Get-ModVersion (AsText (Fld $f 'mod'))
            if ($null -eq $v -or $v -lt $minVer) { $skipped.old++; continue }
        }


        $boss = (AsText (Fld $f 'boss')).Trim()
        if ([string]::IsNullOrWhiteSpace($boss)) { $boss = '?' }

        # ★ 变体（用户 2026-10-31：「你这是把所有的战士、猎人等不同 boss 混在一起了吗？我们目前有 18 个」）：
        #   `boss` 列只有**角色**（DEFECT_BOSS / SILENT_BOSS…，5 个），
        #   真正的 34 只 Boss 是**变体键**：`lineup` 里逗号分隔的那串（第 0 位 = 主 Boss），
        #   多人局用 `bossList`（4 只全在）。⚠ 我们自己的“阵营”写死在这两个字段里，别再指望 `boss` 列。
        $lineupKeys = @($lineup.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
        $listKeys = @(((AsText (Fld $f 'bossList')).Trim()).Split(',') |
                      ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
        $variant = if ($lineupKeys.Count -gt 0) { $lineupKeys[0] } else { $boss }
        $variants = if ($listKeys.Count -gt 0) { $listKeys } elseif ($lineupKeys.Count -gt 0) { $lineupKeys } else { @($boss) }

        [void]$rows.Add([pscustomobject]@{
            boss      = $boss
            variant   = $variant
            variants  = $variants
            bossList  = (AsText (Fld $f 'bossList')).Trim()
            players   = AsNum (Fld $f 'players')
            charMod   = (AsText (Fld $f 'pCharMod')).Trim()
            result    = $result
            turns     = AsNum (Fld $f 'turns')
            pMaxHp    = AsNum (Fld $f 'pMaxHp')
            pDmg      = AsNum (Fld $f 'pDmg')
            pHpEnd    = AsNum (Fld $f 'pHpEnd')
            hpTotal   = AsNum (Fld $f 'hpTotal')
            maxHpTotal= AsNum (Fld $f 'maxHpTotal')
            killed    = AsNum (Fld $f 'bossesKilled')
            bossTotal = AsNum (Fld $f 'bossTotal')
            ascension = AsNum (Fld $f 'ascension')
        })
    }
    Write-Host ("      第 {0} 页：{1} 条（已收 {2}）" -f $page, @($res.data.items).Count, $rows.Count)
    if ($res.data.has_more -and $res.data.page_token) { $pageToken = [string]$res.data.page_token } else { break }
}

}

# ── 过滤：只要单人 / 只要指定人数 / 只要原版角色 / 只要某个进阶（对「联网拉到的」和「自检假数据」都生效）──
if ($SoloOnly -or $usePlayersFilter -or $VanillaChars -or $useAscFilter) {
    $kept = New-Object System.Collections.ArrayList
    foreach ($r in $rows) {
        if ($SoloOnly) {
            if ($null -eq $r.players -or [int]$r.players -ne 1) { $skipped.multi++; continue }
        }
        if ($usePlayersFilter) {
            if ($null -eq $r.players) { $skipped.multi++; continue }              # 老版本没记人数
            if (-not ($playersWanted -contains [int]$r.players)) { $skipped.multi++; continue }
        }
        if ($VanillaChars) {
            $mods = @(([string]$r.charMod).Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
            if ($mods.Count -eq 0) { $skipped.nocharmod++; continue }            # 老版本没记 pCharMod
            if (@($mods | Where-Object { $_ -ne 'vanilla' }).Count -gt 0) { $skipped.modchar++; continue }
        }
        if ($useAscFilter) {
            # ⚠ 读不到进阶的老行一律排除（表里没这一列时**全部**会被排除 —— 先跑
            #   tools\feishu-add-columns.ps1 把 ascension 列建出来，新对局才会带上进阶）
            if ($null -eq $r.ascension) { $skipped.noasc++; continue }
            if (-not ($ascWanted -contains [int]$r.ascension)) { $skipped.noasc++; continue }
        }
        [void]$kept.Add($r)
    }
    $rows = $kept
}

$filterNote = @()
if ($SoloOnly) { $filterNote += '只要单人' }
if ($usePlayersFilter) { $filterNote += ('只要 ' + (($playersWanted | Sort-Object) -join '/') + ' 人局') }
if ($VanillaChars) { $filterNote += '只要原版角色' }
if ($useAscFilter) { $filterNote += ('只要进阶 ' + (($ascWanted | Sort-Object) -join '/')) }
Write-Host ("      跳过：非对局 {0}、未打完 {1}、控制台 {2}、无结果 {3}、旧版本 {4}、人数不符 {5}、模组角色 {6}、读不到角色模组 {7}、进阶不符 {8}" -f `
    $skipped.diag, $skipped.aborted, $skipped.console, $skipped.noresult, $skipped.old,
    $skipped.multi, $skipped.modchar, $skipped.nocharmod, $skipped.noasc) -ForegroundColor DarkGray
if ($filterNote.Count -gt 0) {
    Write-Host ("      过滤条件：" + ($filterNote -join ' + ')) -ForegroundColor DarkGray
}

if ($rows.Count -eq 0) {
    Write-Host '没有可统计的对局数据（可能是表里只有旧版本行，试试去掉 -MinVersion）。' -ForegroundColor Yellow
    exit 0
}

# ── 分组聚合 ─────────────────────────────────────────────────────────────
# 一行可能同时计入**多个**分组（多人局 + -ExplodeMulti：每个变体各记一次）。
function GroupKeys($r) {
    $n = if ($null -eq $r.players) { '?' } else { [int]$r.players }
    $m = if ([string]::IsNullOrWhiteSpace($r.charMod)) { '（未知）' } else { $r.charMod }
    switch ($By) {
        'variant'          { return @($r.variant) }
        'variant+players'  { return @("$($r.variant) · ${n} 人") }
        'variant+charmod'  { return @("$($r.variant) · $m") }
        'boss'             { return @($r.boss) }
        'players'          { return @("${n} 人局") }
        'charmod'          { return @($m) }
        'boss+players'     { return @("$($r.boss) · ${n} 人") }
        'boss+charmod'     { return @("$($r.boss) · $m") }
    }
    return @($r.variant)
}

$isVariantGroup = $By -like 'variant*'
$groups = @{}
$exploded = 0
foreach ($r in $rows) {
    $keys = @(GroupKeys $r)
    if ($ExplodeMulti -and $isVariantGroup -and $null -ne $r.players -and $r.players -gt 1) {
        # 多人局：同一场里的每只 Boss 各记一次（否则只有阵容第 0 位那只被统计）
        $keys = @()
        foreach ($v in $r.variants) {
            if ($By -eq 'variant') { $keys += $v }
            elseif ($By -eq 'variant+players') { $keys += "$v · $([int]$r.players) 人" }
            elseif ($By -eq 'variant+charmod') { $keys += "$v · $(if ([string]::IsNullOrWhiteSpace($r.charMod)) { '（未知）' } else { $r.charMod })" }
        }
        $keys = @($keys | Select-Object -Unique)
        $exploded++
    }

    foreach ($k in $keys) {
        if ([string]::IsNullOrWhiteSpace($k)) { $k = '?' }
        if (-not $groups.ContainsKey($k)) {
            $groups[$k] = [pscustomobject]@{
                Key = $k; Fights = 0; Wins = 0; Losses = 0
                Turns = 0.0; TurnsN = 0
                DmgRatio = 0.0; DmgN = 0
                HpEndRatio = 0.0; HpEndN = 0
                BossLeft = 0.0; BossLeftN = 0
                KillRatio = 0.0; KillN = 0
            }
        }
        $g = $groups[$k]
        $g.Fights++
        if ($r.result -eq 'win') { $g.Wins++ } else { $g.Losses++ }

        if ($null -ne $r.turns) { $g.Turns += $r.turns; $g.TurnsN++ }
        if ($null -ne $r.pMaxHp -and $r.pMaxHp -gt 0) {
            if ($null -ne $r.pDmg) { $g.DmgRatio += ($r.pDmg / $r.pMaxHp); $g.DmgN++ }
# v0.2.452: 结束血量不可能 > 最大生命（实测有 99762/70 这种坏行）⇒ 超了就跳过，别把「残血」列拉到几万 %
    if ($null -ne $r.pHpEnd -and $r.pHpEnd -le $r.pMaxHp) { $g.HpEndRatio += ($r.pHpEnd / $r.pMaxHp); $g.HpEndN++ }
        }
        if ($null -ne $r.maxHpTotal -and $r.maxHpTotal -gt 0 -and $null -ne $r.hpTotal) {
            $g.BossLeft += ($r.hpTotal / $r.maxHpTotal); $g.BossLeftN++
        }
        if ($null -ne $r.bossTotal -and $r.bossTotal -gt 0 -and $null -ne $r.killed) {
            $g.KillRatio += ($r.killed / $r.bossTotal); $g.KillN++
        }
    }
}
if ($ExplodeMulti) {
    Write-Host ("      多人局按 Boss 拆开统计：{0} 场（每场记到它场上的每只 Boss 名下）" -f $exploded) -ForegroundColor DarkGray
}

function Avg($sum, $n) { if ($n -gt 0) { return $sum / $n } else { return $null } }
function Pct($v) { if ($null -eq $v) { return ('{0,5:0.0}%' -f 0).Replace('0.0%', '—') } else { return ('{0,5:0.0}%' -f ($v * 100)) } }
function Num($v, $fmt) { if ($null -eq $v) { return ('{0,6}' -f '—') } else { return ($fmt -f $v) } }

# ── 用「显示宽度」对齐（中文字符占 2 列，`-f` 只按字符数算 ⇒ 以前表头和数据对不齐）──
function CharWidth([char]$ch) {
    $c = [int]$ch
    if ($c -ge 0x1100 -and (
            ($c -ge 0x2E80 -and $c -le 0xA4CF) -or
            ($c -ge 0xAC00 -and $c -le 0xD7A3) -or
            ($c -ge 0xF900 -and $c -le 0xFAFF) -or
            ($c -ge 0xFE30 -and $c -le 0xFE6F) -or
            ($c -ge 0xFF00 -and $c -le 0xFF60) -or
            ($c -ge 0xFFE0 -and $c -le 0xFFE6) -or
            ($c -ge 0x3000 -and $c -le 0x303F) -or
            $c -eq 0x2026)) { return 2 }
    return 1
}
function DispWidth([string]$s) {
    $w = 0
    foreach ($ch in $s.ToCharArray()) { $w += (CharWidth $ch) }
    return $w
}
function PadR([string]$s, [int]$w) {
    $d = DispWidth $s
    if ($d -ge $w) { return $s }
    return $s + (' ' * ($w - $d))
}
function PadL([string]$s, [int]$w) {
    $d = DispWidth $s
    if ($d -ge $w) { return $s }
    return (' ' * ($w - $d)) + $s
}
function CutDisp([string]$s, [int]$w) {
    if ((DispWidth $s) -le $w) { return $s }
    $sb = ''
    $acc = 0
    foreach ($ch in $s.ToCharArray()) {
        $cw = CharWidth $ch
        if ($acc + $cw -gt ($w - 2)) { break }
        $sb += $ch
        $acc += $cw
    }
    return $sb + [char]0x2026
}

$ordered = $groups.Values | Sort-Object -Property @{ Expression = 'Fights'; Descending = $true }, @{ Expression = 'Key'; Descending = $false }

Write-Host ''
Write-Host ('================ 统计（按 {0}；共 {1} 场对局、{2} 个分组）================' -f $By, $rows.Count, $groups.Count) -ForegroundColor Cyan

# 列宽（显示宽度）：分组 26 / 对局 5 / 胜 4 / 负 4 / 胜率 7 / 回合 6 / 承伤 7 / 残血 7 / Boss剩 8 / 打死 7
$header = (PadR '分组' 26) + ' ' + (PadL '对局' 5) + ' ' + (PadL '胜' 4) + ' ' + (PadL '负' 4) + ' ' +
          (PadL '胜率' 7) + ' ' + (PadL '回合' 6) + ' ' + (PadL '承伤' 7) + ' ' + (PadL '残血' 7) + ' ' +
          (PadL 'Boss剩' 8) + ' ' + (PadL '打死' 7)
Write-Host $header -ForegroundColor DarkGray
Write-Host (('-' * 26) + ' ' + ('-' * 5) + ' ' + ('-' * 4) + ' ' + ('-' * 4) + ' ' + ('-' * 7) + ' ' +
            ('-' * 6) + ' ' + ('-' * 7) + ' ' + ('-' * 7) + ' ' + ('-' * 8) + ' ' + ('-' * 7)) -ForegroundColor DarkGray

foreach ($g in $ordered) {
    $winRate = if ($g.Fights -gt 0) { $g.Wins / $g.Fights } else { $null }
    $keyText = CutDisp ([string]$g.Key) 26
    $line = (PadR $keyText 26) + ' ' + (PadL ([string]$g.Fights) 5) + ' ' + (PadL ([string]$g.Wins) 4) + ' ' +
            (PadL ([string]$g.Losses) 4) + ' ' + (PadL (Pct $winRate) 7) + ' ' +
            (PadL (Num (Avg $g.Turns $g.TurnsN) '{0,6:0.0}') 6) + ' ' +
            (PadL (Pct (Avg $g.DmgRatio $g.DmgN)) 7) + ' ' +
            (PadL (Pct (Avg $g.HpEndRatio $g.HpEndN)) 7) + ' ' +
            (PadL (Pct (Avg $g.BossLeft $g.BossLeftN)) 8) + ' ' +
            (PadL (Pct (Avg $g.KillRatio $g.KillN)) 7)
    if ($null -ne $winRate -and $winRate -lt $HardBelow) {
        Write-Host $line -ForegroundColor Red          # 比原版最难的 Boss 还难
    }
    elseif ($null -ne $winRate -and $winRate -gt $EasyAbove) {
        Write-Host $line -ForegroundColor Yellow       # 比原版最温和的 Boss 还简单
    }
    else { Write-Host $line }                          # 落在原版区间内 ⇒ 正常
}
Write-Host ''
Write-Host '提示：承伤 = 玩家掉血比例（越高越难）；残血 = 结束时剩血比例（越低越险）；Boss剩 = 输的局 Boss 还剩多血。' -ForegroundColor DarkGray
Write-Host ('      分档基准：原版 Boss 的玩家胜率约 75%～88%（击杀率 25%～12%）。' ) -ForegroundColor DarkGray
Write-Host ('      红色 = 低于 {0:0}%（比原版最难的还难）；黄色 = 高于 {1:0}%（比原版最温和的还简单）；其余落在原版区间内。' -f `
    ($HardBelow * 100), ($EasyAbove * 100)) -ForegroundColor DarkGray
Write-Host '      样本 <5 场的分组仅供参看。' -ForegroundColor DarkGray
Write-Host ('      分组键 = Boss 变体（当前 34 只）；多人局默认只记阵容第 0 位那只，加 -ExplodeMulti 可让场上每只各记一次。') -ForegroundColor DarkGray

# ── CSV ─────────────────────────────────────────────────────────────────
if (-not $NoCsv) {
    if (-not $Csv) {
        $Csv = Join-Path ([Environment]::GetFolderPath('Desktop')) ("charbosses-stats-{0}.csv" -f (Get-Date -Format 'yyyyMMdd-HHmm'))
    }
    $out = foreach ($g in $ordered) {
        [pscustomobject]@{
            Group    = $g.Key
            Fights   = $g.Fights
            Wins     = $g.Wins
            Losses   = $g.Losses
            WinRate  = if ($g.Fights -gt 0) { [math]::Round($g.Wins / $g.Fights, 4) } else { $null }
            AvgTurns = if ($g.TurnsN -gt 0) { [math]::Round($g.Turns / $g.TurnsN, 2) } else { $null }
            DmgRatio = if ($g.DmgN -gt 0) { [math]::Round($g.DmgRatio / $g.DmgN, 4) } else { $null }
            HpEndRatio = if ($g.HpEndN -gt 0) { [math]::Round($g.HpEndRatio / $g.HpEndN, 4) } else { $null }
            BossLeft = if ($g.BossLeftN -gt 0) { [math]::Round($g.BossLeft / $g.BossLeftN, 4) } else { $null }
            KillRatio = if ($g.KillN -gt 0) { [math]::Round($g.KillRatio / $g.KillN, 4) } else { $null }
        }
    }
    try {
        $out | Export-Csv -Path $Csv -NoTypeInformation -Encoding UTF8
        Write-Host ''
        Write-Host ("CSV 已导出：{0}" -f $Csv) -ForegroundColor Green
    }
    catch { Write-Host ("CSV 导出失败：{0}" -f $_.Exception.Message) -ForegroundColor Yellow }
}
