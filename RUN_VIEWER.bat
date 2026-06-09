@echo off
setlocal
set EXE=C:\ProgramData\i2Systems\Tools\RemoteDesktopViewer\RDV.Viewer.exe

if not exist "%EXE%" (
    echo ERROR: RDV.Viewer.exe not found at:
    echo   %EXE%
    echo Run BUILD.bat first.
    pause
    exit /b 1
)

start "" "%EXE%"
