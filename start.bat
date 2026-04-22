@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT_DIR=%~dp0"
pushd "%ROOT_DIR%" >nul

set "GUI_PORT=5145"
set "RELAY_PORT=%SCORE_RELAY_PORT%"
if not defined RELAY_PORT set "RELAY_PORT=8081"

set "GAME_SERVER_PORT_VALUE=%GAME_SERVER_PORT%"
if not defined GAME_SERVER_PORT_VALUE set "GAME_SERVER_PORT_VALUE=11000"

call :pick_lan_ip

echo Building Server from source...
pushd "%ROOT_DIR%Server" >nul
dotnet build -c Debug -o bin\run
if errorlevel 1 (
    echo Server build failed!
    popd >nul
    popd >nul
    exit /b 1
)
popd >nul

set "RELAY_BASE_URL=%SCORE_RELAY_BASE_URL%"
set "RELAY_PID="
if not defined RELAY_BASE_URL (
    echo Starting local score relay on port %RELAY_PORT%...
    pushd "%ROOT_DIR%WebServer" >nul
    dotnet build -c Debug -o bin\run
    if errorlevel 1 (
        echo WebServer build failed!
        popd >nul
        popd >nul
        exit /b 1
    )
    popd >nul

    call :start_background_process "%ROOT_DIR%WebServer\bin\run" "WebServer.dll %RELAY_PORT%" RELAY_PID
    if defined RELAY_PID echo Score relay PID: !RELAY_PID!
    set "RELAY_BASE_URL=http://127.0.0.1:%RELAY_PORT%"
) else (
    echo Using external score relay: %RELAY_BASE_URL%
)

echo Starting Snake Server in the background...
call :start_background_process "%ROOT_DIR%Server\bin\run" "Server.dll" SERVER_PID
if defined SERVER_PID echo Snake server PID: !SERVER_PID!

set "ADVERTISED_HOST=%GAME_SERVER_ADVERTISED_HOST_OVERRIDE%"
if not defined ADVERTISED_HOST set "ADVERTISED_HOST=127.0.0.1"

call :build_host_list "%ADVERTISED_HOST%" "%LAN_IP%"
set "ADVERTISED_HOST_LIST=%HOST_LIST%"

echo Host machine URL (open this on THIS PC):
echo   http://127.0.0.1:%GUI_PORT%/snake
echo Score relay URL: %RELAY_BASE_URL%
echo Advertised host candidates (stored in DB for others):
echo   %ADVERTISED_HOST_LIST%
echo Advertised join URL format (clients auto-try each host above):
echo   http://^<candidate-host^>:%GUI_PORT%/snake
if defined LAN_IP (
    echo Peer machine URL (open this from OTHER machines on your LAN):
    echo   http://%LAN_IP%:%GUI_PORT%/snake
)

set "ASPNETCORE_ENVIRONMENT=Development"
set "SCORE_RELAY_BASE_URL=%RELAY_BASE_URL%"
set "GAME_SERVER_HOST=127.0.0.1"
set "GAME_SERVER_ADVERTISED_HOST=%ADVERTISED_HOST_LIST%"
set "GAME_SERVER_BLOCKED_HOSTS=%GAME_SERVER_BLOCKED_HOSTS%"
set "GAME_SERVER_PORT=%GAME_SERVER_PORT_VALUE%"
set "GUI_BIND_PORT=%GUI_PORT%"

echo Starting GUI Client in the foreground...
pushd "%ROOT_DIR%GUI" >nul
dotnet run --no-launch-profile
set "GUI_EXIT_CODE=%ERRORLEVEL%"
popd >nul

if defined SERVER_PID taskkill /PID !SERVER_PID! /T /F >nul 2>&1
if defined RELAY_PID taskkill /PID !RELAY_PID! /T /F >nul 2>&1

popd >nul
exit /b %GUI_EXIT_CODE%

:pick_lan_ip
set "LAN_IP="
for /f %%i in ('powershell -NoProfile -Command "$ip = Get-NetIPAddress -AddressFamily IPv4 ^| Where-Object { $_.IPAddress -match '^(10\.|192\.168\.|172\.(1[6-9]^|2[0-9]^|3[01])\.)' -and $_.IPAddress -ne '127.0.0.1' -and $_.IPAddress -notlike '169.254.*' -and $_.InterfaceAlias -notmatch 'Loopback^|Teredo^|vEthernet^|Hyper-V^|WSL^|VPN' } ^| Sort-Object InterfaceMetric ^| Select-Object -First 1 -ExpandProperty IPAddress; if (-not $ip) { $ip = Get-NetIPAddress -AddressFamily IPv4 ^| Where-Object { $_.IPAddress -ne '127.0.0.1' -and $_.IPAddress -notlike '169.254.*' -and $_.InterfaceAlias -notmatch 'Loopback^|Teredo' } ^| Sort-Object InterfaceMetric ^| Select-Object -First 1 -ExpandProperty IPAddress }; if ($ip) { $ip }"') do set "LAN_IP=%%i"
exit /b 0

:build_host_list
set "HOST_LIST=%~1"
if defined HOST_LIST (
    if not "%~2"=="" if /I not "%~1"=="%~2" set "HOST_LIST=%HOST_LIST%,%~2"
) else (
    set "HOST_LIST=%~2"
)
exit /b 0

:start_background_process
set "%~3="
for /f %%i in ('powershell -NoProfile -Command "$p = Start-Process dotnet -ArgumentList \"%~2\" -WorkingDirectory \"%~1\" -WindowStyle Hidden -PassThru; $p.Id"') do set "%~3=%%i"
exit /b 0
