@echo off
setlocal
set ORIG=%~dp0dist\RDV.Viewer.exe
set DXGI=%~dp0dist\RDV.Viewer.Dxgi.exe

echo Which VIEWER do you want to launch?
echo.
echo   [1] Original  (default port 8765)  ^<- talks to original RDV.Host
echo   [2] DXGI      (default port 8766)  ^<- talks to RDV.Host.Dxgi
echo.
choice /C 12 /N /M "Choose 1 or 2: "
if errorlevel 2 goto :dxgi
if errorlevel 1 goto :orig

:orig
if not exist "%ORIG%" (
    echo ERROR: RDV.Viewer.exe not found at:
    echo   %ORIG%
    echo Run BUILD_DOTNET8.bat first.
    pause
    exit /b 1
)
start "" "%ORIG%"
exit /b 0

:dxgi
if not exist "%DXGI%" (
    echo ERROR: RDV.Viewer.Dxgi.exe not found at:
    echo   %DXGI%
    echo Run BUILD_DXGI.bat first.
    pause
    exit /b 1
)
start "" "%DXGI%"
exit /b 0
