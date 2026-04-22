#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Kill all child processes upon exit
trap 'kill $(jobs -p) 2>/dev/null' EXIT SIGINT SIGTERM

GUI_PORT=5145
RELAY_PORT="${SCORE_RELAY_PORT:-8081}"
INCLUDE_VPN_GAME_HOST="${INCLUDE_VPN_GAME_HOST:-0}"
GAME_SERVER_PORT="${GAME_SERVER_PORT:-11000}"

pick_gui_port() {
  # Keep grading/runtime deterministic: always use 5145.
  # If a stale process is holding the port, terminate it.
  lsof -ti "tcp:${GUI_PORT}" -sTCP:LISTEN | xargs kill 2>/dev/null || true
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

pick_lan_iface() {
  if ipconfig getifaddr en0 >/dev/null 2>&1; then
    printf '%s' "en0"
    return 0
  fi

  if ipconfig getifaddr en1 >/dev/null 2>&1; then
    printf '%s' "en1"
    return 0
  fi

  return 1
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

route_iface_for_host() {
  local host="$1"
  route -n get "$host" 2>/dev/null | awk '$1 == "interface:" { print $2; exit }'
}

route_gateway_for_host() {
  local host="$1"
  route -n get "$host" 2>/dev/null | awk '$1 == "gateway:" { print $2; exit }'
}

maybe_repair_local_lan_route() {
  local lan_ip="$1"
  local lan_iface="$2"
  local routed_iface
  local routed_gateway

  if [[ -z "$lan_ip" || -z "$lan_iface" ]]; then
    return 0
  fi

  routed_iface="$(route_iface_for_host "$lan_ip" || true)"
  routed_gateway="$(route_gateway_for_host "$lan_ip" || true)"

  if [[ -z "$routed_iface" || "$routed_iface" == "$lan_iface" ]]; then
    return 0
  fi

  if [[ "$routed_iface" != utun* ]]; then
    return 0
  fi

  echo ""
  echo "Detected VPN route hijack for this host's LAN IP:"
  echo "  ${lan_ip} is currently routed via ${routed_iface}${routed_gateway:+ (gateway ${routed_gateway})}"
  echo "No sudo fix is attempted during startup. Local self-joins use 127.0.0.1 first instead."
}

echo "Building Server from source..."
cd "$ROOT_DIR/Server"
dotnet build -c Debug -o bin/run 2>&1
if [ $? -ne 0 ]; then
  echo "Server build failed!"
  exit 1
fi

cd "$ROOT_DIR"

# Resolve network identity before launching any service so all processes
# receive consistent bind/advertise inputs.
LAN_IFACE="$(pick_lan_iface || true)"
LAN_IP="$(pick_lan_ip)"
VPN_IP="$(pick_primary_vpn_ip || true)"
maybe_repair_local_lan_route "$LAN_IP" "$LAN_IFACE"

RELAY_BASE_URL="${SCORE_RELAY_BASE_URL:-}"
if [[ -z "$RELAY_BASE_URL" ]]; then
  echo "Starting local score relay on port ${RELAY_PORT}..."
  # Kill any stale relay process holding the port before starting a new one.
  lsof -ti "tcp:${RELAY_PORT}" -sTCP:LISTEN | xargs kill 2>/dev/null || true
  cd "$ROOT_DIR/WebServer"
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
  cd "$ROOT_DIR"
else
  echo "Using external score relay: ${RELAY_BASE_URL}"
fi

echo "Starting Snake Server in the background..."
cd "$ROOT_DIR/Server/bin/run"
dotnet Server.dll &
SERVER_PID=$!
echo "Snake server PID: ${SERVER_PID}"

# Wait briefly for the game server socket to bind so the GUI doesn't race it.
for _ in {1..30}; do
  if lsof -nP -iTCP:"${GAME_SERVER_PORT}" -sTCP:LISTEN | grep -q .; then
    break
  fi
  sleep 0.1
done

echo "Starting GUI Client in the foreground..."
cd "$ROOT_DIR/GUI"
SELECTED_GUI_PORT="$(pick_gui_port)"

ADVERTISED_HOST="${GAME_SERVER_ADVERTISED_HOST_OVERRIDE:-}"
if [[ -z "$ADVERTISED_HOST" ]]; then
  # Default to loopback first so the host machine can always join its own
  # advertised session without depending on OS LAN self-routing behavior.
  # Remote peers will auto-fall through to LAN/VPN candidates below.
  ADVERTISED_HOST="127.0.0.1"
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
echo "Same-host self-join behavior:"
echo "  This Mac will try 127.0.0.1 first, then LAN/VPN candidates if needed."
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
  echo "Note: on this host machine, the LAN URL itself may be routed into VPN and fail."
  echo "Self-joins still work because advertised candidates try 127.0.0.1 first."
  echo "Use the LAN URL only from other machines; use 127.0.0.1 locally."
  echo "Optional advanced repair helper: scripts/fix_local_lan_route.sh"
  echo "Set GAME_SERVER_ADVERTISED_HOST_OVERRIDE=<ip> to force a specific advertised host."
  echo "Set INCLUDE_VPN_GAME_HOST=1 only if you explicitly want to advertise VPN join URLs."
  echo "Set GAME_SERVER_BLOCKED_HOSTS=<csv> only if you want to block specific hosts."
fi

echo "Press Ctrl+C to stop both GUI and server."

ASPNETCORE_ENVIRONMENT="Development" SCORE_RELAY_BASE_URL="${RELAY_BASE_URL}" GAME_SERVER_HOST="127.0.0.1" GAME_SERVER_ADVERTISED_HOST="${ADVERTISED_HOST_LIST}" GAME_SERVER_BLOCKED_HOSTS="${BLOCKED_GAME_HOSTS}" GAME_SERVER_PORT="${GAME_SERVER_PORT}" GUI_BIND_PORT="${SELECTED_GUI_PORT}" dotnet run --no-launch-profile
