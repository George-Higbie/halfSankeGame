#!/usr/bin/env bash
set -euo pipefail

# Kill all child processes upon exit
trap 'kill $(jobs -p) 2>/dev/null' EXIT SIGINT SIGTERM

GUI_PORT=5145
FALLBACK_GUI_PORT=5155

pick_gui_port() {
  if lsof -ti "tcp:${GUI_PORT}" -sTCP:LISTEN >/dev/null 2>&1; then
    echo "Port ${GUI_PORT} is already in use; using ${FALLBACK_GUI_PORT} for GUI." >&2
    printf '%s' "${FALLBACK_GUI_PORT}"
    return
  fi

  printf '%s' "${GUI_PORT}"
}

echo "Building Server from source..."
cd Server
dotnet build -c Debug -o bin/run 2>&1
if [ $? -ne 0 ]; then
  echo "Server build failed!"
  exit 1
fi

cd ..

echo "Starting GUI Client in the background..."
cd GUI
SELECTED_GUI_PORT="$(pick_gui_port)"
echo "GUI URL: http://0.0.0.0:${SELECTED_GUI_PORT}"
ASPNETCORE_ENVIRONMENT="Development" ASPNETCORE_URLS="http://0.0.0.0:${SELECTED_GUI_PORT}" dotnet run &

echo "Starting Snake Server in the foreground..."
cd ../Server/bin/run
dotnet Server.dll
