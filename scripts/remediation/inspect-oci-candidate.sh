#!/usr/bin/env bash
set -euo pipefail

digest="${1:?usage: inspect-oci-candidate.sh sha256:<digest>}"
command -v skopeo >/dev/null || { echo 'skopeo is required to inspect published OCI evidence' >&2; exit 1; }
command -v jq >/dev/null || { echo 'jq is required to inspect published OCI evidence' >&2; exit 1; }

config="$(skopeo inspect --config "docker://ghcr.io/matty/terraform-registry@$digest")"
revision="$(jq -r '.config.Labels["org.opencontainers.image.revision"] // empty' <<<"$config")"
[[ "$revision" =~ ^[0-9a-f]{40}$ ]] || { echo 'OCI image lacks an immutable revision label' >&2; exit 1; }
printf '%s\t%s\n' "$digest" "$revision"
