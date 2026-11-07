[CmdletBinding()]
param(
    [Parameter(Position=0)][string]$GameDir = "",
    [Parameter(Position=1)][string]$DeepVerify = ""
)

$ErrorActionPreference = "Continue"
Set-StrictMode -Version Latest

function Write-Step($Step, $Total, $Message) {
    Write-Host ""
    Write-Host "[$Step/$Total] $Message" -ForegroundColor Cyan
}

function Write-OK($Message) {
    Write-Host "  $Message" -ForegroundColor Green
}

function Write-Warn($Message) {
    Write-Host "  WARNING: $Message" -ForegroundColor Yellow
}

function Write-Err($Message) {
    Write-Host "  ERROR: $Message" -ForegroundColor Red
}

function Test-FileSize($Path, $MinSize) {
    if (Test-Path $Path) {
        return (Get-Item $Path).Length -gt $MinSize
    }
    return $false
}

Write-Host ""
Write-Host "========================================" -ForegroundColor White
Write-Host " SlaytheSpire2ModVibeCoding - One-Click Setup" -ForegroundColor White
Write-Host "========================================" -ForegroundColor White
Write-Host ""

$ProjectDir = $PSScriptRoot
if (-not $ProjectDir) { $ProjectDir = (Get-Location).Path }
$ProjectDir = $ProjectDir.TrimEnd('\')

$EnvFile = Join-Path $ProjectDir ".env"

$ToolsDir = Join-Path $ProjectDir "tools"
$DotnetDir = Join-Path $ToolsDir "dotnet"
$GodotDir = Join-Path $ToolsDir "godot"
$IlspyDir = Join-Path $ToolsDir "ilspy"
$IlspyMcpDir = Join-Path $ToolsDir "ILSpy-Mcp"
$Sts2McpDir = Join-Path $ToolsDir "STS2MCP"
$Sts2McpPythonDir = Join-Path $Sts2McpDir "mcp"
$MenuControlDir = Join-Path $ToolsDir "STS2MenuControl"
$PckBuilderDir = Join-Path $ToolsDir "pck_builder"
$UvDir = Join-Path $ToolsDir "uv"
$UvExe = Join-Path $UvDir "uv.exe"
$GodotExe = Join-Path $GodotDir "Godot_v4.5.1-stable_mono_win64.exe"
$RefDir = Join-Path $ProjectDir "references"
$ModDir = Join-Path $ProjectDir "src"
$DefaultGameDir = "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2"
$ModdingTutorialDir = Join-Path $ProjectDir "Modding-Tutorial"

Write-Host "[Pre-Check] Checking if game is running..."
$gameProc = Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue
if ($gameProc) {
    Write-Host "  - Found SlayTheSpire2 process"
    Write-Host ""
    $closeGame = Read-Host "Game is running. Close it now? This is needed for mod installation. (Y/N)"
    if ($closeGame -eq "Y" -or $closeGame -eq "y") {
        Write-Host "  Closing game..."
        Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Write-Host "  Waiting for game to close (max 10 seconds)..."
        $closed = $false
        for ($i = 1; $i -le 10; $i++) {
            Start-Sleep -Seconds 1
            if (-not (Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue)) {
                $closed = $true
                break
            }
        }
        if ($closed) {
            Write-OK "Game closed successfully."
        } else {
            Write-Warn "Game could not be closed automatically."
            Write-Host "  Please close the game manually, then press any key to continue..."
            $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        }
    } else {
        Write-Host "  Game will remain open. Mod installation may fail."
    }
} else {
    Write-OK "- Game is not running."
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Err "Git is not installed or not in PATH."
    Write-Host "  Please install Git from: https://git-scm.com/download/win"
    if ([Console]::IsOutputRedirected -eq $false) { pause }
    exit 1
}
Write-OK "- Git: $((& git --version 2>$null))"

Write-Host ""
Write-Host "[Pre-Check] Scanning system environment..." -ForegroundColor Cyan
$sysDotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
$sysGodotCmd = Get-Command godot -ErrorAction SilentlyContinue
$sysUvCmd = Get-Command uv -ErrorAction SilentlyContinue

$sysDotnet10Sdk = $false
$sysDotnet10Runtime = $false
$sysDotnet8Runtime = $false
$sysDotnetRoot = ""
$useSystemDotnet = $false

if ($sysDotnetCmd) {
    $sysDotnetRoot = if ($env:DOTNET_ROOT) { $env:DOTNET_ROOT } else { Split-Path (Split-Path $sysDotnetCmd.Source) }
    $sysDotnetVer = & $sysDotnetCmd --version 2>$null
    Write-Host "  - System .NET: $sysDotnetVer (at $($sysDotnetCmd.Source))"
    & $sysDotnetCmd --list-sdks 2>$null | ForEach-Object {
        if ($_ -match "10\.0") { $sysDotnet10Sdk = $true }
    }
    & $sysDotnetCmd --list-runtimes 2>$null | ForEach-Object {
        if ($_ -match "10\.0") { $sysDotnet10Runtime = $true }
        if ($_ -match "8\.0") { $sysDotnet8Runtime = $true }
    }
    if ($sysDotnet10Sdk) { Write-Host "    SDK 10.0: found" } else { Write-Host "    SDK 10.0: missing" }
    if ($sysDotnet10Runtime) { Write-Host "    Runtime 10.0: found" } else { Write-Host "    Runtime 10.0: missing" }
    if ($sysDotnet8Runtime) { Write-Host "    Runtime 8.0: found" } else { Write-Host "    Runtime 8.0: missing" }
} else {
    Write-Host "  - System .NET: not found"
}

if ($sysGodotCmd) {
    $sysGodotVer = & $sysGodotCmd --version 2>$null
    Write-Host "  - System Godot: $sysGodotVer (at $($sysGodotCmd.Source))"
} else {
    Write-Host "  - System Godot: not found"
}

if ($sysUvCmd) {
    Write-Host "  - System uv: $(($sysUvCmd.Version.ToString())) (at $($sysUvCmd.Source))"
} else {
    Write-Host "  - System uv: not found"
}

if ($sysDotnet10Sdk -and $sysDotnet8Runtime) {
    Write-OK "- System .NET has all required versions, will use system dotnet (skip download)"
    $useSystemDotnet = $true
    $DotnetDir = $sysDotnetRoot
} else {
    Write-Host "  - Will use isolated .NET from tools/dotnet"
}

Write-Step 0 7 "Setting up .NET SDK (10.0) and runtime (8.0)..."

$env:DOTNET_ROOT = $DotnetDir
$env:PATH = "$DotnetDir;$env:PATH"

$dotnet10Installed = $false
$dotnet10RuntimeInstalled = $false
$dotnet8RuntimeInstalled = $false

if ($useSystemDotnet) {
    Write-OK "- Using system .NET (at $DotnetDir)"
    $dotnet10Installed = $true
    $dotnet10RuntimeInstalled = $true
    $dotnet8RuntimeInstalled = $true
} elseif ((Test-Path (Join-Path $DotnetDir "dotnet.exe")) -and (Test-FileSize (Join-Path $DotnetDir "dotnet.exe") 100000)) {
    $dotnetTest = & "$DotnetDir\dotnet.exe" --version 2>$null
    if ($dotnetTest -match "10\.0") {
        Write-Host "  - Checking isolated .NET SDK versions..."
        & "$DotnetDir\dotnet.exe" --list-sdks 2>$null | ForEach-Object {
            Write-Host "    $_"
            if ($_ -match "10\.0") { $dotnet10Installed = $true }
        }
        Write-Host "  - Checking isolated .NET runtimes..."
        & "$DotnetDir\dotnet.exe" --list-runtimes 2>$null | ForEach-Object {
            if ($_ -match "10\.0" -and $_ -match "Microsoft.WindowsDesktopRuntime") {
                $dotnet10RuntimeInstalled = $true
            }
            if ($_ -match "8\.0") { $dotnet8RuntimeInstalled = $true }
        }
        if ($dotnet10Installed) { Write-OK "- Isolated .NET 10.0 SDK found" }
    } else {
        Write-Host "  - dotnet.exe exists but not working, will reinstall"
    }
}

if (-not $dotnet10Installed) {
    Write-Host "  - Installing .NET 10.0 SDK..."
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $installScript = Join-Path $env:TEMP "dotnet-install.ps1"
        Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installScript -UseBasicParsing

        $dotnetTempDir = Join-Path $env:TEMP "dotnet_temp_install"
        if (Test-Path $dotnetTempDir) { Remove-Item $dotnetTempDir -Recurse -Force -ErrorAction SilentlyContinue }
        & $installScript -InstallDir $dotnetTempDir -Channel 10.0 -Version Latest 2>$null

        if (-not (Test-Path (Join-Path $dotnetTempDir "dotnet.exe"))) { throw "SDK download/extract failed" }

        if (-not (Test-Path $DotnetDir)) {
            New-Item -ItemType Directory -Path $DotnetDir -Force | Out-Null
        } else {
            foreach ($sub in @("sdk.old","shared.old","host.old","templates.old")) {
                $p = Join-Path $DotnetDir $sub
                if (Test-Path $p) { Remove-Item $p -Recurse -Force -ErrorAction SilentlyContinue }
            }
        }

        robocopy $dotnetTempDir $DotnetDir /E /NFL /NDL /NJH /NJS /NC /NS 2>$null

        $dotnet10Installed = & "$DotnetDir\dotnet.exe" --list-sdks 2>$null | Where-Object { $_ -match "10\.0" }
        if (-not $dotnet10Installed) { throw "SDK install verification failed" }
        Write-OK "- .NET 10.0 SDK installed"
        Remove-Item $dotnetTempDir -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $installScript -Force -ErrorAction SilentlyContinue
    } catch {
        Write-Err ".NET 10.0 SDK installation failed: $_"
        if ([Console]::IsOutputRedirected -eq $false) { pause }
        exit 1
    }
} else {
    Write-OK "- .NET 10.0 SDK already installed"
}

if (-not $dotnet10RuntimeInstalled) {
    Write-Host "  - Installing .NET 10.0 runtime..."
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $installScript = Join-Path $env:TEMP "dotnet-install.ps1"
        if (-not (Test-Path $installScript)) {
            Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installScript -UseBasicParsing
        }

        $dotnetTempDir = Join-Path $env:TEMP "dotnet_temp_runtime"
        if (Test-Path $dotnetTempDir) { Remove-Item $dotnetTempDir -Recurse -Force -ErrorAction SilentlyContinue }
        & $installScript -InstallDir $dotnetTempDir -Channel 10.0 -Runtime dotnet 2>$null

        if (Test-Path (Join-Path $dotnetTempDir "dotnet.exe")) {
            robocopy $dotnetTempDir $DotnetDir /E /NFL /NDL /NJH /NJS /NC /NS 2>$null
            Write-OK "- .NET 10.0 runtime installed"
        } else {
            Write-OK "- .NET 10.0 runtime (included in SDK)"
        }
        Remove-Item $dotnetTempDir -Recurse -Force -ErrorAction SilentlyContinue
    } catch {
        Write-Warn ".NET 10.0 runtime installation failed: $_"
    }
} else {
    Write-OK "- .NET 10.0 runtime already installed"
}

if (-not (Test-Path (Join-Path $DotnetDir "dotnet.exe"))) {
    Write-Err "Failed to install .NET SDK to $DotnetDir"
    if ([Console]::IsOutputRedirected -eq $false) { pause }
    exit 1
}
Write-OK "Done (.NET SDKs and runtime ready at $DotnetDir)"

$env:DOTNET_ROOT = $DotnetDir
$env:PATH = "$DotnetDir;$env:PATH"

$dotnet8RuntimeInstalled = $false
& "$DotnetDir\dotnet.exe" --list-runtimes 2>$null | ForEach-Object {
    if ($_ -match "8\.0") { $dotnet8RuntimeInstalled = $true }
}

if (-not $dotnet8RuntimeInstalled) {
    Write-Host "  - Installing .NET 8.0 runtime (required by ILSpy MCP)..."
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $installScript = Join-Path $env:TEMP "dotnet-install.ps1"
        if (-not (Test-Path $installScript)) {
            Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installScript -UseBasicParsing
        }
        $dotnetTempDir = Join-Path $env:TEMP "dotnet_temp_runtime8"
        if (Test-Path $dotnetTempDir) { Remove-Item $dotnetTempDir -Recurse -Force -ErrorAction SilentlyContinue }
        & $installScript -InstallDir $dotnetTempDir -Channel 8.0 -Runtime dotnet 2>$null
        if (Test-Path (Join-Path $dotnetTempDir "dotnet.exe")) {
            robocopy $dotnetTempDir $DotnetDir /E /NFL /NDL /NJH /NJS /NC /NS 2>$null
        }
        $dotnet8Installed = & "$DotnetDir\dotnet.exe" --list-runtimes 2>$null | Where-Object { $_ -match "8\.0" }
        if (-not $dotnet8Installed) { throw "Runtime install verification failed" }
        Write-OK "- .NET 8.0 runtime installed"
        Remove-Item $dotnetTempDir -Recurse -Force -ErrorAction SilentlyContinue
    } catch {
        Write-Warn ".NET 8.0 runtime installation failed: $_"
    }
} else {
    Write-OK "- .NET 8.0 runtime already installed"
}

Write-OK "Done (.NET all runtimes ready)"

# Setup uv (Python package manager for MCP servers)
Write-Host ""
Write-Host "[0.5/7] Setting up uv (Python package manager)..."
$uvInstalled = $false
if ($sysUvCmd) {
    $UvExe = $sysUvCmd.Source
    Write-OK "- Using system uv at $UvExe"
    $uvInstalled = $true
} elseif (Test-Path $UvExe) {
    Write-OK "- uv already installed at $UvDir"
    $uvInstalled = $true
} else {
    Write-Host "  - Downloading uv to $UvDir ..."
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $uvZip = Join-Path $env:TEMP "uv.zip"
        $uvUrl = "https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-pc-windows-msvc.zip"
        Invoke-WebRequest -Uri $uvUrl -OutFile $uvZip -UseBasicParsing
        
        if (-not (Test-Path $UvDir)) { New-Item -ItemType Directory -Path $UvDir -Force | Out-Null }
        Expand-Archive -Path $uvZip -DestinationPath $UvDir -Force
        Remove-Item $uvZip -Force -ErrorAction SilentlyContinue
        
        if (Test-Path $UvExe) {
            Write-OK "- uv installed to $UvDir"
            $uvInstalled = $true
        } else {
            Write-Warn "uv installation failed"
        }
    } catch {
        Write-Warn "Failed to download uv: $_"
        Write-Host "  Falling back to system uv..."
    }
}
if (-not $uvInstalled) {
    $systemUv = Get-Command uv -ErrorAction SilentlyContinue
    if ($systemUv) {
        $UvExe = $systemUv.Source
        Write-OK "- Using system uv at $UvExe"
    } else {
        Write-Warn "uv is not available. Some MCP features may not work."
        Write-Host "  Install from: https://github.com/astral-sh/uv"
    }
}

if (-not (Test-Path $ModdingTutorialDir)) {
    Write-Host "  - Cloning Modding-Tutorial reference..."
    & git clone https://github.com/fresh-milkshake/Modding-Tutorial $ModdingTutorialDir --quiet
    Write-OK "- Done"
} else {
    Write-OK "- Modding-Tutorial already exists"
}

if (-not $GameDir) {
    if (Test-Path $EnvFile) {
        Get-Content $EnvFile -ErrorAction SilentlyContinue | ForEach-Object {
            if ($_ -match '^GAME_DIR=(.*)$') { $GameDir = $Matches[1].Trim('"') }
        }
    }
}
if ($GameDir) {
    Write-Step 1 7 "Game Directory"
    if (Test-Path $EnvFile) {
        Write-Host "  Loaded from .env"
    } else {
        Write-Host "  Using from argument"
    }
} else {
    Write-Step 1 7 "Game Directory"
    Write-Host ""
    Write-Host "  Default: $DefaultGameDir"
    Write-Host ""
    $GameDir = Read-Host "Enter path (or press Enter for default)"
    if (-not $GameDir) { $GameDir = $DefaultGameDir }
}
Write-Host "  Using: $GameDir"

Write-Host "  - Saving to .env..."
"GAME_DIR=$GameDir" | Set-Content -Path $EnvFile -Encoding UTF8 -NoNewline
Write-OK "- .env saved"

Write-Host "  - Updating GameDir in .csproj..."
$csprojPath = Join-Path $ModDir "Sts2ModScaffold.csproj"
if (Test-Path $csprojPath) {
    $csprojContent = Get-Content $csprojPath -Raw -Encoding UTF8
    $csprojContent = $csprojContent -replace '<GameDir>[^<]*</GameDir>', "<GameDir>$GameDir</GameDir>"
    Set-Content -Path $csprojPath -Value $csprojContent -Encoding UTF8 -NoNewline
    Write-OK "- csproj GameDir updated"
} else {
    Write-Warn "- csproj not found at $csprojPath"
}

Write-Step 2 7 "Setting up game assembly references..."
$sts2Dll = Join-Path $GameDir "data_sts2_windows_x86_64\sts2.dll"
if (-not (Test-Path (Join-Path $RefDir "sts2.dll"))) {
    if (-not (Test-Path $sts2Dll)) {
        Write-Err "Game not found at $GameDir"
        if ([Console]::IsOutputRedirected -eq $false) { pause }
        exit 1
    }
    if (-not (Test-Path $RefDir)) { New-Item -ItemType Directory -Path $RefDir -Force | Out-Null }
    Copy-Item (Join-Path $GameDir "data_sts2_windows_x86_64\sts2.dll") $RefDir -Force
    Copy-Item (Join-Path $GameDir "data_sts2_windows_x86_64\0Harmony.dll") $RefDir -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $GameDir "data_sts2_windows_x86_64\GodotSharp.dll") $RefDir -Force -ErrorAction SilentlyContinue
    Write-OK "- Game references copied"
} else {
    Write-OK "- Game references already exist, skipping"
}
Write-OK "Done"

Write-Step 3 7 "Setting up Godot 4.5.1..."
$godotExePath = Join-Path $GodotDir "Godot_v4.5.1-stable_mono_win64.exe"
$downloadGodot = $false
if ($sysGodotCmd) {
    $sysGodotVer = & $sysGodotCmd --version 2>$null
    if ($sysGodotVer -match "4\.5") {
        Write-OK "- Using system Godot $sysGodotVer (at $($sysGodotCmd.Source))"
        $GodotExe = $sysGodotCmd.Source
        $downloadGodot = $false
    } else {
        Write-Warn "System Godot version is $sysGodotVer, need 4.5.1 - will download"
        $downloadGodot = $true
    }
} elseif (Test-Path $godotExePath) {
    Write-Host "  - Godot already installed, verifying..."
    if (Test-FileSize $godotExePath 10000000) {
        Write-OK "- Godot verified"
    } else {
        Write-Warn "Godot file seems corrupted, will re-download"
        $downloadGodot = $true
    }
} else {
    $downloadGodot = $true
}

if ($downloadGodot) {
    if (-not (Test-Path $GodotDir)) { New-Item -ItemType Directory -Path $GodotDir -Force | Out-Null }
    Write-Host "  - Downloading Godot 157MB..."
    try {
        $godotZip = Join-Path $ToolsDir "godot.zip"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri "https://github.com/godotengine/godot/releases/download/4.5.1-stable/Godot_v4.5.1-stable_mono_win64.zip" -OutFile $godotZip
        if (-not (Test-FileSize $godotZip 50000000)) {
            Write-Err "Godot download incomplete or failed ($(if (Test-Path $godotZip) { (Get-Item $godotZip).Length } else { 'N/A' }) bytes)"
            Remove-Item $godotZip -Force -ErrorAction SilentlyContinue
            Write-Warn "Please check your network connection and run install.ps1 again"
        } else {
            Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
            $extractDir = Join-Path $ToolsDir "Godot_v4.5.1-stable_mono_win64"
            if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force -ErrorAction SilentlyContinue }
            [System.IO.Compression.ZipFile]::ExtractToDirectory($godotZip, $ToolsDir)
            if (-not (Test-Path $GodotDir)) { New-Item -ItemType Directory -Path $GodotDir -Force | Out-Null }
            Move-Item -Path "$extractDir\*" -Destination $GodotDir -Force
            Remove-Item $extractDir -Force -Recurse -ErrorAction SilentlyContinue
            Remove-Item $godotZip -Force -ErrorAction SilentlyContinue
            Write-OK "- Godot downloaded and installed"
        }
    } catch {
        Write-Err "Godot download failed: $_"
        Write-Warn "Please check your network connection and run install.ps1 again"
    }
}

if (-not (Test-Path $GodotExe)) {
    Write-Err "Godot executable not found after step 3. Steps 5 and 7 will be skipped."
    Write-Warn "Please check your network and run install.ps1 again"
}
Write-OK "Done"

Write-Step 4 7 "Setting up ILSpy and ILSpy MCP Server..."
$ilspyExe = Join-Path $IlspyDir "ILSpy.exe"
$downloadIlspy = $false
if (Test-Path $ilspyExe) {
    Write-Host "  - ILSpy already installed, verifying..."
    if (Test-FileSize $ilspyExe 100000) {
        Write-OK "- ILSpy verified"
    } else {
        Write-Warn "ILSpy file seems corrupted, will re-download"
        $downloadIlspy = $true
    }
} else {
    $downloadIlspy = $true
}

if ($downloadIlspy) {
    Write-Host "  - Downloading ILSpy..."
    try {
        $ilspyZip = Join-Path $ToolsDir "ilspy.zip"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri "https://github.com/icsharpcode/ILSpy/releases/download/v10.0-preview2/ILSpy_binaries_10.0.0.8282-preview2-x64.zip" -OutFile $ilspyZip
        Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
        [System.IO.Compression.ZipFile]::ExtractToDirectory($ilspyZip, $IlspyDir)
        Remove-Item $ilspyZip -Force -ErrorAction SilentlyContinue
        Write-OK "- ILSpy downloaded and installed"
    } catch {
        Write-Err "ILSpy download/extract failed: $_"
        if (Test-Path $ilspyZip) { Remove-Item $ilspyZip -Force -ErrorAction SilentlyContinue }
    }
}

if (-not (Test-Path (Join-Path $IlspyMcpDir "ILSpy.Mcp.csproj"))) {
    Write-Host "  - Cloning ILSpy MCP Server..."
    & git clone https://github.com/bivex/ILSpy-Mcp.git $IlspyMcpDir --quiet
} else {
    Write-OK "- ILSpy MCP Server already cloned"
}

$ilspyMcpDll = Get-ChildItem -Path (Join-Path $IlspyMcpDir "bin\Release") -Recurse -Filter "ILSpy.Mcp.dll" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
if (-not $ilspyMcpDll -or -not (Test-Path $ilspyMcpDll)) {
    Write-Host "  - Building ILSpy MCP Server..."
    Push-Location $IlspyMcpDir
    & "$DotnetDir\dotnet.exe" build -c Release --verbosity quiet
    Pop-Location
    $ilspyMcpDll = Get-ChildItem -Path (Join-Path $IlspyMcpDir "bin\Release") -Recurse -Filter "ILSpy.Mcp.dll" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
} else {
    Write-OK "- ILSpy MCP Server already built"
}

Write-Host "  - Verifying ILSpy MCP Server DLL..."
if (-not $ilspyMcpDll -or -not (Test-Path $ilspyMcpDll)) {
    Write-Err "ILSpy MCP Server not built"
} else {
        if (Test-FileSize $ilspyMcpDll 1000) {
            Write-OK "- ILSpy MCP Server ready (DLL $(Test-FileSize $ilspyMcpDll -gt 0)) bytes)"
        } else {
            Write-Warn "ILSpy MCP Server DLL seems too small, rebuilding..."
            Push-Location $IlspyMcpDir
            & "$DotnetDir\dotnet.exe" build -c Release --verbosity quiet 2>$null
            Pop-Location
            if (Test-FileSize $ilspyMcpDll 1000) {
                Write-OK "- ILSpy MCP Server ready after rebuild"
            } else {
                Write-Err "ILSpy MCP Server DLL still too small after rebuild"
            }
        }
    }
Write-OK "Done"

Write-Step 5 7 "Setting up STS2MCP..."
if (-not (Test-Path (Join-Path $Sts2McpDir "STS2_MCP.csproj"))) {
    Write-Host "  - Cloning STS2MCP..."
    & git clone https://github.com/Gennadiyev/STS2MCP.git $Sts2McpDir --depth 1 --quiet
} else {
    Write-OK "- STS2MCP already cloned"
}

Write-Host "  - Building STS2MCP DLL..."
$sts2McpDll = Join-Path $Sts2McpDir "bin\Release\net9.0\STS2_MCP.dll"
if (-not (Test-Path $sts2McpDll)) {
    Push-Location $Sts2McpDir
    & "$DotnetDir\dotnet.exe" build STS2_MCP.csproj -c Release -p:STS2GameDir=$GameDir --verbosity quiet 2>$null
    Pop-Location
    if (-not (Test-Path $sts2McpDll)) {
        $sts2McpDll = Get-ChildItem -Path (Join-Path $Sts2McpDir "bin") -Recurse -Filter "STS2_MCP.dll" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
    }
}
if ((Test-Path $sts2McpDll) -and (Test-FileSize $sts2McpDll 1000)) {
    Write-OK "- STS2MCP DLL built"
} else {
    Write-Warn "STS2MCP DLL build may have failed"
}

$gameModsDir = Join-Path $GameDir "mods"
$sts2McpJson = Join-Path $Sts2McpDir "mod_manifest.json"
if (Test-Path $sts2McpDll) {
    if (-not (Test-Path (Join-Path $gameModsDir "STS2_MCP.dll"))) {
        if (-not (Test-Path $gameModsDir)) { New-Item -ItemType Directory -Path $gameModsDir -Force | Out-Null }
        Copy-Item $sts2McpDll $gameModsDir -Force
        Write-OK "- STS2MCP DLL installed to game"
    } else {
        Write-OK "- STS2MCP DLL already in game mods folder"
    }
    if ((Test-Path $sts2McpJson) -and (-not (Test-Path (Join-Path $gameModsDir "STS2_MCP.json")))) {
        Copy-Item $sts2McpJson (Join-Path $gameModsDir "STS2_MCP.json") -Force
        Write-OK "- STS2MCP manifest installed to game"
    }
} else {
    Write-Warn "STS2MCP DLL not built, skipping game installation"
}

Write-Host "  - Setting up STS2MCP Python MCP Server..."
if ((Test-Path $Sts2McpPythonDir) -and (Test-Path $UvExe)) {
    Push-Location $Sts2McpPythonDir
    & $UvExe sync --quiet 2>$null
    Pop-Location
} elseif (-not (Test-Path $UvExe)) {
    Write-Warn "uv not available, skipping Python MCP setup"
}

Write-Host "  - Testing STS2MCP Python MCP Server..."
if (Test-Path $UvExe) {
    Push-Location $Sts2McpPythonDir
    $sts2Test = & $UvExe run python --version 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Warn "STS2MCP Python MCP Server uv sync needed, running..."
        & $UvExe sync
        $sts2Test = & $UvExe run python --version 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Err "STS2MCP Python MCP Server not working"
        } else {
            Write-OK "- STS2MCP Python MCP Server verified (synced)"
        }
    } else {
        Write-OK "- STS2MCP Python MCP Server verified"
    }
    Pop-Location
} else {
    Write-Warn "uv not available, skipping Python MCP test"
}
Write-OK "Done"

Write-Step 5.5 7 "Setting up STS2MenuControl..."
$STS2MenuControlRepo = "https://github.com/L4ntern0/STS2-MenuControl.git"
if (-not (Test-Path (Join-Path $MenuControlDir "STS2MenuControl.csproj"))) {
    Write-Host "  - Cloning STS2MenuControl from GitHub..."
    if (Test-Path $MenuControlDir) { Remove-Item $MenuControlDir -Recurse -Force }
    & git clone --depth 1 $STS2MenuControlRepo $MenuControlDir 2>$null
    if (-not (Test-Path (Join-Path $MenuControlDir "STS2MenuControl.csproj"))) {
        Write-Warn "Failed to clone STS2MenuControl"
    } else {
        Write-OK "- STS2MenuControl cloned"
    }
} else {
    Write-OK "- STS2MenuControl source exists"
}
Write-Host "  - Building STS2MenuControl DLL..."
$menuCtrlDll = Join-Path $MenuControlDir "bin\Release\net9.0\STS2MenuControl.dll"
if (-not (Test-Path $menuCtrlDll)) {
    & "$DotnetDir\dotnet.exe" build (Join-Path $MenuControlDir "STS2MenuControl.csproj") -c Release -p:STS2GameDir=$GameDir --verbosity quiet 2>$null
    if (-not (Test-Path $menuCtrlDll)) {
        $menuCtrlDll = Get-ChildItem -Path (Join-Path $MenuControlDir "bin") -Recurse -Filter "STS2MenuControl.dll" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
    }
}
if ((Test-Path $menuCtrlDll) -and (Test-FileSize $menuCtrlDll 1000)) {
    Write-OK "- STS2MenuControl DLL built"
} else {
    Write-Warn "STS2MenuControl DLL build may have failed"
}
$menuCtrlJson = Join-Path $MenuControlDir "mod_manifest.json"
$gameModsDir = Join-Path $GameDir "mods"
if (Test-Path $menuCtrlDll) {
    if (-not (Test-Path (Join-Path $gameModsDir "STS2MenuControl.dll"))) {
        if (-not (Test-Path $gameModsDir)) { New-Item -ItemType Directory -Path $gameModsDir -Force | Out-Null }
        Copy-Item $menuCtrlDll $gameModsDir -Force
        Write-OK "- STS2MenuControl DLL installed to game"
    } else {
        Write-OK "- STS2MenuControl DLL already in game mods folder"
    }
    if ((Test-Path $menuCtrlJson) -and (-not (Test-Path (Join-Path $gameModsDir "STS2MenuControl.json")))) {
        Copy-Item $menuCtrlJson (Join-Path $gameModsDir "STS2MenuControl.json") -Force
        Write-OK "- STS2MenuControl manifest installed to game"
    }
} else {
    Write-Warn "STS2MenuControl DLL not built, skipping game installation"
}
Write-OK "Done"

Write-Step 6 7 "Configuring MCP servers and opencode.jsonc..."
$launchBat = Join-Path $IlspyDir "LaunchILSpy.bat"
@"
@echo off
set "THIS_DIR=%%~dp0"
set "DOTNET_ROOT=%%THIS_DIR%%..\..\dotnet"
set "PATH=%%DOTNET_ROOT%%\host;%%DOTNET_ROOT%%\shared;%%PATH%%"
start "" "%%THIS_DIR%%ILSpy.exe"
"@ | Set-Content -Path $launchBat -Encoding ASCII

$uvExeForConfig = $UvExe
if (-not $uvExeForConfig) { $uvExeForConfig = "uv" }

$jsoncPath = Join-Path $ProjectDir "opencode.jsonc"
$c = @"
{
  "`$schema": "https://opencode.ai/config.json",
  "mcp": {
    "ilspy": {
      "type": "local",
      "command": ["{{DOTNET_EXE}}", "{{ILSYPY_MCP_DLL}}"],
      "environment": {
        "DOTNET_ROOT": "{{DOTNET_ROOT}}"
      },
      "enabled": true
    },
    "sts2mcp": {
      "type": "local",
      "command": ["{{UV_EXE}}", "run", "--directory", "{{STS2_MCP_PYTHON_DIR}}", "python", "server.py"],
      "enabled": true
    }
  }
}
"@
$c = $c -replace '\{\{DOTNET_EXE\}\}', ((Join-Path $DotnetDir "dotnet.exe" | ConvertTo-Json).Trim('"'))
if ($ilspyMcpDll) {
    $c = $c -replace '\{\{ILSYPY_MCP_DLL\}\}', ($ilspyMcpDll | ConvertTo-Json).Trim('"')
} else {
    Write-Warn "ILSpy MCP DLL not found, opencode.jsonc will have invalid ilspy config"
}
$c = $c -replace '\{\{DOTNET_ROOT\}\}', ($DotnetDir | ConvertTo-Json).Trim('"')
$c = $c -replace '\{\{UV_EXE\}\}', ($uvExeForConfig | ConvertTo-Json).Trim('"')
$c = $c -replace '\{\{STS2_MCP_PYTHON_DIR\}\}', ($Sts2McpPythonDir | ConvertTo-Json).Trim('"')
[System.IO.File]::WriteAllText($jsoncPath, $c, (New-Object System.Text.UTF8Encoding $false))
Write-OK "Done"

Write-Step 7 7 "Build and install mod from src..."

$csprojFiles = Get-ChildItem -Path $ModDir -Filter "*.csproj" -ErrorAction SilentlyContinue
if ($csprojFiles) {
    $csproj = $csprojFiles[0].Name
    Write-Host "  - Project: $csproj"

    $dllName = $csproj -replace '\.csproj$', '.dll'
    $modFolderName = $csproj -replace '\.csproj$', ''
    $modDisplayName = $modFolderName
    $jsonFiles = Get-ChildItem -Path $ModDir -Filter "*.json" -ErrorAction SilentlyContinue
    foreach ($f in $jsonFiles) {
        try {
            $json = Get-Content $f.FullName | ConvertFrom-Json
            if ($json.name) { $modDisplayName = $json.name; break }
        } catch {}
    }
    Write-Host "  - DLL: $dllName"

    $dllPath = Join-Path $ModDir "bin\Release\$dllName"
    $needsBuild = $true
    if (Test-Path $dllPath) {
        if ((Get-Item $dllPath).Length -gt 0) { $needsBuild = $false }
    }

    if ($needsBuild) {
        Write-Host "  - Building project..."
        Push-Location $ModDir
        & "$DotnetDir\dotnet.exe" build -c Release --verbosity quiet 2>$null
        if ($LASTEXITCODE -ne 0) {
            Write-Err "Build failed!"
            Pop-Location
            if ([Console]::IsOutputRedirected -eq $false) { pause }
            exit 1
        }
        Write-OK "- Build succeeded"
        Pop-Location
    } else {
        Write-OK "- Build verified, skipping"
    }

    $modFolderName = $dllName
    if ($modFolderName.EndsWith(".dll")) { $modFolderName = $modFolderName.Substring(0, $modFolderName.Length - 4) }
    $modSubfolder = Join-Path $gameModsDir $modFolderName
    if (-not (Test-Path $modSubfolder)) { New-Item -ItemType Directory -Path $modSubfolder -Force | Out-Null }

    Copy-Item $dllPath $modSubfolder -Force

    $modIdJson = Join-Path $modSubfolder "$modFolderName.json"
    foreach ($f in $jsonFiles) {
        try {
            $json = Get-Content $f.FullName -ErrorAction SilentlyContinue | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($json.id) {
                $json | Add-Member -NotePropertyName "has_pck" -NotePropertyValue $true -Force -PassThru |
                Add-Member -NotePropertyName "has_dll" -NotePropertyValue $true -Force -PassThru |
                Add-Member -NotePropertyName "dependencies" -NotePropertyValue @() -Force -PassThru |
                Add-Member -NotePropertyName "affects_gameplay" -NotePropertyValue ($json.affects_gameplay -eq $true) -Force -PassThru |
                ConvertTo-Json -Depth 3 | Set-Content $modIdJson
            }
        } catch {}
    }

    $modManifest = Join-Path $modSubfolder "mod_manifest.json"
    foreach ($f in $jsonFiles) {
        try {
            $json = Get-Content $f.FullName -ErrorAction SilentlyContinue | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($json) {
                $out = @{
                    pck_name = if ($json.pck_name) { $json.pck_name } else { $json.name -replace ' ' }
                    name = $json.name
                    author = $json.author
                    description = $json.description
                    version = $json.version
                }
                $out | ConvertTo-Json | Set-Content $modManifest
            }
        } catch {}
    }

    Write-Host "  - Creating .pck file..."
    if (-not (Test-Path $GodotExe)) {
        Write-Warn "Godot not found at $GodotExe, skipping PCK creation"
        Write-Host "  - Run install.ps1 again to retry Godot download"
    } else {
    $pckScript = Join-Path $PckBuilderDir "build_pck.gd"
    $pckArgString = "--headless --path `"$PckBuilderDir`" --script `"$pckScript`" -- `"$modManifest`" `"$modSubfolder\$modFolderName.pck`""
    $pckProc = Start-Process -FilePath $GodotExe -ArgumentList $pckArgString -NoNewWindow -Wait -PassThru -RedirectStandardOutput (Join-Path $env:TEMP "pck_out.txt") -RedirectStandardError (Join-Path $env:TEMP "pck_err.txt")
    $pckStdout = Get-Content (Join-Path $env:TEMP "pck_out.txt") -Raw -ErrorAction SilentlyContinue
    $pckStderr = Get-Content (Join-Path $env:TEMP "pck_err.txt") -Raw -ErrorAction SilentlyContinue
    if ($pckStdout) { Write-Host "  $pckStdout" }
    if ($pckStderr) { Write-Host "  $pckStderr" }
    Remove-Item (Join-Path $env:TEMP "pck_out.txt") -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $env:TEMP "pck_err.txt") -Force -ErrorAction SilentlyContinue
    }

    $modPck = Join-Path $modSubfolder "$modFolderName.pck"
    if (Test-Path $modPck) {
        Write-OK "- .pck created successfully"
    } else {
        Write-Warn ".pck creation failed"
    }

    Write-OK "Installed to $modSubfolder"
} else {
    Write-Host "  - No .csproj found in src, skipping build"
}
Write-OK "Done"

Write-Host ""
$runDeepVerify = $DeepVerify
if (-not $runDeepVerify) {
    $runDeepVerify = "Y"
}

if ($runDeepVerify -eq "Y") {
    Write-Host ""
    Write-Host "[Deep Verify] Starting deep verification..."

    Write-Host "[Deep Verify] Closing any running game..."
    Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Write-OK "Done"

    $modLogDir = Join-Path $env:APPDATA "SlayTheSpire2\logs"
    $modLogFile = Join-Path $modLogDir "mod_log.txt"
    $godotLog = Join-Path $modLogDir "godot.log"
    if (Test-Path $modLogFile) { Remove-Item $modLogFile -Force -ErrorAction SilentlyContinue }
    if (Test-Path $godotLog) { Remove-Item $godotLog -Force -ErrorAction SilentlyContinue }
    $scriptStartTime = Get-Date
    Write-Host "[Deep Verify] Old logs cleared, start time: $($scriptStartTime.ToString('yyyy-MM-dd HH:mm:ss'))"

    Write-Host "[Deep Verify] Checking Steam..."
    $steam = Get-Process -Name "steam" -ErrorAction SilentlyContinue
    if (-not $steam) {
        Write-Host "  ERROR: Steam is not running." -ForegroundColor Red
        Write-Host "  Please launch Steam and log in. Polling every 5 seconds..." -ForegroundColor Yellow
        $steamPollCount = 0
        $steamReady = $false
        while ($steamPollCount -lt 120) {
            Start-Sleep -Seconds 5
            $steam = Get-Process -Name "steam" -ErrorAction SilentlyContinue
            if ($steam) {
                Write-Host ""
                Write-OK "- Steam detected!"
                Start-Sleep -Seconds 3
                $steamReady = $true
                break
            }
            $steamPollCount++
            Write-Host -NoNewline "."
        }
        if (-not $steamReady) {
            Write-Host ""
            Write-Err "Steam not detected after 10 minutes. Cannot launch game."
            Write-Host ""
            Write-Host "========================================" -ForegroundColor Red
            Write-Host " Setup Failed - Steam not running" -ForegroundColor Red
            Write-Host "========================================" -ForegroundColor Red
            if ([Console]::IsOutputRedirected -eq $false) { pause }
            exit 1
        }
    } else {
        Write-OK "- Steam is running"
    }

    Write-Host "[Deep Verify] Launching game via Steam..."
    Start-Process "steam://run/2868840"

    Write-Host "[Deep Verify] Waiting for game process..."
    $waitCount = 0
    while ($waitCount -lt 15) {
        Start-Sleep -Seconds 2
        $running = tasklist /FI "IMAGENAME eq SlayTheSpire2.exe" /NH 2>$null | Select-String "SlayTheSpire2"
        if ($running) {
            break
        }
        $waitCount++
        Write-Host -NoNewline "."
    }
    if ($waitCount -ge 15) {
        Write-Host ""
        Write-Err "Game did not start within 30 seconds"
        & powershell -ExecutionPolicy Bypass -File (Join-Path $ToolsDir "close_sts2.ps1")
        Stop-Process -Name "python" -Force -ErrorAction SilentlyContinue
        Stop-Process -Name "uv" -Force -ErrorAction SilentlyContinue
        Write-Host ""
        Write-Host "========================================" -ForegroundColor Red
        Write-Host " Setup Failed - Deep verification error" -ForegroundColor Red
        Write-Host "========================================" -ForegroundColor Red
        if ([Console]::IsOutputRedirected -eq $false) { pause }
        exit 1
    }
    Write-Host ""
    Write-OK "- Game process detected!"

    Write-Host "[Deep Verify] Checking for mod load in godot.log..."
    $modDetected = $false
    $waitCount = 0
    while ($waitCount -lt 30) {
        if (Test-Path $godotLog) {
            $logFile = Get-Item $godotLog
            if ($logFile.LastWriteTime -gt $scriptStartTime) {
                if (Select-String -Path $godotLog -Pattern "Finished mod initialization for '$modDisplayName'" -Quiet) {
                    $modDetected = $true
                    break
                }
            }
        }
        $waitCount++
        Write-Host -NoNewline "."
        Start-Sleep -Seconds 2
    }
    Write-Host ""
    if ($modDetected) {
        Write-OK "- SUCCESS: Mod '$modDisplayName' loaded after $($scriptStartTime.ToString('HH:mm:ss'))"
    } else {
        Write-Warn "Mod not detected in godot.log after 60 seconds"
        if (Test-Path $godotLog) {
            Write-Host "  Searching for any mod entries..."
            $modLines = Select-String -Path $godotLog -Pattern "mod|Mod|init" -ErrorAction SilentlyContinue
            if ($modLines) {
                $modLines | ForEach-Object { Write-Host "  $($_.LineNumber): $($_.Line.Trim())" }
            } else {
                Write-Host "  No mod entries found in log."
            }
        } else {
            Write-Host "  godot.log was never created."
        }
    }

    Write-Host "[Deep Verify] Cleaning up..."
    & powershell -ExecutionPolicy Bypass -File (Join-Path $ToolsDir "close_sts2.ps1")
    Stop-Process -Name "python" -Force -ErrorAction SilentlyContinue
    Stop-Process -Name "uv" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1
    Write-OK "- Game and MCP stopped"

    Write-Host "[Deep Verify] Deep verification complete!"
} else {
    Write-Host "  - Deep verification skipped"
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host " Setup Complete" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Environment is ready. Next steps:"
Write-Host "  1. Run '.\tools\rename-scaffold.ps1 -NewModName `"YourModName`"' to rename scaffold"
Write-Host "  2. Edit src/Hooks/ to implement your mod"
Write-Host "  3. Run install-mod.bat to rebuild and install"
Write-Host "  4. Run '.\tools\launch_sts2.ps1' to launch the game"
Write-Host "  5. Run '.\tools\close_sts2.ps1' to close the game"
Write-Host ""
Write-Host "MCP Servers configured in opencode.jsonc - restart OpenCode to load."
Write-Host ""

Write-Host "[Cleanup] Removing scaffold test mod from game..."
if ($modFolderName) {
    $scaffoldDir = Join-Path $gameModsDir "$modFolderName"
    if (Test-Path $scaffoldDir) {
        Remove-Item $scaffoldDir -Recurse -Force -ErrorAction SilentlyContinue
        if (-not (Test-Path $scaffoldDir)) {
            Write-OK "- Scaffold mod removed from game"
        } else {
            Write-Warn "- Could not fully remove scaffold (files may be locked)"
        }
    } else {
        Write-Host "  - Not found (already removed)"
    }
}
Write-Host ""

if ([Console]::IsOutputRedirected -eq $false) { pause }
