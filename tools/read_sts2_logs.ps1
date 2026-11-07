# Read Slay the Spire 2 logs
$logPath = "$env:APPDATA\SlayTheSpire2\logs\godot.log"
Write-Host "=== STS2 Logs ==="
if (Test-Path $logPath) {
    Get-Content $logPath -Tail 50
} else {
    Write-Host "Log file not found: $logPath"
}
