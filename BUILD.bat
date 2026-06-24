@echo off
setlocal

set OUT=C:\ProgramData\i2Systems\Tools\RemoteDesktopViewer
set THUMBPRINT=B93F080C077A15FBDB3A0850B47429CB142CADF4
set TIMESTAMP=http://timestamp.digicert.com

echo [1/5] Stopping any running RDV processes...
taskkill /F /IM RDV.Host.exe /T >nul 2>&1
taskkill /F /IM RDV.Viewer.exe /T >nul 2>&1
tasklist /FI "IMAGENAME eq RDV.Host.exe" 2>NUL | find /I "RDV.Host.exe" >NUL
if not errorlevel 1 (
    echo ERROR: RDV.Host.exe is still running ^(likely elevated^).
    echo Right-click the tray icon and choose Exit, then re-run BUILD.bat.
    pause
    exit /b 1
)
tasklist /FI "IMAGENAME eq RDV.Viewer.exe" 2>NUL | find /I "RDV.Viewer.exe" >NUL
if not errorlevel 1 (
    echo ERROR: RDV.Viewer.exe is still running. Close it, then re-run BUILD.bat.
    pause
    exit /b 1
)

echo [2/5] Creating output folder...
if not exist "%OUT%" mkdir "%OUT%"

echo [3/5] Publishing RDV.Host...
dotnet publish RDV.Host\RDV.Host.csproj -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:DebugType=none ^
  -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true ^
  -o "%OUT%"
if errorlevel 1 ( echo ERROR: Host build failed. & exit /b 1 )

echo [4/5] Publishing RDV.Viewer...
dotnet publish RDV.Viewer\RDV.Viewer.csproj -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:DebugType=none ^
  -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true ^
  -o "%OUT%"
if errorlevel 1 ( echo ERROR: Viewer build failed. & exit /b 1 )

echo [5/5] Signing executables...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$cert = Get-ChildItem 'Cert:\CurrentUser\My\%THUMBPRINT%' -ErrorAction Stop;" ^
  "foreach ($exe in 'RDV.Host.exe','RDV.Viewer.exe') {" ^
  "  $sig = Set-AuthenticodeSignature -FilePath \"%OUT%\$exe\" -Certificate $cert -HashAlgorithm SHA256 -TimestampServer '%TIMESTAMP%' -IncludeChain All;" ^
  "  if ($sig.Status -ne 'Valid') { Write-Host \"ERROR: signing $exe -> $($sig.Status)\"; exit 1 }" ^
  "  Write-Host \"  $exe -> $($sig.Status)\"" ^
  "}"
if errorlevel 1 ( echo ERROR: Signing failed. & exit /b 1 )

echo.
echo Build complete. Output: %OUT%
echo   RDV.Host.exe   — run on the home PC (as Administrator, first time)
echo   RDV.Viewer.exe — run on the work laptop
