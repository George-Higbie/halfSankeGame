@echo off

echo Building Server from source...
cd Server
dotnet build -c Debug -o bin\run
if %ERRORLEVEL% neq 0 (
    echo Server build failed!
    exit /b 1
)
cd ..

echo Starting GUI Client in the background...
cd GUI
start /B dotnet run

echo Starting Snake Server in the foreground...
cd ..\Server\bin\run
dotnet Server.dll
