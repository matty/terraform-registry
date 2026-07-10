# Phase-gate commands

Each `phase-*.sh` script fails closed unless its corresponding environment variable
is set. Set the variable to the phase's acceptance command only after that command
exists in version control and covers the gate listed in the delivery plan.

| Phase | Variable | Required acceptance scope |
|---:|---|---|
| 0 | `REMEDIATION_PHASE_0_GATE` | Fresh/populated/journaled/interrupted migration matrix, checked-in disposable SQLite backup/restore evidence check, and SQLite FK checks. PostgreSQL restore and production evidence are operator-recorded in `docs/phase-0-migration-recovery-runbook.md`. |
| 1 | `REMEDIATION_PHASE_1_GATE` | Terraform Local/Azure/S3 installs, protocol, authorization, SemVer, and non-root image checks. |
| 2 | `REMEDIATION_PHASE_2_GATE` | Ingestion limits, identity lifecycle, API keys/tokens, cancellation, and rate-limit saturation. |
| 2b | `REMEDIATION_PHASE_2_B_GATE` | Mirror contention/limits/leases/signatures plus 100k-version SQL evidence. |
| 3 | `REMEDIATION_PHASE_3_GATE` | Publication race and injected-failure matrix, durable job crash/retry/dead-letter tests. |
| 4 | `REMEDIATION_PHASE_4_GATE` | Operator authorization, query budget, and asynchronous reconciliation responsiveness. |
| 5 | `REMEDIATION_PHASE_5_GATE` | Outbox durability, cancellation propagation, policy/scan and redaction checks. |
| 6 | `REMEDIATION_PHASE_6_GATE` | Full Terraform backend and fault/load certification plus runbook review. |

For example, once a checked-in Phase 0 matrix exists:

```bash
REMEDIATION_PHASE_0_GATE='dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --no-build --configuration Release --filter FullyQualifiedName~MigrationMatrix' \
  scripts/remediation/remediation.sh verify 0
```
