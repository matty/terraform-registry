namespace TerraformRegistry.Services;

public class TerraformLoginOptions
{
    public TimeSpan AuthorizationCodeLifetime { get; set; } = TimeSpan.FromMinutes(5);
}
