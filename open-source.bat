@echo off
setlocal
rem Open the decompiled game sources ( .recon\src-decompiled ) in VS Code.
rem Pure ASCII on purpose: cmd reads .bat as ANSI and mangles CJK comments.

cd /d "%~dp0"
set "SRC=%CD%\.recon\src-decompiled"

if not exist "%SRC%" (
    echo [X] Not found: %SRC%
    echo     Run: dotnet tools\ilspy-cli\bin\Release\net10.0\ilspycli.dll dump .refs\sts2_111.dll .recon\src-decompiled all
    pause
    exit /b 1
)

where code >nul 2>nul
if errorlevel 1 (
    echo [i] "code" not in PATH, opening Explorer instead.
    start "" explorer "%SRC%"
) else (
    start "" code "%SRC%"
)
exit /b 0
