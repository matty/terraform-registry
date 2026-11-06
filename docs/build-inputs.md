# Reproducible release build inputs

This record is the source of truth for external inputs used to build a release
artifact. Every image digest and GitHub Action revision is immutable; Renovate
is responsible for proposing reviewed updates.

| Input | Pinned revision | Used by |
| --- | --- | --- |
| Terraform CLI | `1.14.2`, `hashicorp/terraform@sha256:eee2f7d5725bfcfd734dfc9fe5a3df4b58b00eb8cc874993458108d8943265cf` | Local, Azure, and S3-compatible Terraform smoke tests |
| Terraform CLI support window | `1.12.0`, `hashicorp/terraform@sha256:be40b1de9a0f97b1e859235aca824d1bac4cf5c0dd715074aa45595ea055aa8b`; `1.14.2`, `hashicorp/terraform@sha256:eee2f7d5725bfcfd734dfc9fe5a3df4b58b00eb8cc874993458108d8943265cf` | Release certification matrix for Local, Azurite, MinIO, signed-provider, and non-root image evidence |
| terraform-config-inspect | `2fb54c236733ee65ee877105d595c124c993c64d` archive SHA-256 `83aedf832593023babc90bd49dce5adb58e8f0774bacea4992a3d350f33af915`, rebuilt with `golang.org/x/text@v0.41.0` | Release Docker image |
| Go builder | `golang:1.27-alpine@sha256:fa3d596898d9f126af7c01d14ee7597bab3f39cccf392ef36b69fe77cd681f18` | terraform-config-inspect build stage |
| Node builder | `node:24-alpine@sha256:50c8e8ca1d27439048670df5883f32d57cf81cff6233222c893fd0d9884cbd81` | Generated frontend build stage |
| .NET SDK builder | `mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:5562fd626eec39f6f53218f9b7d7856ab7195d51d846568304fbe85071a3b6e6` | Release Docker image |
| terraform-provider-smoke build inputs | Digest-pinned `.NET SDK` above; Alpine v3.24 `bash=5.3.9-r1`, `gnupg=2.4.9-r1`, `openssl=3.5.9-r0`, `python3=3.14.10-r1` | Non-root fabricated-provider certification helper. The helper only installs these exact package revisions; changing one requires updating this row and its Dockerfile together. |
| ASP.NET runtime | `mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:486297ae006e95ee2ee3112a2c03e3d26e41fbbc64b7a29e28e7a18cc36b12bb`; Alpine `libcrypto3=3.5.9-r0`, `libssl3=3.5.9-r0` | Release Docker image |
| Development .NET SDK | `mcr.microsoft.com/dotnet/sdk:10.0@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e` | Development Docker image |
| PostgreSQL | `postgres:18@sha256:4ef4dbc939d61acea57712655ddb4b4ab27419c913f94cca0cd57cb3ea3c2280` | Development Compose stack and storage-emulator smoke tests |
| pgAdmin | `dpage/pgadmin4:latest@sha256:3591b3d02910dcd817dec6fcaa681c278ba6966945283dd0ea0042731c330c83` | Development Compose stack |
| Azurite | `mcr.microsoft.com/azure-storage/azurite:3.33.0@sha256:2628ee10a72833cc344b9d194cd8b245543892b307d16cf26a2cf55a15b816af` | Azure Blob storage-emulator smoke tests |
| MinIO server | `pgsty/minio:RELEASE.2026-08-04T00-00-00Z@sha256:b6bfe7239bfc83fb90d31612d9704d86039dd714f7904b3f1ad68f211e602372` | S3-compatible storage-emulator smoke tests |
| MinIO client | `pgsty/mc:RELEASE.2026-10-18T00-00-00Z@sha256:6ecdaec70bc5e8cf9d0e9a70c190f3beb1c83f11617ff4333f1fcd71ab1daf18` | S3-compatible storage-emulator initialization |
| Caddy | `caddy:2.11-alpine@sha256:5f5c8640aae01df9654968d946d8f1a56c497f1dd5c5cda4cf95ab7c14d58648` | Storage-emulator smoke-test reverse proxy |

The gate at `scripts/verification/gates/supply-chain-pinning.sh` ensures these
references remain immutable, including every Compose image used by development
or the CI storage-emulator smoke tests, and that all workflow actions use a
40-character commit SHA. It deliberately permits a human-readable version
comment beside an action SHA, but never a mutable action tag as the executable
reference.
