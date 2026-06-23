@echo off
setlocal
set ORIG=%~dp0dist\RDV.Host.exe
set DXGI=%~dp0dist\RDV.Host.Dxgi.exe

echo Which HOST do you want to launch?
echo.
echo   [1] Original  (GDI capture, port 8765)        ^<- known good
echo   [2] DXGI      (Desktop Duplication, port 8766) ^<- handles fullscreen games
echo.
choice /C 12 /N /M "Choose 1 or 2: "
if errorlevel 2 goto :dxgi
if errorlevel 1 goto :orig

:orig
if not exist "%ORIG%" (
    echo ERROR: RDV.Host.exe not found at:
    echo   %ORIG%
    echo Run BUILD_DOTNET8.bat first.
    pause
    exit /b 1
)
tasklist /FI "IMAGENAME eq RDV.Host.exe" 2>NUL | find /I "RDV.Host.exe" >NUL
if not errorlevel 1 (
    echo Original RDV Host is already running. Check the system tray.
    timeout /t 2 >NUL
    exit /b 0
)
start "" "%ORIG%"
exit /b 0

:dxgi
if not exist "%DXGI%" (
    echo ERROR: RDV.Host.Dxgi.exe not found at:
    echo   %DXGI%
    echo Run BUILD_DXGI.bat first.
    pause
    exit /b 1
)
tasklist /FI "IMAGENAME eq RDV.Host.Dxgi.exe" 2>NUL | find /I "RDV.Host.Dxgi.exe" >NUL
if not errorlevel 1 (
    echo DXGI RDV Host is already running. Check the system tray.
    timeout /t 2 >NUL
    exit /b 0
)
start "" "%DXGI%"
exit /b 0
