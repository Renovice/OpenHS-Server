@echo off
rem OpenHS accounts: pick the account to play, or make / rename / delete / restore one.
rem Double-click it. The same as the "account" commands (admin.bat account ..., or in the server window; ADMIN.md).
rem   P  play: makes the account active and starts the server and the game (run-local.bat player <name>)
rem   N  new account (empty, or a copy of another)   R  rename   D  delete (restorable for 24 hours)   U  restore a deleted one
setlocal EnableExtensions
set "SERVER=%~dp0"
set "SERVER=%SERVER:~0,-1%"
if not exist "%SERVER%\run\HsServer.exe" (
    echo HsServer.exe not built yet: run setup.bat ^(or run-local.bat^) once first.
    pause
    exit /b 1
)

:menu
cls
echo OpenHS accounts
echo.
call "%SERVER%\admin.bat" account list
echo.
echo   P = play   N = new   R = rename   D = delete   U = undo a delete   Q = quit
choice /c PNRDUQ /n /m "Choose: "
if errorlevel 6 exit /b 0
if errorlevel 5 goto restore
if errorlevel 4 goto delete
if errorlevel 3 goto rename
if errorlevel 2 goto new
goto play

:play
set "NAME="
set /p "NAME=Account to play (Enter = the active one, marked *): "
if "%NAME%"=="" (
    call "%SERVER%\run-local.bat"
) else (
    call "%SERVER%\run-local.bat" player %NAME%
)
exit /b %errorlevel%

:new
set "NAME="
set "FROM="
set /p "NAME=New account name (letters, digits, - and _): "
if "%NAME%"=="" goto menu
set /p "FROM=Copy another account's cards and progress? Its name, or Enter for an empty one: "
if "%FROM%"=="" (
    call "%SERVER%\admin.bat" account new %NAME%
) else (
    call "%SERVER%\admin.bat" account new %NAME% from %FROM%
)
pause
goto menu

:rename
set "NAME="
set "TO="
set /p "NAME=Account to rename: "
if "%NAME%"=="" goto menu
set /p "TO=New name: "
if "%TO%"=="" goto menu
call "%SERVER%\admin.bat" account rename %NAME% %TO%
pause
goto menu

:delete
set "NAME="
set /p "NAME=Account to delete: "
if "%NAME%"=="" goto menu
choice /c YN /m "Delete %NAME%? It can be restored for 24 hours, then it is gone for good"
if errorlevel 2 goto menu
call "%SERVER%\admin.bat" account delete %NAME%
pause
goto menu

:restore
set "NAME="
set /p "NAME=Deleted account to bring back: "
if "%NAME%"=="" goto menu
call "%SERVER%\admin.bat" account restore %NAME%
pause
goto menu
