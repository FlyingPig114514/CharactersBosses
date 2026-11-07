<#
.SYNOPSIS
  生成创意工坊封面图 `workshop/image.png`（920×920，PNG，远小于 Steam 的 1MB 上限）。

.DESCRIPTION
  Steam 的 `image.png` 有两条硬要求（上传器 `src/UploadCommand.cs` 里写死的）：
    · 文件名必须叫 `image.png`，放在**工作区根目录**（不是 content 里）；
    · 必须 < 1MB。
  这张图是**排版封面**（深色底 + 标题 + 五个角色标签）。如果你有更好的实战截图，
  直接覆盖 `workshop/image.png` 就行（同样 920×920、<1MB）。

  用法：
    powershell -ExecutionPolicy Bypass -File tools\make-workshop-image.ps1
    powershell -ExecutionPolicy Bypass -File tools\make-workshop-image.ps1 -Out D:\x\image.png
#>
[CmdletBinding()]
param(
    [string]$Out = '',
    [string]$Version = '0.2.248'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

if (-not $Out) {
    $root = Split-Path -Parent $PSScriptRoot
    $Out = Join-Path $root 'workshop\image.png'
}
$dir = Split-Path -Parent $Out
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }

$S = 920
$bmp = New-Object System.Drawing.Bitmap $S, $S
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

function C([int]$a, [int]$r, [int]$gg, [int]$b) { [System.Drawing.Color]::FromArgb($a, $r, $gg, $b) }

# ── 背景：竖向渐变 + 一点冷色光晕 ──
$rect = New-Object System.Drawing.Rectangle 0, 0, $S, $S
$brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $rect, (C 255 16 20 26), (C 255 30 38 48), [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
$g.FillRectangle($brush, $rect)
$brush.Dispose()

$glowPath = New-Object System.Drawing.Drawing2D.GraphicsPath
$glowPath.AddEllipse(-260, -320, 1300, 900)
$glow = New-Object System.Drawing.Drawing2D.PathGradientBrush($glowPath)
$glow.CenterColor = (C 60 90 140 190)
$glow.SurroundColors = @((C 0 0 0 0))
$g.FillPath($glow, $glowPath)
$glow.Dispose(); $glowPath.Dispose()

# ── 文字小工具 ──
function DrawCenter([string]$text, [System.Drawing.Font]$font, [System.Drawing.Brush]$brush, [single]$y) {
    $size = $g.MeasureString($text, $font)
    $x = [single](($S - $size.Width) / 2.0)
    $g.DrawString($text, $font, $brush, $x, $y)
    return $size
}

$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$muted = New-Object System.Drawing.SolidBrush (C 255 150 165 180)
$accent = New-Object System.Drawing.SolidBrush (C 255 235 175 90)

# 顶部小标
$fSmall = New-Object System.Drawing.Font('Microsoft YaHei', 17, [System.Drawing.FontStyle]::Regular)
DrawCenter 'SLAY THE SPIRE 2  ·  MOD' $fSmall $muted 74 | Out-Null

# 主标题
$fTitle = New-Object System.Drawing.Font('Microsoft YaHei', 74, [System.Drawing.FontStyle]::Bold)
DrawCenter '角色当 Boss' $fTitle $white 150 | Out-Null

# 英文副标题
$fSub = New-Object System.Drawing.Font('Segoe UI', 24, [System.Drawing.FontStyle]::Bold)
DrawCenter 'Vanilla Characters as Bosses' $fSub $accent 276 | Out-Null

# 一句话
$fLine = New-Object System.Drawing.Font('Microsoft YaHei', 19, [System.Drawing.FontStyle]::Regular)
DrawCenter '每一幕的 Boss 都换成原版可玩角色' $fLine $muted 330 | Out-Null

# ── 五个角色标签（3 + 2，显式算好每个位置再画，免得路径数学出错）──
$chars = @('铁甲战士', '静默猎手', '故障机器人', '亡灵契约师', '储君')
$fChip = New-Object System.Drawing.Font('Microsoft YaHei', 25, [System.Drawing.FontStyle]::Bold)
[float]$chipW = 250
[float]$chipH = 88
[float]$gapX = 18
[float]$row1Y = 430
[float]$row2Y = $row1Y + $chipH + 26

$slots = @()
[float]$row1W = 3 * $chipW + 2 * $gapX
for ($i = 0; $i -lt 3; $i++) {
    $slots += , @([float](($S - $row1W) / 2.0 + $i * ($chipW + $gapX)), $row1Y, $chars[$i])
}
[float]$row2W = 2 * $chipW + $gapX
for ($i = 0; $i -lt 2; $i++) {
    $slots += , @([float](($S - $row2W) / 2.0 + $i * ($chipW + $gapX)), $row2Y, $chars[$i + 3])
}

$chipFill = New-Object System.Drawing.SolidBrush (C 255 30 38 48)
$chipPen = New-Object System.Drawing.Pen (C 255 96 116 138), 2
foreach ($slot in $slots) {
    [float]$sx = $slot[0]
    [float]$sy = $slot[1]
    [string]$label = $slot[2]

    [float]$r = 16
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($sx, $sy, $r * 2, $r * 2, 180, 90)
    $path.AddArc($sx + $chipW - $r * 2, $sy, $r * 2, $r * 2, 270, 90)
    $path.AddArc($sx + $chipW - $r * 2, $sy + $chipH - $r * 2, $r * 2, $r * 2, 0, 90)
    $path.AddArc($sx, $sy + $chipH - $r * 2, $r * 2, $r * 2, 90, 90)
    $path.CloseFigure()
    $g.FillPath($chipFill, $path)
    $g.DrawPath($chipPen, $path)
    $path.Dispose()

    $size = $g.MeasureString($label, $fChip)
    [float]$tx = $sx + ($chipW - $size.Width) / 2.0
    [float]$ty = $sy + ($chipH - $size.Height) / 2.0
    $g.DrawString($label, $fChip, $white, $tx, $ty)
}
$chipFill.Dispose(); $chipPen.Dispose()
# ── 底部两行 ──
$fFoot = New-Object System.Drawing.Font('Microsoft YaHei', 18, [System.Drawing.FontStyle]::Regular)
DrawCenter '自带卡组 · 遗物 · 出牌剧本 · 跟踪锁定 · 多人联机' $fFoot $muted 748 | Out-Null

$fTag = New-Object System.Drawing.Font('Segoe UI', 15, [System.Drawing.FontStyle]::Regular)
DrawCenter ("v{0}  ·  Requires BaseLib" -f $Version) $fTag $accent 792 | Out-Null

# 底部细线
$linePen = New-Object System.Drawing.Pen (C 70 120 140 165), 1
$g.DrawLine($linePen, 210, 848, $S - 210, 848)
$linePen.Dispose()
DrawCenter 'steam workshop' ([System.Drawing.Font]::new('Segoe UI', 13)) (New-Object System.Drawing.SolidBrush (C 255 110 125 140)) 862 | Out-Null

$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

$file = Get-Item $Out
Write-Host ('封面已生成：{0}（{1} × {1}，{2:N0} KB）' -f $file.FullName, $S, ($file.Length / 1KB)) -ForegroundColor Green
if ($file.Length -ge 1MB) { Write-Host '⚠ 超过 1MB，Steam 会拒收，需要压缩。' -ForegroundColor Red }
