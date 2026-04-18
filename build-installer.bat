@echo off
REM ============================================================
REM  DarkQuill — Build Release + MSI Installer
REM  Run from the repo root: .\build-installer.bat
REM ============================================================

setlocal enabledelayedexpansion

echo.
echo ============================================================
echo  DarkQuill Installer Build
echo ============================================================
echo.

REM -- Step 1: Clean previous publish output
echo [1/4] Cleaning previous publish output...
if exist "publish\darkquill" rd /s /q "publish\darkquill"
if exist "publish\installer" rd /s /q "publish\installer"
echo       Done.
echo.

REM -- Step 2: Publish self-contained release build
echo [2/4] Publishing self-contained release build (win-x64)...
dotnet publish src\DarkQuill\DarkQuill.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=false ^
    -p:IncludeNativeLibrariesForSelfExtract=false ^
    -o publish\darkquill

if !errorlevel! neq 0 (
    echo.
    echo ERROR: dotnet publish failed. See output above.
    exit /b 1
)
echo       Done.
echo.

REM -- Step 3: Verify critical files exist
echo [3/4] Verifying publish output...
set MISSING=0

if not exist "publish\darkquill\DarkQuill.exe" (
    echo       MISSING: DarkQuill.exe
    set MISSING=1
)
if not exist "publish\darkquill\runtimes\win-x64\whisper.dll" (
    echo       WARNING: whisper.dll not found at runtimes\win-x64\whisper.dll
)

if !MISSING! neq 0 (
    echo.
    echo ERROR: Critical files missing from publish output.
    exit /b 1
)

echo       Publish output looks good.
echo.

REM -- Step 4: Build the MSI
echo [4/4] Building MSI installer...

REM Check if WiX is available
where wix >nul 2>nul
if !errorlevel! neq 0 (
    echo.
    echo  WiX Toolset v4+ is not installed or not on PATH.
    echo  Install it with:  dotnet tool install --global wix
    echo  Then run:         wix extension add WixToolset.UI.wixext
    echo.
    echo  The publish output is ready at: publish\darkquill\
    echo  You can build the MSI manually after installing WiX.
    exit /b 0
)

dotnet build src\DarkQuill.Installer\DarkQuill.Installer.wixproj ^
    -c Release ^
    -o publish\installer

if !errorlevel! neq 0 (
    echo.
    echo ERROR: MSI build failed. See output above.
    echo.
    echo  Common fixes:
    echo  - Install WiX: dotnet tool install --global wix
    echo  - Make sure publish\darkquill\ has the app files
    exit /b 1
)

echo.
echo ============================================================
echo  Build complete!
echo.
echo  Publish output: publish\darkquill\
echo  MSI installer:  publish\installer\DarkQuill.Installer.msi
echo ============================================================
echo.
