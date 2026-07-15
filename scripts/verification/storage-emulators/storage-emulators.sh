#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
SOURCE_COMPOSE="$ROOT/scripts/verification/storage-emulators/compose.yaml"
DEFAULT_HOME="$HOME/.terraform-registry-storage-test"
HOME_DIR="$DEFAULT_HOME"
PROVIDER=""
COMMAND=""

usage() {
  printf 'Usage: %s <start|status|test|clean> [--provider azure|s3|all] [--home ABSOLUTE_PATH]\n' "$0" >&2
  exit 2
}

while (($#)); do
  case "$1" in
    start|status|test|clean) [[ -z "$COMMAND" ]] || usage; COMMAND="$1" ;;
    --provider) PROVIDER="${2:-}"; shift ;;
    --home) HOME_DIR="${2:-}"; shift ;;
    *) usage ;;
  esac
  shift
done

[[ -n "$COMMAND" && "$HOME_DIR" = /* ]] || usage
if [[ -z "$PROVIDER" ]]; then
  PROVIDER="$([[ "$COMMAND" = test ]] && printf all || printf azure)"
fi
[[ "$PROVIDER" = azure || "$PROVIDER" = s3 || ( "$COMMAND" = test && "$PROVIDER" = all ) ]] || usage

marker="$HOME_DIR/.terraform-registry-storage-test"
project_id="$(printf '%s' "$HOME_DIR" | sha256sum | cut -c1-12)"
PROJECT="tfregstorage${project_id}"

compose() {
  docker compose --project-name "$PROJECT" --project-directory "$HOME_DIR" -f "$HOME_DIR/compose.yaml" "$@"
}

initialize() {
  mkdir -p "$HOME_DIR"
  cp "$SOURCE_COMPOSE" "$HOME_DIR/compose.yaml"
  printf '%s\n' 'registry.local {' '  tls internal' '  reverse_proxy app:5131' '}' > "$HOME_DIR/Caddyfile"
  printf 'REGISTRY_ROOT=%s\nSTORAGE_PROVIDER=%s\n' "$ROOT" "$PROVIDER" > "$HOME_DIR/.env"
  printf '%s\n' "$PROJECT" > "$marker"
}

require_initialized() {
  if [[ ! -f "$marker" ]]; then
    printf 'Storage emulator harness at %s is not initialized. Run start first.\n' "$HOME_DIR" >&2
    exit 1
  fi
}

case "$COMMAND" in
  start)
    initialize
    compose up -d --build --force-recreate azurite minio postgres
    compose run --rm minio-init
    compose up -d --build --force-recreate app caddy
    ;;
  status)
    require_initialized
    running="$(compose ps --status running --services)"
    for service in azurite minio postgres app caddy; do
      grep -qx "$service" <<<"$running" || {
        printf 'Storage emulator harness service %s is not running.\n' "$service" >&2
        exit 1
      }
    done
    printf 'Storage emulator harness is running at %s.\n' "$HOME_DIR"
    ;;
  test)
    exec "$ROOT/scripts/verification/phase-1-storage-emulator-terraform-smoke.sh" \
      --provider "$PROVIDER" --home "$HOME_DIR"
    ;;
  clean)
    require_initialized
    [[ "$(cat "$marker")" = "$PROJECT" ]] || {
      printf 'Refusing to clean harness with an invalid marker at %s.\n' "$HOME_DIR" >&2
      exit 1
    }
    compose down --volumes --remove-orphans
    docker run --rm -v "$HOME_DIR:/harness" alpine:3.21 sh -c \
      'rm -rf /harness/* /harness/.[!.]* /harness/..?*'
    rm -rf "$HOME_DIR"
    printf 'Storage emulator harness removed from %s.\n' "$HOME_DIR"
    ;;
esac
