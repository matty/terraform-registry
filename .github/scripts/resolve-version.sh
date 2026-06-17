#!/usr/bin/env bash
set -euo pipefail

version_override="${1:-}"

if [[ -n "$version_override" ]]; then
  printf '%s\n' "$version_override"
  exit 0
fi

ref_type="${GITHUB_REF_TYPE:-}"
ref_name="${GITHUB_REF_NAME:-}"

if [[ "$ref_type" == "tag" ]]; then
  if [[ "$ref_name" =~ ^v?([0-9]{4}\.[0-9]{1,2}\.[0-9]+([.-][0-9A-Za-z.-]+)?)$ ]]; then
    printf '%s\n' "${BASH_REMATCH[1]}"
    exit 0
  fi

  echo "Release tags must use CalVer format vYYYY.M.PATCH, for example v2026.6.0" >&2
  exit 1
fi

calver_date="${CALVER_DATE:-$(date -u +%Y-%m-%d)}"

if [[ ! "$calver_date" =~ ^([0-9]{4})-([0-9]{2})-[0-9]{2}$ ]]; then
  echo "CALVER_DATE must use YYYY-MM-DD format" >&2
  exit 1
fi

year="${BASH_REMATCH[1]}"
month="$((10#${BASH_REMATCH[2]}))"

if [[ "$ref_type" == "branch" && "$ref_name" == "main" ]]; then
  max_patch=0

  while IFS= read -r tag; do
    if [[ "$tag" =~ ^v?${year}\.${month}\.([0-9]+)$ ]]; then
      patch="${BASH_REMATCH[1]}"
      if (( patch > max_patch )); then
        max_patch="$patch"
      fi
    fi
  done < <(git tag --list)

  printf '%s.%s.%s\n' "$year" "$month" "$((max_patch + 1))"
  exit 0
fi

build_number="${GITHUB_RUN_NUMBER:-}"

if [[ ! "$build_number" =~ ^[0-9]+$ ]]; then
  echo "GITHUB_RUN_NUMBER must be set to a numeric value when no version override or CalVer tag is present" >&2
  exit 1
fi

printf '%s.%s.%s\n' "$year" "$month" "$build_number"
