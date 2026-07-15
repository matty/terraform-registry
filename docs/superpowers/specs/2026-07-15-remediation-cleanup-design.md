# Remediation Cleanup Design

## Purpose

Retire the completed remediation campaign without removing the ongoing CI and
release safeguards it introduced. The remaining automation will use a neutral
`scripts/verification/` namespace that reflects its continuing purpose.

## Retained verification

Move these durable checks from `scripts/remediation/` to
`scripts/verification/` and update every in-repository caller:

- supply-chain input pinning;
- release-runbook contract checks;
- storage-emulator lifecycle and Terraform smoke checks;
- the Terraform backend matrix; and
- pre-publication final-candidate certification.

The corresponding always-on or release-candidate CI jobs retain their current
behavior. The release operations and build-input documentation remains, with
paths and wording updated to use the new namespace.

## Retired campaign material

Remove one-off audit gates, their contract scripts, and workflows that only
run for named temporary branches. Remove remediation-only branch filters from
CI and security workflows. Remove the CI/security bootstrap document because
it describes the concluded remediation branching process.

This includes phase 0 and phase 1 deployment gates, bounded-ingestion,
mirror-containment, publication-fault, fault/load, and operability
certifications, plus the real-Azure phase 1 workflow.

## Verification

Use repository searches to prove no `scripts/remediation/` paths or removed
job names remain. Run shell syntax checks for retained scripts and focused
contract checks that do not need Docker, Terraform, cloud credentials, or a
.NET SDK. Validate the workflow YAML using Ruby's built-in YAML parser when
available.

The full .NET test suite is not runnable in this environment because the
repository requires SDK 10.0.301 while only 10.0.109 is installed.
