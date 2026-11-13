@echo off
setlocal
cd /d "%~dp0"
dotnet publish "GatewayApp/GatewayApp.csproj" -c Release -r win-x64 --self-contained true -o artifacts\release -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if errorlevel 1 exit /b 1
powershell -NoProfile -File "%~dp0.github\scripts\package_release.ps1"
exit /b %errorlevel%
