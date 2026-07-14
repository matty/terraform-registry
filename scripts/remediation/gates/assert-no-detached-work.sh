#!/usr/bin/env bash
set -euo pipefail

source_root="${1:?usage: assert-no-detached-work.sh <application-source-root>}"

if grep -R -n -E --include='*.cs' \
  'Task\.Run|Task\.Factory\.StartNew|async[[:space:]]+void|ThreadPool\.|new[[:space:]]+Thread' \
  "$source_root"; then
  echo 'Detached background work is not permitted in the application source.' >&2
  exit 1
fi
