# Release operations runbook

This runbook is the operating procedure for a Terraform Registry release. It
uses only interfaces that are present in this repository. Deployment-platform
actions (traffic shifting, secret-manager updates, database snapshots, and
alert routing) remain the responsibility of the platform that runs the image.
Do not substitute mutable image tags for an immutable digest.

## Required release record

Create a change record before touching production. Record the operator, UTC
start/end time, Git SHA, old and proposed image digests, CalVer version, target
database/storage provider, change approval, backup identifier/retention,
readiness output, and rollback decision. Keep credentials out of that record.

The CI workflow publishes `ghcr.io/<owner>/terraform-registry` only for
`main`, `develop`, `release/**`, and `hotfix/**` pushes (or an explicitly
requested dispatch). `main` gets a CalVer tag; see [the release-version
policy](../README.md#release-versioning). Resolve the digest from the registry
after its CI publication, then record it before rollout:

```bash
: "${IMAGE:=ghcr.io/<owner>/terraform-registry}"
: "${VERSION:?CalVer version produced by CI}"
docker buildx imagetools inspect "$IMAGE:$VERSION"
# Copy the reported sha256 digest into the change record.
```

The image is designed to run as the non-root `app` user. Retain the existing
writable mounts for `/app/modules`, `/app/providers`, and `/data` when using
local storage; the Phase 1 deployment gate verifies those paths.

## Pre-rollout verification

1. Confirm CI has passed for the exact commit and examine required-check and
   review status before approving the change.
2. Run the reproducibility gate from the release checkout:

   ```bash
   bash scripts/remediation/gates/supply-chain-pinning.sh
   ```

3. Verify the candidate digest locally when Docker is available. The marker is
   an image-build assertion, not a deployment-platform health check:

   ```bash
   marker="release-$(git rev-parse --short HEAD)"
   docker build --build-arg "FRONTEND_BUILD_MARKER=$marker" -t terraform-registry:release-check .
   test "$(docker image inspect --format '{{.Config.User}}' terraform-registry:release-check)" = app
   test "$(docker run --rm --entrypoint cat terraform-registry:release-check /app/web/.build-marker)" = "$marker"
   docker run --rm --entrypoint /bin/sh terraform-registry:release-check \
     -c 'test -w /app/modules && test -w /app/providers && test -w /data'
   ```

4. Use the repository's portable storage contract before changing a cloud
   deployment. It starts disposable Azurite and MinIO and requires no cloud
   credentials:

   ```bash
   bash scripts/remediation/phase-1-storage-emulator-terraform-smoke.sh --provider all
   ```

5. For a real Azure managed-identity change, run the protected-environment
   gate; an emulator cannot issue a user-delegation key:

   ```bash
   TF_REGISTRY_REQUIRE_REAL_AZURE=1 \
     bash scripts/remediation/gates/phase-1-real-azure-user-delegation-sas.sh
   ```

## Backup, restore, and migration state

Every schema rollout requires a verified backup and a disposable restore of
the exact candidate binary. Follow the detailed [migration recovery
runbook](phase-0-migration-recovery-runbook.md); it contains the PostgreSQL
`pg_dump`/`pg_restore` and SQLite online-backup commands, row-count and journal
comparisons, foreign-key check, timing/lock evidence, and unsafe-state path.

Before rollout, capture `SchemaVersions` and classify it. Stop if the journal
is absent, non-contiguous, contains an unknown script, or a journaled migration
does not have its required schema shape. Never delete journal rows, renumber an
embedded migration, or make an unreviewed production schema edit.

Database changes are roll-forward only. If the candidate fails migration or
readiness, remove it from traffic, preserve logs/digests/journal and take a new
backup, restore the last known-good backup into a disposable target, validate
the reviewed repair binary there, then recover through the platform change
process. Application-only changes may instead revert the release commit and
deploy the prior immutable image digest.

## Digest rollout and rollback

1. Put the old and new immutable digests in the change record. Do not deploy
   `latest`, `develop`, or `release` as a rollback target.
2. Apply the new digest through the deployment platform while retaining the
   prior digest and its configuration revision for rollback.
3. Wait for the platform to report the new instances healthy, then probe the
   registry from the production network:

   ```bash
   : "${REGISTRY_URL:?for example https://registry.example.com}"
   curl --fail --silent --show-error "$REGISTRY_URL/health"
   curl --fail --silent --show-error "$REGISTRY_URL/ready"
   # Component reasons are deliberately protected by normal registry auth.
   curl --fail --silent --show-error \
     -H "Authorization: Bearer $TF_REG_AUTHORIZATIONTOKEN" \
     "$REGISTRY_URL/ready?detail=true"
   ```

4. Verify a read of an existing module and provider with an appropriately
   scoped client. For the configured storage backend, verify download success
   and confirm no unexpected `5xx` responses in platform logs.
5. If this is an application-only release and verification fails, shift traffic
   back to the recorded prior digest, re-run `/health` and `/ready`, and record
   the rollback. For a database release, keep traffic removed and use the
   roll-forward recovery procedure above; do not deploy an old binary against a
   newer schema unless its declared compatibility window permits it.

## Signing and secret rotation

The runtime currently takes one active key for each of these settings:

| Purpose | Configuration setting | Operational consequence |
| --- | --- | --- |
| Portal sessions | `TF_REG_OIDC__JWTSECRETKEY` | Existing portal sessions signed by the old key stop validating after a restart. |
| API-key HMAC digests | `TF_REG_APIKEYSECURITY__DIGESTKEY` | Existing `v1:` API-key digests cannot be verified after a simple key replacement. Reissue/revoke affected API keys as an approved migration; do not rotate this setting as an emergency toggle. |
| Artifact download tokens | `TF_REG_ARTIFACTDOWNLOADTOKENS__SIGNINGKEY` | Previously issued local artifact download links stop validating after a restart; wait for their configured expiry before assuming no clients hold them. |
| Mirror package URLs | `TF_REG_MIRROR__PACKAGEURLSIGNINGKEY` (or OIDC JWT fallback) | Previously issued mirror URLs stop validating; coordinate the same expiry window. |

Provider package signing is different: provider versions reference an uploaded
GPG `KeyId`. Upload and verify the new public key, publish new provider versions
with that key, retain old public keys while clients need old versions, and only
remove a key after the compatibility window and audit review.

Because the current configuration has no active/previous-key set, it does not
implement transparent overlap for the four runtime secrets above. Schedule a
maintenance window, record the expiry/reissue impact, update the secret through
the platform, restart to load it, verify `/ready`, and test a newly issued
session/link/key. Do not claim dual-key verification until the application adds
it and its tests pass.

## Extraction jobs and mirror cache

Use a normal authenticated operator session with the relevant `module_docs.*`
or `mirror.*` permission; the static authorization token only exposes protected
readiness detail and is not a substitute for role-based administration.

Inspect extraction configuration and queue state before intervention:

```bash
: "${REGISTRY_URL:?}"
: "${OPERATOR_BEARER_TOKEN:?role-based operator token}"
auth=(-H "Authorization: Bearer $OPERATOR_BEARER_TOKEN")
curl --fail --silent --show-error "${auth[@]}" "$REGISTRY_URL/api/admin/module-docs/summary"
curl --fail --silent --show-error "${auth[@]}" \
  "$REGISTRY_URL/api/admin/module-docs/modules?status=failed&limit=50&offset=0"
```

For one known module version, requeue its extraction; this is auditable and
returns `202 Accepted` when queued:

```bash
curl --fail --silent --show-error -X POST "${auth[@]}" \
  "$REGISTRY_URL/api/admin/module-docs/modules/<namespace>/<name>/<provider>/<version>/requeue"
```

Do not repeatedly requeue a failing job without preserving its error and
checking the archive/tool configuration. The documented operational defaults
are a 15-second extraction timeout, a 1,000 pending-job maximum, a 60-second
lease, three retries, and a 500-ms poll interval; deployment configuration can
override these values. Use the module-docs summary and module detail endpoint
to confirm the job reaches its expected terminal state.

Inspect mirror cache and live leases before purge:

```bash
curl --fail --silent --show-error "${auth[@]}" \
  "$REGISTRY_URL/api/admin/mirror/config"
curl --fail --silent --show-error "${auth[@]}" \
  "$REGISTRY_URL/api/admin/mirror/providers?limit=50&offset=0"
curl --fail --silent --show-error "${auth[@]}" \
  "$REGISTRY_URL/api/admin/mirror/modules?limit=50&offset=0"
curl --fail --silent --show-error "${auth[@]}" \
  "$REGISTRY_URL/api/admin/mirror/leases?limit=50&offset=0"
```

Purge one coordinate only after confirming it has no live lease. A purge returns
`204 No Content`, `404` when absent, or `409` when in use; treat `409` as a
successful safety refusal and retry only after the lease ends. Example provider
purge:

```bash
curl --fail --silent --show-error -X DELETE "${auth[@]}" \
  "$REGISTRY_URL/api/admin/mirror/providers/<hostname>/<namespace>/<type>/<version>/<os>/<arch>"
```

Cache configuration changes use `PUT /api/admin/mirror/config`; capture the
current JSON first, make the smallest reviewed change, apply it with a
`mirror.configure` principal, then verify it with the GET endpoint and audit
log. Do not bulk-delete storage objects outside these APIs: cache accounting,
lease protection, and audit records would be bypassed.

An `admin.audit` principal can retrieve the audit record for an operational
change without inspecting the database directly:

```bash
curl --fail --silent --show-error "${auth[@]}" \
  "$REGISTRY_URL/api/admin/audit?action=mirror.config_updated&limit=50&offset=0"
```

## Alerts and incident response

The application provides `/health` (liveness) and `/ready` (database, startup,
module storage, and provider-storage readiness). It does not ship a Prometheus
endpoint, alert rules, a pager integration, or a deployment-controller API.
Configure those in the hosting platform, and base an availability alert on
failed `/ready` probes rather than `/health` alone.

Route application logs to the platform's retained log service and alert on:

- sustained `5xx` or readiness failures;
- startup/migration failure or a non-ready storage-initialization state;
- repeated extraction failures, queue saturation, or dead-letter growth;
- mirror lease loss, cache-budget refusal, repeated upstream failures, and
  unexpected cache purge failures;
- authorization failures or unexpected token/session validation errors;
- backup, restore, or rollout failures.

When an alert fires, record the affected digest/configuration revision and
timestamps, check `/ready?detail=true` with normal authentication, inspect the
relevant audited admin state, and preserve logs before changing traffic or
configuration. Escalate database unsafe-state alerts to the migration recovery
procedure rather than attempting an in-place repair.

## Compatibility windows

- A database change must use expand/dual-read/backfill/contract and retain old
  read compatibility for at least one deployed application version. Contract
  cleanup occurs only after that window and backup evidence.
- Old provider versions retain their referenced GPG public key until clients no
  longer need them. New provider versions must name their signing key.
- Artifact replacement or archive migration must not silently rewrite user
  artifacts; record a checksum transition and preserve the archive-format
  contract.
- The Azure user-delegation SAS contract needs a real protected Azure
  environment; Azurite proves only the portable Blob contract. The MinIO
  harness proves S3-compatible behavior, not an arbitrary cloud provider.
- Before changing any runtime signing secret, account for the explicit session,
  URL, and API-key limitations in the rotation section. There is no implicit
  old-key acceptance window for the single-key settings.

## Closure checklist

- [ ] Exact old/new immutable digests, SHA, CalVer version, approval, and UTC
      timing are attached to the release record.
- [ ] Backup, disposable restore, migration journal, and readiness evidence are
      attached for every schema change.
- [ ] `/health`, `/ready`, authenticated readiness detail, and representative
      module/provider reads succeeded on the deployed digest.
- [ ] Job/cache operations, secret changes, and platform alerts were either not
      needed or were recorded with their verification and rollback result.
- [ ] The compatibility window and rollback/roll-forward decision are recorded.
