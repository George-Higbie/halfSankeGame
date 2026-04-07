# Snake GUI (PS9)

This is the Blazor GUI client for the Snake assignment (PS9). It connects to the provided server which speaks a line-delimited JSON protocol.

Quick run (macOS):

```bash
# make server and AI clients executable (one-time)
chmod +x /path/to/SnakeApps-osx-arm64/Server-osx-arm64/Server
chmod +x /path/to/SnakeApps-osx-arm64/AIClient-osx-arm64/AIClient

# start the server
cd /path/to/SnakeApps-osx-arm64/Server-osx-arm64
./Server

# start the GUI
cd /path/to/Snake_Handout/GUI
dotnet run

# open browser at the URL printed by dotnet (e.g. http://localhost:5000)
open http://localhost:5000/snake
```

Controls: WASD or arrow keys. Use the connection box (top-left) to enter host, port, and player name.

Notes: This project was adapted to build with .NET 8.0 on the local machine. The original handout targeted .NET 10.0.

