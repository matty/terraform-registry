#!/usr/bin/env bash
set -euo pipefail

out_dir="${1:?output directory required}"
mkdir -p "$out_dir"

provider_name="example"
version="1.0.0"
os_name="linux"
arch_name="amd64"
package="terraform-provider-${provider_name}_${version}_${os_name}_${arch_name}.zip"
binary="terraform-provider-${provider_name}_v${version}"

work_dir="$(mktemp -d)"
trap 'rm -rf "$work_dir"' EXIT

printf '#!/usr/bin/env sh\nexit 0\n' > "$work_dir/$binary"
chmod +x "$work_dir/$binary"

if command -v zip >/dev/null 2>&1; then
    (cd "$work_dir" && zip -q "$out_dir/$package" "$binary")
elif command -v python3 >/dev/null 2>&1; then
    python3 - "$work_dir" "$binary" "$out_dir/$package" <<'PY'
import sys
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile

work_dir = Path(sys.argv[1])
binary = sys.argv[2]
package = Path(sys.argv[3])

with ZipFile(package, "w", ZIP_DEFLATED) as archive:
    archive.write(work_dir / binary, binary)
PY
else
    echo "zip or python3 is required to create the provider package" >&2
    exit 1
fi

if command -v sha256sum >/dev/null 2>&1; then
    (cd "$out_dir" && sha256sum "$package" > "terraform-provider-${provider_name}_${version}_SHA256SUMS")
elif command -v shasum >/dev/null 2>&1; then
    (cd "$out_dir" && shasum -a 256 "$package" > "terraform-provider-${provider_name}_${version}_SHA256SUMS")
else
    echo "sha256sum or shasum is required" >&2
    exit 1
fi

gpg_home="$out_dir/gpg-home"
mkdir -p "$gpg_home"
chmod 700 "$gpg_home"

GNUPGHOME="$gpg_home" gpg --batch --pinentry-mode loopback --passphrase '' --quick-generate-key "Provider Test <provider-test@example.com>" rsa2048 sign 1d
key_id="$(GNUPGHOME="$gpg_home" gpg --list-keys --with-colons | awk -F: '/^pub:/ { print $5; exit }')"

GNUPGHOME="$gpg_home" gpg --batch --pinentry-mode loopback --passphrase '' --yes --detach-sign "$out_dir/terraform-provider-${provider_name}_${version}_SHA256SUMS"
GNUPGHOME="$gpg_home" gpg --armor --export "$key_id" > "$out_dir/public-key.asc"
printf '%s' "$key_id" > "$out_dir/key-id.txt"
