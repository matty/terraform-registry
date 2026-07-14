# Terraform Registry remediation status

> Generated from `scripts/remediation/requirement-evidence.tsv` by
> `scripts/remediation/validate-requirement-evidence.sh --write-status`.
> Do not edit this ledger by hand.

The validator confirms every requirement in the specification has an immutable
merge record reachable from the checked-out candidate, an executable automation
gate, and a checked-in evidence path. It intentionally does not infer a release
certification from historical records.

The listed gates document merged-work automation; their branch-specific CI
invocations are not treated as current-candidate evidence. A completed candidate
must instead be tied to one successful CI run with the combined
`Pre-publication candidate verification` job successful. Its immutable candidate
identity is verified from that run's bound pre-publication evidence artifact,
because a workflow-dispatch run itself is recorded at the workflow ref.

## Requirement evidence

| Requirement | State | Pull request | Merge record | Automation gate | Checked-in evidence |
|---|---|---|---|---|---|
| `DB-001` | MERGED | [#53](https://github.com/matty/terraform-registry/pull/53) | `1c5cd723516e7aabb87b5b8a03d8df836b2f9766` | `scripts/remediation/gates/phase-0.sh` | `TerraformRegistry.Tests/DbUpPostgresqlMigrationTests.cs` |
| `DB-002` | MERGED | [#53](https://github.com/matty/terraform-registry/pull/53) | `1c5cd723516e7aabb87b5b8a03d8df836b2f9766` | `scripts/remediation/gates/phase-0.sh` | `TerraformRegistry.Tests/DbUpIncrementalMigrationTests.cs` |
| `DB-003` | MERGED | [#53](https://github.com/matty/terraform-registry/pull/53) | `1c5cd723516e7aabb87b5b8a03d8df836b2f9766` | `scripts/remediation/gates/phase-0.sh` | `TerraformRegistry.Tests/DbUpMigratorTests.cs` |
| `DB-004` | MERGED | [#53](https://github.com/matty/terraform-registry/pull/53) | `1c5cd723516e7aabb87b5b8a03d8df836b2f9766` | `scripts/remediation/gates/phase-0.sh` | `TerraformRegistry.Tests/DbUpMigratorTests.cs` |
| `DB-005` | MERGED | [#53](https://github.com/matty/terraform-registry/pull/53) | `1c5cd723516e7aabb87b5b8a03d8df836b2f9766` | `scripts/remediation/gates/phase-0.sh` | `docs/phase-0-migration-recovery-runbook.md` |
| `DB-006` | MERGED | [#53](https://github.com/matty/terraform-registry/pull/53) | `1c5cd723516e7aabb87b5b8a03d8df836b2f9766` | `scripts/remediation/gates/phase-0.sh` | `scripts/remediation/phase-0-docker-e2e.sh` |
| `MOD-001` | MERGED | [#67](https://github.com/matty/terraform-registry/pull/67) | `aa61bc37f045a65f7f378759fb5fc15feb9126ac` | `scripts/remediation/gates/phase-1.sh` | `scripts/remediation/phase-1-local-terraform-smoke.sh` |
| `MOD-002` | MERGED | [#67](https://github.com/matty/terraform-registry/pull/67) | `aa61bc37f045a65f7f378759fb5fc15feb9126ac` | `scripts/remediation/gates/phase-1.sh` | `scripts/remediation/phase-1-local-terraform-smoke.sh` |
| `MOD-003` | MERGED | [#67](https://github.com/matty/terraform-registry/pull/67) | `aa61bc37f045a65f7f378759fb5fc15feb9126ac` | `scripts/remediation/gates/phase-1.sh` | `scripts/remediation/phase-1-storage-emulator-terraform-smoke.sh` |
| `MOD-004` | MERGED | [#67](https://github.com/matty/terraform-registry/pull/67) | `aa61bc37f045a65f7f378759fb5fc15feb9126ac` | `scripts/remediation/gates/phase-1.sh` | `TerraformRegistry.Tests/UnitTests/AzureBlob/AzureBlobModuleServiceDownloadTests.cs` |
| `MOD-005` | MERGED | [#84](https://github.com/matty/terraform-registry/pull/84) | `339b0c79430c05dc3d1d3b105b803537d9957c5d` | `scripts/remediation/gates/bounded-ingestion-security.sh` | `TerraformRegistry.Tests/UnitTests/AzureBlob/AzureBlobModuleServiceUploadTests.cs` |
| `MOD-006` | MERGED | [#67](https://github.com/matty/terraform-registry/pull/67) | `aa61bc37f045a65f7f378759fb5fc15feb9126ac` | `scripts/remediation/gates/phase-1.sh` | `TerraformRegistry.Tests/Utilities/SemVerValidatorTests.cs` |
| `MOD-007` | MERGED | [#67](https://github.com/matty/terraform-registry/pull/67) | `aa61bc37f045a65f7f378759fb5fc15feb9126ac` | `scripts/remediation/gates/phase-1.sh` | `scripts/remediation/phase-1-storage-emulator-terraform-smoke.sh` |
| `MOD-008` | MERGED | [#67](https://github.com/matty/terraform-registry/pull/67) | `aa61bc37f045a65f7f378759fb5fc15feb9126ac` | `scripts/remediation/gates/phase-1.sh` | `TerraformRegistry.Tests/UnitTests/AzureBlob/AzureBlobModuleServiceDownloadTests.cs` |
| `ING-001` | MERGED | [#84](https://github.com/matty/terraform-registry/pull/84) | `339b0c79430c05dc3d1d3b105b803537d9957c5d` | `scripts/remediation/gates/bounded-ingestion-security.sh` | `TerraformRegistry.Tests/UnitTests/ArchiveWorkspaceFactoryTests.cs` |
| `ING-002` | MERGED | [#96](https://github.com/matty/terraform-registry/pull/96) | `11e426b088918fa449875044353a86106a974f80` | `scripts/remediation/gates/publication-fault-gate.sh` | `TerraformRegistry.Tests/UnitTests/ModuleExtractionServiceTests.cs` |
| `ING-003` | MERGED | [#96](https://github.com/matty/terraform-registry/pull/96) | `11e426b088918fa449875044353a86106a974f80` | `scripts/remediation/gates/publication-fault-gate.sh` | `TerraformRegistry.Tests/UnitTests/ModuleExtractionServiceTests.cs` |
| `ING-004` | MERGED | [#84](https://github.com/matty/terraform-registry/pull/84) | `339b0c79430c05dc3d1d3b105b803537d9957c5d` | `scripts/remediation/gates/bounded-ingestion-security.sh` | `TerraformRegistry.Tests/UnitTests/RateLimitOptionsTests.cs` |
| `ING-005` | MERGED | [#96](https://github.com/matty/terraform-registry/pull/96) | `11e426b088918fa449875044353a86106a974f80` | `scripts/remediation/gates/publication-fault-gate.sh` | `TerraformRegistry.Tests/UnitTests/ModuleExtractionServiceTests.cs` |
| `ING-006` | MERGED | [#84](https://github.com/matty/terraform-registry/pull/84) | `339b0c79430c05dc3d1d3b105b803537d9957c5d` | `scripts/remediation/gates/bounded-ingestion-security.sh` | `TerraformRegistry.Tests/UnitTests/RateLimitOptionsTests.cs` |
| `STO-001` | MERGED | [#100](https://github.com/matty/terraform-registry/pull/100) | `6de4eef89f0e36259f2d08dfe256cb5de3299c0a` | `scripts/remediation/gates/publication-fault-gate.sh` | `TerraformRegistry.Tests/UnitTests/AzureBlob/AzureBlobModuleServiceUploadTests.cs` |
| `STO-002` | MERGED | [#100](https://github.com/matty/terraform-registry/pull/100) | `6de4eef89f0e36259f2d08dfe256cb5de3299c0a` | `scripts/remediation/gates/publication-fault-gate.sh` | `TerraformRegistry.Tests/UnitTests/AzureBlob/AzureBlobModuleServiceUploadTests.cs` |
| `STO-003` | MERGED | [#100](https://github.com/matty/terraform-registry/pull/100) | `6de4eef89f0e36259f2d08dfe256cb5de3299c0a` | `scripts/remediation/gates/publication-fault-gate.sh` | `TerraformRegistry.Tests/UnitTests/AzureBlob/AzureBlobModuleServiceUploadTests.cs` |
| `STO-004` | MERGED | [#100](https://github.com/matty/terraform-registry/pull/100) | `6de4eef89f0e36259f2d08dfe256cb5de3299c0a` | `scripts/remediation/gates/publication-fault-gate.sh` | `TerraformRegistry.Tests/UnitTests/AzureBlob/AzureBlobModuleServiceUploadTests.cs` |
| `STO-005` | MERGED | [#53](https://github.com/matty/terraform-registry/pull/53) | `1c5cd723516e7aabb87b5b8a03d8df836b2f9766` | `scripts/remediation/gates/phase-0.sh` | `TerraformRegistry.Tests/UnitTests/StorageInitializationHostedServiceTests.cs` |
| `STO-006` | MERGED | [#101](https://github.com/matty/terraform-registry/pull/101) | `3d0c6b4569287f32d8c35639f64336f35ac4e354` | `scripts/remediation/gates/fault-load-certification.sh` | `TerraformRegistry.Tests/UnitTests/StorageInitializationHostedServiceTests.cs` |
| `VCS-001` | MERGED | [#84](https://github.com/matty/terraform-registry/pull/84) | `339b0c79430c05dc3d1d3b105b803537d9957c5d` | `scripts/remediation/gates/bounded-ingestion-security.sh` | `TerraformRegistry.Tests/UnitTests/VcsWebhookHandlersTests.cs` |
| `VCS-002` | MERGED | [#84](https://github.com/matty/terraform-registry/pull/84) | `339b0c79430c05dc3d1d3b105b803537d9957c5d` | `scripts/remediation/gates/bounded-ingestion-security.sh` | `TerraformRegistry.Tests/UnitTests/VcsWebhookHandlersTests.cs` |
| `DEP-001` | MERGED | [#67](https://github.com/matty/terraform-registry/pull/67) | `aa61bc37f045a65f7f378759fb5fc15feb9126ac` | `scripts/remediation/gates/phase-1.sh` | `TerraformRegistry.Tests/UnitTests/AzureBlob/AzureBlobModuleServiceDelegationTests.cs` |
| `DEP-002` | MERGED | [#67](https://github.com/matty/terraform-registry/pull/67) | `aa61bc37f045a65f7f378759fb5fc15feb9126ac` | `scripts/remediation/gates/phase-1.sh` | `scripts/remediation/phase-1-local-terraform-smoke.sh` |
| `DEP-003` | MERGED | [#67](https://github.com/matty/terraform-registry/pull/67) | `aa61bc37f045a65f7f378759fb5fc15feb9126ac` | `scripts/remediation/gates/phase-1.sh` | `Dockerfile` |
| `IAM-001` | MERGED | [#67](https://github.com/matty/terraform-registry/pull/67) | `aa61bc37f045a65f7f378759fb5fc15feb9126ac` | `scripts/remediation/gates/phase-1.sh` | `TerraformRegistry.Tests/IntegrationTests/ApiKeySharingAuthorizationTests.cs` |
| `IAM-002` | MERGED | [#84](https://github.com/matty/terraform-registry/pull/84) | `339b0c79430c05dc3d1d3b105b803537d9957c5d` | `scripts/remediation/gates/bounded-ingestion-security.sh` | `TerraformRegistry.Tests/IntegrationTests/OidcSecurityTests.cs` |
| `IAM-003` | MERGED | [#84](https://github.com/matty/terraform-registry/pull/84) | `339b0c79430c05dc3d1d3b105b803537d9957c5d` | `scripts/remediation/gates/bounded-ingestion-security.sh` | `TerraformRegistry.Tests/UnitTests/NamespaceAuthorizationServiceTests.cs` |
| `IAM-004` | MERGED | [#84](https://github.com/matty/terraform-registry/pull/84) | `339b0c79430c05dc3d1d3b105b803537d9957c5d` | `scripts/remediation/gates/bounded-ingestion-security.sh` | `TerraformRegistry.Tests/UnitTests/NamespaceAuthorizationServiceTests.cs` |
| `IAM-005` | MERGED | [#67](https://github.com/matty/terraform-registry/pull/67) | `aa61bc37f045a65f7f378759fb5fc15feb9126ac` | `scripts/remediation/gates/phase-1.sh` | `TerraformRegistry.Tests/IntegrationTests/ApiKeySharingAuthorizationTests.cs` |
| `IAM-006` | MERGED | [#84](https://github.com/matty/terraform-registry/pull/84) | `339b0c79430c05dc3d1d3b105b803537d9957c5d` | `scripts/remediation/gates/bounded-ingestion-security.sh` | `TerraformRegistry.Tests/UnitTests/ApiKeyServiceSecurityTests.cs` |
| `IAM-007` | MERGED | [#84](https://github.com/matty/terraform-registry/pull/84) | `339b0c79430c05dc3d1d3b105b803537d9957c5d` | `scripts/remediation/gates/bounded-ingestion-security.sh` | `TerraformRegistry.Tests/UnitTests/RateLimitOptionsTests.cs` |
| `IAM-008` | MERGED | [#84](https://github.com/matty/terraform-registry/pull/84) | `339b0c79430c05dc3d1d3b105b803537d9957c5d` | `scripts/remediation/gates/bounded-ingestion-security.sh` | `TerraformRegistry.Tests/UnitTests/ArtifactDownloadTokenServiceTests.cs` |
| `IAM-009` | MERGED | [#67](https://github.com/matty/terraform-registry/pull/67) | `aa61bc37f045a65f7f378759fb5fc15feb9126ac` | `scripts/remediation/gates/phase-1.sh` | `TerraformRegistry.Tests/IntegrationTests/SecurityStartupTests.cs` |
| `IAM-010` | MERGED | [#109](https://github.com/matty/terraform-registry/pull/109) | `93d82d47739b5d3212811d5385f9f340f7248257` | `scripts/remediation/gates/operability-certification.sh` | `TerraformRegistry.Tests/IntegrationTests/OidcSecurityTests.cs` |
| `IAM-011` | MERGED | [#67](https://github.com/matty/terraform-registry/pull/67) | `aa61bc37f045a65f7f378759fb5fc15feb9126ac` | `scripts/remediation/gates/phase-1.sh` | `TerraformRegistry.Tests/IntegrationTests/ApiKeySharingAuthorizationTests.cs` |
| `IAM-012` | MERGED | [#84](https://github.com/matty/terraform-registry/pull/84) | `339b0c79430c05dc3d1d3b105b803537d9957c5d` | `scripts/remediation/gates/bounded-ingestion-security.sh` | `TerraformRegistry.Tests/IntegrationTests/OidcSecurityTests.cs` |
| `IAM-013` | MERGED | [#84](https://github.com/matty/terraform-registry/pull/84) | `339b0c79430c05dc3d1d3b105b803537d9957c5d` | `scripts/remediation/gates/bounded-ingestion-security.sh` | `TerraformRegistry.Tests/IntegrationTests/TerraformLoginAuthorizationTests.cs` |
| `MIR-001` | MERGED | [#91](https://github.com/matty/terraform-registry/pull/91) | `f1fb5ce88c0da7aac6b67cb7bf867fd9dd5ac963` | `scripts/remediation/gates/mirror-containment-gate.sh` | `TerraformRegistry.Tests/UnitTests/MirrorPolicyServiceTests.cs` |
| `MIR-002` | MERGED | [#91](https://github.com/matty/terraform-registry/pull/91) | `f1fb5ce88c0da7aac6b67cb7bf867fd9dd5ac963` | `scripts/remediation/gates/mirror-containment-gate.sh` | `TerraformRegistry.Tests/UnitTests/MirrorPolicyServiceTests.cs` |
| `MIR-003` | MERGED | [#91](https://github.com/matty/terraform-registry/pull/91) | `f1fb5ce88c0da7aac6b67cb7bf867fd9dd5ac963` | `scripts/remediation/gates/mirror-containment-gate.sh` | `TerraformRegistry.Tests/UnitTests/MirrorPolicyServiceTests.cs` |
| `MIR-004` | MERGED | [#91](https://github.com/matty/terraform-registry/pull/91) | `f1fb5ce88c0da7aac6b67cb7bf867fd9dd5ac963` | `scripts/remediation/gates/mirror-containment-gate.sh` | `TerraformRegistry.Tests/UnitTests/MirrorPolicyServiceTests.cs` |
| `MIR-005` | MERGED | [#91](https://github.com/matty/terraform-registry/pull/91) | `f1fb5ce88c0da7aac6b67cb7bf867fd9dd5ac963` | `scripts/remediation/gates/mirror-containment-gate.sh` | `TerraformRegistry.Tests/UnitTests/MirrorPolicyServiceTests.cs` |
| `MIR-006` | MERGED | [#91](https://github.com/matty/terraform-registry/pull/91) | `f1fb5ce88c0da7aac6b67cb7bf867fd9dd5ac963` | `scripts/remediation/gates/mirror-containment-gate.sh` | `TerraformRegistry.Tests/UnitTests/MirrorPolicyServiceTests.cs` |
| `MIR-007` | MERGED | [#91](https://github.com/matty/terraform-registry/pull/91) | `f1fb5ce88c0da7aac6b67cb7bf867fd9dd5ac963` | `scripts/remediation/gates/mirror-containment-gate.sh` | `TerraformRegistry.Tests/UnitTests/ProviderMirrorServiceTests.cs` |
| `MIR-008` | MERGED | [#122](https://github.com/matty/terraform-registry/pull/122) | `c2d82ffbe88e799bf76c99b66b154ce6f386694b` | `scripts/remediation/gates/mirror-containment-gate.sh` | `TerraformRegistry.Tests/UnitTests/ProviderPackageValidatorTests.cs` |
| `MIR-009` | MERGED | [#103](https://github.com/matty/terraform-registry/pull/103) | `24fe66efc0593e39d8b4ae876c9527506d0216a9` | `scripts/remediation/gates/fault-load-certification.sh` | `TerraformRegistry.Tests/UnitTests/MirrorAdminHandlersTests.cs` |
| `PERF-001` | MERGED | [#93](https://github.com/matty/terraform-registry/pull/93) | `b84ebeccf4a7e54ff68402b73879784bdc397cca` | `scripts/remediation/gates/mirror-containment-gate.sh` | `TerraformRegistry.Tests/MigrationAcceptanceMatrixTests.cs` |
| `PERF-002` | MERGED | [#84](https://github.com/matty/terraform-registry/pull/84) | `339b0c79430c05dc3d1d3b105b803537d9957c5d` | `scripts/remediation/gates/bounded-ingestion-security.sh` | `TerraformRegistry.Tests/UnitTests/AzureBlob/AzureBlobProviderArtifactStorageTests.cs` |
| `PERF-003` | MERGED | [#102](https://github.com/matty/terraform-registry/pull/102) | `6e55a17db0a5d98fe5e18a23639ad10604473f48` | `scripts/remediation/gates/fault-load-certification.sh` | `TerraformRegistry.Tests/UnitTests/ProviderPackageValidatorTests.cs` |
| `PERF-004` | MERGED | [#108](https://github.com/matty/terraform-registry/pull/108) | `488d4630287de81c93a0d1982c4c6501ef0588d5` | `scripts/remediation/gates/operability-certification.sh` | `TerraformRegistry.Tests/UnitTests/LlmHandlersCancellationTests.cs` |
| `REL-001` | MERGED | [#104](https://github.com/matty/terraform-registry/pull/104) | `338b05617aa691b94e7fa1d5aa4073f81e2faccc` | `scripts/remediation/gates/operability-certification.sh` | `TerraformRegistry.Tests/UnitTests/DurableOutboxProcessorTests.cs` |
| `REL-002` | MERGED | [#104](https://github.com/matty/terraform-registry/pull/104) | `338b05617aa691b94e7fa1d5aa4073f81e2faccc` | `scripts/remediation/gates/operability-certification.sh` | `TerraformRegistry.Tests/UnitTests/ModuleDownloadAnalyticsQueueTests.cs` |
| `REL-003` | MERGED | [#105](https://github.com/matty/terraform-registry/pull/105) | `3f83a4295f2ce649a6bf275f4896297c8d22b331` | `scripts/remediation/gates/operability-certification.sh` | `TerraformRegistry.Tests/IntegrationTests/HttpDeliveryPolicyTests.cs` |
| `REL-004` | MERGED | [#106](https://github.com/matty/terraform-registry/pull/106) | `2ace3de16dc98b759757810760c2fdde40141a08` | `scripts/remediation/gates/operability-certification.sh` | `TerraformRegistry.Tests/IntegrationTests/BrowserSecurityHeaderTests.cs` |
| `SUP-001` | MERGED | [#45](https://github.com/matty/terraform-registry/pull/45) | `7b4deebf0dc3f349110b7bfac53fa522a0478bc8` | `scripts/remediation/gates/test-supply-chain-pinning.sh` | `global.json` |
| `SUP-002` | MERGED | [#110](https://github.com/matty/terraform-registry/pull/110) | `b4fdd701e4df739ea09868a99bf8c29e0d83bd69` | `scripts/remediation/gates/test-supply-chain-pinning.sh` | `Dockerfile` |
| `SUP-003` | MERGED | [#110](https://github.com/matty/terraform-registry/pull/110) | `b4fdd701e4df739ea09868a99bf8c29e0d83bd69` | `scripts/remediation/gates/test-supply-chain-pinning.sh` | `TerraformRegistry/web-src/package.json` |

## Current release candidate

Final certification is **pending** while the candidate evidence uses `REQUIRED`.
The required fields are declared, rather than fabricated, in
[`release-candidate-evidence.md`](release-candidate-evidence.md): image digest,
source revision, verification-run URL, Terraform backend matrix, fault/load, and
operability results. The validator rejects absent fields and rejects a mixture of
pending and completed values.
