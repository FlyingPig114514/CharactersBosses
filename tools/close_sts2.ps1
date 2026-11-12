# Close Slay the Spire 2
#
# ⚠ 2026-11-12：Windows 11 已经**没有 tskill** 了（原第二行 `tskill SlayTheSpire2` 会报
#   CommandNotFoundException）。这里改成 Stop-Process 兜底：taskkill 抓不到
#   （例如进程名大小写/路径不同、或 taskkill 被杀软拦）时也能关掉。
$ErrorActionPreference = 'SilentlyContinue'

taskkill /F /IM "SlayTheSpire2.exe" 2>$null | Out-Null

$procs = Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue
if ($procs) {
    Write-Host ("[close] taskkill 没关掉，改用 Stop-Process：" + (($procs | ForEach-Object { $_.Id }) -join ', '))
    $procs | Stop-Process -Force -ErrorAction SilentlyContinue
}

# 等进程真正退出（最多 10 秒），否则 install-mod.bat 复制 DLL 会被文件占用
for ($i = 0; $i -lt 20; $i++) {
    if (-not (Get-Process -Name 'SlayTheSpire2' -ErrorAction SilentlyContinue)) {
        Write-Host '[close] 游戏已退出'
        exit 0
    }
    Start-Sleep -Milliseconds 500
}
Write-Host '[close] ⚠ 游戏仍在运行（可能卡在退出流程）' -ForegroundColor Yellow
exit 1
