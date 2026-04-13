# Snake Game — PS9

Multiplayer Snake client and server for CS 3500 PS9.

**Authors:** Alex Waldmann, George Higbie  
**Date:** 2026-04-12

## Quick Start

### macOS / Linux

```bash
chmod +x start.sh
./start.sh
```

### Windows

```cmd
start.bat
```

Both scripts build the server from source, launch the GUI client in the background, then start the server in the foreground. Once running, open your browser to:

```
http://localhost:5000/snake
```

### Manual Start (alternative)

```bash
cd Server && dotnet build -c Debug -o bin/run && cd ../Server/bin/run && dotnet Server.dll &
cd GUI && dotnet run
```

## Controls

- **WASD** or **Arrow keys** to move
- Connection box (top-left sidebar) to enter host, port, and player name
- Supports 1-player and 2-player split-screen modes

## Project Structure

| Directory      | Purpose                                         |
| -------------- | ----------------------------------------------- |
| `GUI/`         | Blazor Server (.NET 10) game client             |
| `GUI.Tests/`   | MSTest unit + integration tests (106 tests)     |
| `Server/`      | Snake game server (builds from source)          |
| `AIClient-osx-arm64/` | Pre-built AI client for macOS ARM        |

## Build & Test

```bash
dotnet build Snake.sln
dotnet test GUI.Tests/GUI.Tests.csproj
```

## Notes

- The GUI uses `InteractiveServer` render mode (SignalR-based Blazor).
- Move commands are sent as JSON lines: `{"moving":"left"}`.
- 15 snake skins with stripe, checker, diamond, and wave patterns.
