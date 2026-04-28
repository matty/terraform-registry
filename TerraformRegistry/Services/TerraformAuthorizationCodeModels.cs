namespace TerraformRegistry.Services;

public sealed record TerraformAuthorizationCodeCreateRequest(
    string UserId,
    string ClientId,
    string RedirectUri,
    string State,
    string CodeChallenge,
    string CodeChallengeMethod);

public sealed record TerraformAuthorizationCode(
    string Code,
    string UserId,
    string ClientId,
    string RedirectUri,
    string State,
    string CodeChallenge,
    string CodeChallengeMethod,
    DateTime ExpiresAtUtc);
