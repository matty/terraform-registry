# Quick Wins Feature Pack: Design Spec

**Date:** 2026-03-16
**Branch:** `develop`
**Features:** API Key Expiration, Health/Readiness, Download Analytics, Webhooks

---

## 1. API Key Expiration Enforcement

### Problem
The `expires_at` column exists on the `api_keys` table but is never checked during authentication. Expired keys remain valid indefinitely.

### Design

**Change `ValidateApiKeyAsync` return type** from `ApiKey?` to a result record:

```csharp
public record ApiKeyValidationResult(ApiKey? Key, bool IsExpired);
```

**Validation flow in `ApiKeyService.ValidateApiKeyAsync`:**
1. Existing logic: look up keys by prefix, verify Argon2id hash
2. New: if key is found and `ExpiresAt` is non-null and `ExpiresAt < DateTime.UtcNow`, return `new ApiKeyValidationResult(null, IsExpired: true)`
3. Otherwise return `new ApiKeyValidationResult(apiKey, IsExpired: false)`

**Middleware change in `AuthenticationMiddleware`:**
- Call `ValidateApiKeyAsync`, check `result.IsExpired`
- If expired: return `401 Unauthorized` with body `{"error": "API key has expired"}`
- If key is null and not expired: return `401` with existing generic message

**Interface change:** `IApiKeyService.ValidateApiKeyAsync` return type updates to `Task<ApiKeyValidationResult>`.

**Static token exemption:** The static `AuthorizationToken` path in `AuthenticationMiddleware` (direct string equality check) is intentionally exempt from expiration. It has no concept of expiration and remains unchanged. Only API keys validated through `ValidateApiKeyAsync` are subject to expiration enforcement.

**Result type placement:** `ApiKeyValidationResult` is defined in `TerraformRegistry/Services/` alongside the existing `ApiKeyUpdateResult` record (which lives at the bottom of `ApiKeyService.cs`). This follows the existing convention — result types live with their service, not in the Models assembly.

### Files Modified
- `TerraformRegistry/Services/ApiKeyService.cs` (add `ApiKeyValidationResult` record, add expiry check)
- `TerraformRegistry/Services/IApiKeyService.cs`
- `TerraformRegistry/Middleware/AuthenticationMiddleware.cs`

### Migration
None required.

---

## 2. Health & Readiness Endpoints

### Problem
No health or readiness endpoints exist. Required for container orchestration and operational monitoring.

### Design

**`GET /health`** — Liveness probe
- No auth required
- Always returns `200 OK` with `{"status": "healthy"}`
- No dependency checks

**`GET /ready`** — Readiness probe
- No auth required (minimal response)
- Checks database connectivity (lightweight query) and storage backend availability
- Returns `200 OK` with `{"status": "ready"}` if all checks pass
- Returns `503 Service Unavailable` with `{"status": "not_ready"}` if any check fails
- **No component details exposed** — this endpoint is publicly accessible

**`GET /ready?detail=true`** — Detailed readiness (authenticated only)
- Requires valid auth (API key, bearer token, or session)
- Same checks as `/ready`
- Returns component-level breakdown:

```json
{
  "status": "ready",
  "checks": {
    "database": { "status": "healthy" },
    "storage": { "status": "healthy" }
  }
}
```

On failure:
```json
{
  "status": "not_ready",
  "checks": {
    "database": { "status": "healthy" },
    "storage": { "status": "unhealthy", "reason": "Storage path not writable" }
  }
}
```

Unauthenticated callers with `?detail=true` receive the minimal response (same as without the parameter).

### Implementation

**New interface methods:**
- `IDatabaseService.CheckConnectionAsync()` — executes `SELECT 1` (PostgreSQL) or `SELECT 1` (SQLite)
- `IModuleService.CheckStorageAsync()` — local: checks directory exists and is writable; Azure: calls `GetPropertiesAsync()` on the container client to verify connectivity (the constructor already ensures the container exists at startup, so this check validates ongoing reachability rather than existence)

**New handler:** `HealthHandlers.cs` with `HandleHealth` and `HandleReady` static methods.

**Authentication detection for detail mode:** The `/health` and `/ready` paths are NOT in `AuthenticationMiddleware.ProtectedPathPrefixes`, so the middleware won't populate `context.User` by default. The `HandleReady` handler must manually attempt authentication when `detail=true` is requested by checking all three credential sources (mirroring `AuthenticationMiddleware`):
1. **Static bearer token:** compare `Authorization: Bearer <token>` against the configured `AuthorizationToken`
2. **API key:** resolve `IApiKeyService` and call `ValidateApiKeyAsync` on the bearer token
3. **Session cookie:** resolve `JwtService` and call `ValidateToken` on the `tf-session` cookie

If none succeed, `detail=true` is silently ignored and the minimal response is returned.

### Files Modified
- `TerraformRegistry/Handlers/HealthHandlers.cs` (new)
- `TerraformRegistry/Program.cs` (register endpoints)
- `TerraformRegistry.API/IDatabaseService.cs` (add `CheckConnectionAsync`)
- `TerraformRegistry.API/IModuleService.cs` (add `CheckStorageAsync`)
- `TerraformRegistry.PostgreSQL/PostgreSqlDatabaseService.cs` (implement check)
- `TerraformRegistry/Services/SqliteDatabaseService.cs` (implement check)
- `TerraformRegistry/Services/LocalModuleService.cs` (implement check)
- `TerraformRegistry.AzureBlob/AzureBlobModuleService.cs` (implement check)

### Migration
None required.

---

## 3. Download Analytics

### Problem
The `module_downloads` table exists in the schema and a `record_module_download()` PostgreSQL function is defined, but **download recording is never called from application code**. The download handlers serve files without recording the event. There is also no API or UI to query download data.

### Design

**API Endpoints (all require authentication):**

**`GET /api/analytics/downloads/summary`**
Returns:
```json
{
  "totalDownloads": 1234,
  "downloadsToday": 15,
  "downloadsThisWeek": 87,
  "downloadsThisMonth": 342,
  "uniqueModules": 28
}
```

**`GET /api/analytics/downloads/top?limit=10&period=30d`**
Returns:
```json
{
  "period": "30d",
  "modules": [
    {
      "namespace": "myorg",
      "name": "vpc",
      "provider": "aws",
      "downloads": 156
    }
  ]
}
```
Valid periods: `7d`, `30d`, `90d`, `all`. Default: `30d`. Limit default: 10, max: 50.

**`GET /api/analytics/downloads/trends?period=30d&interval=day`**
Returns:
```json
{
  "period": "30d",
  "interval": "day",
  "data": [
    { "date": "2026-03-01", "downloads": 12 },
    { "date": "2026-03-02", "downloads": 8 }
  ]
}
```
Valid intervals: `day`, `week`, `month`. Default: `day`.

**`GET /api/analytics/downloads/module/{namespace}/{name}/{provider}`**
Returns:
```json
{
  "namespace": "myorg",
  "name": "vpc",
  "provider": "aws",
  "totalDownloads": 456,
  "versions": [
    { "version": "1.2.0", "downloads": 200 },
    { "version": "1.1.0", "downloads": 180 },
    { "version": "1.0.0", "downloads": 76 }
  ],
  "trend": [
    { "date": "2026-03-01", "downloads": 5 },
    { "date": "2026-03-02", "downloads": 3 }
  ]
}
```

### Prerequisite: Wire Up Download Recording

Before analytics queries return meaningful data, downloads must actually be recorded:

- Add `RecordDownloadAsync(namespace, name, provider, version, clientIp, userAgent)` to `IDatabaseService` — no `moduleId` parameter, since the existing PostgreSQL `record_module_download()` function resolves `module_id` internally via SELECT
- **PostgreSQL:** call the existing `record_module_download(p_namespace, p_name, p_provider, p_version, p_client_ip, p_user_agent)` function (6 parameters, no `moduleId`)
- **SQLite:** create the `module_downloads` table (it only exists in the PostgreSQL migration) and perform a direct `INSERT`, looking up `module_id` by querying the `modules` table (mirroring the PostgreSQL function's behavior)
- Call `RecordDownloadAsync` from `ModuleHandlers.DownloadModule` and `ModuleHandlers.DownloadLatestModule` after serving the download
- This adds a SQLite migration for the `module_downloads` table

### Backend Implementation
- New `IAnalyticsService` interface with methods matching each endpoint
- PostgreSQL implementation: standard SQL with `DATE_TRUNC` for grouping
- SQLite implementation: `strftime` for date grouping
- New `AnalyticsHandlers.cs` for endpoint handlers

### Frontend
- New `/analytics` page in sidebar navigation
- Summary cards row at top (total, today, this week, this month)
- Line chart for download trends using `vue-chartjs` + `chart.js`
- Top modules table with download counts
- Click-through to per-module detail page with version breakdown bar chart

### New Dependencies
- `vue-chartjs` + `chart.js` (frontend only)

### Auth Middleware Update
`/api/analytics` must be added to `ProtectedPathPrefixes` in `AuthenticationMiddleware.cs` to enforce authentication on analytics endpoints.

### Interface Placement Note
`IAnalyticsService` is placed in `TerraformRegistry.API/` alongside `IDatabaseService` and `IModuleService`, since both PostgreSQL and SQLite implementations reference it. This diverges from `IApiKeyService` which lives in `TerraformRegistry/Services/` — the existing split is not retroactively changed.

### Files Created/Modified
- `TerraformRegistry.API/IDatabaseService.cs` (add `RecordDownloadAsync`)
- `TerraformRegistry.API/IAnalyticsService.cs` (new)
- `TerraformRegistry.PostgreSQL/PostgreSqlDatabaseService.cs` (implement `RecordDownloadAsync`)
- `TerraformRegistry.PostgreSQL/PostgreSqlAnalyticsService.cs` (new)
- `TerraformRegistry/Services/SqliteDatabaseService.cs` (implement `RecordDownloadAsync`, create `module_downloads` table)
- `TerraformRegistry/Services/SqliteAnalyticsService.cs` (new)
- `TerraformRegistry/Handlers/AnalyticsHandlers.cs` (new)
- `TerraformRegistry/Handlers/ModuleHandlers.cs` (call `RecordDownloadAsync` in download handlers)
- `TerraformRegistry/Middleware/AuthenticationMiddleware.cs` (add `/api/analytics` to protected prefixes)
- `TerraformRegistry/Program.cs` (register endpoints, register service)
- `frontend/pages/analytics.vue` (new)
- `frontend/composables/useAnalytics.ts` (new)
- `frontend/components/analytics/` (new — chart components)

### Migration
- SQLite: `module_downloads` table creation (does not exist in SQLite yet)
- PostgreSQL: none — table and function already exist

---

## 4. Webhooks

### Problem
No mechanism to notify external systems when modules are uploaded, deleted, restored, or purged.

### Design

**Event types:** `module.uploaded`, `module.deleted`, `module.restored`, `module.purged`

**Payload:**
```json
{
  "event": "module.uploaded",
  "timestamp": "2026-03-16T12:00:00Z",
  "module": {
    "namespace": "myorg",
    "name": "vpc",
    "provider": "aws",
    "version": "1.2.0"
  }
}
```

**Delivery:**
- HTTP POST to the configured URL
- `Content-Type: application/json`
- If a secret is configured: `X-Signature-256` header with `sha256=<HMAC-SHA256 hex digest of body>`
- Fire-and-forget on a background thread (module operations do not block)
- Single attempt, no retries (first iteration)
- 5-second timeout per request
- Uses `IHttpClientFactory` for proper connection management

### Database Schema (Migration 1.0.4)

```sql
CREATE TABLE IF NOT EXISTS webhooks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    url TEXT NOT NULL,
    secret TEXT,
    events TEXT[] NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_webhooks_user_id ON webhooks(user_id);
CREATE INDEX idx_webhooks_is_active ON webhooks(is_active);
```

**SQLite equivalent:** Uses `TEXT` for events (JSON array string) and `TEXT` for id. The `DEFAULT gen_random_uuid()` is omitted — UUIDs are generated in application code via `Guid.NewGuid()` (matching the existing `ApiKey` pattern where `Id = Guid.NewGuid()` in the service layer).

### API Endpoints (all require authentication)

- `GET /api/webhooks` — List current user's webhooks
- `POST /api/webhooks` — Create webhook `{ url, events, secret? }`
- `PUT /api/webhooks/{id}` — Update webhook `{ url?, events?, secret?, isActive? }`
- `DELETE /api/webhooks/{id}` — Delete webhook (owner only)

### Service Layer

**Class hierarchy and DI registration:**

`IWebhookService` (in `TerraformRegistry.API/`) defines CRUD methods only:
- `ListWebhooksAsync(userId)` — List user's webhooks
- `CreateWebhookAsync(userId, url, events, secret?)` — Create webhook
- `UpdateWebhookAsync(webhookId, userId, ...)` — Update (owner only)
- `DeleteWebhookAsync(webhookId, userId)` — Delete (owner only)
- `GetActiveWebhooksForEventAsync(eventType)` — Query active webhooks matching an event type

`PostgreSqlWebhookService` and `SqliteWebhookService` implement `IWebhookService` (database CRUD). Registered in DI per database provider, same pattern as `IDatabaseService`.

`WebhookDispatcher` is a **separate class** (not an `IWebhookService` implementation). It takes `IWebhookService` and `IHttpClientFactory` via DI. It exposes a single method:
- `FireEventAsync(eventType, moduleInfo)` — calls `GetActiveWebhooksForEventAsync`, then POSTs to each URL on a background thread

`WebhookDispatcher` is registered in DI as a singleton. `ModuleHandlers` receive `WebhookDispatcher` (concrete type) as a parameter, not `IWebhookService`.

**Integration points in `ModuleHandlers`:**
- After `UploadModule` succeeds → `FireEventAsync("module.uploaded", ...)`
- After `DeleteModuleVersion` succeeds → `FireEventAsync("module.deleted", ...)`
- After `RestoreModuleVersion` succeeds → `FireEventAsync("module.restored", ...)`
- After `PurgeModuleVersion` succeeds → `FireEventAsync("module.purged", ...)`

**Handler signature changes:** `ModuleHandlers` is a static class where dependencies are injected as method parameters via minimal API route binding. Each of the four handler methods above must gain an `IWebhookService webhookService` parameter, and the corresponding `app.Map*` route registrations in `Program.cs` must be updated accordingly.

### Model

```csharp
[Table("webhooks")]
public class Webhook
{
    public Guid Id { get; set; }
    public string UserId { get; set; }
    public string Url { get; set; }
    [JsonIgnore]
    public string? Secret { get; set; }
    public string[] Events { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### Frontend
- New `/settings/webhooks` page (in settings sidebar alongside API keys and trash)
- Table listing webhooks: URL, events (as badges), active toggle, actions
- Create/edit modal: URL input, event checkboxes, optional secret field
- Delete with confirmation modal

### Auth Middleware Update
`/api/webhooks` must be added to `ProtectedPathPrefixes` in `AuthenticationMiddleware.cs` to enforce authentication on webhook CRUD endpoints.

### Interface Placement Note
`IWebhookService` is placed in `TerraformRegistry.API/` alongside `IDatabaseService` and `IModuleService`. Same rationale as `IAnalyticsService` — see note in Feature 3.

### Files Created/Modified
- `TerraformRegistry.Models/Webhook.cs` (new)
- `TerraformRegistry.API/IWebhookService.cs` (new)
- `TerraformRegistry.PostgreSQL/PostgreSqlWebhookService.cs` (new)
- `TerraformRegistry/Services/SqliteWebhookService.cs` (new)
- `TerraformRegistry/Services/WebhookDispatcher.cs` (new — handles HTTP delivery)
- `TerraformRegistry/Handlers/WebhookHandlers.cs` (new)
- `TerraformRegistry.PostgreSQL/Migrations/Migration_1_0_4.cs` (new)
- `TerraformRegistry/Handlers/ModuleHandlers.cs` (add `IWebhookService` parameter + `FireEventAsync` calls)
- `TerraformRegistry/Middleware/AuthenticationMiddleware.cs` (add `/api/webhooks` to protected prefixes)
- `TerraformRegistry/Program.cs` (register services, endpoints, and update route handler lambdas)
- `frontend/pages/settings/webhooks.vue` (new)
- `frontend/composables/useWebhooks.ts` (new)

---

## Implementation Order

1. **API Key Expiration** — smallest change, immediate security value
2. **Health & Readiness** — small scope, no DB changes
3. **Download Analytics** — medium scope, no DB changes, new frontend page
4. **Webhooks** — largest scope, new DB table, new service, frontend page

---

## Out of Scope (Future Iterations)

- Webhook retry logic / dead letter queue
- Webhook delivery logs / status tracking in UI
- Rate limiting on analytics endpoints
- Analytics data export (CSV/JSON)
- Webhook test/ping endpoint
