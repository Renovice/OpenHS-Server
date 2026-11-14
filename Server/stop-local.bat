@echo off
rem Stop the server and client that run-local.bat started (nothing else is touched).
setlocal
set "RUN=%~dp0run"
if exist "%RUN%\HsServer.exe" (
    "%RUN%\HsServer.exe" local --stop
) else (
    echo nothing to stop
)
