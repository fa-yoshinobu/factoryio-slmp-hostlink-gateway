@echo off
setlocal

set "ROOT=%~dp0"
set "EXE=%ROOT%publish\win-x64-single\GatewayApp.exe"

if not exist "%EXE%" (
  echo [error] Missing exe: "%EXE%"
  echo [hint] Run compile.bat first.
  exit /b 1
)

echo [start] "%EXE%"
start "" "%EXE%"
exit /b 0

