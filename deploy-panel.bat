@echo off
echo ================================================
echo Kneeboard Panel Deploy Script
echo ================================================
echo.

REM 1. Build the package
echo [1/4] Building package...
cd /d "D:\KneeboardServer\EFB\PackageSources\vendor\Kneeboard"
call npm run rebuild
if errorlevel 1 (
    echo ERROR: Build failed!
    pause
    exit /b 1
)
echo Build complete!
echo.

REM 2. Close MSFS if running
echo [2/4] Checking for running MSFS...
tasklist /FI "IMAGENAME eq FlightSimulator.exe" 2>NUL | find /I /N "FlightSimulator.exe">NUL
if "%ERRORLEVEL%"=="0" (
    echo WARNING: MSFS is running! Please close it first.
    pause
    exit /b 1
)
echo MSFS not running, continuing...
echo.

REM 3. Delete Coherent cache
echo [3/4] Deleting Coherent cache...
if exist "%LOCALAPPDATA%\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalCache\Local\Coherent" (
    rmdir /s /q "%LOCALAPPDATA%\Packages\Microsoft.FlightSimulator_8wekyb3d8bbwe\LocalCache\Local\Coherent"
    echo Coherent cache deleted!
) else (
    echo Coherent cache not found, skipping...
)
echo.

REM 4. Copy to Community folder
echo [4/4] Deploying to MSFS Community folder...
set "SOURCE=D:\KneeboardServer\EFB\PackageSources\vendor\Kneeboard\dist"
set "DEST=%APPDATA%\Microsoft Flight Simulator 2024\Packages\Community\gsimulations-kneeboard"

REM Delete old package
if exist "%DEST%" (
    echo Removing old package...
    rmdir /s /q "%DEST%"
)

REM Copy new package
echo Copying new package...
xcopy /E /I /Y "%SOURCE%" "%DEST%"
if errorlevel 1 (
    echo ERROR: Copy failed!
    pause
    exit /b 1
)

echo.
echo ================================================
echo DEPLOYMENT COMPLETE!
echo ================================================
echo.
echo Next steps:
echo 1. Start MSFS 2024
echo 2. Start Kneeboard Server
echo 3. Open the panel in MSFS
echo 4. Check console (Ctrl+Shift+F12) for:
echo    - "[Kneeboard] Starting initialization..."
echo    - "[Kneeboard] DOM elements found after X attempts"
echo    - "[Kneeboard] Initialized successfully"
echo.
pause
