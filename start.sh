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

pick_lan_ip() {
  local ip
  ip="$(ipconfig getifaddr en0 2>/dev/null || true)"
  if [[ -z "$ip" ]]; then
    ip="$(ipconfig getifaddr en1 2>/dev/null || true)"
  fi
  printf '%s' "$ip"
}

list_local_ipv4s() {
  ifconfig | awk '/inet / {print $2}' | grep -Ev '^127\.' | sort -u
}

echo "Building Server from source..."
cd Server
dotnet build -c Debug -o bin/run 2>&1
if [ $? -ne 0 ]; then
  echo "Server build failed!"
  exit 1
fi

cd ..

echo "Starting Snake Server in the background..."
cd Server/bin/run
dotnet Server.dll &
SERVER_PID=$!
echo "Snake server PID: ${SERVER_PID}"

echo "Starting GUI Client in the foreground..."
cd ../../../GUI
SELECTED_GUI_PORT="$(pick_gui_port)"
LAN_IP="$(pick_lan_ip)"
echo "GUI URL: http://0.0.0.0:${SELECTED_GUI_PORT}"
if [[ -n "$LAN_IP" ]]; then
  echo "LAN URL: http://${LAN_IP}:${SELECTED_GUI_PORT}/snake"
fi
echo "Detected local IPv4 addresses:"
list_local_ipv4s | sed 's/^/  - /'
echo "Press Ctrl+C to stop both GUI and server."

# Run a short self-probe after startup to show which local IPs are reachable.
(
  sleep 4
  local_ips="$(list_local_ipv4s)"
  while IFS= read -r ip; do
    [[ -z "$ip" ]] && continue
    if curl -sS --max-time 2 "http://${ip}:${SELECTED_GUI_PORT}/health" >/dev/null 2>&1; then
      echo "Reachable local URL: http://${ip}:${SELECTED_GUI_PORT}/snake"
    fi
  done <<< "$local_ips"
) &

ASPNETCORE_ENVIRONMENT="Development" GAME_SERVER_HOST="${LAN_IP:-localhost}" GAME_SERVER_PORT="11000" GUI_BIND_PORT="${SELECTED_GUI_PORT}" ASPNETCORE_URLS="http://0.0.0.0:${SELECTED_GUI_PORT}" dotnet run
