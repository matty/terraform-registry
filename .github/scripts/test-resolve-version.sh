#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
resolver="${script_dir}/resolve-version.sh"

assert_equals() {
  local expected="$1"
  local actual="$2"
  local scenario="$3"

  if [[ "$actual" != "$expected" ]]; then
    echo "${scenario}: expected '${expected}', got '${actual}'" >&2
    exit 1
  fi
}

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

git -C "$work_dir" init --quiet
git -C "$work_dir" config user.email test@example.com
git -C "$work_dir" config user.name "Test User"
git -C "$work_dir" commit --quiet --allow-empty -m initial

assert_equals "2030.1.2" "$("$resolver" 2030.1.2)" "manual override"

assert_equals "2026.6.0" "$(
  cd "$work_dir"
  GITHUB_REF_TYPE=tag GITHUB_REF_NAME=v2026.6.0 "$resolver"
)" "release tag"

assert_equals "2026.6.123" "$(
  cd "$work_dir"
  CALVER_DATE=2026-06-16 GITHUB_REF_TYPE=branch GITHUB_REF_NAME=develop GITHUB_RUN_NUMBER=123 "$resolver"
)" "develop branch"

git -C "$work_dir" tag v2026.6.1
git -C "$work_dir" tag v2026.6.2
git -C "$work_dir" tag v2026.5.9
git -C "$work_dir" tag v2025.6.9

assert_equals "2026.6.3" "$(
  cd "$work_dir"
  CALVER_DATE=2026-06-16 GITHUB_REF_TYPE=branch GITHUB_REF_NAME=main GITHUB_RUN_NUMBER=999 "$resolver"
)" "main branch increments from current month tags"

git -C "$work_dir" tag v2026.7.7

assert_equals "2026.8.1" "$(
  cd "$work_dir"
  CALVER_DATE=2026-08-01 GITHUB_REF_TYPE=branch GITHUB_REF_NAME=main GITHUB_RUN_NUMBER=1000 "$resolver"
)" "main branch starts at one when no current month tags exist"
