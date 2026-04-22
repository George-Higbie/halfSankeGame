#!/usr/bin/env bash
set -euo pipefail

# Kill all child processes upon exit
trap 'kill $(jobs -p) 2>/dev/null' EXIT SIGINT SIGTERM

GUI_PORT=5145
FALLBACK_GUI_PORT=5155
RELAY_PORT="${SCORE_RELAY_PORT:-8081}"
INCLUDE_VPN_GAME_HOST="${INCLUDE_VPN_GAME_HOST:-0}"

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

pick_primary_vpn_ip() {
  ifconfig | awk '
    /^[a-z0-9]+:/ {
      iface=$1
      sub(":", "", iface)
      next
    }
    iface ~ /^utun/ && /inet / {
      print $2
      exit
    }
  '
}

build_host_list() {
  local primary="$1"
  local lan="$2"
  local vpn="$3"
  local joined=""
  local host

  for host in "$primary" "$lan" "$vpn"; do
    if [[ -z "$host" ]]; then
      continue
    fi

    if [[ -z "$joined" ]]; then
      joined="$host"
      continue
    fi

    if [[ ",${joined}," == *",${host},"* ]]; then
      continue
    fi

    joined+="${joined:+,}${host}"
  done

  printf '%s' "$joined"
}

list_local_ipv4s() {
  ifconfig | awk '/inet / {print $2}' | grep -Ev '^127\.' | sort -u
}

list_vpn_ipv4s() {
  ifconfig | awk '
    /^[a-z0-9]+:/ {
      iface=$1
      sub(":", "", iface)
      next
    }
    iface ~ /^utun/ && /inet / {
      print $2
    }
  ' | sort -u
}

echo "Building Server from source..."
cd Server
dotnet build -c Debug -o bin/run 2>&1
if [ $? -ne 0 ]; then
  echo "Server build failed!"
  exit 1
fi

cd ..

RELAY_BASE_URL="${SCORE_RELAY_BASE_URL:-}"
if [[ -z "$RELAY_BASE_URL" ]]; then
  echo "Starting local score relay on port ${RELAY_PORT}..."
  cd WebServer
  dotnet build -c Debug -o bin/run 2>&1
  if [ $? -ne 0 ]; then
    echo "WebServer build failed!"
    exit 1
  fi

  cd bin/run
  dotnet WebServer.dll "${RELAY_PORT}" &
  RELAY_PID=$!
  echo "Score relay PID: ${RELAY_PID}"

  RELAY_BASE_URL="http://127.0.0.1:${RELAY_PORT}"
  cd ../../..
else
  echo "Using external score relay: ${RELAY_BASE_URL}"
fi

echo "Starting Snake Server in the background..."
cd Server/bin/run
dotnet Server.dll &
SERVER_PID=$!
echo "Snake server PID: ${SERVER_PID}"

echo "Starting GUI Client in the foreground..."
cd ../../../GUI
SELECTED_GUI_PORT="$(pick_gui_port)"
LAN_IP="$(pick_lan_ip)"
VPN_IP="$(pick_primary_vpn_ip || true)"

ADVERTISED_HOST="${GAME_SERVER_ADVERTISED_HOST_OVERRIDE:-}"
if [[ -z "$ADVERTISED_HOST" ]]; then
  if [[ -n "$LAN_IP" ]]; then
    ADVERTISED_HOST="$LAN_IP"
  else
    ADVERTISED_HOST="127.0.0.1"
  fi
fi

if [[ "$INCLUDE_VPN_GAME_HOST" == "1" ]]; then
  ADVERTISED_HOST_LIST="$(build_host_list "$ADVERTISED_HOST" "$LAN_IP" "$VPN_IP")"
else
  ADVERTISED_HOST_LIST="$(build_host_list "$ADVERTISED_HOST" "$LAN_IP" "")"
fi

echo "Host machine URL (open this on THIS Mac):"
echo "  http://127.0.0.1:${SELECTED_GUI_PORT}/snake"
echo "Score relay URL: ${RELAY_BASE_URL}"
echo "Advertised host candidates (stored in DB for others):"
echo "  ${ADVERTISED_HOST_LIST}"
echo "Advertised join URL format (clients auto-try each host above):"
echo "  http://<candidate-host>:${SELECTED_GUI_PORT}/snake"
if [[ -n "$LAN_IP" ]]; then
  echo "Peer machine URL (open this from OTHER machines on your Wi-Fi/LAN):"
  echo "  http://${LAN_IP}:${SELECTED_GUI_PORT}/snake"
fi
if [[ -n "$VPN_IP" && "$INCLUDE_VPN_GAME_HOST" == "1" ]]; then
  echo "Peer machine VPN URL (open this from OTHER machines on same VPN):"
  echo "  http://${VPN_IP}:${SELECTED_GUI_PORT}/snake"
fi
echo "Detected local IPv4 addresses:"
list_local_ipv4s | sed 's/^/  - /'

VPN_IPS="$(list_vpn_ipv4s || true)"
BLOCKED_GAME_HOSTS="${GAME_SERVER_BLOCKED_HOSTS:-}"
if [[ -n "$VPN_IPS" ]]; then
  echo ""
  echo "VPN interfaces detected (utun):"
  echo "$VPN_IPS" | sed 's/^/  - /'
  echo "Game hosting remains LAN/local by default. VPN is used only for outbound DB access."
  echo "Note: on this host machine, the LAN URL may be routed into VPN and fail."
  echo "Use 127.0.0.1 locally; use the LAN URL only from other machines."
  echo "Set GAME_SERVER_ADVERTISED_HOST_OVERRIDE=<ip> to force a specific advertised host."
  echo "Set INCLUDE_VPN_GAME_HOST=1 only if you explicitly want to advertise VPN join URLs."
  echo "Set GAME_SERVER_BLOCKED_HOSTS=<csv> only if you want to block specific hosts."
fi

echo "Press Ctrl+C to stop both GUI and server."

ASPNETCORE_ENVIRONMENT="Development" SCORE_RELAY_BASE_URL="${RELAY_BASE_URL}" GAME_SERVER_HOST="127.0.0.1" GAME_SERVER_ADVERTISED_HOST="${ADVERTISED_HOST_LIST}" GAME_SERVER_BLOCKED_HOSTS="${BLOCKED_GAME_HOSTS}" GAME_SERVER_PORT="11000" GUI_BIND_PORT="${SELECTED_GUI_PORT}" ASPNETCORE_URLS="http://0.0.0.0:${SELECTED_GUI_PORT}" dotnet run
