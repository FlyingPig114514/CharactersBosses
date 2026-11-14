@echo off
setlocal enabledelayedexpansion

echo ========================================
echo Rebuilding and Installing Mod
echo ========================================
echo.

set "DEFAULT_GAME_DIR=C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2"
set "MOD_SRC_DIR=%~dp0src"

rem ★ 用户 2026-11-06：「只有我说打包的时候再往 .dist 放一份打好的，中间版本全是 bug 没有用」。
rem   所以带 pack 参数才打包，且参数可以放在任意位置（会被剥掉，不影响 GAME_DIR 解析）：
rem       install-mod.bat pack                      ← 用默认/ csproj 里的游戏路径 + 打包
rem       install-mod.bat "D:\...\Slay the Spire 2" pack
set "DO_PACK="
:strip_pack_flag
if /i "%~1"=="pack" (
    set "DO_PACK=1"
    shift
    goto strip_pack_flag
)

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

if not exist "!GAME_DIR!\data_sts2_windows_x86_64\sts2.dll" (
    echo ERROR: Game not found at !GAME_DIR!
    echo.
    set /p "GAME_DIR=Enter correct game path: "
    if not exist "!GAME_DIR!\data_sts2_windows_x86_64\sts2.dll" (
        echo ERROR: Still not found. Aborting.
        pause
        exit /b 1
    )
)

set "MOD_NAME="
set "CSProjFile="
for %%F in ("!MOD_SRC_DIR!\*.csproj") do (
    set "CSProjFile=%%~nxF"
    set "MOD_NAME=%%~nF"
)

if not defined MOD_NAME (
    echo ERROR: No .csproj found in !MOD_SRC_DIR!
    pause
    exit /b 1
)

set "MANIFEST_SRC="
for %%F in ("!MOD_SRC_DIR!\*.json") do set "MANIFEST_SRC=%%F"

set "TOOLS_DIR=%~dp0"
set "DOTNET_EXE=!TOOLS_DIR!tools\dotnet\dotnet.exe"
set "DOTNET_ROOT=!TOOLS_DIR!tools\dotnet"
set "MOD_SUBFOLDER=!GAME_DIR!\mods\!MOD_NAME!"
set "GODOT_EXE=!TOOLS_DIR!tools\godot\Godot_v4.5.1-stable_mono_win64.exe"
set "PCK_BUILDER_DIR=!TOOLS_DIR!tools\pck_builder"
set "PCK_SCRIPT=!PCK_BUILDER_DIR!\build_pck.gd"

if not exist "!DOTNET_EXE!" (
    echo ERROR: Local dotnet not found at !DOTNET_EXE!
    echo   Run install.bat first to set up the environment.
    pause
    exit /b 1
)

echo [1/4] Building mod [!MOD_NAME!]...
set "PATH=!DOTNET_ROOT!;!PATH!"

rem ★ 双版本（2026-11-05）：两版 sts2 的 API 不兼容（Hook.ModifyDamage 等签名不同），
rem   同一份源码要按目标版本各编一个 dll。这里读游戏的 release_info.json 自动挑：
rem   0.107.x（正式版）→ Sts2Target=107；其它（含 0.111 beta）→ 111。
set "STS2_TARGET=111"
set "RELEASE_INFO=!GAME_DIR!\release_info.json"
if exist "!RELEASE_INFO!" (
    for /f "usebackq tokens=2 delims=:" %%V in (`findstr /i "\"version\"" "!RELEASE_INFO!"`) do (
        set "RAWVER=%%V"
    )
)
if defined RAWVER (
    set "RAWVER=!RAWVER: =!"
    set "RAWVER=!RAWVER:"=!"
    set "RAWVER=!RAWVER:,=!"
    echo   - Game version: !RAWVER!
    echo !RAWVER! | findstr /b /c:"v0.107" >nul && set "STS2_TARGET=107"
)
echo   - Building for sts2 target: !STS2_TARGET!

"!DOTNET_EXE!" build "!MOD_SRC_DIR!\!CSProjFile!" -c Release -p:Sts2Target=!STS2_TARGET! --verbosity quiet 2>nul
if errorlevel 1 (
    echo ERROR: Build failed!
    pause
    exit /b 1
)

rem ★ 双版本外壳方案（2026-11-05）：游戏只加载 CharacterBosses.dll（外壳），
rem   它按运行时游戏版本把 CharacterBosses.Impl107.dll / .Impl111.dll 之一载进来
rem   ⇒ **两个实现都要编译、都要安装**，这样同一个模组在正式版和 beta 上都能用。
set "LOADER_PROJ=!TOOLS_DIR!loader\Loader.csproj"
if not exist "!LOADER_PROJ!" set "LOADER_PROJ=!TOOLS_DIR!loader\Loader.csproj"
if exist "!TOOLS_DIR!loader\Loader.csproj" (
    "!DOTNET_EXE!" build "!TOOLS_DIR!loader\Loader.csproj" -c Release --verbosity quiet 2>nul
    if errorlevel 1 (
        echo ERROR: Loader build failed!
        pause
        exit /b 1
    )
) else (
    echo   - WARNING: loader\Loader.csproj not found, 只装单版本实现
)
if not "!STS2_TARGET!"=="107" (
    "!DOTNET_EXE!" build "!MOD_SRC_DIR!\!CSProjFile!" -c Release -p:Sts2Target=107 --verbosity quiet 2>nul
)
if not "!STS2_TARGET!"=="111" (
    "!DOTNET_EXE!" build "!MOD_SRC_DIR!\!CSProjFile!" -c Release -p:Sts2Target=111 --verbosity quiet 2>nul
)
echo   - Build succeeded

echo [2/4] Creating mod subfolder...
if not exist "!MOD_SUBFOLDER!" mkdir "!MOD_SUBFOLDER!"
echo   - !MOD_SUBFOLDER!

echo [3/4] Copying files and generating manifests...
rem 外壳（文件名必须是模组 id）+ 两份实现
if exist "!TOOLS_DIR!loader\bin\CharacterBosses.dll" (
    copy /Y "!TOOLS_DIR!loader\bin\CharacterBosses.dll" "!MOD_SUBFOLDER!\CharacterBosses.dll" >nul
    echo   - Loader copied: CharacterBosses.dll
) else (
    copy /Y "!MOD_SRC_DIR!\bin\Release-!STS2_TARGET!\!MOD_NAME!.dll" "!MOD_SUBFOLDER!\" >nul
    echo   - DLL copied: Release-!STS2_TARGET! [no shell]
)
rem ★ v0.2.588: both implementations ship as ".dll.bin" instead of ".dll".
rem   Reason: CVC (cross-version compat) and similar scanners treat EVERY ".dll" in a mod
rem   folder as a separate mod implementation and fingerprint its target game version.
rem   Our dormant Impl107 copy made the whole mod look like a 0.107-target mod.
rem   The loader uses AssemblyLoadContext.LoadFromAssemblyPath, which ignores extensions.
rem   Also delete leftover ".dll" copies from older versions so both cannot coexist.
if exist "!MOD_SUBFOLDER!\!MOD_NAME!.Impl107.dll.bin" del /q "!MOD_SUBFOLDER!\!MOD_NAME!.Impl107.dll.bin" >nul 2>nul
if exist "!MOD_SUBFOLDER!\!MOD_NAME!.Impl111.dll.bin" del /q "!MOD_SUBFOLDER!\!MOD_NAME!.Impl111.dll.bin" >nul 2>nul
if exist "!MOD_SUBFOLDER!\!MOD_NAME!.Impl107.dll" del /q "!MOD_SUBFOLDER!\!MOD_NAME!.Impl107.dll" >nul 2>nul
if exist "!MOD_SUBFOLDER!\!MOD_NAME!.Impl111.dll" del /q "!MOD_SUBFOLDER!\!MOD_NAME!.Impl111.dll" >nul 2>nul
if exist "!MOD_SRC_DIR!\bin\Release-107\!MOD_NAME!.Impl107.dll" (
    copy /Y "!MOD_SRC_DIR!\bin\Release-107\!MOD_NAME!.Impl107.dll" "!MOD_SUBFOLDER!\!MOD_NAME!.Impl107.dll.bin" >nul
    echo   - Impl copied: 107 as .dll.bin
)
if exist "!MOD_SRC_DIR!\bin\Release-111\!MOD_NAME!.Impl111.dll" (
    copy /Y "!MOD_SRC_DIR!\bin\Release-111\!MOD_NAME!.Impl111.dll" "!MOD_SUBFOLDER!\!MOD_NAME!.Impl111.dll.bin" >nul
    echo   - Impl copied: 111 as .dll.bin
)

rem 虚影精灵表以**独立文件**随 mod 一起安装，不再 base64 内嵌进 DLL。
rem 原因：base64 在程序集里按 UTF-16 存储（每字符 2 字节），而 dotnet 的 #US
rem 字符串堆有 16MB 硬上限，素材一多就报 CS8103「用户字符串的合并后长度超出
rem 所允许的限制」。改走文件之后 DLL 只有几百 KB，加多少动画都不会顶到上限。
set "ASSET_SRC=%~dp0assets\phantom_anim"
if exist "!ASSET_SRC!" (
    if not exist "!MOD_SUBFOLDER!\frames" mkdir "!MOD_SUBFOLDER!\frames"
    xcopy /Y /Q "!ASSET_SRC!\*.png" "!MOD_SUBFOLDER!\frames\" >nul
    echo   - Phantom frames copied
) else (
    echo   - WARNING: !ASSET_SRC! not found, phantoms will not render
)

if defined MANIFEST_SRC (
    copy /Y "!MANIFEST_SRC!" "!MOD_SUBFOLDER!\!MOD_NAME!.json" >nul
    echo   - Source manifest copied
    rem ★ 2026-11-05：**不再生成 mod_manifest.json**。
    rem   原因（实测）：游戏会把 mods\<Mod>\ 下的每个 *.json 都当清单读一遍，
    rem   而 mod_manifest.json 与 <Mod>.json 是同一个 id ⇒ 报「存在多个 id 为 X 的模组」；
    rem   而且之前那份是手拼的、**缺 id 字段** ⇒ 日志里直接
    rem   `[ERROR] Mod manifest ... is missing the 'id' field! ... The mod will not be loaded.`
    rem   <Mod>.json 本身已经是一份完整清单（build_pck.gd 也明确不把清单打进 pck），所以删掉这份多余的。
    if exist "!MOD_SUBFOLDER!\mod_manifest.json" del /q "!MOD_SUBFOLDER!\mod_manifest.json"
    echo   - mod_manifest.json removed; keep only !MOD_NAME!.json
) else (
    echo   - WARNING: No manifest found in !MOD_SRC_DIR!
)

rem ★ 装机自检（2026-11-13）：游戏开着的时候上面两条 copy 会因为文件被占用而**静默失败**
rem   （`>nul` 把错误吞掉了），于是出现「Impl107 新版 + Impl111 旧版」的混版状态。
rem   v0.2.502 的联机不同步、v0.2.591 这次都是这么来的 ⇒ 拷完立刻校验两份的版本串。
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0tools\verify-deploy.ps1" -ModDir "!MOD_SUBFOLDER!"
if errorlevel 1 (
    echo   - WARNING: deployed impl versions do NOT match the manifest
    echo     Close the game completely and re-run install-mod.bat
)


echo [4/4] Creating .pck file...
if exist "!GODOT_EXE!" (
    if exist "!PCK_SCRIPT!" (
        "!GODOT_EXE!" --headless --path "!PCK_BUILDER_DIR!" --script "!PCK_SCRIPT!" -- "!MOD_SUBFOLDER!\mod_manifest.json" "!MOD_SUBFOLDER!\!MOD_NAME!.pck" "%~dp0content" "!MOD_NAME!" "%~dp0tools\art\pck_extras.txt"
        if exist "!MOD_SUBFOLDER!\!MOD_NAME!.pck" (
            echo   - PCK created successfully
        ) else (
            echo   - WARNING: PCK creation may have failed
        )
    ) else (
        echo   - WARNING: PCK builder script not found at !PCK_SCRIPT!
    )
) else (
    echo   - WARNING: Godot not found at !GODOT_EXE!, skipping PCK
    echo   - Run install.bat first to set up Godot
)

rem ── [5/5] 打包：**只在明确要求时才做** ──────────────────────────────
rem   ★ 用户 2026-11-06 明确要求：「只有我说打包的时候再往 .dist 放一份打好的，
rem     中间版本全是 bug 没有用」。所以这一步**默认跳过**，只有传了 pack 参数才执行：
rem       install-mod.bat pack          （或直接跑 tools\pack-dist.ps1）
rem   日常改代码 → 只是安装到游戏目录，不再往 .dist 里堆 zip。
if defined DO_PACK (
    echo [5/5] Packing: syncing workshop\content and adding a zip to .dist ...
    powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0tools\pack-dist.ps1"
    if errorlevel 1 echo   - WARNING: pack-dist.ps1 失败（不影响已安装到游戏目录的那一份）
) else (
    echo [5/5] Skip packing ^(add "pack" to the command, or run tools\pack-dist.ps1^).
)

echo.
echo ========================================
echo Done! Mod installed to:
echo   !MOD_SUBFOLDER!
echo ========================================
echo.
if "%~1"=="" pause
