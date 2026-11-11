@echo off
setlocal
rem Open the extracted Slay the Spire 2 project in Godot 4.5.1 mono (browse only).
rem APPDATA / LOCALAPPDATA must point inside the workspace, otherwise Godot
rem writes editor config to the real AppData.
rem Keep this file pure ASCII: cmd reads .bat as ANSI and mangles CJK comments.

cd /d "%~dp0"

set "APPDATA=%CD%\.recon\_profile\AppData\Roaming"
set "LOCALAPPDATA=%CD%\.recon\_profile\AppData\Local"
set "GODOT=%CD%\tools\godot\Godot_v4.5.1-stable_mono_win64.exe"
set "PROJ=%CD%\.recon\project"

if not exist "%GODOT%" (
    echo [X] Godot not found: %GODOT%
    pause
    exit /b 1
)
if not exist "%PROJ%\project.godot" (
    echo [X] Project not found: %PROJ%
    echo     Extract the pck first with tools\art\extract_pck_project.gd
    pause
    exit /b 1
)

echo Starting Godot 4.5.1 mono editor...
echo   project: %PROJ%
start "StS2-Godot" "%GODOT%" --path "%PROJ%" --editor
exit /b 0
