@echo off
rem OpenHS first-time setup (run once, then run-local.bat):
rem   1. copies your Hearthstone install into "Hearthstone main game" (a private copy; your real install is never changed)
rem   2. adds the mod loader (BepInEx) and the OpenHS plugins to that copy
rem   3. builds the server
rem   4. exports the card database from your own game (Server\data\dbf-<version>-<date>)
rem Usage: setup.bat                     (finds Hearthstone through Battle.net's registration)
rem        setup.bat "D:\Games\Hearthstone"
rem Needs: the .NET 9 SDK (https://dotnet.microsoft.com/download), about 15 GB free disk space.
setlocal EnableExtensions
set "SERVER=%~dp0"
set "SERVER=%SERVER:~0,-1%"
for %%I in ("%SERVER%\..") do set "ROOT=%%~fI"
set "GAME=%ROOT%\Hearthstone main game"
set "LIVE=%~1"

where dotnet >nul 2>nul || (echo The .NET SDK is missing: install it from https://dotnet.microsoft.com/download & goto failed)

if "%LIVE%"=="" for /f "tokens=2,*" %%A in ('reg query "HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Hearthstone" /v InstallLocation 2^>nul ^| find "InstallLocation"') do set "LIVE=%%B"
if "%LIVE%"=="" for /f "tokens=2,*" %%A in ('reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Hearthstone" /v InstallLocation 2^>nul ^| find "InstallLocation"') do set "LIVE=%%B"
if "%LIVE%"=="" set "LIVE=C:\Program Files (x86)\Hearthstone"
if not exist "%LIVE%\Hearthstone.exe" (
  echo Hearthstone wasn't found at "%LIVE%".
  echo Run: setup.bat "C:\path\to\Hearthstone"
  goto failed
)
tasklist /fi "imagename eq Hearthstone.exe" | find /i "Hearthstone.exe" >nul && (echo Close Hearthstone first. & goto failed)

echo [1/4] Copying Hearthstone from "%LIVE%" into "%GAME%" (the first time takes a while)...
rem other mods in your install (BepInEx, loaders) are not copied
robocopy "%LIVE%" "%GAME%" /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 /XD BepInEx Logs /XF winhttp.dll version.dll doorstop_config.ini HsMod.dll *.log >nul
if errorlevel 8 (echo Copying failed. & goto failed)

echo [2/4] Adding the mod loader and the OpenHS plugins...
robocopy "%SERVER%\loader" "%GAME%" /E /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 (echo Copying the loader failed. & goto failed)

echo [3/4] Building the server and the client plugin...
dotnet build "%SERVER%\src\HsServer\HsServer.csproj" -c Release -o "%SERVER%\run" -nologo -v q || goto failed
dotnet build "%SERVER%\client\HsLocalServer\HsLocalServer.csproj" -c Release -nologo -v q || goto failed
copy /y "%SERVER%\client\HsLocalServer\bin\Release\net48\HsLocalServer.dll" "%GAME%\BepInEx\plugins\HsLocalServer.dll" >nul || goto failed

echo [4/4] Exporting the card database from your game (the game opens once and closes by itself)...
"%SERVER%\run\HsServer.exe" update --yes --no-client-sync || goto failed

echo.
echo Setup done. Start the server and the game with run-local.bat
echo (run-local.bat norecording = a fresh account; see README.md and ADMIN.md)
pause
exit /b 0

:failed
echo.
echo Setup stopped. Fix the problem above and run setup.bat again (it continues where it can).
pause
exit /b 1
