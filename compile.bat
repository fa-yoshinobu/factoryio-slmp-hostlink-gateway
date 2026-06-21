@echo off
setlocal

set "ROOT=%~dp0"
set "PROJECT=%ROOT%GatewayApp\GatewayApp.csproj"
set "PUBLISH_DIR=%ROOT%publish\win-x64-single"
set "EXE=%PUBLISH_DIR%\FactoryIOGateway.exe"

echo [compile] Factory I/O Gateway single-file publish
echo [compile] Project: "%PROJECT%"
echo [compile] Output : "%EXE%"

if exist "%PUBLISH_DIR%" (
  tasklist /FI "IMAGENAME eq FactoryIOGateway.exe" 2>nul | find /I "FactoryIOGateway.exe" >nul
  if not errorlevel 1 (
    echo [error] FactoryIOGateway.exe is running. Close the app before publishing.
    exit /b 1
  )
  rmdir /s /q "%PUBLISH_DIR%"
  if exist "%PUBLISH_DIR%" (
    echo [error] Failed to remove publish directory. Close any app or Explorer window using:
    echo [error] "%PUBLISH_DIR%"
    exit /b 1
  )
)

dotnet publish "%PROJECT%" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -o "%PUBLISH_DIR%" ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=None ^
  -p:DebugSymbols=false

if errorlevel 1 (
  echo [error] publish failed.
  exit /b 1
)

if not exist "%EXE%" (
  echo [error] single-file exe was not created.
  exit /b 1
)

for /f "delims=" %%F in ('dir /b /a-d "%PUBLISH_DIR%"') do (
  if /i not "%%F"=="FactoryIOGateway.exe" (
    echo [compile] remove extra publish file: %%F
    del /f /q "%PUBLISH_DIR%\%%F"
  )
)

for /d %%D in ("%PUBLISH_DIR%\*") do (
  echo [compile] remove extra publish directory: %%~nxD
  rmdir /s /q "%%D"
)

for /f "delims=" %%F in ('dir /b "%PUBLISH_DIR%"') do (
  if /i not "%%F"=="FactoryIOGateway.exe" (
    echo [error] output must be one exe file only, but found: %%F
    exit /b 1
  )
)

echo [compile] done: "%EXE%"
exit /b 0
