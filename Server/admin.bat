@echo off
rem Admin console: give cards, set dust, switch events on/off, reset a player.
rem   admin.bat                                 (lists the commands; full guide: ADMIN.md)
rem   admin.bat players
rem   admin.bat give default EX1_116 2 golden
rem   admin.bat dust default +1000
rem   admin.bat event on dust_refund_36_4_2 14
rem Talks to the running server (a logged-in client sees card and dust changes at once);
rem with the server stopped it changes the saved files directly.
setlocal
set "RUN=%~dp0run"
if not exist "%RUN%\HsServer.exe" (
    echo HsServer.exe not built yet: run run-local.bat once first
    exit /b 1
)
"%RUN%\HsServer.exe" admin %*
