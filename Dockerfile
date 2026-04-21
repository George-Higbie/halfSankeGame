FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

COPY Snake.sln ./
COPY global.json ./
COPY GUI/ GUI/
COPY GUI.Tests/ GUI.Tests/
COPY Server/ Server/
COPY WebServer/ WebServer/

RUN dotnet restore Snake.sln
RUN dotnet publish Server/Server.csproj -c Release -o /out/server
RUN dotnet publish GUI/GUI.csproj -c Release -o /out/gui

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

COPY --from=build /out/server ./server
COPY --from=build /out/gui ./gui
COPY deploy/start-container.sh ./deploy/start-container.sh

ENV ASPNETCORE_ENVIRONMENT=Production
ENV GAME_SERVER_HOST=127.0.0.1
ENV GAME_SERVER_PORT=11000
ENV GUI_BIND_PORT=10000
ENV ASPNETCORE_URLS=http://0.0.0.0:10000

EXPOSE 10000

ENTRYPOINT ["/bin/bash", "/app/deploy/start-container.sh"]