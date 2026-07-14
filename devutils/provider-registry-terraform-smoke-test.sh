#!/usr/bin/env bash
set -euo pipefail

repo_root="${REPO_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
app_port="${TF_REG_SMOKE_APP_PORT:-$((15000 + RANDOM % 10000))}"
registry_port="${TF_REG_SMOKE_REGISTRY_PORT:-$((25000 + RANDOM % 10000))}"
remote_app_base_url="${TF_REG_SMOKE_REMOTE_APP_BASE_URL:-}"
remote_public_base_url="${TF_REG_SMOKE_REMOTE_PUBLIC_BASE_URL:-}"
remote_tls_cert="${TF_REG_SMOKE_SSL_CERT_FILE:-}"
registry_host="localhost:${registry_port}"
app_base_url="http://localhost:${app_port}"
public_base_url="https://${registry_host}"
auth_token="${TF_REG_SMOKE_AUTH_TOKEN:-provider-smoke-token}"
provider_namespace="${TF_REG_SMOKE_NAMESPACE:-acme$(date +%s)}"
provider_type="example"
provider_version="1.0.0"
work_dir="$(mktemp -d)"
app_pid=""
proxy_pid=""
server_log=""

cleanup() {
    if [[ -n "${proxy_pid}" ]] && kill -0 "${proxy_pid}" >/dev/null 2>&1; then
        kill "${proxy_pid}" >/dev/null 2>&1 || true
        wait "${proxy_pid}" >/dev/null 2>&1 || true
    fi

    if [[ -n "${app_pid}" ]] && kill -0 "${app_pid}" >/dev/null 2>&1; then
        kill "${app_pid}" >/dev/null 2>&1 || true
        wait "${app_pid}" >/dev/null 2>&1 || true
    fi
    rm -rf "${work_dir}"
}

finish() {
    status=$?
    if [[ "${status}" -ne 0 && -n "${server_log}" && -f "${server_log}" ]]; then
        echo "----- registry server log -----" >&2
        cat "${server_log}" >&2
        echo "-------------------------------" >&2
    fi
    if [[ "${status}" -ne 0 && -f "${work_dir}/tls-proxy.log" ]]; then
        echo "----- tls proxy log -----" >&2
        cat "${work_dir}/tls-proxy.log" >&2
        echo "-------------------------" >&2
    fi

    cleanup
    exit "${status}"
}
trap finish EXIT

require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        echo "$1 is required for the provider registry Terraform smoke test" >&2
        exit 1
    fi
}

ensure_terraform() {
    if command -v terraform >/dev/null 2>&1; then
        return
    fi

    if [[ "${TF_REG_SMOKE_AUTO_INSTALL_TERRAFORM:-}" != "1" ]]; then
        echo "terraform is required for the provider registry Terraform smoke test" >&2
        exit 1
    fi

    local terraform_version
    terraform_version="${TF_REG_SMOKE_TERRAFORM_VERSION:-$(curl -fsSL https://releases.hashicorp.com/terraform/ | python3 -c 'import re, sys; versions=re.findall(r"/terraform/([0-9]+\.[0-9]+\.[0-9]+)/", sys.stdin.read()); print(versions[0])')}"
    local terraform_archive="${work_dir}/terraform.zip"
    local terraform_bin_dir="${work_dir}/terraform-bin"
    mkdir -p "${terraform_bin_dir}"

    curl -fsSLo "${terraform_archive}" "https://releases.hashicorp.com/terraform/${terraform_version}/terraform_${terraform_version}_linux_amd64.zip"
    python3 -m zipfile -e "${terraform_archive}" "${terraform_bin_dir}"
    chmod +x "${terraform_bin_dir}/terraform"
    PATH="${terraform_bin_dir}:${PATH}"
}

json_string() {
    python3 -c 'import json, sys; print(json.dumps(sys.stdin.read()))'
}

require_command curl
require_command dotnet
require_command gpg
require_command openssl
require_command python3
ensure_terraform

if [[ -n "${remote_app_base_url}" || -n "${remote_public_base_url}" || -n "${remote_tls_cert}" ]]; then
    [[ -n "${remote_app_base_url}" && -n "${remote_public_base_url}" && -n "${remote_tls_cert}" ]] || {
        echo "Remote provider smoke requires TF_REG_SMOKE_REMOTE_APP_BASE_URL, TF_REG_SMOKE_REMOTE_PUBLIC_BASE_URL, and TF_REG_SMOKE_SSL_CERT_FILE" >&2
        exit 2
    }
    app_base_url="${remote_app_base_url}"
    public_base_url="${remote_public_base_url}"
    registry_host="${public_base_url#https://}"
    tls_cert="${remote_tls_cert}"
else
    tls_cert="${work_dir}/localhost.crt"
fi

release_dir="${work_dir}/release"
mkdir -p "${release_dir}"
bash "${repo_root}/TerraformRegistry.Tests/TestData/provider-release/create-test-provider-release.sh" "${release_dir}"

tls_key="${work_dir}/localhost.key"
if [[ -z "${remote_app_base_url}" ]]; then
    openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days 1 \
        -subj "/CN=localhost" \
        -addext "subjectAltName=DNS:localhost,IP:127.0.0.1" \
        -keyout "${tls_key}" \
        -out "${tls_cert}" >/dev/null 2>&1
fi

tls_proxy="${work_dir}/tls-proxy.py"
cat > "${tls_proxy}" <<'PY'
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import http.client
import os
import ssl

APP_PORT = int(os.environ["APP_PORT"])
REGISTRY_PORT = int(os.environ["REGISTRY_PORT"])
TLS_CERT = os.environ["TLS_CERT"]
TLS_KEY = os.environ["TLS_KEY"]


HOP_BY_HOP_HEADERS = {
    "connection",
    "keep-alive",
    "proxy-authenticate",
    "proxy-authorization",
    "te",
    "trailer",
    "trailers",
    "transfer-encoding",
    "upgrade",
}


class ReverseProxy(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def do_GET(self):
        self.forward()

    def do_HEAD(self):
        self.forward()

    def do_POST(self):
        self.forward()

    def do_PUT(self):
        self.forward()

    def forward(self):
        content_length = int(self.headers.get("Content-Length", "0") or "0")
        body = self.rfile.read(content_length) if content_length > 0 else None
        headers = {
            key: value
            for key, value in self.headers.items()
            if key.lower() not in HOP_BY_HOP_HEADERS
        }
        headers["Host"] = f"localhost:{APP_PORT}"

        connection = http.client.HTTPConnection("127.0.0.1", APP_PORT, timeout=60)
        try:
            connection.request(self.command, self.path, body=body, headers=headers)
            response = connection.getresponse()
            response_body = b"" if self.command == "HEAD" else response.read()

            self.send_response(response.status, response.reason)
            for key, value in response.getheaders():
                if key.lower() not in HOP_BY_HOP_HEADERS and key.lower() != "content-length":
                    self.send_header(key, value)
            self.send_header("Content-Length", str(len(response_body)))
            self.send_header("Connection", "close")
            self.end_headers()

            if self.command != "HEAD":
                self.wfile.write(response_body)
                self.wfile.flush()
        finally:
            self.close_connection = True
            connection.close()

    def log_message(self, format, *args):
        print(format % args, flush=True)


def main():
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.load_cert_chain(TLS_CERT, TLS_KEY)
    server = ThreadingHTTPServer(("127.0.0.1", REGISTRY_PORT), ReverseProxy)
    server.socket = context.wrap_socket(server.socket, server_side=True)
    server.serve_forever()


main()
PY

server_log="${work_dir}/registry.log"
if [[ -z "${remote_app_base_url}" ]]; then
    ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS="${app_base_url}" \
    TF_REG_BaseUrl="${public_base_url}" \
    TF_REG_Port="${app_port}" \
    TF_REG_AuthorizationToken="${auth_token}" \
    TF_REG_DevAuthBypass=true \
    TF_REG_AdminEmails=dev@localhost \
    TF_REG_DatabaseProvider=sqlite \
    TF_REG_Sqlite__ConnectionString="Data Source=${work_dir}/terraform-registry.db" \
    TF_REG_StorageProvider=local \
    TF_REG_ModuleStoragePath="${work_dir}/modules" \
    TF_REG_ProviderStoragePath="${work_dir}/providers" \
    TF_REG_Oidc__JwtSecretKey=provider-smoke-jwt-secret-key-32-chars \
        dotnet run --project "${repo_root}/TerraformRegistry/TerraformRegistry.csproj" --no-launch-profile >"${server_log}" 2>&1 &
    app_pid="$!"
fi

for _ in {1..90}; do
    if curl -fsS "${app_base_url}/.well-known/terraform.json" >/dev/null 2>&1; then
        break
    fi

    if ! kill -0 "${app_pid}" >/dev/null 2>&1; then
        cat "${server_log}" >&2
        exit 1
    fi

    sleep 1
done

if ! curl -fsS "${app_base_url}/.well-known/terraform.json" >/dev/null; then
    cat "${server_log}" >&2
    echo "Registry did not become ready at ${app_base_url}" >&2
    exit 1
fi

if [[ -z "${remote_app_base_url}" ]]; then
    APP_PORT="${app_port}" REGISTRY_PORT="${registry_port}" TLS_CERT="${tls_cert}" TLS_KEY="${tls_key}" \
        python3 "${tls_proxy}" >"${work_dir}/tls-proxy.log" 2>&1 &
    proxy_pid="$!"
fi

for _ in {1..30}; do
    if curl --cacert "${tls_cert}" -fsS "${public_base_url}/.well-known/terraform.json" >/dev/null 2>&1; then
        break
    fi

    if ! kill -0 "${proxy_pid}" >/dev/null 2>&1; then
        cat "${work_dir}/tls-proxy.log" >&2
        exit 1
    fi

    sleep 1
done

curl --cacert "${tls_cert}" -fsS "${public_base_url}/.well-known/terraform.json" >/dev/null

curl -fsS -X POST "${app_base_url}/api/auth/dev-login" >/dev/null

key_id="$(cat "${release_dir}/key-id.txt")"
public_key_json="$(json_string < "${release_dir}/public-key.asc")"
package_file="${release_dir}/terraform-provider-${provider_type}_${provider_version}_linux_amd64.zip"
shasums_file="${release_dir}/terraform-provider-${provider_type}_${provider_version}_SHA256SUMS"
signature_file="${shasums_file}.sig"
shasum="$(awk '{ print $1; exit }' "${shasums_file}")"

curl -fsS -X POST "${app_base_url}/api/providers" \
    -H "Content-Type: application/json" \
    -d "{\"namespace\":\"${provider_namespace}\",\"type\":\"${provider_type}\",\"display_name\":\"Example\"}" >/dev/null

curl -fsS -X POST "${app_base_url}/api/providers/${provider_namespace}/${provider_type}/gpg-keys" \
    -H "Content-Type: application/json" \
    -d "{\"key_id\":\"${key_id}\",\"ascii_armor\":${public_key_json},\"source\":\"smoke-test\"}" >/dev/null

curl -fsS -X POST "${app_base_url}/api/providers/${provider_namespace}/${provider_type}/versions" \
    -H "Content-Type: application/json" \
    -d "{\"version\":\"${provider_version}\",\"protocols\":[\"5.2\"],\"key_id\":\"${key_id}\"}" >/dev/null

curl -fsS -X PUT "${app_base_url}/api/providers/${provider_namespace}/${provider_type}/versions/${provider_version}/shasums" \
    -H "Content-Type: text/plain" \
    --data-binary @"${shasums_file}" >/dev/null

curl -fsS -X PUT "${app_base_url}/api/providers/${provider_namespace}/${provider_type}/versions/${provider_version}/shasums.sig" \
    -H "Content-Type: application/octet-stream" \
    --data-binary @"${signature_file}" >/dev/null

curl -fsS -X POST "${app_base_url}/api/providers/${provider_namespace}/${provider_type}/versions/${provider_version}/platforms" \
    -H "Content-Type: application/json" \
    -d "{\"os\":\"linux\",\"arch\":\"amd64\",\"filename\":\"terraform-provider-${provider_type}_${provider_version}_linux_amd64.zip\",\"shasum\":\"${shasum}\"}" >/dev/null

curl -fsS -X PUT "${app_base_url}/api/providers/${provider_namespace}/${provider_type}/versions/${provider_version}/platforms/linux/amd64/package" \
    -H "Content-Type: application/zip" \
    --data-binary @"${package_file}" >/dev/null

metadata_file="${work_dir}/download-metadata.json"
curl --cacert "${tls_cert}" -fsS "${public_base_url}/v1/providers/${provider_namespace}/${provider_type}/${provider_version}/download/linux/amd64" \
    -o "${metadata_file}"

download_url="$(
    python3 -c 'import json, sys; from urllib.parse import urljoin; print(urljoin(sys.argv[2] + "/", json.load(open(sys.argv[1]))["download_url"]))' \
        "${metadata_file}" "${public_base_url}"
)"
downloaded_package="${work_dir}/downloaded-provider.zip"
curl --cacert "${tls_cert}" -fsS "${download_url}" -o "${downloaded_package}"

expected_shasum="$(sha256sum "${package_file}" | awk '{ print $1 }')"
actual_shasum="$(sha256sum "${downloaded_package}" | awk '{ print $1 }')"
if [[ "${actual_shasum}" != "${expected_shasum}" ]]; then
    echo "Downloaded provider package checksum mismatch before terraform init" >&2
    echo "Expected: ${expected_shasum}" >&2
    echo "Actual:   ${actual_shasum}" >&2
    ls -l "${package_file}" "${downloaded_package}" >&2
    exit 1
fi

terraform_dir="${work_dir}/terraform"
mkdir -p "${terraform_dir}"
cat > "${terraform_dir}/main.tf" <<HCL
terraform {
  required_providers {
    ${provider_type} = {
      source  = "${registry_host}/${provider_namespace}/${provider_type}"
      version = "${provider_version}"
    }
  }
}
HCL

cat > "${terraform_dir}/terraform.rc" <<HCL
credentials "${registry_host}" {
  token = "${auth_token}"
}
HCL

SSL_CERT_FILE="${tls_cert}" TF_CLI_CONFIG_FILE="${terraform_dir}/terraform.rc" terraform -chdir="${terraform_dir}" init -input=false -no-color
