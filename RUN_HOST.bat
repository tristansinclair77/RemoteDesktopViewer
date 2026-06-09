@echo off
setlocal
set EXE=C:\ProgramData\i2Systems\Tools\RemoteDesktopViewer\RDV.Host.exe

if not exist "%EXE%" (
    echo ERROR: RDV.Host.exe not found at:
    echo   %EXE%
    echo Run BUILD.bat first.
    pause
    exit /b 1
)

tasklist /FI "IMAGENAME eq RDV.Host.exe" 2>NUL | find /I "RDV.Host.exe" >NUL
if not errorlevel 1 (
    echo RDV Host is already running. Check the system tray.
    timeout /t 2 >NUL
    exit /b 0
)

start "" "%EXE%"
