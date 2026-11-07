# Wait for Slay the Spire 2 to start
param([int]$TimeoutSeconds = 60)

$startTime = Get-Date
while (((Get-Date) - $startTime).TotalSeconds -lt $TimeoutSeconds) {
    $tmpFile = Join-Path $env:TEMP "sts2_check.txt"
    tasklist /FI "IMAGENAME eq SlayTheSpire2.exe" /NH > $tmpFile 2>$null
    $content = Get-Content $tmpFile -Raw -ErrorAction SilentlyContinue
    Remove-Item $tmpFile -Force -ErrorAction SilentlyContinue
    if ($content -and $content -match "SlayTheSpire2\.exe\s+(\d+)") {
        Write-Host "Game started (PID: $($Matches[1]))"
        exit 0
    }
    Start-Sleep -Seconds 2
}
Write-Host "Game did not start within ${TimeoutSeconds}s"
exit 1
