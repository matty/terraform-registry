#!/usr/bin/env bash
set -euo pipefail

mirror_url="${1:?network mirror URL required, for example https://registry.company.com/mirror/providers/}"
credentials_host="${2:?Terraform credentials host required, include port when non-443}"
token="${3:?Terraform registry token required}"
provider_namespace="${4:-hashicorp}"
provider_type="${5:-null}"
provider_version="${6:-3.2.4}"

if [[ "${mirror_url}" != https://* || "${mirror_url}" != */ ]]; then
    echo "network mirror URL must be HTTPS and include a trailing slash" >&2
    exit 1
fi

require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        echo "$1 is required" >&2
        exit 1
    fi
}

require_command terraform

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

cat >"${work_dir}/terraform.rc" <<HCL
credentials "${credentials_host}" {
  token = "${token}"
}

provider_installation {
  network_mirror {
    url = "${mirror_url}"
    include = ["${credentials_host}/${provider_namespace}/${provider_type}"]
  }

  direct {
    exclude = ["${credentials_host}/${provider_namespace}/${provider_type}"]
  }
}
HCL

cat >"${work_dir}/main.tf" <<HCL
terraform {
  required_providers {
    ${provider_type} = {
      source  = "${credentials_host}/${provider_namespace}/${provider_type}"
      version = "${provider_version}"
    }
  }
}
HCL

TF_CLI_CONFIG_FILE="${work_dir}/terraform.rc" terraform -chdir="${work_dir}" init -input=false
