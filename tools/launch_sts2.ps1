# Launch Slay the Spire 2 via Steam
function Wait-SteamReady {
    $steam = Get-Process -Name "steam" -ErrorAction SilentlyContinue
    if ($steam) {
        return $true
    }
    Write-Host "ERROR: Steam is not running." -ForegroundColor Red
    Write-Host "  Please launch Steam and log in. Polling every 5 seconds..." -ForegroundColor Yellow
    $pollCount = 0
    while ($pollCount -lt 120) {
        Start-Sleep -Seconds 5
        $steam = Get-Process -Name "steam" -ErrorAction SilentlyContinue
        if ($steam) {
            Write-Host ""
            Write-Host "  Steam detected!" -ForegroundColor Green
            Start-Sleep -Seconds 3
            return $true
        }
        $pollCount++
        $elapsed = $pollCount * 5
        Write-Host -NoNewline "."
    }
    Write-Host ""
    Write-Host "ERROR: Steam not detected after 10 minutes. Aborting." -ForegroundColor Red
    return $false
}

if (-not (Wait-SteamReady)) {
    exit 1
}
Start-Process "steam://run/2868840"
