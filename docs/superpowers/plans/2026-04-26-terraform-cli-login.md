# Terraform CLI Login Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement official Terraform `terraform login` support using `login.v1` service discovery, OAuth authorization code with PKCE, and fresh 90-day per-user API keys.

**Architecture:** Extend the well-known discovery document with a `login.v1` object, add OAuth-style authorize/token endpoints backed by short-lived authorization codes, and reuse the existing OIDC portal session plus API key storage. Terraform CLI receives a standard OAuth token response and then uses the returned API key against the existing `/v1/*` module endpoints.

**Tech Stack:** ASP.NET Core minimal APIs, existing OIDC services, existing API key persistence, xUnit integration/unit tests, Nuxt frontend login/API-key pages.

---

### Task 1: Discovery Contract

**Files:**
- Modify: `TerraformRegistry.Models/ServiceDiscovery.cs`
- Modify: `TerraformRegistry/Handlers/ServiceDiscoveryHandlers.cs`
- Test: `TerraformRegistry.Tests/IntegrationTests/WellKnownEndpointTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task WellKnown_Endpoint_Exposes_LoginV1_OAuth_Metadata()
{
    var response = await _client.GetAsync("/.well-known/terraform.json");
    var content = await response.Content.ReadAsStringAsync();

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    using var jsonDoc = JsonDocument.Parse(content);
    var root = jsonDoc.RootElement;

    Assert.Equal("/v1/modules/", root.GetProperty("modules.v1").GetString());

    var login = root.GetProperty("login.v1");
    Assert.Equal("terraform-cli", login.GetProperty("client").GetString());
    Assert.Equal("/api/auth/terraform/authorize", login.GetProperty("authz").GetString());
    Assert.Equal("/api/auth/terraform/token", login.GetProperty("token").GetString());
    Assert.Contains(
        login.GetProperty("grant_types").EnumerateArray().Select(x => x.GetString()),
        x => x == "authz_code");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --filter WellKnown_Endpoint_Exposes_LoginV1_OAuth_Metadata -v minimal`
Expected: FAIL because `login.v1` is missing from the discovery document.

- [ ] **Step 3: Write minimal implementation**

```csharp
public sealed class TerraformLoginDiscovery
{
    [JsonPropertyName("client")]
    public string Client { get; set; } = "terraform-cli";

    [JsonPropertyName("grant_types")]
    public string[] GrantTypes { get; set; } = ["authz_code"];

    [JsonPropertyName("authz")]
    public string Authz { get; set; } = "/api/auth/terraform/authorize";

    [JsonPropertyName("token")]
    public string Token { get; set; } = "/api/auth/terraform/token";
}

public class ServiceDiscovery
{
    [JsonPropertyName("modules.v1")]
    public string ModulesV1 { get; set; } = "/v1/modules/";

    [JsonPropertyName("login.v1")]
    public TerraformLoginDiscovery LoginV1 { get; set; } = new();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --filter WellKnown_Endpoint_Exposes_LoginV1_OAuth_Metadata -v minimal`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add TerraformRegistry.Models/ServiceDiscovery.cs TerraformRegistry/Handlers/ServiceDiscoveryHandlers.cs TerraformRegistry.Tests/IntegrationTests/WellKnownEndpointTests.cs
git commit -m "feat: expose terraform login discovery metadata"
```

### Task 2: Authorization Code Service

**Files:**
- Create: `TerraformRegistry/Services/TerraformLoginOptions.cs`
- Create: `TerraformRegistry/Services/TerraformAuthorizationCodeModels.cs`
- Create: `TerraformRegistry/Services/ITerraformAuthorizationCodeStore.cs`
- Create: `TerraformRegistry/Services/InMemoryTerraformAuthorizationCodeStore.cs`
- Test: `TerraformRegistry.Tests/UnitTests/TerraformAuthorizationCodeStoreTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public void ConsumeCode_WithMatchingPkce_AndRedirectUri_Succeeds_OnlyOnce()
{
    var store = new InMemoryTerraformAuthorizationCodeStore(new TerraformLoginOptions { AuthorizationCodeLifetimeMinutes = 5 });
    var code = store.Create(new TerraformAuthorizationCodeRequest(
        "user-1",
        "terraform-cli",
        "http://127.0.0.1:10000/",
        "state-123",
        "challenge",
        "S256"));

    var first = store.Consume(code.Code, "terraform-cli", "http://127.0.0.1:10000/");
    var second = store.Consume(code.Code, "terraform-cli", "http://127.0.0.1:10000/");

    Assert.NotNull(first);
    Assert.Null(second);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --filter ConsumeCode_WithMatchingPkce_AndRedirectUri_Succeeds_OnlyOnce -v minimal`
Expected: FAIL because the store and request types do not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
public sealed record TerraformAuthorizationCodeRequest(
    string UserId,
    string ClientId,
    string RedirectUri,
    string State,
    string CodeChallenge,
    string CodeChallengeMethod);

public interface ITerraformAuthorizationCodeStore
{
    TerraformAuthorizationCode Create(TerraformAuthorizationCodeRequest request);
    TerraformAuthorizationCode? Consume(string code, string clientId, string redirectUri);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --filter TerraformAuthorizationCodeStoreTests -v minimal`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add TerraformRegistry/Services/TerraformLoginOptions.cs TerraformRegistry/Services/TerraformAuthorizationCodeModels.cs TerraformRegistry/Services/ITerraformAuthorizationCodeStore.cs TerraformRegistry/Services/InMemoryTerraformAuthorizationCodeStore.cs TerraformRegistry.Tests/UnitTests/TerraformAuthorizationCodeStoreTests.cs
git commit -m "feat: add terraform oauth authorization code store"
```

### Task 3: Authorize Endpoint And OIDC Continuation

**Files:**
- Modify: `TerraformRegistry/Handlers/AuthHandlers.cs`
- Modify: `TerraformRegistry/Program.cs`
- Modify: `TerraformRegistry/web-src/composables/useAuth.ts`
- Modify: `TerraformRegistry/web-src/pages/login.vue`
- Test: `TerraformRegistry.Tests/IntegrationTests/TerraformLoginAuthorizationTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task TerraformAuthorize_WithoutPortalSession_Redirects_To_Login_WithContinuation()
{
    var response = await _client.GetAsync("/api/auth/terraform/authorize?client_id=terraform-cli&redirect_uri=http://127.0.0.1:10000/&response_type=code&state=abc&code_challenge=xyz&code_challenge_method=S256");

    Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    Assert.StartsWith("/login?", response.Headers.Location!.OriginalString);
    Assert.Contains("returnTo=", response.Headers.Location!.OriginalString);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --filter TerraformAuthorize_WithoutPortalSession_Redirects_To_Login_WithContinuation -v minimal`
Expected: FAIL because the authorize endpoint does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
app.MapGet("/api/auth/terraform/authorize", (HttpContext context) =>
    AuthHandlers.BeginTerraformAuthorization(context));

const loginWithOidc = (provider: string, returnTo?: string) => {
  const query = returnTo ? `?returnTo=${encodeURIComponent(returnTo)}` : "";
  window.location.href = `/api/auth/login/${provider}${query}`;
};
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --filter TerraformLoginAuthorizationTests -v minimal`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add TerraformRegistry/Handlers/AuthHandlers.cs TerraformRegistry/Program.cs TerraformRegistry/web-src/composables/useAuth.ts TerraformRegistry/web-src/pages/login.vue TerraformRegistry.Tests/IntegrationTests/TerraformLoginAuthorizationTests.cs
git commit -m "feat: add terraform authorize endpoint and login continuation"
```

### Task 4: Token Exchange And CLI API Keys

**Files:**
- Modify: `TerraformRegistry/Services/IApiKeyService.cs`
- Modify: `TerraformRegistry/Services/ApiKeyService.cs`
- Modify: `TerraformRegistry/Handlers/AuthHandlers.cs`
- Modify: `TerraformRegistry/Program.cs`
- Test: `TerraformRegistry.Tests/IntegrationTests/TerraformLoginTokenTests.cs`
- Test: `TerraformRegistry.Tests/IntegrationTests/ApiKeyExpirationTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task TerraformToken_WithValidCodeAndPkce_ReturnsNewApiToken_ValidForModules()
{
    var (client, verifier, redirectUri, state, code) = await StartAuthorizedTerraformLoginAsync();

    var tokenResponse = await client.PostAsync("/api/auth/terraform/token",
        new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "terraform-cli",
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["redirect_uri"] = redirectUri
        }));

    Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --filter TerraformToken_WithValidCodeAndPkce_ReturnsNewApiToken_ValidForModules -v minimal`
Expected: FAIL because the token endpoint and CLI-specific API key creation do not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
public Task<(string RawToken, ApiKey Key)> CreateTerraformCliApiKeyAsync(string userId, string description, DateTime expiresAt);

return Results.Ok(new
{
    access_token = rawToken,
    token_type = "Bearer"
});
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --filter TerraformLoginTokenTests -v minimal`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add TerraformRegistry/Services/IApiKeyService.cs TerraformRegistry/Services/ApiKeyService.cs TerraformRegistry/Handlers/AuthHandlers.cs TerraformRegistry/Program.cs TerraformRegistry.Tests/IntegrationTests/TerraformLoginTokenTests.cs TerraformRegistry.Tests/IntegrationTests/ApiKeyExpirationTests.cs
git commit -m "feat: exchange terraform oauth codes for cli api keys"
```

### Task 5: UI Metadata And Final Verification

**Files:**
- Modify: `TerraformRegistry/web-src/pages/settings/api-keys.vue`
- Modify: `README.md`
- Test: `TerraformRegistry.Tests/IntegrationTests/StaticTokenAuthorizationTests.cs`
- Test: `TerraformRegistry.Tests/IntegrationTests/TerraformLoginTokenTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async Task RepeatedTerraformLogins_IssueDistinctTokens()
{
    var first = await CompleteTerraformLoginAsync();
    var second = await CompleteTerraformLoginAsync();

    Assert.NotEqual(first.AccessToken, second.AccessToken);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --filter RepeatedTerraformLogins_IssueDistinctTokens -v minimal`
Expected: FAIL until the token issuance path always creates a fresh key.

- [ ] **Step 3: Write minimal implementation**

```vue
<span v-if="key.expiresAt" class="text-xs text-neutral-500">
  Expires {{ new Date(key.expiresAt).toLocaleDateString() }}
</span>
```

```md
terraform login registry.company.com
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TerraformRegistry.Tests/TerraformRegistry.Tests.csproj --filter "WellKnown_Endpoint_Exposes_LoginV1_OAuth_Metadata|TerraformLoginAuthorizationTests|TerraformLoginTokenTests|StaticTokenAuthorizationTests" -v minimal`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add TerraformRegistry/web-src/pages/settings/api-keys.vue README.md TerraformRegistry.Tests/IntegrationTests/StaticTokenAuthorizationTests.cs TerraformRegistry.Tests/IntegrationTests/TerraformLoginTokenTests.cs
git commit -m "feat: document and surface terraform cli login tokens"
```
