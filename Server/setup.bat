@echo off
rem OpenHS first-time setup (run once, then run-local.bat).
rem
rem The simple way: copy your Hearthstone folder (e.g. to D:\PrivateHS), put this OpenHS-Server folder inside it
rem (D:\PrivateHS\OpenHS-Server\Server\setup.bat) and run setup.bat. That copy then becomes the game that connects to this server;
rem your real Hearthstone is never changed.
rem If OpenHS-Server is NOT inside a Hearthstone folder, setup makes the copy itself ("Hearthstone main game" next to Server\)
rem from your Battle.net install (or: setup.bat "D:\Games\Hearthstone").
rem
rem Steps: 1. the game copy  2. the mod loader (BepInEx) + OpenHS plugins in it  3. build the server  4. the card database
rem Needs: the .NET 9 SDK (https://dotnet.microsoft.com/download).
setlocal EnableExtensions
set "SERVER=%~dp0"
set "SERVER=%SERVER:~0,-1%"
for %%I in ("%SERVER%\..") do set "ROOT=%%~fI"
for %%I in ("%ROOT%\..") do set "PARENT=%%~fI"
set "LIVE=%~1"

where dotnet >nul 2>nul || (echo The .NET SDK is missing: install it from https://dotnet.microsoft.com/download & goto failed)
tasklist /fi "imagename eq Hearthstone.exe" | find /i "Hearthstone.exe" >nul && (echo Close Hearthstone first. & goto failed)

if "%LIVE%"=="" for /f "tokens=2,*" %%A in ('reg query "HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Hearthstone" /v InstallLocation 2^>nul ^| find "InstallLocation"') do set "LIVE=%%B"
if "%LIVE%"=="" for /f "tokens=2,*" %%A in ('reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Hearthstone" /v InstallLocation 2^>nul ^| find "InstallLocation"') do set "LIVE=%%B"
if "%LIVE%"=="" set "LIVE=C:\Program Files (x86)\Hearthstone"
for %%I in ("%LIVE%") do set "LIVE=%%~fI"

rem where the game copy is: the folder this repo sits in, the repo folder itself, or a copy next to Server\
set "GAME="
set "INPLACE="
if exist "%PARENT%\Hearthstone.exe" (set "GAME=%PARENT%" & set "INPLACE=1")
if not defined GAME if exist "%ROOT%\Hearthstone.exe" (set "GAME=%ROOT%" & set "INPLACE=1")
if not defined GAME set "GAME=%ROOT%\Hearthstone main game"

if defined INPLACE if /i "%GAME%"=="%LIVE%" (
  echo This OpenHS-Server folder is inside your REAL Hearthstone install ^(%GAME%^).
  echo Setup would add mods to the game you play on Blizzard's servers. Copy the Hearthstone folder somewhere else
  echo ^(e.g. D:\PrivateHS^), move OpenHS-Server into the copy and run setup.bat there.
  choice /m "Continue anyway and turn your real install into the OpenHS game"
  if errorlevel 2 goto failed
)

if defined INPLACE (
  echo [1/4] Using the game folder this server is in: "%GAME%"
) else (
  if not exist "%LIVE%\Hearthstone.exe" (
    echo Hearthstone wasn't found at "%LIVE%".
    echo Either put this OpenHS-Server folder inside a copy of your Hearthstone folder, or run: setup.bat "C:\path\to\Hearthstone"
    goto failed
  )
  echo [1/4] Copying Hearthstone from "%LIVE%" into "%GAME%" ^(the first time takes a while^)...
  rem other mods in your install (BepInEx, loaders) are not copied
  robocopy "%LIVE%" "%GAME%" /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 /XD BepInEx Logs /XF winhttp.dll version.dll doorstop_config.ini HsMod.dll *.log >nul
  if errorlevel 8 (echo Copying failed. & goto failed)
)

echo [2/4] Adding the mod loader and the OpenHS plugins to "%GAME%"...
robocopy "%SERVER%\loader" "%GAME%" /E /NFL /NDL /NJH /NJS /NP >nul
if errorlevel 8 (echo Copying the loader failed. & goto failed)

echo [3/4] Building the server and the client plugin...
dotnet build "%SERVER%\src\HsServer\HsServer.csproj" -c Release -o "%SERVER%\run" -nologo -v q || goto failed
dotnet build "%SERVER%\client\HsLocalServer\HsLocalServer.csproj" -c Release -nologo -v q || goto failed
copy /y "%SERVER%\client\HsLocalServer\bin\Release\net48\HsLocalServer.dll" "%GAME%\BepInEx\plugins\HsLocalServer.dll" >nul || goto failed

echo [4/4] Exporting the card database from the game (it opens once and closes by itself)...
"%SERVER%\run\HsServer.exe" update --yes --no-client-sync || goto failed

echo.
echo Setup done. Start the server and the game with run-local.bat
echo (a fresh account; server commands: ADMIN.md)
pause
exit /b 0

:failed
echo.
echo Setup stopped. Fix the problem above and run setup.bat again (it continues where it can).
pause
exit /b 1
