@echo off
rem Build and run all server tests.
dotnet test "%~dp0tests\HsServer.Tests" -c Release -nologo
if errorlevel 1 pause
