#!/usr/bin/env bash
#
# Continuous dev traffic generator for the MyApp stack.
#
# Usage:
#   ./backend/scripts/generate-traffic.sh start    # background loop
#   ./backend/scripts/generate-traffic.sh status
#   ./backend/scripts/generate-traffic.sh stop
#   ./backend/scripts/generate-traffic.sh run      # foreground (Ctrl+C)
#
# Environment:
#   INTERVAL=3             seconds between cycles
#   API_HOST=http://localhost:5101
#   ORDERS_HOST=http://localhost:5103
#   EMAIL=admin@example.com
#   PASSWORD=ChangeMe-12345

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
API_HOST="${API_HOST:-http://localhost:5101}"
ORDERS_HOST="${ORDERS_HOST:-http://localhost:5103}"
INTERVAL="${INTERVAL:-3}"
EMAIL="${EMAIL:-admin@example.com}"
PASSWORD="${PASSWORD:-ChangeMe-12345}"
PID_FILE="${PID_FILE:-$SCRIPT_DIR/.generate-traffic.pid}"
LOG_FILE="${LOG_FILE:-$SCRIPT_DIR/.generate-traffic.log}"

TOKEN=""
TOKEN_TIME=0

log() {
  # stderr: create_product is called in command substitution, so only the
  # product id may go to stdout.
  printf '%s %s\n' "$(date -u +'%Y-%m-%dT%H:%M:%SZ')" "$*" >&2
}

require() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "Missing required command: $1" >&2
    exit 1
  }
}

login() {
  local now response
  now="$(date +%s)"
  response="$(curl -sS --max-time 10 -X POST "$API_HOST/auth/login" \
    -H 'Content-Type: application/json' \
    -d "{\"Email\":\"$EMAIL\",\"Password\":\"$PASSWORD\"}" || true)"

  TOKEN="$(printf '%s' "$response" | jq -r '.access_token // empty' 2>/dev/null || true)"
  if [[ -z "$TOKEN" ]]; then
    log "login failed: $response"
    return 1
  fi

  TOKEN_TIME="$now"
  log "logged in as $EMAIL"
}

ensure_token() {
  local now
  now="$(date +%s)"
  # Access token default lifetime is 10 minutes; refresh after 8.
  if [[ -z "$TOKEN" || $((now - TOKEN_TIME)) -ge 480 ]]; then
    login
  fi
}

create_product() {
  local name response code id
  name="Load-$(date +%s)-$RANDOM"
  response="$(curl -sS --max-time 10 -w '\n%{http_code}' \
    -H "Authorization: Bearer $TOKEN" \
    -X POST "$API_HOST/products" \
    -H 'Content-Type: application/json' \
    -d "{\"Name\":\"$name\",\"Category\":\"Books\",\"Price\":19.99,\"Stock\":10}" || true)"
  code="$(printf '%s' "$response" | tail -n1)"
  id="$(printf '%s' "$response" | sed '$d' | tr -d '"' | tr -d '\r')"

  if [[ "$code" == "201" && -n "$id" ]]; then
    log "created product $id ($name)"
    printf '%s' "$id"
    return 0
  fi

  log "create product failed: HTTP $code"
  return 1
}

read_products() {
  local code
  code="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' \
    -H "Authorization: Bearer $TOKEN" \
    "$API_HOST/products?page=1&pageSize=5&desc=false" || true)"
  log "read products: HTTP $code"
}

place_order() {
  local product_id="$1" cart_code order_code
  # Give OrdersApi a moment to consume ProductCreated before adding to the cart.
  sleep 2

  cart_code="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' \
    -H "Authorization: Bearer $TOKEN" \
    -X PUT "$ORDERS_HOST/cart/items" \
    -H 'Content-Type: application/json' \
    -d "{\"productId\":\"$product_id\",\"quantity\":1}" || true)"
  log "add to cart: HTTP $cart_code"

  if [[ "$cart_code" != "204" && "$cart_code" != "409" ]]; then
    return 0
  fi

  order_code="$(curl -sS --max-time 10 -o /dev/null -w '%{http_code}' \
    -H "Authorization: Bearer $TOKEN" \
    -X POST "$ORDERS_HOST/orders" || true)"
  log "place order: HTTP $order_code"
}

cycle() {
  local product_id
  ensure_token || return 0
  product_id="$(create_product)" || return 0
  read_products
  place_order "$product_id"
}

run_loop() {
  log "traffic generator starting: interval=${INTERVAL}s api=$API_HOST orders=$ORDERS_HOST"
  trap 'log "traffic generator stopped"; exit 0' INT TERM
  while true; do
    cycle
    sleep "$INTERVAL"
  done
}

is_running() {
  [[ -f "$PID_FILE" ]] || return 1
  local pid
  pid="$(cat "$PID_FILE" 2>/dev/null || true)"
  [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null
}

start_bg() {
  if is_running; then
    echo "Already running (pid $(cat "$PID_FILE"))."
    return 0
  fi
  : > "$LOG_FILE"
  INTERVAL="$INTERVAL" API_HOST="$API_HOST" ORDERS_HOST="$ORDERS_HOST" \
    EMAIL="$EMAIL" PASSWORD="$PASSWORD" PID_FILE="$PID_FILE" LOG_FILE="$LOG_FILE" \
    nohup bash "$0" run >>"$LOG_FILE" 2>&1 &
  echo $! > "$PID_FILE"
  echo "Started (pid $(cat "$PID_FILE"))."
  echo "Log: $LOG_FILE"
  echo "Stop: $0 stop"
}

stop_bg() {
  if ! is_running; then
    rm -f "$PID_FILE"
    echo "Not running."
    return 0
  fi
  local pid
  pid="$(cat "$PID_FILE")"
  kill "$pid" 2>/dev/null || true
  for _ in $(seq 1 20); do
    kill -0 "$pid" 2>/dev/null || break
    sleep 0.2
  done
  kill -9 "$pid" 2>/dev/null || true
  rm -f "$PID_FILE"
  echo "Stopped (pid $pid)."
}

status_bg() {
  if is_running; then
    echo "Running (pid $(cat "$PID_FILE"))."
  else
    echo "Not running."
  fi
}

main() {
  require curl
  require jq

  case "${1:-}" in
    start) start_bg ;;
    stop) stop_bg ;;
    status) status_bg ;;
    run) run_loop ;;
    *)
      echo "Usage: $0 {start|stop|status|run}"
      exit 1
      ;;
  esac
}

main "$@"
