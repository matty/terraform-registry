using TerraformRegistry.Services;

namespace TerraformRegistry.Tests.UnitTests;

public class TerraformAuthorizationCodeStoreTests
{
    [Fact]
    public void ConsumeCodeWithMatchingClientAndRedirectUriSucceedsOnlyOnce()
    {
        var store = new InMemoryTerraformAuthorizationCodeStore(new TerraformLoginOptions
        {
            AuthorizationCodeLifetime = TimeSpan.FromMinutes(5)
        });

        var issued = store.Create(new TerraformAuthorizationCodeCreateRequest(
            "user-1",
            "terraform-cli",
            "http://127.0.0.1:10000/",
            "state-123",
            "challenge-123",
            "S256"));

        var first = store.Consume(issued.Code, "terraform-cli", "http://127.0.0.1:10000/");
        var second = store.Consume(issued.Code, "terraform-cli", "http://127.0.0.1:10000/");

        Assert.NotNull(first);
        Assert.Equal("user-1", first!.UserId);
        Assert.Null(second);
    }

    [Fact]
    public void ConsumeCodeWithMismatchedRedirectUriFails()
    {
        var store = new InMemoryTerraformAuthorizationCodeStore(new TerraformLoginOptions
        {
            AuthorizationCodeLifetime = TimeSpan.FromMinutes(5)
        });

        var issued = store.Create(new TerraformAuthorizationCodeCreateRequest(
            "user-1",
            "terraform-cli",
            "http://127.0.0.1:10000/",
            "state-123",
            "challenge-123",
            "S256"));

        var consumed = store.Consume(issued.Code, "terraform-cli", "http://127.0.0.1:10001/");

        Assert.Null(consumed);
    }
}
