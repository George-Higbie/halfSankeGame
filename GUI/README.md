# Snake GUI (PS9)

Blazor Server client for the Snake game assignment (PS9). Connects to the server using a line-delimited JSON protocol over TCP.

**Authors:** Alex Waldmann, George Higbie  
**Date:** 2026-04-12

## Quick Start (preferred)

From the repo root, use the provided start scripts — they build the server and launch everything:

```bash
# macOS / Linux
chmod +x start.sh
./start.sh

# Windows
start.bat
```

Then open your browser to `http://localhost:5000/snake`.

## Manual Start

```bash
# Terminal 1 — Server
cd Server && dotnet build -c Debug -o bin/run && cd bin/run && dotnet Server.dll

# Terminal 2 — GUI
cd GUI && dotnet run
open http://localhost:5000/snake
```

## Controls

- **WASD** or **Arrow keys** to move.
- Use the sidebar connection box to enter host, port, and player name.
- Supports 1P and 2P split-screen modes.

## Partners

- P1: Alex Waldmann
- P2: George Higbie

