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

The gate at `scripts/remediation/gates/supply-chain-pinning.sh` ensures these
references remain immutable and that all workflow actions use a 40-character
commit SHA. It deliberately permits a human-readable version comment beside an
action SHA, but never a mutable action tag as the executable reference.
