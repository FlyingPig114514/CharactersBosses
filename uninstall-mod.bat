@echo off
setlocal enabledelayedexpansion

echo ========================================
echo Uninstalling Mod from Game
echo ========================================
echo.

set "DEFAULT_GAME_DIR=C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2"
set "MOD_SRC_DIR=%~dp0src"

if not "%~1"=="" (
    set "GAME_DIR=%~1"
    echo Game Directory: !GAME_DIR!
) else (
    set "GAME_DIR="
    for /f "delims=" %%L in ('findstr /C:"GameDir" "!MOD_SRC_DIR!\*.csproj" 2^>nul') do (
        set "LINE=%%L"
    )
    if defined LINE (
        set "GAME_DIR=!LINE:*<GameDir>=!"
        set "GAME_DIR=!GAME_DIR:</GameDir>=!"
    )
    if not defined GAME_DIR set "GAME_DIR=!DEFAULT_GAME_DIR!"
    echo Using GameDir from csproj: !GAME_DIR!
)
echo.

if not exist "!GAME_DIR!\mods" (
    echo ERROR: Mods directory not found at !GAME_DIR!\mods
    echo Game may not be installed at that path.
    pause
    exit /b 1
)

set "MOD_NAME="
for %%F in ("!MOD_SRC_DIR!\*.csproj") do set "MOD_NAME=%%~nF"

if not defined MOD_NAME (
    echo ERROR: No .csproj found in !MOD_SRC_DIR!
    pause
    exit /b 1
)

set "MOD_SUBFOLDER=!GAME_DIR!\mods\!MOD_NAME!"
set "MCP_DLL=!GAME_DIR!\mods\STS2_MCP.dll"
set "MCP_JSON=!GAME_DIR!\mods\STS2_MCP.json"
set "MC_DLL=!GAME_DIR!\mods\STS2MenuControl.dll"
set "MC_JSON=!GAME_DIR!\mods\STS2MenuControl.json"

echo [1/3] Removing mod [!MOD_NAME!]...
if exist "!MOD_SUBFOLDER!" (
    rmdir /S /Q "!MOD_SUBFOLDER!"
    echo   - Removed !MOD_SUBFOLDER!
) else (
    echo   - Not found: !MOD_SUBFOLDER! (already removed)
)

echo [2/3] Removing STS2MCP files...
set "MCP_REMOVED=0"
if exist "!MCP_DLL!" (
    del /Q "!MCP_DLL!"
    echo   - Removed STS2_MCP.dll
    set "MCP_REMOVED=1"
)
if exist "!MCP_JSON!" (
    del /Q "!MCP_JSON!"
    echo   - Removed STS2_MCP.json
    set "MCP_REMOVED=1"
)
if "!MCP_REMOVED!"=="0" (
    echo   - Not found (already removed)
)

echo [3/3] Removing STS2MenuControl files...
set "MC_REMOVED=0"
if exist "!MC_DLL!" (
    del /Q "!MC_DLL!"
    echo   - Removed STS2MenuControl.dll
    set "MC_REMOVED=1"
)
if exist "!MC_JSON!" (
    del /Q "!MC_JSON!"
    echo   - Removed STS2MenuControl.json
    set "MC_REMOVED=1"
)
if "!MC_REMOVED!"=="0" (
    echo   - Not found (already removed)
)

echo.
echo ========================================
echo Done! Mod, STS2MCP, and STS2MenuControl removed.
echo ========================================
echo.
pause
