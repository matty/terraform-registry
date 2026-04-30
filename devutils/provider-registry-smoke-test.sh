#!/usr/bin/env bash
set -euo pipefail

registry_host="${1:?registry host required, for example registry.company.com}"
token="${2:?registry API token required}"
provider_namespace="${3:-acme}"
provider_type="${4:-example}"
provider_version="${5:-1.0.0}"

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

cat > "$work_dir/main.tf" <<HCL
terraform {
  required_providers {
    ${provider_type} = {
      source  = "${registry_host}/${provider_namespace}/${provider_type}"
      version = "${provider_version}"
    }
  }
}
HCL

cat > "$work_dir/terraform.rc" <<HCL
credentials "${registry_host}" {
  token = "${token}"
}
HCL

TF_CLI_CONFIG_FILE="$work_dir/terraform.rc" terraform -chdir="$work_dir" init
