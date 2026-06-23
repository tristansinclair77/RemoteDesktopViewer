@echo off
setlocal

REM Builds the DXGI host and viewer ONLY. Does NOT touch the original RDV.Host /
REM RDV.Viewer EXEs in dist\ -- writes RDV.Host.Dxgi.exe and RDV.Viewer.Dxgi.exe.
REM Safe to run while RDV.Host.exe is still serving a remote session.

set OUT=%~dp0dist
set THUMBPRINT=2BD8E89BABDE8EE56906BDD577BB1E794AA797DC
set TIMESTAMP=http://timestamp.digicert.com

echo [1/4] Stopping any running DXGI processes (original RDV.Host left alone)...
taskkill /F /IM RDV.Host.Dxgi.exe /T >nul 2>&1
taskkill /F /IM RDV.Viewer.Dxgi.exe /T >nul 2>&1
tasklist /FI "IMAGENAME eq RDV.Host.Dxgi.exe" 2>NUL | find /I "RDV.Host.Dxgi.exe" >NUL
if not errorlevel 1 (
    echo ERROR: RDV.Host.Dxgi.exe is still running ^(likely elevated^).
    echo Right-click the DXGI tray icon and choose Exit, then re-run this script.
    pause
    exit /b 1
)
tasklist /FI "IMAGENAME eq RDV.Viewer.Dxgi.exe" 2>NUL | find /I "RDV.Viewer.Dxgi.exe" >NUL
if not errorlevel 1 (
    echo ERROR: RDV.Viewer.Dxgi.exe is still running. Close it, then re-run this script.
    pause
    exit /b 1
)

if not exist "%OUT%" mkdir "%OUT%"

echo [2/4] Publishing RDV.Host.Dxgi (self-contained .NET 8)...
dotnet publish RDV.Host.Dxgi\RDV.Host.Dxgi.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:DebugType=none ^
  -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true ^
  -o "%OUT%"
if errorlevel 1 ( echo ERROR: DXGI Host build failed. & exit /b 1 )

echo [3/4] Publishing RDV.Viewer.Dxgi (self-contained .NET 8)...
dotnet publish RDV.Viewer.Dxgi\RDV.Viewer.Dxgi.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:DebugType=none ^
  -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true ^
  -o "%OUT%"
if errorlevel 1 ( echo ERROR: DXGI Viewer build failed. & exit /b 1 )

echo [4/4] Signing executables...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$cert = Get-ChildItem 'Cert:\CurrentUser\My\%THUMBPRINT%' -ErrorAction Stop;" ^
  "foreach ($exe in 'RDV.Host.Dxgi.exe','RDV.Viewer.Dxgi.exe') {" ^
  "  $sig = Set-AuthenticodeSignature -FilePath \"%OUT%\$exe\" -Certificate $cert -HashAlgorithm SHA256 -TimestampServer '%TIMESTAMP%' -IncludeChain All;" ^
  "  if ($sig.Status -ne 'Valid') { Write-Host \"ERROR: signing $exe -> $($sig.Status)\"; exit 1 }" ^
  "  Write-Host \"  $exe -> $($sig.Status)\"" ^
  "}"
if errorlevel 1 ( echo ERROR: Signing failed. & exit /b 1 )

echo.
echo DXGI build complete. Output:
echo   %OUT%\RDV.Host.Dxgi.exe   - DXGI Desktop Duplication host (port 8766, separate config)
echo   %OUT%\RDV.Viewer.Dxgi.exe - matching viewer (default port 8766)
