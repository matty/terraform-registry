using TerraformRegistry.Models;

namespace TerraformRegistry.Handlers;

/// <summary>
///     Handlers for service discovery
/// </summary>
public static class ServiceDiscoveryHandlers
{
    /// <summary>
    ///     Terraform service discovery endpoint
    /// </summary>
    public static IResult GetServiceDiscovery()
    {
        return Results.Ok(new ServiceDiscovery());
    }
}
