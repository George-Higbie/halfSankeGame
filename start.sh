#!/bin/bash

# Kill all child processes upon exit
trap 'kill $(jobs -p) 2>/dev/null' EXIT SIGINT SIGTERM

echo "Building Server from source..."
cd Server
dotnet build -c Debug -r osx-arm64 --no-self-contained -o bin/run 2>&1
if [ $? -ne 0 ]; then
  echo "Server build failed!"
  exit 1
fi

cd ..

echo "Starting GUI Client in the background..."
cd GUI
dotnet run &

echo "Starting Snake Server in the foreground..."
cd ../Server/bin/run
./Server
