#!/bin/bash

# Kill all child processes upon exit
trap 'kill $(jobs -p) 2>/dev/null' EXIT SIGINT SIGTERM

echo "Starting GUI Client in the background..."
cd GUI
# Run dotnet in the background
dotnet run &

# Go back to the root, then into the server directory
cd ../Server-osx-arm64
chmod +x Server

echo "Starting Snake Server in the foreground..."
# Run the server in the foreground so its loop runs correctly and we see the FPS logs
./Server
