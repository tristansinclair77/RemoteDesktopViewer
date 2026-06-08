@echo off
setlocal

set OUT=C:\ProgramData\i2Systems\Tools\RemoteDesktopViewer
set SIGNTOOL="C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
set THUMBPRINT=B93F080C077A15FBDB3A0850B47429CB142CADF4
set TIMESTAMP=http://timestamp.digicert.com

echo [1/4] Creating output folder...
if not exist "%OUT%" mkdir "%OUT%"

echo [2/4] Publishing RDV.Host (self-contained .NET 8)...
dotnet publish RDV.Host\RDV.Host.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:DebugType=none -o "%OUT%"
if errorlevel 1 ( echo ERROR: Host build failed. & exit /b 1 )

echo [3/4] Publishing RDV.Viewer (self-contained .NET 8)...
dotnet publish RDV.Viewer\RDV.Viewer.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:DebugType=none -o "%OUT%"
if errorlevel 1 ( echo ERROR: Viewer build failed. & exit /b 1 )

echo [4/4] Signing executables...
%SIGNTOOL% sign /sha1 %THUMBPRINT% /fd SHA256 /tr "%TIMESTAMP%" /td SHA256 "%OUT%\RDV.Host.exe"
if errorlevel 1 ( echo ERROR: Signing RDV.Host.exe failed. & exit /b 1 )

%SIGNTOOL% sign /sha1 %THUMBPRINT% /fd SHA256 /tr "%TIMESTAMP%" /td SHA256 "%OUT%\RDV.Viewer.exe"
if errorlevel 1 ( echo ERROR: Signing RDV.Viewer.exe failed. & exit /b 1 )

echo.
echo Build complete. Output: %OUT%
echo   RDV.Host.exe   — run on the home PC (as Administrator, first time)
echo   RDV.Viewer.exe — run on the work laptop
echo   Note: .NET 8 runtime is bundled — no installation required on target machines.
