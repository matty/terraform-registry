using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace TerraformRegistry.Middleware;

public class CustomBearerHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public CustomBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // The middleware has already run and set the user if successful.
        // We just check if the user is set.
        if (Context.User?.Identity?.IsAuthenticated == true)
        {
            // Use the existing principal and return a ticket for this scheme.
            var ticket = new AuthenticationTicket(Context.User, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        // Log why we are failing
        Logger.LogInformation("CustomBearerHandler: User is not authenticated for {Path}. Context.User.Identity.IsAuthenticated: {IsAuthenticated}",
            Context.Request.Path,
            Context.User?.Identity?.IsAuthenticated);

        return Task.FromResult(AuthenticateResult.NoResult());
    }
}
