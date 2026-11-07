param(
    [Parameter(Mandatory=$false)]
    [string]$NewModName
)

$ErrorActionPreference = "Stop"
$workingDir = Get-Location
$srcDir = Join-Path $workingDir "src"

if (-not (Test-Path $srcDir)) {
    Write-Host "ERROR: src directory not found" -ForegroundColor Red
    exit 1
}

$oldModName = "Sts2ModScaffold"
$oldNamespace = "Sts2ModScaffold"
$oldId = "com.vibecoding.sts2mod"

# Check if already renamed
$alreadyRenamed = $false
$currentCsproj = Get-ChildItem -Path $srcDir -Filter "*.csproj" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($currentCsproj -and $currentCsproj.Name -ne "$oldModName.csproj") {
    $alreadyRenamed = $true
    Write-Host "Scaffold has already been renamed to: $($currentCsproj.Name -replace '.csproj', '')" -ForegroundColor Yellow
}

# If no name provided, ask or generate
if ([string]::IsNullOrWhiteSpace($NewModName)) {
    Write-Host ""
    Write-Host "No mod name provided. Please enter a name for your mod." -ForegroundColor Yellow
    Write-Host "Or press Enter to use default name 'MyFirstMod'"
    Write-Host ""
    Write-Host -NoNewline "Mod name: " -ForegroundColor Cyan
    $NewModName = Read-Host
    
    if ([string]::IsNullOrWhiteSpace($NewModName)) {
        $NewModName = "MyFirstMod"
        Write-Host "Using default: $NewModName" -ForegroundColor Gray
    }
}

# Convert to valid C# identifier and ID
$safeName = $NewModName -replace '[^a-zA-Z0-9]', ''
if ([string]::IsNullOrWhiteSpace($safeName)) {
    Write-Host "ERROR: Invalid mod name. Please use letters and numbers only." -ForegroundColor Red
    exit 1
}
$newId = "com.vibecoding." + $safeName.ToLower()

Write-Host ""
Write-Host "Renaming scaffold from '$oldModName' to '$NewModName'" -ForegroundColor Cyan
Write-Host "  Mod ID: $oldId -> $newId" -ForegroundColor Cyan
Write-Host ""

# 1. Rename .csproj file
$oldCsproj = Join-Path $srcDir "$oldModName.csproj"
$newCsproj = Join-Path $srcDir "$safeName.csproj"
if (Test-Path $oldCsproj) {
    Rename-Item -Path $oldCsproj -NewName "$safeName.csproj" -Force
    Write-Host "[1/6] Renamed $oldModName.csproj -> $safeName.csproj" -ForegroundColor Green
} elseif (Test-Path $newCsproj) {
    Write-Host "[1/6] $safeName.csproj already exists, skipping" -ForegroundColor Yellow
} else {
    Write-Host "[1/6] $oldCsproj not found, skipping" -ForegroundColor Yellow
}

# 2. Update .csproj content
$csproj = Join-Path $srcDir "$safeName.csproj"
if (Test-Path $csproj) {
    $content = Get-Content $csproj -Raw
    $content = $content -replace $oldModName, $safeName
    Set-Content -Path $csproj -Value $content
    Write-Host "[2/6] Updated $safeName.csproj content" -ForegroundColor Green
}

# 3. Update ModEntry.cs namespace, class name, and ModId
$modEntryPath = Join-Path $srcDir "ModEntry.cs"
if (Test-Path $modEntryPath) {
    $content = Get-Content $modEntryPath -Raw
    $content = $content -replace "namespace $oldNamespace", "namespace $safeName"
    $content = $content -replace $oldNamespace, $safeName
    $content = $content -replace "ModName = ""$oldModName""", "ModName = ""$NewModName"""
    $content = $content -replace 'ModId = "' + $oldId + '"', "ModId = ""$newId"""
    $content = $content -replace $oldId, $newId
    Set-Content -Path $modEntryPath -Value $content
    Write-Host "[3/6] Updated ModEntry.cs" -ForegroundColor Green
}

# 4. Update all Hook files namespace
$hooksDir = Join-Path $srcDir "Hooks"
if (Test-Path $hooksDir) {
    Get-ChildItem -Path $hooksDir -Filter "*.cs" -Recurse | ForEach-Object {
        $content = Get-Content $_.FullName -Raw
        $content = $content -replace "using $oldNamespace", "using $safeName"
        $content = $content -replace "namespace $oldNamespace", "namespace $safeName"
        Set-Content -Path $_.FullName -Value $content
        Write-Host "[4/6] Updated Hooks\$($_.Name)" -ForegroundColor Green
    }
}

# 5. (removed - no more specific hook files)

# 6. Update manifest JSON
$manifestPath = Join-Path $srcDir "com.vibecoding.sts2mod.json"
if (-not (Test-Path $manifestPath)) {
    $manifestPath = Join-Path $srcDir "$newId.json"
}
$newManifestPath = Join-Path $srcDir "$newId.json"
if (Test-Path $manifestPath) {
    $content = Get-Content $manifestPath -Raw
    $json = $content | ConvertFrom-Json
    $json.id = $newId
    $json.name = $NewModName
    $json.description = "A mod created with Sts2ModScaffold template"
    $newContent = $json | ConvertTo-Json -Depth 10
    Set-Content -Path $manifestPath -Value $newContent
    if ($manifestPath -ne $newManifestPath) {
        Rename-Item -Path $manifestPath -NewName "$newId.json" -Force
    }
    Write-Host "[6/6] Updated manifest -> $newId.json" -ForegroundColor Green
}

Write-Host ""
Write-Host "Scaffold renamed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Edit src/Hooks/ to implement your mod"
Write-Host "  2. Run install.bat to build and install"
