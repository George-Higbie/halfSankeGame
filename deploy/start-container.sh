#!/usr/bin/env bash
set -euo pipefail

PORT_VALUE="${PORT:-${GUI_BIND_PORT:-10000}}"
export GUI_BIND_PORT="$PORT_VALUE"
export ASPNETCORE_URLS="http://0.0.0.0:${PORT_VALUE}"
export GAME_SERVER_HOST="${GAME_SERVER_HOST:-127.0.0.1}"
export GAME_SERVER_PORT="${GAME_SERVER_PORT:-11000}"

cleanup() {
  if [[ -n "${SERVER_PID:-}" ]]; then
    kill "$SERVER_PID" 2>/dev/null || true
  fi
}

trap cleanup EXIT SIGINT SIGTERM

echo "Starting Snake server on port ${GAME_SERVER_PORT}..."
cd /app/server
./Server &
SERVER_PID=$!

echo "Starting GUI web host on port ${PORT_VALUE}..."
cd /app/gui
./GUI