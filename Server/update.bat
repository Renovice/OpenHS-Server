@echo off
rem After a Hearthstone patch: bring the private client (Hearthstone main game) and the server's card database up to date.
rem   1. copies the real install's new / changed game files into the private client (asks first; our plugins, the
rem      doorstop loader and Logs are never touched, nothing is deleted)
rem   2. starts the private client once so its recorder exports the card data, then closes it
rem   3. saves that as a new Server\data\dbf-^<version^>-^<date^> (used from the server's next start) and lists what changed
rem Close Hearthstone and the local server first. Launching the server never updates anything by itself.
setlocal
set "RUN=%~dp0run"
if not exist "%RUN%\HsServer.exe" (
    echo HsServer.exe not built yet: run run-local.bat once first
    pause
    exit /b 1
)
"%RUN%\HsServer.exe" update %*
pause
