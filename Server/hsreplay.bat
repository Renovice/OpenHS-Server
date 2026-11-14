@echo off
rem Import HSReplay replays (the XML of games uploaded to hsreplay.net) as recordings our tools can read.
rem   1. Save replay files (.xml / .xml.gz) into RESEARCH\hsreplay-inbox
rem   2. Double-click hsreplay.bat (or: hsreplay.bat path\to\replay.xml)
rem Each game lands in RESEARCH\captures\hsreplay-<name>\ (player names are not copied).
setlocal
set "RUN=%~dp0run"
set "INBOX=%~dp0..\RESEARCH\hsreplay-inbox"
if not exist "%RUN%\HsServer.exe" (
    echo HsServer.exe not built yet: run run-local.bat once first
    pause
    exit /b 1
)
if "%~1"=="" (
    "%RUN%\HsServer.exe" hsreplay "%INBOX%"
) else (
    "%RUN%\HsServer.exe" hsreplay "%~1"
)
pause
