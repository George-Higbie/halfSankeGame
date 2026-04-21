# Project Context

- Solution: Snake.sln
- SDK: .NET 10 (global.json)
- Projects:
  - GUI: Blazor Server client for Snake gameplay
  - Server: TCP Snake game server
  - GUI.Tests: MSTest suite for GUI models/controllers
  - WebServer: PS10 score HTTP server (raw TCP/HTTP)

## Build and Test

- Build all: dotnet build Snake.sln
- Test GUI: dotnet test GUI.Tests/GUI.Tests.csproj
- Run GUI: dotnet run --project GUI/GUI.csproj
- Run game server: dotnet run --project Server/Server.csproj
- Run score web server: dotnet run --project WebServer/WebServer.csproj

## PS10 Database Notes

- Client writes live game and player stats directly to MySQL using MySql.Data.
- Connection string is intentionally embedded in source per assignment policy.
- Tables used:
  - Games(GameId, StartTime, EndTime)
  - Players(EntryId, GameId, PlayerId, PlayerName, MaxScore, EnterTime, LeaveTime)

## Conventions

- Warnings are treated as errors in GUI.
- Public APIs are XML-documented.
- GUI game state is event-driven through NetworkController and GameController.
