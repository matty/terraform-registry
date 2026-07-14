# Reproducible release build inputs

This record is the source of truth for external inputs used to build a release
artifact. Every image digest and GitHub Action revision is immutable; Renovate
is responsible for proposing reviewed updates.

| Input | Pinned revision | Used by |
| --- | --- | --- |
| Terraform CLI | `1.14.2`, `hashicorp/terraform@sha256:eee2f7d5725bfcfd734dfc9fe5a3df4b58b00eb8cc874993458108d8943265cf` | Local, Azure, and S3-compatible Terraform smoke tests |
| terraform-config-inspect | `2fb54c236733ee65ee877105d595c124c993c64d` | Release Docker image |
| Go builder | `golang:1.26-alpine@sha256:0178a641fbb4858c5f1b48e34bdaabe0350a330a1b1149aabd498d0699ff5fb2` | terraform-config-inspect build stage |
| Node builder | `node:24-alpine@sha256:a0b9bf06e4e6193cf7a0f58816cc935ff8c2a908f81e6f1a95432d679c54fbfd` | Generated frontend build stage |
| .NET SDK builder | `mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:940f919ae84dd92ccd4aab7686fa5b777870b006c9360351039e16bcaad73d89` | Release Docker image |
| ASP.NET runtime | `mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:57bd717ac18ff6c8a39cc0ee4a76c1f15adc46df50434c73eff0c3f1df4c88f0` | Release Docker image |
| Development .NET SDK | `mcr.microsoft.com/dotnet/sdk:10.0@sha256:ea8bde36c11b6e7eec2656d0e59101d4462f6bd630f2c8201ed0572b295d5` | Development Docker image |
| PostgreSQL | `postgres:18@sha256:48ebba8b80dc3be58b5ae431f47a33535289959cddfe13f5f887298de959fae0` | Development Compose stack and storage-emulator smoke tests |
| pgAdmin | `dpage/pgadmin4:latest@sha256:40fa840c5bb7c8463957f1255b01283732c2d8c9396a956d180f8e6c296753b3` | Development Compose stack |
| Azurite | `mcr.microsoft.com/azure-storage/azurite:3.33.0@sha256:2628ee10a72833cc344b9d194cd8b245543892b307d16cf26a2cf55a15b816af` | Azure Blob storage-emulator smoke tests |
| MinIO server | `minio/minio:RELEASE.2025-04-22T22-12-26Z@sha256:a1ea29fa28355559ef137d71fc570e508a214ec84ff8083e39bc5428980b015e` | S3-compatible storage-emulator smoke tests |
| MinIO client | `minio/mc:RELEASE.2025-03-12T17-29-24Z@sha256:470f5546b596e16c7816b9c3fa7a78ce4076bb73c2c73f7faeec0c8043923123` | S3-compatible storage-emulator initialization |
| Caddy | `caddy:2.11-alpine@sha256:5f5c8640aae01df9654968d946d8f1a56c497f1dd5c5cda4cf95ab7c14d58648` | Storage-emulator smoke-test reverse proxy |

The gate at `scripts/remediation/gates/supply-chain-pinning.sh` ensures these
references remain immutable, including every Compose image used by development
or the CI storage-emulator smoke tests, and that all workflow actions use a
40-character commit SHA. It deliberately permits a human-readable version
comment beside an action SHA, but never a mutable action tag as the executable
reference.
