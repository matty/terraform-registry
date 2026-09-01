# Nuxt 4 Security Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore green npm and NuGet security gates by migrating the SPA to a supported Nuxt 4/Nuxt UI 4 pairing and updating Testcontainers without changing application behavior.

**Architecture:** Keep the frontend as a client-rendered, statically generated SPA with its existing root-level source layout. Force Nuxt 4 to use that layout explicitly, retain npm as the only package manager, and keep the independent .NET test dependency update in its own commit.

**Tech Stack:** Nuxt 4.5.2, Nuxt UI 4.11.0, Node 24/npm 11, Vue 3, ASP.NET Core/.NET 10, xUnit, Testcontainers.PostgreSql 4.14.0, Bash verification gates.

**Spec:** `docs/superpowers/specs/2026-09-01-nuxt-4-security-migration-design.md`

## Global Constraints

- Preserve every current route, the `ssr: false` SPA model, styling, runtime configuration, and user-visible behavior.
- Keep the root-level frontend layout; do not move `pages/`, `components/`, `composables/`, `layouts/`, `middleware/`, `assets/`, or `app.vue` into a new directory.
- Use Node 24/npm 11 for dependency resolution because host npm 10.9.8 crashes in Arborist while replacing the Nuxt peer graph.
- Keep only `package.json` and `package-lock.json` as frontend package-manager inputs; remove `pnpm-lock.yaml` and `pnpm-workspace.yaml`.
- Do not suppress npm/NuGet audits, force an unsupported module pairing, add compatibility exceptions, redesign UI, enable SSR, or upgrade unrelated direct dependencies.
- Resolve Testcontainers through the dotnet CLI; do not hand-edit project XML.
- Treat a `NUXT_B8013` module-compatibility warning as a failure even if Nuxt preparation exits zero.

---

### Task 1: Remove the SSH.NET advisory through Testcontainers

**Files:**
- Modify: `TerraformRegistry.Tests/TerraformRegistry.Tests.csproj`

**Interfaces:**
- Consumes: Existing xUnit PostgreSQL fixtures using `Testcontainers.PostgreSql`.
- Produces: Testcontainers.PostgreSql 4.14.0 resolving SSH.NET 2026.0.0 with no NU1903/NU1904 audit failure.

- [ ] **Step 1: Confirm the recorded RED audit on the base dependency graph**

The baseline failure already reproduced on `origin/develop` is:

```text
TerraformRegistry.Tests.csproj : error NU1903: Package 'SSH.NET' 2025.1.0 has a known high severity vulnerability
Testcontainers.PostgreSql 4.13.0 -> Testcontainers 4.13.0 -> SSH.NET 2025.1.0
```

Verify the base project reference without changing branches:

```bash
git show origin/develop:TerraformRegistry.Tests/TerraformRegistry.Tests.csproj \
  | grep 'Testcontainers.PostgreSql.*4.13.0'
```

Expected: one matching PackageReference.

- [ ] **Step 2: Apply the minimal package update through the .NET CLI**

Run with the repository-compatible SDK container:

```bash
docker run --rm --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -e NUGET_PACKAGES=/tmp/nuget \
  -v "$PWD:/src" -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0.301 \
  dotnet add TerraformRegistry.Tests/TerraformRegistry.Tests.csproj \
  package Testcontainers.PostgreSql --version 4.14.0 --no-restore
```

Expected: the CLI reports that the existing PackageReference was updated to 4.14.0.

- [ ] **Step 3: Verify the blocking NuGet audit is GREEN**

```bash
docker run --rm --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -e NUGET_PACKAGES=/tmp/nuget \
  -v "$PWD:/src" -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0.301 \
  dotnet restore \
  -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=high \
  '-warnaserror:NU1903;NU1904'
```

Expected: exit 0 and no SSH.NET audit finding.

- [ ] **Step 4: Verify the resolved transitive version**

```bash
docker run --rm --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -e NUGET_PACKAGES=/tmp/nuget \
  -v "$PWD:/src" -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0.301 \
  dotnet list TerraformRegistry.Tests/TerraformRegistry.Tests.csproj package --include-transitive \
  | grep -E 'Testcontainers.PostgreSql|SSH.NET'
```

Expected: Testcontainers.PostgreSql 4.14.0 and SSH.NET 2026.0.0.

- [ ] **Step 5: Run Slopwatch against the modified project file**

```bash
slopwatch analyze --no-baseline --fail-on warning \
  -f TerraformRegistry.Tests/TerraformRegistry.Tests.csproj
```

Expected: no new issue. Inline package versions are the repository's existing package-management pattern; do not add a suppression or baseline entry.

- [ ] **Step 6: Commit the independent .NET security update**

```bash
git add TerraformRegistry.Tests/TerraformRegistry.Tests.csproj
git commit -m "fix(deps): update Testcontainers to remove SSH.NET advisory"
```

---

### Task 2: Upgrade the frontend to Nuxt 4 and Nuxt UI 4

**Files:**
- Modify: `TerraformRegistry/web-src/package.json`
- Modify: `TerraformRegistry/web-src/package-lock.json`
- Modify: `TerraformRegistry/web-src/nuxt.config.ts`
- Delete: `TerraformRegistry/web-src/pnpm-lock.yaml`
- Delete: `TerraformRegistry/web-src/pnpm-workspace.yaml`

**Interfaces:**
- Consumes: Existing root-level Nuxt SPA directories and the current 19-route static output.
- Produces: Nuxt 4.5.2 with Nuxt UI 4.11.0 enabled, using explicit Nuxt 3-compatible source-directory resolution.

- [ ] **Step 1: Preserve the RED compatibility evidence**

With Nuxt 3.21.11 and Nuxt UI 4.11.0, run:

```bash
docker run --rm --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -v "$PWD:/src" -w /src node:24 \
  bash -lc 'npm ci && npm run generate'
```

Expected: failure containing `NUXT_B8013`, `Nuxt version >=4.1.0 is required`, and missing `.nuxt/ui.css`. This proves the secure UI version cannot run on Nuxt 3.

- [ ] **Step 2: Resolve the supported package graph with CI's npm version**

From `TerraformRegistry/web-src`:

```bash
docker run --rm --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -v "$PWD:/src" -w /src node:24 \
  npm install --package-lock-only 'nuxt@^4.5.2' '@nuxt/ui@^4.11.0'
```

Expected: `package.json` declares `nuxt: ^4.5.2` and `@nuxt/ui: ^4.11.0`; npm reports no high or critical vulnerability.

- [ ] **Step 3: Keep the existing source tree explicitly**

Add these properties immediately inside `defineNuxtConfig` in `TerraformRegistry/web-src/nuxt.config.ts`:

```ts
export default defineNuxtConfig({
  srcDir: '.',
  dir: {
    app: 'app',
  },
```

Leave `ssr: false`, modules, CSS, runtime config, prerender routes, and Vite proxy behavior unchanged. This follows the official Nuxt 4 migration option for retaining a v3 directory layout.

- [ ] **Step 4: Remove pnpm inputs**

Delete exactly:

```text
TerraformRegistry/web-src/pnpm-lock.yaml
TerraformRegistry/web-src/pnpm-workspace.yaml
```

Do not remove `package-lock.json`.

- [ ] **Step 5: Clean-install and generate the SPA**

```bash
docker run --rm --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -v "$PWD:/src" -w /src node:24 \
  bash -lc 'npm ci && npm audit --audit-level=high && npm run generate 2>&1 | tee /tmp/nuxt-generate.log; ! grep -q NUXT_B8013 /tmp/nuxt-generate.log'
```

Expected: exit 0, Nuxt UI remains enabled, and generation emits the current 19 routes.

- [ ] **Step 6: Verify every current route exists in static output**

From `TerraformRegistry/web-src`:

```bash
for route in \
  index.html login/index.html callback/index.html settings/index.html \
  settings/trash/index.html settings/account/index.html settings/api-keys/index.html \
  analytics/index.html providers/index.html admin/audit/index.html \
  admin/mirror/index.html admin/webhooks/index.html admin/users/index.html \
  admin/vcs-connections/index.html admin/roles/index.html admin/module-docs/index.html; do
  test -f ".output/public/$route" || { echo "Missing generated route: $route" >&2; exit 1; }
done
```

Expected: exit 0. Dynamic module/provider routes are runtime client routes and are not expected as fixed prerendered paths.

- [ ] **Step 7: Inspect the migration-only diff**

```bash
git diff --check
git diff --stat -- \
  TerraformRegistry/web-src/package.json \
  TerraformRegistry/web-src/package-lock.json \
  TerraformRegistry/web-src/nuxt.config.ts \
  TerraformRegistry/web-src/pnpm-lock.yaml \
  TerraformRegistry/web-src/pnpm-workspace.yaml
```

Expected: only the two package manifests, Nuxt config, and two pnpm deletions.

- [ ] **Step 8: Commit the supported frontend package graph**

```bash
git add TerraformRegistry/web-src/package.json \
  TerraformRegistry/web-src/package-lock.json \
  TerraformRegistry/web-src/nuxt.config.ts \
  TerraformRegistry/web-src/pnpm-lock.yaml \
  TerraformRegistry/web-src/pnpm-workspace.yaml
git commit -m "fix(deps): migrate frontend to Nuxt 4"
```

---

### Task 3: Retire the Nuxt UI exception and enforce npm-only inputs

**Files:**
- Modify: `scripts/verification/gates/supply-chain-pinning.sh`
- Modify: `scripts/verification/gates/test-supply-chain-pinning.sh`
- Delete: `docs/security-exceptions/SUP-003-nuxt-ui.md`

**Interfaces:**
- Consumes: Secure Nuxt 4/UI 4 package manifest and npm lockfile from Task 2.
- Produces: A portable Bash gate that rejects pnpm inputs and no longer carries a vulnerability exception for fixed software.

- [ ] **Step 1: Add RED fixture tests for npm-only package inputs**

In `test-supply-chain-pinning.sh`, copy the real npm manifests into the fixture:

```bash
cp "$ROOT/TerraformRegistry/web-src/package.json" \
  "$ROOT/TerraformRegistry/web-src/package-lock.json" \
  "$fixture_root/TerraformRegistry/web-src/"
```

Remove the SUP-003 fixture copy and the `UAuthForm`/`UForm` reachability fixture cases. Add:

```bash
touch "$fixture_root/TerraformRegistry/web-src/pnpm-lock.yaml"
expect_failure 'a pnpm lockfile in the npm-only frontend'
rm "$fixture_root/TerraformRegistry/web-src/pnpm-lock.yaml"

touch "$fixture_root/TerraformRegistry/web-src/pnpm-workspace.yaml"
expect_failure 'a pnpm workspace file in the npm-only frontend'
rm "$fixture_root/TerraformRegistry/web-src/pnpm-workspace.yaml"

sed -i 's/"nuxt": "[^"]*"/"nuxt": "^3.21.11"/' \
  "$fixture_root/TerraformRegistry/web-src/package.json"
expect_failure 'a Nuxt 3 frontend manifest'
```

Run:

```bash
bash scripts/verification/gates/test-supply-chain-pinning.sh
```

Expected RED: the new pnpm case unexpectedly succeeds because the production gate does not enforce npm-only inputs yet.

- [ ] **Step 2: Replace the expired exception logic with package-input policy**

In `supply-chain-pinning.sh`, replace `EXCEPTION` and the entire `contains_affected_nuxt_form`/owner/expiry block with:

```bash
FRONTEND_DIR="$ROOT/TerraformRegistry/web-src"
PACKAGE_MANIFEST="$FRONTEND_DIR/package.json"
PACKAGE_LOCK="$FRONTEND_DIR/package-lock.json"

test -f "$PACKAGE_MANIFEST"
test -f "$PACKAGE_LOCK"

for pnpm_input in "$FRONTEND_DIR/pnpm-lock.yaml" "$FRONTEND_DIR/pnpm-workspace.yaml"; do
  if [[ -e "$pnpm_input" ]]; then
    echo "npm-only frontend contains pnpm input: $pnpm_input" >&2
    exit 1
  fi
done

grep -Eq '"nuxt"[[:space:]]*:[[:space:]]*"\^4\.' "$PACKAGE_MANIFEST"
grep -Eq '"@nuxt/ui"[[:space:]]*:[[:space:]]*"\^4\.11\.' "$PACKAGE_MANIFEST"
```

Keep the Docker, Compose, Terraform, and GitHub Action pinning logic unchanged.

- [ ] **Step 3: Delete the resolved security exception**

Delete:

```text
docs/security-exceptions/SUP-003-nuxt-ui.md
```

- [ ] **Step 4: Run the portable gate tests GREEN**

```bash
bash scripts/verification/gates/supply-chain-pinning.sh
bash scripts/verification/gates/test-supply-chain-pinning.sh
```

Expected: both exit 0; negative fixture messages are expected during `test-supply-chain-pinning.sh`, but its final exit must be zero.

- [ ] **Step 5: Commit policy and exception retirement**

```bash
git add scripts/verification/gates/supply-chain-pinning.sh \
  scripts/verification/gates/test-supply-chain-pinning.sh \
  docs/security-exceptions/SUP-003-nuxt-ui.md
git commit -m "chore: retire Nuxt UI security exception"
```

---

### Task 4: Run repository-wide verification and publish the replacement PR

**Files:**
- Verify only; no expected source modifications.

**Interfaces:**
- Consumes: Tasks 1-3 as separate linear commits.
- Produces: A reviewable pull request against `develop` that supersedes #147 and #148.

- [ ] **Step 1: Run the complete frontend CI sequence**

From `TerraformRegistry/web-src`:

```bash
docker run --rm --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -v "$PWD:/src" -w /src node:24 \
  bash -lc 'npm ci && npm audit --audit-level=high && npm run generate 2>&1 | tee /tmp/nuxt-generate.log; ! grep -q NUXT_B8013 /tmp/nuxt-generate.log'
```

Expected: exit 0 with no high/critical audit finding or compatibility warning.

- [ ] **Step 2: Run the repository contract gates**

From the repository root:

```bash
bash scripts/verification/gates/release-runbooks.sh
bash scripts/verification/gates/test-release-runbooks-gate.sh
bash scripts/verification/gates/test-supply-chain-pinning.sh
bash scripts/verification/test-terraform-backend-matrix.sh
```

Expected: exit 0.

- [ ] **Step 3: Run audited restore and Release build**

```bash
docker run --rm --user "$(id -u):$(id -g)" \
  -e HOME=/tmp -e NUGET_PACKAGES=/tmp/nuget \
  -v "$PWD:/src" -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0.301 bash -lc \
  "dotnet restore -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=high '-warnaserror:NU1903;NU1904' && dotnet build --no-restore --configuration Release"
```

Expected: exit 0 with no NU1903/NU1904 finding.

- [ ] **Step 4: Run the complete .NET suite with real dependencies**

```bash
docker run --rm \
  --group-add "$(stat -c '%g' /var/run/docker.sock)" \
  -e HOME=/tmp -e NUGET_PACKAGES=/tmp/nuget \
  -e ASPNETCORE_ENVIRONMENT=Test -e TESTCONTAINERS_RYUK_DISABLED=true \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -v "$PWD:/src" -w /src \
  mcr.microsoft.com/dotnet/sdk:10.0.301 bash -lc \
  "apt-get update -qq && apt-get install -y -qq gnupg >/dev/null && dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --configuration Release --logger 'console;verbosity=minimal'"
```

Expected: 785 passed and 0 failed on the current `origin/develop`. If PR #151 merges first, rebase this branch and expect 786 passed and 0 failed.

- [ ] **Step 5: Run final hygiene checks**

```bash
git diff --check origin/develop...HEAD
git log --merges origin/develop..HEAD
git status --short --branch
```

Expected: no whitespace errors, no merge commits, and a clean worktree.

- [ ] **Step 6: Request code review**

Review `origin/develop..HEAD` against this plan and the design spec. Fix every Critical or Important finding and rerun the affected verification command before publishing.

- [ ] **Step 7: Push and open the replacement pull request**

```bash
git push -u origin fix/dependency-security-baseline
gh pr create \
  --base develop \
  --head fix/dependency-security-baseline \
  --title "fix(deps): restore dependency security baseline" \
  --body "Migrates the SPA to supported Nuxt 4 and Nuxt UI 4 releases, removes pnpm inputs and the resolved SUP-003 exception, and updates Testcontainers to remove the SSH.NET advisory. Supersedes #147 and #148."
```

- [ ] **Step 8: Wait for required GitHub checks before merge**

```bash
gh pr checks --watch
```

Expected: all required checks pass. Do not merge while any required check is failing, pending indefinitely, or missing.
