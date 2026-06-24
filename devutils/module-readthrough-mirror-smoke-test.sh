#!/usr/bin/env bash
set -euo pipefail

registry_url="${1:?registry URL required, for example https://registry.company.com}"
token="${2:?Terraform registry token required}"
module_namespace="${3:-terraform-aws-modules}"
module_name="${4:-vpc}"
module_provider="${5:-aws}"
module_version="${6:-5.21.0}"

require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        echo "$1 is required" >&2
        exit 1
    fi
}

require_command curl
require_command python3
require_command terraform

registry_host="$(python3 - "${registry_url}" <<'PY'
from urllib.parse import urlparse
import sys
parsed = urlparse(sys.argv[1])
if parsed.scheme not in ("http", "https") or not parsed.netloc:
    raise SystemExit("registry URL must include http(s) scheme and host")
print(parsed.netloc)
PY
)"

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

curl -fsS "${registry_url%/}/.well-known/terraform.json" | python3 -c 'import json, sys; data=json.load(sys.stdin); assert "modules.v1" in data'

cat >"${work_dir}/terraform.rc" <<HCL
credentials "${registry_host}" {
  token = "${token}"
}
HCL

write_module_config() {
    local target_dir="$1"
    mkdir -p "${target_dir}"
    cat >"${target_dir}/main.tf" <<HCL
module "mirror_smoke" {
  source  = "${registry_host}/${module_namespace}/${module_name}/${module_provider}"
  version = "${module_version}"
}
HCL
}

first_dir="${work_dir}/first"
second_dir="${work_dir}/second"
write_module_config "${first_dir}"
write_module_config "${second_dir}"

TF_CLI_CONFIG_FILE="${work_dir}/terraform.rc" terraform -chdir="${first_dir}" init -input=false
TF_CLI_CONFIG_FILE="${work_dir}/terraform.rc" terraform -chdir="${second_dir}" init -input=false

if [[ -n "${TF_REG_SMOKE_RECURSIVE_MODULE_SOURCE:-}" ]]; then
    recursive_dir="${work_dir}/recursive"
    mkdir -p "${recursive_dir}"
    cat >"${recursive_dir}/main.tf" <<HCL
module "recursive" {
  source = "${TF_REG_SMOKE_RECURSIVE_MODULE_SOURCE}"
}
HCL

    if TF_CLI_CONFIG_FILE="${work_dir}/terraform.rc" terraform -chdir="${recursive_dir}" init -input=false >"${work_dir}/recursive.log" 2>&1; then
        echo "recursive registry module source was accepted unexpectedly" >&2
        cat "${work_dir}/recursive.log" >&2
        exit 1
    fi
fi
