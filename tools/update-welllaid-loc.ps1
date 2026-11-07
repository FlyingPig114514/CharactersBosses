# 只替换 `SILENT_BOSS.well_laid`（「计划妥当」特殊效果说明）里的**第二段**，标题和第一段原样保留。
#
# 用户 2026-11-04 要求：第一段不变，第二段改成
#   「她的谋杀只会在其保留在手牌中时才会因抽牌增加伤害。」
#
# ⚠ 绝不使用 Get-Content/Set-Content 改含中文的文件（本机 PS 5.1 会按 ANSI 解码 UTF-8 ⇒ 整文件乱码）。
#    这里统一用 [System.IO.File]::ReadAllText / WriteAllText + UTF8Encoding($false)。

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# 各语言的**新第二段**（保留 [gold] 卡名标记，与各自语言里「谋杀」的译名一致）
$newPara = @{
    'deu' = 'Ihr [gold]Ermorden[/gold] erhält nur dann zusätzlichen Schaden durch gezogene Karten, solange sie es auf der Hand behält.'
    'eng' = 'Her [gold]Murder[/gold] only gains damage from drawing cards while it is retained in her hand.'
    'esp' = 'Su [gold]Asesinato[/gold] solo gana daño al robar cartas mientras lo conserve en la mano.'
    'fra' = 'Son [gold]Meurtre[/gold] ne gagne des dégâts en piochant que tant qu''il est conservé dans sa main.'
    'ind' = '[gold]Pembunuhan[/gold] miliknya hanya mendapat tambahan kerusakan dari menarik kartu selama ia menahannya di tangan.'
    'ita' = 'Il suo [gold]Omicidio[/gold] guadagna danno dalle carte pescate solo finché resta conservato in mano.'
    'jpn' = '彼女の[gold]殺害[/gold]は、これが手札に保留されている間にカードを引いたときだけダメージが増える。'
    'kor' = '그녀의 [gold]살해[/gold]는 손에 보존되어 있는 동안 카드를 뽑을 때만 피해가 증가합니다.'
    'pol' = 'Jej [gold]Morderstwo[/gold] zyskuje obrażenia za dobrane karty tylko wtedy, gdy jest zatrzymane w ręce.'
    'ptb' = 'O [gold]Homicídio[/gold] dela só ganha dano ao comprar cartas enquanto estiver retido na mão.'
    'rus' = 'Её [gold]«Налет»[/gold] получает дополнительный урон от добранных карт только пока удерживается в руке.'
    'spa' = 'Su [gold]Despachar[/gold] solo gana daño al robar cartas mientras lo retenga en la mano.'
    'tha' = '[gold]ฆาตกรรม[/gold] ของเธอจะได้รับความเสียหายเพิ่มจากการจั่วการ์ดเฉพาะเมื่อมันถูกเก็บไว้ในมือเท่านั้น'
    'tur' = 'Onun [gold]Cinayet[/gold] kartı yalnızca elinde tutulduğu sürece çekilen kartlardan hasar kazanır.'
    'zhs' = '她的[gold]谋杀[/gold]只会在其保留在手牌中时才会因抽牌增加伤害。'
    'zht' = '她的[gold]謀殺[/gold]只會在保留在手牌中時才會因抽牌增加傷害。'
}

$key = '"SILENT_BOSS.well_laid"'
$nl  = '\n'      # JSON 里的换行是「反斜杠 + n」两个字符
$utf8 = New-Object System.Text.UTF8Encoding($false)
$changed = 0
$skipped = @()

foreach ($lang in $newPara.Keys) {
    $path = Join-Path $root "content\localization\$lang\monsters.json"
    if (-not (Test-Path $path)) { $skipped += "$lang(no file)"; continue }

    $text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    if ($text.IndexOf($key) -lt 0) { $skipped += "$lang(no key)"; continue }

    $lines = $text -split "`r?`n"
    $hit = -1
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i].Contains($key)) { $hit = $i; break }
    }
    if ($hit -lt 0) { $skipped += "$lang(no line)"; continue }

    $line = $lines[$hit]
    # 值 = key 之后第一个 " 开始，到行尾那个 " 结束（值里没有裸引号，只有 [gold] 标记和 \n）
    $q1 = $line.IndexOf('"', $line.IndexOf($key) + $key.Length)
    $q2 = $line.LastIndexOf('"')
    if ($q1 -lt 0 -or $q2 -le $q1) { $skipped += "$lang(parse)"; continue }

    $value = $line.Substring($q1 + 1, $q2 - $q1 - 1)
    $parts = $value.Split([string[]]@($nl), [System.StringSplitOptions]::None)
    if ($parts.Length -lt 3) { $skipped += "$lang(parts=$($parts.Length))"; continue }

    $newValue = $parts[0] + $nl + $parts[1] + $nl + $newPara[$lang]
    $lines[$hit] = $line.Substring(0, $q1 + 1) + $newValue + $line.Substring($q2)

    [System.IO.File]::WriteAllText($path, ($lines -join "`n"), $utf8)
    $changed++
    Write-Host ("[ok] {0}: {1}" -f $lang, $newValue) -ForegroundColor Green
}

Write-Host ""
Write-Host ("changed={0}  skipped=[{1}]" -f $changed, ($skipped -join ','))
