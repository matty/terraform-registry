namespace TerraformRegistry.Handlers;

using Models;

/// <summary>
/// Handlers for service discovery
/// </summary>
public static class ServiceDiscoveryHandlers
{
    /// <summary>
    /// Terraform service discovery endpoint
    /// </summary>
    public static IResult GetServiceDiscovery()
    {
        return Results.Ok(new ServiceDiscovery());
    }
}