# Quick Wins Feature Pack Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add API key expiration enforcement, health/readiness endpoints, download analytics, and webhooks to the Terraform registry.

**Architecture:** Four independent features built sequentially. Each feature follows TDD: write failing test, implement, verify, commit. Backend is .NET 10 minimal APIs with PostgreSQL/SQLite dual support. Frontend is Nuxt 3 with Nuxt UI components.

**Tech Stack:** C# / .NET 10, PostgreSQL, SQLite, xUnit + Testcontainers, Nuxt 3, Vue 3, TypeScript, vue-chartjs

**Spec:** `docs/superpowers/specs/2026-03-16-quick-wins-design.md`

---

## Chunk 1: API Key Expiration Enforcement

### Task 1: Add ApiKeyValidationResult record and update interface

**Files:**
- Modify: `TerraformRegistry/Services/ApiKeyService.cs:191-198`
- Modify: `TerraformRegistry/Services/IApiKeyService.cs:17`

- [ ] **Step 1: Add the result record to ApiKeyService.cs**

At the bottom of `ApiKeyService.cs`, after the existing `ApiKeyUpdateResult` record (line 198), add:

```csharp
public record ApiKeyValidationResult(ApiKey? Key, bool IsExpired);
```

- [ ] **Step 2: Update the interface return type**

In `IApiKeyService.cs`, change line 17 from:

```csharp
Task<ApiKey?> ValidateApiKeyAsync(string rawToken);
```

to:

```csharp
Task<ApiKeyValidationResult> ValidateApiKeyAsync(string rawToken);
```

- [ ] **Step 3: Verify it compiles (expect errors in consumers)**

Run: `dotnet build TerraformRegistry/TerraformRegistry.csproj 2>&1 | head -30`
Expected: Build errors in `AuthenticationMiddleware.cs` because `ValidateApiKeyAsync` now returns `ApiKeyValidationResult` instead of `ApiKey?`.

---

### Task 2: Write failing test for expired key rejection

**Files:**
- Create: `TerraformRegistry.Tests/IntegrationTests/ApiKeyExpirationTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class ApiKeyExpirationTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    protected const string AuthToken = "default-auth-token";

    [Fact]
    public async Task ExpiredApiKey_Returns401WithExpiredMessage()
    {
        var client = _factory.CreateClient();

        // Create a user and API key via the service directly
        using var scope = _factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<TerraformRegistry.Services.IApiKeyService>();
        var dbService = scope.ServiceProvider.GetRequiredService<TerraformRegistry.API.Interfaces.IDatabaseService>();

        // Create a test user
        var user = await apiKeyService.GetOrCreateUserAsync("test@example.com", "test", "test-provider-id");

        // Create an API key
        var (rawToken, key) = await apiKeyService.CreateApiKeyAsync(user.Id, "test-expired-key");

        // Set the key's expiration to the past
        key.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        await dbService.UpdateApiKeyAsync(key);

        // Use the expired key
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        var response = await client.GetAsync("/v1/modules");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("expired", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidApiKey_WithFutureExpiration_Succeeds()
    {
        var client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<TerraformRegistry.Services.IApiKeyService>();
        var dbService = scope.ServiceProvider.GetRequiredService<TerraformRegistry.API.Interfaces.IDatabaseService>();

        var user = await apiKeyService.GetOrCreateUserAsync("test2@example.com", "test", "test-provider-id-2");
        var (rawToken, key) = await apiKeyService.CreateApiKeyAsync(user.Id, "test-valid-key");

        // Set expiration to the future
        key.ExpiresAt = DateTime.UtcNow.AddDays(30);
        await dbService.UpdateApiKeyAsync(key);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        var response = await client.GetAsync("/v1/modules");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ValidApiKey_WithNoExpiration_Succeeds()
    {
        var client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var apiKeyService = scope.ServiceProvider.GetRequiredService<TerraformRegistry.Services.IApiKeyService>();

        var user = await apiKeyService.GetOrCreateUserAsync("test3@example.com", "test", "test-provider-id-3");
        var (rawToken, _) = await apiKeyService.CreateApiKeyAsync(user.Id, "test-no-expiry-key");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        var response = await client.GetAsync("/v1/modules");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TerraformRegistry.Tests --filter "ApiKeyExpirationTests" --no-restore -v n 2>&1 | tail -20`
Expected: Build failure (interface mismatch from Task 1) or test failure.

---

### Task 3: Implement expiration check in ApiKeyService

**Files:**
- Modify: `TerraformRegistry/Services/ApiKeyService.cs:43-66`

- [ ] **Step 1: Update ValidateApiKeyAsync to check expiration**

Replace the `ValidateApiKeyAsync` method (lines 43-66) with:

```csharp
    public async Task<ApiKeyValidationResult> ValidateApiKeyAsync(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return new ApiKeyValidationResult(null, false);
        }

        var prefix = rawToken.Length >= 8 ? rawToken.Substring(0, 8) : rawToken;

        // Find keys with matching prefix to minimize Argon2 checks (expensive)
        var candidates = await dbService.GetApiKeysByPrefixAsync(prefix);

        foreach (var key in candidates)
        {
            if (VerifyHash(rawToken, key.TokenHash))
            {
                // Check if the key has expired
                if (key.ExpiresAt.HasValue && key.ExpiresAt.Value < DateTime.UtcNow)
                {
                    return new ApiKeyValidationResult(null, true);
                }

                key.LastUsedAt = DateTime.UtcNow;
                await dbService.UpdateApiKeyAsync(key);
                return new ApiKeyValidationResult(key, false);
            }
        }

        return new ApiKeyValidationResult(null, false);
    }
```

---

### Task 4: Update AuthenticationMiddleware for expiration

**Files:**
- Modify: `TerraformRegistry/Middleware/AuthenticationMiddleware.cs:50-70`

- [ ] **Step 1: Update the API key validation block**

Replace lines 50-70 (the API key check block inside the `if (!token.Contains('.') ...)` block) with:

```csharp
                if (!token.Contains('.') || token.Count(c => c == '.') != 2)
                {
                    using var scope = context.RequestServices.CreateScope();
                    var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
                    var result = await apiKeyService.ValidateApiKeyAsync(token);

                    if (result.IsExpired)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new { error = "API key has expired" });
                        return;
                    }

                    if (result.Key != null)
                    {
                        var claims = new List<Claim>
                        {
                            new(ClaimTypes.NameIdentifier, result.Key.UserId.ToString()),
                            new(ClaimTypes.AuthenticationMethod, "ApiKey")
                        };
                        var identity = new ClaimsIdentity(claims, "ApiKey");
                        context.User = new ClaimsPrincipal(identity);

                        await next(context);
                        return;
                    }
                }
```

- [ ] **Step 2: Verify build succeeds**

Run: `dotnet build TerraformRegistry.sln --no-restore 2>&1 | tail -10`
Expected: Build succeeded.

- [ ] **Step 3: Run tests**

Run: `dotnet test TerraformRegistry.Tests --filter "ApiKeyExpirationTests" --no-restore -v n 2>&1 | tail -20`
Expected: All 3 tests pass.

- [ ] **Step 4: Run full test suite to check for regressions**

Run: `dotnet test TerraformRegistry.Tests --no-restore -v n 2>&1 | tail -20`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add TerraformRegistry/Services/ApiKeyService.cs TerraformRegistry/Services/IApiKeyService.cs TerraformRegistry/Middleware/AuthenticationMiddleware.cs TerraformRegistry.Tests/IntegrationTests/ApiKeyExpirationTests.cs
git commit -m "feat: enforce API key expiration on authentication"
```

---

## Chunk 2: Health & Readiness Endpoints

### Task 5: Add CheckConnectionAsync to IDatabaseService and implementations

**Files:**
- Modify: `TerraformRegistry.API/Interfaces/IDatabaseService.cs`
- Modify: `TerraformRegistry.PostgreSQL/PostgreSQLDatabaseService.cs`
- Modify: `TerraformRegistry/Services/SqliteDatabaseService.cs`

- [ ] **Step 1: Add method to interface**

Add to `IDatabaseService.cs` at the end (before the closing `}`):

```csharp
    /// <summary>
    ///     Checks database connectivity by executing a lightweight query
    /// </summary>
    Task<bool> CheckConnectionAsync();
```

- [ ] **Step 2: Implement in PostgreSqlDatabaseService**

Add method to the class:

```csharp
    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
```

- [ ] **Step 3: Implement in SqliteDatabaseService**

Add method to the class:

```csharp
    public async Task<bool> CheckConnectionAsync()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
```

- [ ] **Step 4: Verify build**

Run: `dotnet build TerraformRegistry.sln --no-restore 2>&1 | tail -10`
Expected: Build succeeded.

---

### Task 6: Add CheckStorageAsync to IModuleService and implementations

**Files:**
- Modify: `TerraformRegistry.API/Interfaces/IModuleService.cs`
- Modify: `TerraformRegistry/Services/LocalModuleService.cs`
- Modify: `TerraformRegistry.AzureBlob/AzureBlobModuleService.cs`

- [ ] **Step 1: Add method to interface**

Add to `IModuleService.cs` at the end:

```csharp
    /// <summary>
    ///     Checks storage backend availability
    /// </summary>
    Task<(bool Healthy, string? Reason)> CheckStorageAsync();
```

- [ ] **Step 2: Add abstract method to ModuleService base class**

Find the `ModuleService` abstract class (likely `TerraformRegistry.API/ModuleService.cs` or similar) and add:

```csharp
    public abstract Task<(bool Healthy, string? Reason)> CheckStorageAsync();
```

**This must be done before the override implementations below, otherwise `CS0115` compile error.**

- [ ] **Step 3: Implement in LocalModuleService**

Add method:

```csharp
    public override Task<(bool Healthy, string? Reason)> CheckStorageAsync()
    {
        if (!Directory.Exists(_moduleStoragePath))
        {
            return Task.FromResult((false, (string?)"Storage directory does not exist"));
        }

        try
        {
            var testFile = Path.Combine(_moduleStoragePath, ".health-check");
            File.WriteAllText(testFile, "ok");
            File.Delete(testFile);
            return Task.FromResult((true, (string?)null));
        }
        catch (Exception ex)
        {
            return Task.FromResult((false, (string?)$"Storage path not writable: {ex.Message}"));
        }
    }
```

- [ ] **Step 4: Implement in AzureBlobModuleService**

Add method:

```csharp
    public override async Task<(bool Healthy, string? Reason)> CheckStorageAsync()
    {
        try
        {
            await _containerClient.GetPropertiesAsync();
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, $"Azure Blob Storage unreachable: {ex.Message}");
        }
    }
```

- [ ] **Step 5: Verify build**

Run: `dotnet build TerraformRegistry.sln --no-restore 2>&1 | tail -10`
Expected: Build succeeded.

---

### Task 7: Write failing health endpoint tests

**Files:**
- Create: `TerraformRegistry.Tests/IntegrationTests/HealthEndpointTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Xunit.Abstractions;

namespace TerraformRegistry.Tests.IntegrationTests;

public class HealthEndpointTests(ITestOutputHelper output) : IntegrationTestBase(output, AuthToken)
{
    protected const string AuthToken = "default-auth-token";

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", content);
    }

    [Fact]
    public async Task Ready_ReturnsOkWithMinimalResponse()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("ready", content);
        // Should NOT contain component details
        Assert.DoesNotContain("database", content);
        Assert.DoesNotContain("storage", content);
    }

    [Fact]
    public async Task Ready_WithDetailAndAuth_ReturnsComponentBreakdown()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthToken);

        var response = await client.GetAsync("/ready?detail=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("database", content);
        Assert.Contains("storage", content);
    }

    [Fact]
    public async Task Ready_WithDetailButNoAuth_ReturnsMinimalResponse()
    {
        var client = _factory.CreateClient();
        // No auth header

        var response = await client.GetAsync("/ready?detail=true");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("database", content);
        Assert.DoesNotContain("storage", content);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TerraformRegistry.Tests --filter "HealthEndpointTests" --no-restore -v n 2>&1 | tail -20`
Expected: FAIL (404 — endpoints don't exist yet).

---

### Task 8: Implement HealthHandlers and register endpoints

**Files:**
- Create: `TerraformRegistry/Handlers/HealthHandlers.cs`
- Modify: `TerraformRegistry/Program.cs`

- [ ] **Step 1: Create HealthHandlers.cs**

```csharp
using System.Security.Claims;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Services;

namespace TerraformRegistry.Handlers;

public static class HealthHandlers
{
    public static IResult HandleHealth()
    {
        return Results.Ok(new { status = "healthy" });
    }

    public static async Task<IResult> HandleReady(
        IDatabaseService dbService,
        IModuleService moduleService,
        HttpContext context,
        IConfiguration configuration)
    {
        var dbHealthy = await dbService.CheckConnectionAsync();
        var (storageHealthy, storageReason) = await moduleService.CheckStorageAsync();
        var allHealthy = dbHealthy && storageHealthy;

        // Check if detail is requested and caller is authenticated
        var detailRequested = context.Request.Query.ContainsKey("detail") &&
                              context.Request.Query["detail"].ToString().Equals("true", StringComparison.OrdinalIgnoreCase);

        var showDetail = false;
        if (detailRequested)
        {
            showDetail = await IsAuthenticatedAsync(context, configuration);
        }

        if (showDetail)
        {
            var detailResponse = new
            {
                status = allHealthy ? "ready" : "not_ready",
                checks = new
                {
                    database = new { status = dbHealthy ? "healthy" : "unhealthy" },
                    storage = new
                    {
                        status = storageHealthy ? "healthy" : "unhealthy",
                        reason = storageHealthy ? null : storageReason
                    }
                }
            };

            return allHealthy
                ? Results.Ok(detailResponse)
                : Results.Json(detailResponse, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var minimalResponse = new { status = allHealthy ? "ready" : "not_ready" };
        return allHealthy
            ? Results.Ok(minimalResponse)
            : Results.Json(minimalResponse, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<bool> IsAuthenticatedAsync(HttpContext context, IConfiguration configuration)
    {
        // Check 1: Static bearer token
        var header = context.Request.Headers["Authorization"].FirstOrDefault();
        if (!string.IsNullOrEmpty(header) && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = header.Substring("Bearer ".Length);
            var authToken = configuration["AuthorizationToken"];
            if (!string.IsNullOrEmpty(authToken) && token == authToken)
            {
                return true;
            }

            // Check 2: API key
            if (!token.Contains('.') || token.Count(c => c == '.') != 2)
            {
                using var scope = context.RequestServices.CreateScope();
                var apiKeyService = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
                var result = await apiKeyService.ValidateApiKeyAsync(token);
                if (result.Key != null)
                {
                    return true;
                }
            }
        }

        // Check 3: Session cookie
        var sessionToken = context.Request.Cookies["tf-session"];
        if (!string.IsNullOrEmpty(sessionToken))
        {
            var jwtService = context.RequestServices.GetRequiredService<JwtService>();
            var principal = jwtService.ValidateToken(sessionToken);
            if (principal != null)
            {
                return true;
            }
        }

        return false;
    }
}
```

- [ ] **Step 2: Register endpoints in Program.cs**

Add after the `ServiceDiscovery` endpoint registration (after line 266) in `Program.cs`:

```csharp
app.MapGet("/health", HealthHandlers.HandleHealth)
    .WithTags("Health")
    .WithDescription("Liveness probe — always returns 200 if the process is running")
    .Produces(200);

app.MapGet("/ready", (IDatabaseService dbService, IModuleService moduleService, HttpContext context, IConfiguration config) =>
        HealthHandlers.HandleReady(dbService, moduleService, context, config))
    .WithTags("Health")
    .WithDescription("Readiness probe — checks database and storage. Use ?detail=true with auth for component details.")
    .Produces(200)
    .Produces(503);
```

- [ ] **Step 3: Run health tests**

Run: `dotnet test TerraformRegistry.Tests --filter "HealthEndpointTests" --no-restore -v n 2>&1 | tail -20`
Expected: All 4 tests pass.

- [ ] **Step 4: Run full test suite**

Run: `dotnet test TerraformRegistry.Tests --no-restore -v n 2>&1 | tail -20`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add TerraformRegistry.API/Interfaces/IDatabaseService.cs TerraformRegistry.API/Interfaces/IModuleService.cs TerraformRegistry.PostgreSQL/ TerraformRegistry/Services/SqliteDatabaseService.cs TerraformRegistry/Services/LocalModuleService.cs TerraformRegistry.AzureBlob/AzureBlobModuleService.cs TerraformRegistry/Handlers/HealthHandlers.cs TerraformRegistry/Program.cs TerraformRegistry.Tests/IntegrationTests/HealthEndpointTests.cs
git commit -m "feat: add /health and /ready endpoints with dependency checks"
```

---

## Chunk 3: Download Recording (Analytics Prerequisite)

### Task 9: Add RecordDownloadAsync to IDatabaseService

**Files:**
- Modify: `TerraformRegistry.API/Interfaces/IDatabaseService.cs`

- [ ] **Step 1: Add method to interface**

Add to `IDatabaseService.cs`:

```csharp
    /// <summary>
    ///     Records a module download event
    /// </summary>
    Task RecordDownloadAsync(string @namespace, string name, string provider, string version, string? clientIp, string? userAgent);
```

---

### Task 10: Implement RecordDownloadAsync in PostgreSQL

**Files:**
- Modify: `TerraformRegistry.PostgreSQL/PostgreSQLDatabaseService.cs`

- [ ] **Step 1: Add implementation**

```csharp
    public async Task RecordDownloadAsync(string @namespace, string name, string provider, string version, string? clientIp, string? userAgent)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT record_module_download(@p0, @p1, @p2, @p3, @p4, @p5)", conn);
        cmd.Parameters.AddWithValue("@p0", @namespace);
        cmd.Parameters.AddWithValue("@p1", name);
        cmd.Parameters.AddWithValue("@p2", provider);
        cmd.Parameters.AddWithValue("@p3", version);
        cmd.Parameters.AddWithValue("@p4", (object?)clientIp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@p5", (object?)userAgent ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
```

---

### Task 11: Implement RecordDownloadAsync in SQLite (with table creation)

**Files:**
- Modify: `TerraformRegistry/Services/SqliteDatabaseService.cs`

- [ ] **Step 1: Add module_downloads table to InitializeDatabase**

In the `InitializeDatabase` method, add after the `api_keys` table creation:

```sql
CREATE TABLE IF NOT EXISTS module_downloads (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    module_id INTEGER REFERENCES modules(id) ON DELETE CASCADE,
    namespace TEXT NOT NULL,
    name TEXT NOT NULL,
    provider TEXT NOT NULL,
    version TEXT NOT NULL,
    download_time TEXT NOT NULL DEFAULT (datetime('now')),
    client_ip TEXT,
    user_agent TEXT
);
CREATE INDEX IF NOT EXISTS idx_module_downloads_time ON module_downloads(download_time);
```

- [ ] **Step 2: Add implementation**

```csharp
    public async Task RecordDownloadAsync(string @namespace, string name, string provider, string version, string? clientIp, string? userAgent)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        // Look up module_id
        using var lookupCmd = connection.CreateCommand();
        lookupCmd.CommandText = "SELECT id FROM modules WHERE namespace = $ns AND name = $name AND provider = $provider AND version = $version AND deleted_at IS NULL";
        lookupCmd.Parameters.AddWithValue("$ns", @namespace);
        lookupCmd.Parameters.AddWithValue("$name", name);
        lookupCmd.Parameters.AddWithValue("$provider", provider);
        lookupCmd.Parameters.AddWithValue("$version", version);
        var moduleId = await lookupCmd.ExecuteScalarAsync();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"INSERT INTO module_downloads (module_id, namespace, name, provider, version, download_time, client_ip, user_agent)
                            VALUES ($moduleId, $ns, $name, $provider, $version, $time, $ip, $ua)";
        cmd.Parameters.AddWithValue("$moduleId", moduleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ns", @namespace);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$provider", provider);
        cmd.Parameters.AddWithValue("$version", version);
        cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("o"));
        cmd.Parameters.AddWithValue("$ip", (object?)clientIp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ua", (object?)userAgent ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
```

---

### Task 12: Wire download recording into ModuleHandlers

**Files:**
- Modify: `TerraformRegistry/Handlers/ModuleHandlers.cs:100-153`
- Modify: `TerraformRegistry/Program.cs` (download route lambdas)

- [ ] **Step 1: Add IDatabaseService parameter to DownloadModule**

Update the `DownloadModule` method signature to add `IDatabaseService dbService`:

```csharp
    public static async Task<IResult> DownloadModule(
        string @namespace,
        string name,
        string provider,
        string version,
        IModuleService moduleService,
        IDatabaseService dbService,
        HttpContext context)
    {
```

After `context.Response.Headers["X-Terraform-Get"] = downloadPath;` (line 114), add:

```csharp
        // Extract values from HttpContext BEFORE Task.Run (HttpContext is not thread-safe)
        var clientIp = context.Connection.RemoteIpAddress?.ToString();
        var userAgent = context.Request.Headers["User-Agent"].ToString();

        // Record the download (fire-and-forget, don't block the response)
        _ = Task.Run(async () =>
        {
            try
            {
                await dbService.RecordDownloadAsync(@namespace, name, provider, version, clientIp, userAgent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record download for {Namespace}/{Name}/{Provider}/{Version}",
                    @namespace, name, provider, version);
            }
        });
```

- [ ] **Step 2: Update DownloadLatestModule similarly**

Add `IDatabaseService dbService` parameter and pass it to the inner `DownloadModule` call:

```csharp
    public static async Task<IResult> DownloadLatestModule(
        string @namespace,
        string name,
        string provider,
        IModuleService moduleService,
        IDatabaseService dbService,
        HttpContext context)
    {
        // ... existing code ...
        return await DownloadModule(@namespace, name, provider, latest, moduleService, dbService, context);
    }
```

- [ ] **Step 3: Update route lambdas in Program.cs**

Update the download route (around line 291):

```csharp
app.MapGet("/v1/modules/{namespace}/{name}/{provider}/{version}/download", (string @namespace, string name,
            string provider, string version, IModuleService moduleService, IDatabaseService dbService, HttpContext context) =>
        ModuleHandlers.DownloadModule(@namespace, name, provider, version, moduleService, dbService, context))
```

Update the latest download route (around line 299):

```csharp
app.MapGet("/v1/modules/{namespace}/{name}/{provider}/download",
        (string @namespace, string name, string provider, IModuleService moduleService, IDatabaseService dbService, HttpContext context) =>
            ModuleHandlers.DownloadLatestModule(@namespace, name, provider, moduleService, dbService, context))
```

- [ ] **Step 4: Verify build**

Run: `dotnet build TerraformRegistry.sln --no-restore 2>&1 | tail -10`
Expected: Build succeeded.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test TerraformRegistry.Tests --no-restore -v n 2>&1 | tail -20`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add TerraformRegistry.API/Interfaces/IDatabaseService.cs TerraformRegistry.PostgreSQL/ TerraformRegistry/Services/SqliteDatabaseService.cs TerraformRegistry/Handlers/ModuleHandlers.cs TerraformRegistry/Program.cs
git commit -m "feat: record module downloads for analytics"
```

---

## Chunk 4: Analytics API & Auth Middleware Update

### Task 13: Add /api/analytics to protected path prefixes

**Files:**
- Modify: `TerraformRegistry/Middleware/AuthenticationMiddleware.cs:17`

- [ ] **Step 1: Update ProtectedPathPrefixes**

Change line 17 from:

```csharp
    private static readonly string[] ProtectedPathPrefixes = ["/v1/", "/api/keys"];
```

to:

```csharp
    private static readonly string[] ProtectedPathPrefixes = ["/v1/", "/api/keys", "/api/analytics", "/api/webhooks"];
```

(Adding both `/api/analytics` and `/api/webhooks` now to avoid revisiting this file later.)

**Note:** Do NOT add `/api/analytics` or `/api/webhooks` to the fallthrough block at line 127. That carve-out exists only for `/api/keys` (an MVC controller using `[Authorize]`). Analytics and webhook routes are minimal API endpoints — the middleware handles their 401 directly, matching the pattern used by all `/v1/` routes.

- [ ] **Step 3: Commit**

```bash
git add TerraformRegistry/Middleware/AuthenticationMiddleware.cs
git commit -m "feat: protect /api/analytics and /api/webhooks endpoints"
```

---

### Task 14: Create IAnalyticsService interface

**Files:**
- Create: `TerraformRegistry.API/Interfaces/IAnalyticsService.cs`

- [ ] **Step 1: Create the interface**

```csharp
namespace TerraformRegistry.API.Interfaces;

public interface IAnalyticsService
{
    Task<DownloadSummary> GetDownloadSummaryAsync();
    Task<TopModulesResult> GetTopModulesAsync(int limit, string period);
    Task<DownloadTrendsResult> GetDownloadTrendsAsync(string period, string interval);
    Task<ModuleAnalyticsResult?> GetModuleAnalyticsAsync(string @namespace, string name, string provider, string period);
}

public record DownloadSummary(
    long TotalDownloads,
    long DownloadsToday,
    long DownloadsThisWeek,
    long DownloadsThisMonth,
    long UniqueModules);

public record TopModuleEntry(string Namespace, string Name, string Provider, long Downloads);
public record TopModulesResult(string Period, IReadOnlyList<TopModuleEntry> Modules);

public record TrendEntry(string Date, long Downloads);
public record DownloadTrendsResult(string Period, string Interval, IReadOnlyList<TrendEntry> Data);

public record VersionDownloads(string Version, long Downloads);
public record ModuleAnalyticsResult(
    string Namespace,
    string Name,
    string Provider,
    long TotalDownloads,
    IReadOnlyList<VersionDownloads> Versions,
    IReadOnlyList<TrendEntry> Trend);
```

---

### Task 15: Implement PostgreSqlAnalyticsService

**Files:**
- Create: `TerraformRegistry.PostgreSQL/PostgreSqlAnalyticsService.cs`

- [ ] **Step 1: Create the service**

```csharp
using Npgsql;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.PostgreSQL;

public class PostgreSqlAnalyticsService(string connectionString) : IAnalyticsService
{
    private static DateTime GetPeriodStart(string period)
    {
        return period switch
        {
            "7d" => DateTime.UtcNow.AddDays(-7),
            "30d" => DateTime.UtcNow.AddDays(-30),
            "90d" => DateTime.UtcNow.AddDays(-90),
            "all" => DateTime.MinValue,
            _ => DateTime.UtcNow.AddDays(-30)
        };
    }

    private static string GetTruncInterval(string interval)
    {
        return interval switch
        {
            "day" => "day",
            "week" => "week",
            "month" => "month",
            _ => "day"
        };
    }

    public async Task<DownloadSummary> GetDownloadSummaryAsync()
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(@"
            SELECT
                COUNT(*) AS total,
                COUNT(*) FILTER (WHERE download_time >= CURRENT_DATE) AS today,
                COUNT(*) FILTER (WHERE download_time >= CURRENT_DATE - INTERVAL '7 days') AS this_week,
                COUNT(*) FILTER (WHERE download_time >= CURRENT_DATE - INTERVAL '30 days') AS this_month,
                COUNT(DISTINCT (namespace, name, provider)) AS unique_modules
            FROM module_downloads", conn);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new DownloadSummary(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4));
    }

    public async Task<TopModulesResult> GetTopModulesAsync(int limit, string period)
    {
        var periodStart = GetPeriodStart(period);
        limit = Math.Clamp(limit, 1, 50);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var sql = @"
            SELECT namespace, name, provider, COUNT(*) AS downloads
            FROM module_downloads
            WHERE ($1 = '0001-01-01'::timestamp OR download_time >= $1::timestamptz)
            GROUP BY namespace, name, provider
            ORDER BY downloads DESC
            LIMIT $2";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(periodStart);
        cmd.Parameters.AddWithValue(limit);

        var modules = new List<TopModuleEntry>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            modules.Add(new TopModuleEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3)));
        }

        return new TopModulesResult(period, modules);
    }

    public async Task<DownloadTrendsResult> GetDownloadTrendsAsync(string period, string interval)
    {
        var periodStart = GetPeriodStart(period);
        var trunc = GetTruncInterval(interval);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var sql = $@"
            SELECT DATE_TRUNC('{trunc}', download_time)::date AS d, COUNT(*) AS downloads
            FROM module_downloads
            WHERE ($1 = '0001-01-01'::timestamp OR download_time >= $1::timestamptz)
            GROUP BY d
            ORDER BY d";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue(periodStart);

        var data = new List<TrendEntry>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            data.Add(new TrendEntry(
                reader.GetDateTime(0).ToString("yyyy-MM-dd"),
                reader.GetInt64(1)));
        }

        return new DownloadTrendsResult(period, interval, data);
    }

    public async Task<ModuleAnalyticsResult?> GetModuleAnalyticsAsync(string @namespace, string name, string provider, string period)
    {
        var periodStart = GetPeriodStart(period);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        // Total downloads
        await using var totalCmd = new NpgsqlCommand(@"
            SELECT COUNT(*) FROM module_downloads
            WHERE namespace = $1 AND name = $2 AND provider = $3", conn);
        totalCmd.Parameters.AddWithValue(@namespace);
        totalCmd.Parameters.AddWithValue(name);
        totalCmd.Parameters.AddWithValue(provider);
        var total = (long)(await totalCmd.ExecuteScalarAsync() ?? 0);

        if (total == 0) return null;

        // Per-version breakdown
        await using var versionCmd = new NpgsqlCommand(@"
            SELECT version, COUNT(*) AS downloads
            FROM module_downloads
            WHERE namespace = $1 AND name = $2 AND provider = $3
            GROUP BY version
            ORDER BY downloads DESC", conn);
        versionCmd.Parameters.AddWithValue(@namespace);
        versionCmd.Parameters.AddWithValue(name);
        versionCmd.Parameters.AddWithValue(provider);

        var versions = new List<VersionDownloads>();
        await using var vReader = await versionCmd.ExecuteReaderAsync();
        while (await vReader.ReadAsync())
        {
            versions.Add(new VersionDownloads(vReader.GetString(0), vReader.GetInt64(1)));
        }
        await vReader.CloseAsync();

        // Trend
        await using var trendCmd = new NpgsqlCommand(@"
            SELECT DATE_TRUNC('day', download_time)::date AS d, COUNT(*) AS downloads
            FROM module_downloads
            WHERE namespace = $1 AND name = $2 AND provider = $3
              AND ($4 = '0001-01-01'::timestamp OR download_time >= $4::timestamptz)
            GROUP BY d
            ORDER BY d", conn);
        trendCmd.Parameters.AddWithValue(@namespace);
        trendCmd.Parameters.AddWithValue(name);
        trendCmd.Parameters.AddWithValue(provider);
        trendCmd.Parameters.AddWithValue(periodStart);

        var trend = new List<TrendEntry>();
        await using var tReader = await trendCmd.ExecuteReaderAsync();
        while (await tReader.ReadAsync())
        {
            trend.Add(new TrendEntry(tReader.GetDateTime(0).ToString("yyyy-MM-dd"), tReader.GetInt64(1)));
        }

        return new ModuleAnalyticsResult(@namespace, name, provider, total, versions, trend);
    }
}
```

---

### Task 16: Implement SqliteAnalyticsService

**Files:**
- Create: `TerraformRegistry/Services/SqliteAnalyticsService.cs`

- [ ] **Step 1: Create the service**

```csharp
using Microsoft.Data.Sqlite;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Services;

public class SqliteAnalyticsService(string connectionString) : IAnalyticsService
{
    private static string GetPeriodFilter(string period)
    {
        return period switch
        {
            "7d" => "AND download_time >= datetime('now', '-7 days')",
            "30d" => "AND download_time >= datetime('now', '-30 days')",
            "90d" => "AND download_time >= datetime('now', '-90 days')",
            "all" => "",
            _ => "AND download_time >= datetime('now', '-30 days')"
        };
    }

    private static string GetDateFormat(string interval)
    {
        return interval switch
        {
            "day" => "%Y-%m-%d",
            "week" => "%Y-W%W",
            "month" => "%Y-%m",
            _ => "%Y-%m-%d"
        };
    }

    public async Task<DownloadSummary> GetDownloadSummaryAsync()
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT
                COUNT(*) AS total,
                COUNT(CASE WHEN download_time >= date('now') THEN 1 END) AS today,
                COUNT(CASE WHEN download_time >= datetime('now', '-7 days') THEN 1 END) AS this_week,
                COUNT(CASE WHEN download_time >= datetime('now', '-30 days') THEN 1 END) AS this_month,
                COUNT(DISTINCT namespace || '/' || name || '/' || provider) AS unique_modules
            FROM module_downloads";

        using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return new DownloadSummary(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4));
    }

    public async Task<TopModulesResult> GetTopModulesAsync(int limit, string period)
    {
        limit = Math.Clamp(limit, 1, 50);
        var filter = GetPeriodFilter(period);

        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT namespace, name, provider, COUNT(*) AS downloads
            FROM module_downloads
            WHERE 1=1 {filter}
            GROUP BY namespace, name, provider
            ORDER BY downloads DESC
            LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        var modules = new List<TopModuleEntry>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            modules.Add(new TopModuleEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3)));
        }

        return new TopModulesResult(period, modules);
    }

    public async Task<DownloadTrendsResult> GetDownloadTrendsAsync(string period, string interval)
    {
        var filter = GetPeriodFilter(period);
        var fmt = GetDateFormat(interval);

        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT strftime('{fmt}', download_time) AS d, COUNT(*) AS downloads
            FROM module_downloads
            WHERE 1=1 {filter}
            GROUP BY d
            ORDER BY d";

        var data = new List<TrendEntry>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            data.Add(new TrendEntry(reader.GetString(0), reader.GetInt64(1)));
        }

        return new DownloadTrendsResult(period, interval, data);
    }

    public async Task<ModuleAnalyticsResult?> GetModuleAnalyticsAsync(string @namespace, string name, string provider, string period)
    {
        var filter = GetPeriodFilter(period);

        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        // Total
        using var totalCmd = conn.CreateCommand();
        totalCmd.CommandText = "SELECT COUNT(*) FROM module_downloads WHERE namespace = $ns AND name = $name AND provider = $provider";
        totalCmd.Parameters.AddWithValue("$ns", @namespace);
        totalCmd.Parameters.AddWithValue("$name", name);
        totalCmd.Parameters.AddWithValue("$provider", provider);
        var total = (long)(await totalCmd.ExecuteScalarAsync() ?? 0);

        if (total == 0) return null;

        // Versions
        using var vCmd = conn.CreateCommand();
        vCmd.CommandText = @"
            SELECT version, COUNT(*) AS downloads FROM module_downloads
            WHERE namespace = $ns AND name = $name AND provider = $provider
            GROUP BY version ORDER BY downloads DESC";
        vCmd.Parameters.AddWithValue("$ns", @namespace);
        vCmd.Parameters.AddWithValue("$name", name);
        vCmd.Parameters.AddWithValue("$provider", provider);

        var versions = new List<VersionDownloads>();
        using var vReader = await vCmd.ExecuteReaderAsync();
        while (await vReader.ReadAsync())
        {
            versions.Add(new VersionDownloads(vReader.GetString(0), vReader.GetInt64(1)));
        }

        // Trend
        using var tCmd = conn.CreateCommand();
        tCmd.CommandText = $@"
            SELECT strftime('%Y-%m-%d', download_time) AS d, COUNT(*) AS downloads
            FROM module_downloads
            WHERE namespace = $ns AND name = $name AND provider = $provider {filter}
            GROUP BY d ORDER BY d";
        tCmd.Parameters.AddWithValue("$ns", @namespace);
        tCmd.Parameters.AddWithValue("$name", name);
        tCmd.Parameters.AddWithValue("$provider", provider);

        var trend = new List<TrendEntry>();
        using var tReader = await tCmd.ExecuteReaderAsync();
        while (await tReader.ReadAsync())
        {
            trend.Add(new TrendEntry(tReader.GetString(0), tReader.GetInt64(1)));
        }

        return new ModuleAnalyticsResult(@namespace, name, provider, total, versions, trend);
    }
}
```

---

### Task 17: Create AnalyticsHandlers and register DI + routes

**Files:**
- Create: `TerraformRegistry/Handlers/AnalyticsHandlers.cs`
- Modify: `TerraformRegistry/Program.cs`

- [ ] **Step 1: Create AnalyticsHandlers.cs**

```csharp
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Handlers;

public static class AnalyticsHandlers
{
    public static async Task<IResult> GetSummary(IAnalyticsService analyticsService)
    {
        var summary = await analyticsService.GetDownloadSummaryAsync();
        return Results.Ok(summary);
    }

    public static async Task<IResult> GetTopModules(IAnalyticsService analyticsService, int limit = 10, string period = "30d")
    {
        var result = await analyticsService.GetTopModulesAsync(limit, period);
        return Results.Ok(result);
    }

    public static async Task<IResult> GetTrends(IAnalyticsService analyticsService, string period = "30d", string interval = "day")
    {
        var result = await analyticsService.GetDownloadTrendsAsync(period, interval);
        return Results.Ok(result);
    }

    public static async Task<IResult> GetModuleAnalytics(
        string @namespace, string name, string provider,
        IAnalyticsService analyticsService, string period = "30d")
    {
        var result = await analyticsService.GetModuleAnalyticsAsync(@namespace, name, provider, period);
        if (result == null) return Results.NotFound(new { error = "No download data for this module" });
        return Results.Ok(result);
    }
}
```

- [ ] **Step 2: Register IAnalyticsService in Program.cs DI**

Add after the `IApiKeyService` registration (around line 117):

```csharp
// Register Analytics Service
builder.Services.AddSingleton<IAnalyticsService>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var databaseProvider = config["DatabaseProvider"]?.ToLower() ?? "sqlite";
    return databaseProvider switch
    {
        "postgres" => new PostgreSQL.PostgreSqlAnalyticsService(config["PostgreSQL:ConnectionString"]!),
        "sqlite" => new SqliteAnalyticsService(config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
        _ => throw new Exception($"Invalid database provider: '{databaseProvider}'")
    };
});
```

Add required using at top of Program.cs:

```csharp
using TerraformRegistry.API.Interfaces;
```

(This using may already exist — only add if missing.)

- [ ] **Step 3: Register analytics routes in Program.cs**

Add after the health endpoints:

```csharp
// Analytics endpoints (auth enforced by AuthenticationMiddleware via ProtectedPathPrefixes, no .RequireAuthorization() needed)
app.MapGet("/api/analytics/downloads/summary", (IAnalyticsService analytics) =>
        AnalyticsHandlers.GetSummary(analytics))
    .WithTags("Analytics")
    .WithDescription("Download summary statistics");

app.MapGet("/api/analytics/downloads/top", (IAnalyticsService analytics, int limit = 10, string period = "30d") =>
        AnalyticsHandlers.GetTopModules(analytics, limit, period))
    .WithTags("Analytics")
    .WithDescription("Top downloaded modules");

app.MapGet("/api/analytics/downloads/trends", (IAnalyticsService analytics, string period = "30d", string interval = "day") =>
        AnalyticsHandlers.GetTrends(analytics, period, interval))
    .WithTags("Analytics")
    .WithDescription("Download trends over time");

app.MapGet("/api/analytics/downloads/module/{namespace}/{name}/{provider}",
        (string @namespace, string name, string provider, IAnalyticsService analytics, string period = "30d") =>
            AnalyticsHandlers.GetModuleAnalytics(@namespace, name, provider, analytics, period))
    .WithTags("Analytics")
    .WithDescription("Per-module download analytics");
```

- [ ] **Step 4: Verify build**

Run: `dotnet build TerraformRegistry.sln --no-restore 2>&1 | tail -10`
Expected: Build succeeded.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test TerraformRegistry.Tests --no-restore -v n 2>&1 | tail -20`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add TerraformRegistry.API/Interfaces/IAnalyticsService.cs TerraformRegistry.PostgreSQL/PostgreSqlAnalyticsService.cs TerraformRegistry/Services/SqliteAnalyticsService.cs TerraformRegistry/Handlers/AnalyticsHandlers.cs TerraformRegistry/Middleware/AuthenticationMiddleware.cs TerraformRegistry/Program.cs
git commit -m "feat: add download analytics API endpoints"
```

---

## Chunk 5: Analytics Frontend

### Task 18: Install chart dependencies

- [ ] **Step 1: Install vue-chartjs and chart.js**

Run: `cd /home/rocky/terraform-registry/TerraformRegistry/web-src && pnpm add vue-chartjs chart.js`

- [ ] **Step 2: Commit**

```bash
git add TerraformRegistry/web-src/package.json TerraformRegistry/web-src/pnpm-lock.yaml
git commit -m "chore: add vue-chartjs and chart.js dependencies"
```

---

### Task 19: Create useAnalytics composable

**Files:**
- Create: `TerraformRegistry/web-src/composables/useAnalytics.ts`

- [ ] **Step 1: Create the composable**

```typescript
// NOTE: getAuthHeaders comes from the useAuth() composable, not a named import.
// Call `const { getAuthHeaders } = useAuth()` inside each function body.

export interface DownloadSummary {
  totalDownloads: number
  downloadsToday: number
  downloadsThisWeek: number
  downloadsThisMonth: number
  uniqueModules: number
}

export interface TopModuleEntry {
  namespace: string
  name: string
  provider: string
  downloads: number
}

export interface TrendEntry {
  date: string
  downloads: number
}

export interface VersionDownloads {
  version: string
  downloads: number
}

export interface ModuleAnalytics {
  namespace: string
  name: string
  provider: string
  totalDownloads: number
  versions: VersionDownloads[]
  trend: TrendEntry[]
}

export function useAnalytics() {
  const { getAuthHeaders } = useAuth()

  async function getSummary(): Promise<DownloadSummary> {
    return await $fetch('/api/analytics/downloads/summary', {
      headers: getAuthHeaders(),
    })
  }

  async function getTopModules(limit = 10, period = '30d'): Promise<{ period: string, modules: TopModuleEntry[] }> {
    return await $fetch(`/api/analytics/downloads/top?limit=${limit}&period=${period}`, {
      headers: getAuthHeaders(),
    })
  }

  async function getTrends(period = '30d', interval = 'day'): Promise<{ period: string, interval: string, data: TrendEntry[] }> {
    return await $fetch(`/api/analytics/downloads/trends?period=${period}&interval=${interval}`, {
      headers: getAuthHeaders(),
    })
  }

  async function getModuleAnalytics(ns: string, name: string, provider: string, period = '30d'): Promise<ModuleAnalytics> {
    return await $fetch(`/api/analytics/downloads/module/${ns}/${name}/${provider}?period=${period}`, {
      headers: getAuthHeaders(),
    })
  }

  return { getSummary, getTopModules, getTrends, getModuleAnalytics }
}
```

---

### Task 20: Create analytics page

**Files:**
- Create: `TerraformRegistry/web-src/pages/analytics.vue`

- [ ] **Step 1: Create the page**

This is a larger file. Create `pages/analytics.vue` with:
- Summary cards row (4 UCards showing total, today, week, month)
- A line chart for trends (using `vue-chartjs` `Line` component)
- A top modules table
- Period selector (7d / 30d / 90d / all)

The page should follow the existing patterns from `settings/api-keys.vue` and `settings/trash.vue` (Nuxt UI components, `$fetch` via composable, reactive state with `ref`/`onMounted`).

- [ ] **Step 2: Add analytics to sidebar navigation**

In `TerraformRegistry/web-src/layouts/default.vue`, add an entry to the `mainLinks` array in `<script setup>` (alongside Modules and Trash). Do NOT add an inline `<NuxtLink>` — the sidebar uses `v-for` over data arrays with `isActive()` for styling:

```typescript
{ label: 'Analytics', icon: 'i-lucide-bar-chart-3', to: '/analytics' }
```

Add this after the Modules entry and before Trash in the `mainLinks` array.

- [ ] **Step 3: Build frontend to verify**

Run: `cd /home/rocky/terraform-registry/TerraformRegistry/web-src && pnpm build 2>&1 | tail -10`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add TerraformRegistry/web-src/composables/useAnalytics.ts TerraformRegistry/web-src/pages/analytics.vue TerraformRegistry/web-src/layouts/default.vue
git commit -m "feat: add download analytics dashboard page"
```

---

## Chunk 6: Webhooks Backend

### Task 21: Create Webhook model

**Files:**
- Create: `TerraformRegistry.Models/Webhook.cs`

- [ ] **Step 1: Create the model**

```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace TerraformRegistry.Models;

[Table("webhooks")]
public class Webhook
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("user_id")]
    [ForeignKey("User")]
    public string UserId { get; set; } = string.Empty;

    [Column("url")]
    [Required]
    public string Url { get; set; } = string.Empty;

    [Column("secret")]
    [JsonIgnore]
    public string? Secret { get; set; }

    [Column("events")]
    [Required]
    public string[] Events { get; set; } = [];

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

---

### Task 22: Create IWebhookService interface

**Files:**
- Create: `TerraformRegistry.API/Interfaces/IWebhookService.cs`

- [ ] **Step 1: Create the interface**

```csharp
using TerraformRegistry.Models;

namespace TerraformRegistry.API.Interfaces;

public interface IWebhookService
{
    Task<IEnumerable<Webhook>> ListWebhooksAsync(string userId);
    Task<Webhook> CreateWebhookAsync(string userId, string url, string[] events, string? secret);
    Task<Webhook?> UpdateWebhookAsync(Guid webhookId, string userId, string? url, string[]? events, string? secret, bool? isActive);
    Task<bool> DeleteWebhookAsync(Guid webhookId, string userId);
    Task<IEnumerable<Webhook>> GetActiveWebhooksForEventAsync(string eventType);
}
```

---

### Task 23: Create PostgreSQL webhook migration and service

**Files:**
- Create: `TerraformRegistry.PostgreSQL/Migrations/Migration_1_0_4.cs`
- Create: `TerraformRegistry.PostgreSQL/PostgreSqlWebhookService.cs`

- [ ] **Step 1: Create migration**

Follow the existing migration pattern (see Migration_1_0_3.cs). The migration class should create the webhooks table with the SQL from the spec.

- [ ] **Step 2: Create PostgreSqlWebhookService**

Implement all IWebhookService methods using NpgsqlCommand with parameterized queries. For events, use PostgreSQL TEXT[] type. For `GetActiveWebhooksForEventAsync`, query where `is_active = true AND $1 = ANY(events)`.

---

### Task 24: Create SQLite webhook service

**Files:**
- Modify: `TerraformRegistry/Services/SqliteDatabaseService.cs` (add webhooks table to InitializeDatabase)
- Create: `TerraformRegistry/Services/SqliteWebhookService.cs`

- [ ] **Step 1: Add webhooks table to SQLite init**

Add to `InitializeDatabase()`:

```sql
CREATE TABLE IF NOT EXISTS webhooks (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    url TEXT NOT NULL,
    secret TEXT,
    events TEXT NOT NULL,
    is_active INTEGER NOT NULL DEFAULT 1,
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);
```

- [ ] **Step 2: Create SqliteWebhookService**

Implement all IWebhookService methods. Store events as JSON array text (`System.Text.Json.JsonSerializer.Serialize/Deserialize`). For `GetActiveWebhooksForEventAsync`, query all active webhooks and filter in-memory (SQLite has no native array contains).

---

### Task 25: Create WebhookDispatcher

**Files:**
- Create: `TerraformRegistry/Services/WebhookDispatcher.cs`

- [ ] **Step 1: Create the dispatcher**

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Services;

public class WebhookDispatcher(IWebhookService webhookService, IHttpClientFactory httpClientFactory, ILogger<WebhookDispatcher> logger)
{
    public void FireEvent(string eventType, string @namespace, string name, string provider, string version)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var webhooks = await webhookService.GetActiveWebhooksForEventAsync(eventType);
                var payload = JsonSerializer.Serialize(new
                {
                    @event = eventType,
                    timestamp = DateTime.UtcNow.ToString("o"),
                    module = new { @namespace, name, provider, version }
                });

                foreach (var webhook in webhooks)
                {
                    try
                    {
                        await DeliverAsync(webhook.Url, webhook.Secret, payload);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to deliver webhook {WebhookId} to {Url}", webhook.Id, webhook.Url);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fire webhook event {EventType}", eventType);
            }
        });
    }

    private async Task DeliverAsync(string url, string? secret, string payload)
    {
        var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(5);

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrEmpty(secret))
        {
            var signature = ComputeHmacSha256(payload, secret);
            request.Headers.Add("X-Signature-256", $"sha256={signature}");
        }

        await client.SendAsync(request);
    }

    private static string ComputeHmacSha256(string payload, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(keyBytes, payloadBytes);
        return Convert.ToHexStringLower(hash);
    }
}
```

---

### Task 26: Create WebhookHandlers and register everything

**Files:**
- Create: `TerraformRegistry/Handlers/WebhookHandlers.cs`
- Modify: `TerraformRegistry/Program.cs`
- Modify: `TerraformRegistry/Handlers/ModuleHandlers.cs`

- [ ] **Step 1: Create WebhookHandlers.cs**

```csharp
using System.Security.Claims;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Handlers;

public static class WebhookHandlers
{
    public static async Task<IResult> ListWebhooks(IWebhookService webhookService, HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
        var webhooks = await webhookService.ListWebhooksAsync(userId);
        return Results.Ok(webhooks);
    }

    public static async Task<IResult> CreateWebhook(IWebhookService webhookService, HttpContext context, HttpRequest request)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var body = await request.ReadFromJsonAsync<CreateWebhookRequest>();
        if (body == null || string.IsNullOrEmpty(body.Url) || body.Events == null || body.Events.Length == 0)
        {
            return Results.BadRequest(new { error = "url and events are required" });
        }

        var webhook = await webhookService.CreateWebhookAsync(userId, body.Url, body.Events, body.Secret);
        return Results.Created($"/api/webhooks/{webhook.Id}", webhook);
    }

    public static async Task<IResult> UpdateWebhook(Guid id, IWebhookService webhookService, HttpContext context, HttpRequest request)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

        var body = await request.ReadFromJsonAsync<UpdateWebhookRequest>();
        var updated = await webhookService.UpdateWebhookAsync(id, userId, body?.Url, body?.Events, body?.Secret, body?.IsActive);
        if (updated == null) return Results.NotFound(new { error = "Webhook not found or access denied" });
        return Results.Ok(updated);
    }

    public static async Task<IResult> DeleteWebhook(Guid id, IWebhookService webhookService, HttpContext context)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
        var result = await webhookService.DeleteWebhookAsync(id, userId);
        return result ? Results.NoContent() : Results.NotFound(new { error = "Webhook not found or access denied" });
    }
}

public record CreateWebhookRequest(string Url, string[] Events, string? Secret);
public record UpdateWebhookRequest(string? Url, string[]? Events, string? Secret, bool? IsActive);
```

- [ ] **Step 2: Register DI and routes in Program.cs**

Add DI registration after the analytics service:

```csharp
// Register Webhook Service
builder.Services.AddSingleton<IWebhookService>(provider =>
{
    var config = provider.GetRequiredService<IConfiguration>();
    var databaseProvider = config["DatabaseProvider"]?.ToLower() ?? "sqlite";
    return databaseProvider switch
    {
        "postgres" => new PostgreSQL.PostgreSqlWebhookService(config["PostgreSQL:ConnectionString"]!),
        "sqlite" => new SqliteWebhookService(config["Sqlite:ConnectionString"] ?? "Data Source=terraform.db"),
        _ => throw new Exception($"Invalid database provider: '{databaseProvider}'")
    };
});

builder.Services.AddSingleton<WebhookDispatcher>();
```

Add routes:

```csharp
// Webhook endpoints
// Webhook endpoints (auth enforced by AuthenticationMiddleware via ProtectedPathPrefixes)
app.MapGet("/api/webhooks", (IWebhookService webhookService, HttpContext context) =>
        WebhookHandlers.ListWebhooks(webhookService, context))
    .WithTags("Webhooks");

app.MapPost("/api/webhooks", (IWebhookService webhookService, HttpContext context, HttpRequest request) =>
        WebhookHandlers.CreateWebhook(webhookService, context, request))
    .WithTags("Webhooks");

app.MapPut("/api/webhooks/{id}", (Guid id, IWebhookService webhookService, HttpContext context, HttpRequest request) =>
        WebhookHandlers.UpdateWebhook(id, webhookService, context, request))
    .WithTags("Webhooks");

app.MapDelete("/api/webhooks/{id}", (Guid id, IWebhookService webhookService, HttpContext context) =>
        WebhookHandlers.DeleteWebhook(id, webhookService, context))
    .WithTags("Webhooks");
```

- [ ] **Step 3: Wire WebhookDispatcher into ModuleHandlers**

Add `WebhookDispatcher webhookDispatcher` parameter to `UploadModule`, `DeleteModuleVersion`, `RestoreModuleVersion`, and `PurgeModuleVersion`. After each successful operation, call:

```csharp
webhookDispatcher.FireEvent("module.uploaded", @namespace, name, provider, version);
```

(Use the appropriate event type for each handler.)

Update the corresponding route lambdas in `Program.cs` to inject `WebhookDispatcher`. For example, the upload route becomes:

```csharp
app.MapPost("/v1/modules/{namespace}/{name}/{provider}/{version}", async (string @namespace, string name,
            string provider, string version, HttpRequest request, IModuleService moduleService, WebhookDispatcher webhookDispatcher) =>
        await ModuleHandlers.UploadModule(@namespace, name, provider, version, request, moduleService, webhookDispatcher))
```

Apply the same pattern to `DeleteModuleVersion`, `RestoreModuleVersion`, and `PurgeModuleVersion` routes — add `WebhookDispatcher webhookDispatcher` to both the lambda parameter list and the handler call.

- [ ] **Step 4: Verify build**

Run: `dotnet build TerraformRegistry.sln --no-restore 2>&1 | tail -10`
Expected: Build succeeded.

- [ ] **Step 5: Run full test suite**

Run: `dotnet test TerraformRegistry.Tests --no-restore -v n 2>&1 | tail -20`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add TerraformRegistry.Models/Webhook.cs TerraformRegistry.API/Interfaces/IWebhookService.cs TerraformRegistry.PostgreSQL/ TerraformRegistry/Services/SqliteWebhookService.cs TerraformRegistry/Services/SqliteDatabaseService.cs TerraformRegistry/Services/WebhookDispatcher.cs TerraformRegistry/Handlers/WebhookHandlers.cs TerraformRegistry/Handlers/ModuleHandlers.cs TerraformRegistry/Program.cs
git commit -m "feat: add webhook system with CRUD API and event dispatching"
```

---

## Chunk 7: Webhooks Frontend

### Task 27: Create useWebhooks composable

**Files:**
- Create: `TerraformRegistry/web-src/composables/useWebhooks.ts`

- [ ] **Step 1: Create the composable**

```typescript
// NOTE: getAuthHeaders comes from the useAuth() composable, not a named import.
// Call `const { getAuthHeaders } = useAuth()` inside each function body.

export interface Webhook {
  id: string
  userId: string
  url: string
  events: string[]
  isActive: boolean
  createdAt: string
  updatedAt: string
}

export const WEBHOOK_EVENTS = [
  'module.uploaded',
  'module.deleted',
  'module.restored',
  'module.purged',
] as const

export function useWebhooks() {
  const { getAuthHeaders } = useAuth()

  async function listWebhooks(): Promise<Webhook[]> {
    return await $fetch('/api/webhooks', { headers: getAuthHeaders() })
  }

  async function createWebhook(url: string, events: string[], secret?: string): Promise<Webhook> {
    return await $fetch('/api/webhooks', {
      method: 'POST',
      headers: getAuthHeaders(),
      body: { url, events, secret },
    })
  }

  async function updateWebhook(id: string, data: { url?: string, events?: string[], secret?: string, isActive?: boolean }): Promise<Webhook> {
    return await $fetch(`/api/webhooks/${id}`, {
      method: 'PUT',
      headers: getAuthHeaders(),
      body: data,
    })
  }

  async function deleteWebhook(id: string): Promise<void> {
    await $fetch(`/api/webhooks/${id}`, {
      method: 'DELETE',
      headers: getAuthHeaders(),
    })
  }

  return { listWebhooks, createWebhook, updateWebhook, deleteWebhook }
}
```

---

### Task 28: Create webhooks settings page

**Files:**
- Create: `TerraformRegistry/web-src/pages/settings/webhooks.vue`
- Modify: `TerraformRegistry/web-src/layouts/default.vue`

- [ ] **Step 1: Create the page**

Follow the pattern from `settings/api-keys.vue`:
- List webhooks in a table/card layout (URL, events as badges, active toggle, actions)
- Create form: URL input, event checkboxes (using `WEBHOOK_EVENTS` from composable), optional secret field
- Edit modal for updating URL/events/secret/active
- Delete with confirmation modal

- [ ] **Step 2: Add Webhooks link to sidebar**

In `layouts/default.vue`, add an entry to the `settingsLinks` array in `<script setup>` (alongside API Keys and Account). Do NOT add an inline `<NuxtLink>`:

```typescript
{ label: 'Webhooks', icon: 'i-lucide-webhook', to: '/settings/webhooks' }
```

Add this after the API Keys entry in the `settingsLinks` array.

- [ ] **Step 3: Build frontend**

Run: `cd /home/rocky/terraform-registry/TerraformRegistry/web-src && pnpm build 2>&1 | tail -10`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add TerraformRegistry/web-src/composables/useWebhooks.ts TerraformRegistry/web-src/pages/settings/webhooks.vue TerraformRegistry/web-src/layouts/default.vue
git commit -m "feat: add webhooks management UI"
```

---

## Chunk 8: Final Integration Testing & Cleanup

### Task 29: Write integration tests for analytics and webhooks

**Files:**
- Create: `TerraformRegistry.Tests/IntegrationTests/AnalyticsEndpointTests.cs`
- Create: `TerraformRegistry.Tests/IntegrationTests/WebhookEndpointTests.cs`

- [ ] **Step 1: Create AnalyticsEndpointTests**

Test that:
- Unauthenticated requests to `/api/analytics/*` return 401
- Authenticated requests return 200 with expected JSON shapes
- Upload a module, download it, then verify analytics reflect the download

- [ ] **Step 2: Create WebhookEndpointTests**

Test that:
- CRUD operations work (create, list, update, delete)
- Unauthenticated requests return 401
- Users can only manage their own webhooks

- [ ] **Step 3: Run full test suite**

Run: `dotnet test TerraformRegistry.Tests --no-restore -v n 2>&1 | tail -20`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add TerraformRegistry.Tests/IntegrationTests/AnalyticsEndpointTests.cs TerraformRegistry.Tests/IntegrationTests/WebhookEndpointTests.cs
git commit -m "test: add integration tests for analytics and webhook endpoints"
```

### Task 30: Build and verify web frontend

- [ ] **Step 1: Build frontend production bundle**

Run: `cd /home/rocky/terraform-registry/TerraformRegistry/web-src && pnpm build`

- [ ] **Step 2: Copy to web folder if needed**

Follow whatever existing build process copies the built frontend to the `web/` folder.

- [ ] **Step 3: Run lint**

Run: `~/bin/lint-dotnet /home/rocky/terraform-registry`

- [ ] **Step 4: Final commit if any fixes needed**

```bash
git add -A
git commit -m "chore: lint fixes and production frontend build"
```
