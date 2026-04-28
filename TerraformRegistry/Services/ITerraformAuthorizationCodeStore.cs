namespace TerraformRegistry.Services;

public interface ITerraformAuthorizationCodeStore
{
    TerraformAuthorizationCode Create(TerraformAuthorizationCodeCreateRequest request);
    TerraformAuthorizationCode? Consume(string code, string clientId, string redirectUri);
}
