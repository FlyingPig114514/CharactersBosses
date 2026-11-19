# 一次性脚本（用户 2026-11-19）：把 14 种非中文语言的「恐惧版 彗星」文案从英文占位换成真正的本地化译文。
# ⚠ 只改这两行：powers.json 的 COMET_BOSS_POWER.description_dread / monsters.json 的 REGENT_BOSS.comet_dread。
# ⚠ 必须用 .NET 的 UTF8 读写（本机 PS 5.1 的 Get-Content/Set-Content 会按 ANSI 解码毁掉中文）。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$enc = New-Object System.Text.UTF8Encoding($false)

# lang => @{ Title=…; B1=…; B2=… }   （B1 = 第一句，B2 = 第二句）
$map = @{
  'deu' = @{ Title = 'Komet'; B1 = 'Jedes Mal, wenn Der Regent [gold]Schutt[/gold] erzeugt, wird er stattdessen in deinen [gold]Abwurfstapel[/gold] gelegt.'; B2 = 'Jedes Mal, wenn du [gold]Schutt[/gold] spielst, werden alle [gold]Schwach[/gold] und [gold]Verwundbar[/gold] von allen Spielern entfernt.' }
  'eng' = @{ Title = 'Comet'; B1 = 'Whenever the Regent creates Debris, it is added to your discard pile instead.'; B2 = 'Whenever you play a Debris, all Weak and Vulnerable are removed from all players.' }
  'esp' = @{ Title = 'Cometa'; B1 = 'Cada vez que El Monarca genera [gold]Escombros[/gold], en su lugar se agregan a tu [gold]pila de descarte[/gold].'; B2 = 'Cada vez que juegas [gold]Escombros[/gold], se eliminan todo el [gold]debilitamiento[/gold] y la [gold]vulnerabilidad[/gold] de todos los jugadores.' }
  'spa' = @{ Title = 'Cometa'; B1 = 'Cada vez que El Regente genere [gold]Escombros[/gold], en su lugar se añaden a tu [gold]Pila de Descarte[/gold].'; B2 = 'Cada vez que juegues [gold]Escombros[/gold], se eliminan toda la [gold]Debilidad[/gold] y la [gold]Vulnerabilidad[/gold] de todos los jugadores.' }
  'fra' = @{ Title = 'Comète'; B1 = 'Chaque fois que Le Régent génère des [gold]Débris[/gold], ils sont ajoutés à votre [gold]Défausse[/gold] à la place.'; B2 = 'Chaque fois que vous jouez des [gold]Débris[/gold], tous les [gold]Affaibli[/gold] et [gold]Vulnérable[/gold] sont retirés de tous les joueurs.' }
  'ind' = @{ Title = 'Komet'; B1 = 'Setiap kali Si Regent membuat [gold]Puing[/gold], [gold]Puing[/gold] itu malah ditambahkan ke [gold]Tumpukan Singkir[/gold]-mu.'; B2 = 'Setiap kali kamu memainkan [gold]Puing[/gold], semua [gold]Lemah[/gold] dan [gold]Rentan[/gold] dihapus dari semua pemain.' }
  'ita' = @{ Title = 'Cometa'; B1 = 'Ogni volta che il Regent genera [gold]Rottami[/gold], vengono invece aggiunti al tuo [gold]pila degli scarti[/gold].'; B2 = 'Ogni volta che giochi [gold]Rottami[/gold], tutti gli effetti di [gold]Debolezza[/gold] e [gold]Vulnerabilità[/gold] vengono rimossi da tutti i giocatori.' }
  'jpn' = @{ Title = '彗星'; B1 = 'リージェントが[gold]デブリ[/gold]を生成するたび、代わりにあなたの捨て札に加える。'; B2 = 'あなたが[gold]デブリ[/gold]をプレイするたび、全プレイヤーの[gold]脱力[/gold]と[gold]弱体[/gold]をすべて除去する。' }
  'kor' = @{ Title = '혜성'; B1 = '리젠트가 잔해를 생성할 때마다 대신 당신의 버린 카드 더미에 추가합니다.'; B2 = '당신이 잔해를 사용할 때마다 모든 플레이어의 모든 약화와 취약을 제거합니다.' }
  'pol' = @{ Title = 'Kometa'; B1 = 'Kiedykolwiek Monarcha stworzy „Gruz”, trafia on zamiast tego do twojego stosu kart odrzuconych.'; B2 = 'Kiedykolwiek zagrasz „Gruz”, cała Słabość i Wrażliwość zostają zdjęte ze wszystkich graczy.' }
  'ptb' = @{ Title = 'Cometa'; B1 = 'Sempre que O Regente gerar [gold]Escombros[/gold], eles em vez disso são adicionados na sua [gold]Pilha de Descarte[/gold].'; B2 = 'Sempre que você jogar [gold]Escombros[/gold], toda a [gold]Fraqueza[/gold] e [gold]Vulnerável[/gold] de todos os jogadores são removidos.' }
  'rus' = @{ Title = 'Комета'; B1 = 'Каждый раз, когда Наследник создает «Обломок», тот попадает вместо этого в вашу стопку сброса.'; B2 = 'Каждый раз, когда вы разыгрываете «Обломок», со всех игроков снимаются вся слабость и уязвимость.' }
  'tha' = @{ Title = 'Comet'; B1 = 'ทุกครั้งที่ Regent สร้าง[gold]เศษซาก[/gold] มันจะถูกเพิ่มลงใน[gold]กองทิ้ง[/gold]ของคุณแทน'; B2 = 'ทุกครั้งที่คุณเล่น[gold]เศษซาก[/gold] ลบ[gold]ความอ่อนแอ[/gold]และ[gold]จุดอ่อน[/gold]ทั้งหมดออกจากผู้เล่นทุกคน' }
  'tur' = @{ Title = 'Kuyruklu Yıldız'; B1 = 'Naip [gold]Moloz[/gold] oluşturduğunda, bunun yerine senin [gold]Atık Deste[/gold]''ne eklenir.'; B2 = '[gold]Moloz[/gold] oynadığında, tüm oyunculardaki tüm [gold]Zayıf[/gold] ve [gold]Savunmasız[/gold] etkileri kaldırılır.' }
}

function Set-Line([string]$path, [string]$key, [string]$value) {
    $lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
    $hit = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match ('"' + [regex]::Escape($key) + '"')) {
            $lines[$i] = '    "' + $key + '":  "' + $value + '",'
            $hit = $true
        }
    }
    if ($hit) { [System.IO.File]::WriteAllText($path, ($lines -join "`n"), $enc) }
    return $hit
}

$ok = 0; $bad = @()
foreach ($lang in $map.Keys) {
    $e = $map[$lang]
    $body = $e.B1 + '\n' + $e.B2
    $pPowers = Join-Path $root ("content\localization\$lang\powers.json")
    $pMon = Join-Path $root ("content\localization\$lang\monsters.json")
    $a = Set-Line $pPowers 'COMET_BOSS_POWER.description_dread' $body
    $b = Set-Line $pMon 'REGENT_BOSS.comet_dread' ($e.Title + '\n' + $body)
    if ($a -and $b) { $ok++; Write-Host "[$lang] ok" } else { $bad += $lang; Write-Host "[$lang] MISS powers=$a monsters=$b" }
}
Write-Host ("done: $ok languages, failed=" + ($bad -join ','))
