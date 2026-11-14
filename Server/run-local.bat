@echo off
rem Start the C# private server and the RE-project Hearthstone client connected to it (127.0.0.1, no Battle.net).
rem Double-click, or from a console:
rem   run-local.bat             engine games on the rules engine (default)
rem   run-local.bat replay      replay recorded live matches instead
rem   run-local.bat noclient    server only
rem   run-local.bat norecording start without any recorded live login (the public server's start-up; own accounts in
rem                             Server\stateccounts-norecording); the log lists every request it can't answer yet
rem   run-local.bat basic       engine games on the old basic engine (legacy)
rem   run-local.bat sabber      same as the default (kept so older notes still work)
rem   run-local.bat player Bob  log in as player "Bob" (own decks and save data in Server\state\accounts\bob); default = "default"
rem The server runs in this window (log also in Server\logs\server-*.log). Close the window or press Ctrl+C to stop it.
setlocal
set "SERVER=%~dp0"
set "SERVER=%SERVER:~0,-1%"
set "RUN=%SERVER%\run"
set "PLUGINS=%SERVER%\..\Hearthstone main game\BepInEx\plugins"

set "FLAGS="
set "RULES=sabber"
:args
if "%~1"=="" goto args_done
if /i "%~1"=="replay" set "FLAGS=%FLAGS% --replay"
if /i "%~1"=="noclient" set "FLAGS=%FLAGS% --no-client"
if /i "%~1"=="norecording" set "FLAGS=%FLAGS% --no-recording"
if /i "%~1"=="sabber" set "RULES=sabber"
if /i "%~1"=="basic" set "RULES=basic"
if /i "%~1"=="player" (set "FLAGS=%FLAGS% --player %~2" & shift)
shift
goto args
:args_done
set "FLAGS=%FLAGS% --rules %RULES%"

rem Stop what a previous run started (only its own server/client), so the exe and plugin can be replaced.
if exist "%RUN%\HsServer.exe" "%RUN%\HsServer.exe" local --stop

echo building server...
dotnet build "%SERVER%\src\HsServer\HsServer.csproj" -c Release -o "%RUN%" -nologo -v q
if errorlevel 1 goto failed

echo building client plugin...
dotnet build "%SERVER%\client\HsLocalServer\HsLocalServer.csproj" -c Release -nologo -v q
if errorlevel 1 goto failed
copy /y "%SERVER%\client\HsLocalServer\bin\Release\net48\HsLocalServer.dll" "%PLUGINS%\HsLocalServer.dll" >nul
if errorlevel 1 goto failed

"%RUN%\HsServer.exe" local%FLAGS%
if errorlevel 1 goto failed
exit /b 0

:failed
echo.
echo run-local failed - see the messages above.
pause
exit /b 1
