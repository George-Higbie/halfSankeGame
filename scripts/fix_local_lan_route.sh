#!/usr/bin/env bash
set -euo pipefail

LAN_IFACE="${1:-}"
LAN_IP="${2:-}"

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

route_iface_for_host() {
  local host="$1"
  route -n get "$host" 2>/dev/null | awk '$1 == "interface:" { print $2; exit }'
}

route_gateway_for_host() {
  local host="$1"
  route -n get "$host" 2>/dev/null | awk '$1 == "gateway:" { print $2; exit }'
}

if [[ -z "$LAN_IFACE" ]]; then
  LAN_IFACE="$(pick_lan_iface || true)"
fi

if [[ -z "$LAN_IP" && -n "$LAN_IFACE" ]]; then
  LAN_IP="$(ipconfig getifaddr "$LAN_IFACE" 2>/dev/null || true)"
fi

if [[ -z "$LAN_IFACE" || -z "$LAN_IP" ]]; then
  echo "Unable to determine LAN interface/IP." >&2
  exit 1
fi

BEFORE_IFACE="$(route_iface_for_host "$LAN_IP" || true)"
BEFORE_GATEWAY="$(route_gateway_for_host "$LAN_IP" || true)"

echo "Before: ${LAN_IP} -> interface ${BEFORE_IFACE:-unknown}${BEFORE_GATEWAY:+ (gateway ${BEFORE_GATEWAY})}"

if route -n change -host "$LAN_IP" -ifscope "$LAN_IFACE" -interface "$LAN_IP" >/dev/null 2>&1; then
  :
else
  route -n delete -host "$LAN_IP" >/dev/null 2>&1 || true
  route -n add -host "$LAN_IP" -ifscope "$LAN_IFACE" -interface "$LAN_IP" >/dev/null
fi

AFTER_IFACE="$(route_iface_for_host "$LAN_IP" || true)"
AFTER_GATEWAY="$(route_gateway_for_host "$LAN_IP" || true)"

echo "After:  ${LAN_IP} -> interface ${AFTER_IFACE:-unknown}${AFTER_GATEWAY:+ (gateway ${AFTER_GATEWAY})}"

if [[ "$AFTER_IFACE" != "$LAN_IFACE" ]]; then
  echo "Route repair did not bind ${LAN_IP} to ${LAN_IFACE}." >&2
  exit 1
fi

echo "Local LAN self-route repaired for ${LAN_IP} on ${LAN_IFACE}."
