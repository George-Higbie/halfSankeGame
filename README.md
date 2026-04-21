# Snake Game — PS9

Multiplayer Snake client and server for CS 3500 PS9.

Authors: Alex Waldmann, George Higbie  
Date: 2026-04-12

## Local Run

### macOS / Linux

```bash
chmod +x start.sh
./start.sh
```

### Windows

```cmd
start.bat
```

Then open:

```text
http://localhost:5145/snake
```

The launcher also prints detected local IPs and a reachable local URL hint.

## If Partner Access Fails on LAN

This is usually a network routing issue between machines (VPN/interface mismatch or AP isolation), not a C# runtime issue.

Use hosted deployment instead of peer-to-peer LAN hosting.

## Deployment (Render, Docker)

This repo includes:

- Dockerfile
- .dockerignore
- render.yaml
- deploy/start-container.sh

### Deploy Steps

1. Push repo to GitHub.
2. Create a Render account and connect the repo.
3. Create a new Blueprint deployment (Render will read render.yaml).
4. Wait for build/deploy to complete.
5. Open:

```text
https://<your-render-service>.onrender.com/snake
```

## Build & Test

```bash
dotnet build Snake.sln
dotnet test GUI.Tests/GUI.Tests.csproj
```

## Architecture Notes

- GUI is Blazor Server (InteractiveServer) and runs as a server-side web app.
- Snake server runs on TCP 11000.
- Score/game discovery uses SQL and open game sessions are loaded from DB.
