#!/usr/bin/env bash
set -euo pipefail

digest="${1:?usage: inspect-oci-candidate.sh sha256:<digest>}"
command -v jq >/dev/null || { echo 'jq is required to inspect published OCI evidence' >&2; exit 1; }

if command -v skopeo >/dev/null; then
  config="$(skopeo inspect --config "docker://ghcr.io/matty/terraform-registry@$digest")"
  revision="$(jq -r '.config.Labels["org.opencontainers.image.revision"] // empty' <<<"$config")"
elif command -v docker >/dev/null; then
  image="ghcr.io/matty/terraform-registry@$digest"
  docker pull "$image" >/dev/null
  revision="$(docker image inspect "$image" --format '{{index .Config.Labels "org.opencontainers.image.revision"}}')"
else
  echo 'skopeo or docker is required to inspect published OCI evidence' >&2
  exit 1
fi

[[ "$revision" =~ ^[0-9a-f]{40}$ ]] || { echo 'OCI image lacks an immutable revision label' >&2; exit 1; }
printf '%s\t%s\n' "$digest" "$revision"
