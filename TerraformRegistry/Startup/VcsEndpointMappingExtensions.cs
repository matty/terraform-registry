using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;

namespace TerraformRegistry.Startup;

internal static class VcsEndpointMappingExtensions
{
    public static WebApplication MapVcsEndpoints(this WebApplication app)
    {
        app.MapVcsSourceEndpoints();
        app.MapVcsConnectionEndpoints();
        app.MapGitHubWebhookEndpoint();

        return app;
    }

    private static WebApplication MapVcsSourceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/vcs/sources", (IVcsSourceService vcsService, HttpContext context) =>
                VcsHandlers.ListVcsSources(vcsService, context))
            .WithTags("VCS");

        app.MapPost("/api/vcs/sources",
                (IVcsSourceService vcsService, IVcsConnectionService connectionService,
                        IGitHubVcsService githubService, IAuditService auditService, HttpContext context,
                        HttpRequest request) =>
                    VcsHandlers.CreateVcsSource(vcsService, connectionService, githubService, auditService, context,
                        request))
            .WithTags("VCS");

        app.MapGet("/api/vcs/sources/module/{namespace}/{name}/{provider}",
                (string @namespace, string name, string provider, IVcsSourceService vcsService, HttpContext context) =>
                    VcsHandlers.GetVcsSourceByModule(vcsService, context, @namespace, name, provider))
            .WithTags("VCS");

        app.MapPut("/api/vcs/sources/{id}",
                (Guid id, IVcsSourceService vcsService, IVcsConnectionService connectionService,
                        IAuditService auditService, HttpContext context, HttpRequest request) =>
                    VcsHandlers.UpdateVcsSource(id, vcsService, connectionService, auditService, context, request))
            .WithTags("VCS");

        app.MapDelete("/api/vcs/sources/{id}",
                (Guid id, IVcsSourceService vcsService, IAuditService auditService, HttpContext context) =>
                    VcsHandlers.DeleteVcsSource(id, vcsService, auditService, context))
            .WithTags("VCS");

        app.MapPost("/api/vcs/sources/{id}/sync",
                (Guid id, IVcsSourceService vcsService, IGitHubVcsService githubService, HttpContext context,
                        HttpRequest request) =>
                    VcsHandlers.SyncVcsSource(id, vcsService, githubService, context, request))
            .WithTags("VCS");

        return app;
    }

    private static WebApplication MapVcsConnectionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/vcs-connections", (IVcsConnectionService connectionService, HttpContext context) =>
                VcsHandlers.ListConnections(connectionService, context))
            .WithTags("VCS");

        app.MapPost("/api/admin/vcs-connections",
                (IVcsConnectionService connectionService, IConfiguration config, IAuditService auditService,
                        HttpContext context, HttpRequest request) =>
                    VcsHandlers.CreateConnection(connectionService, config, auditService, context, request))
            .WithTags("VCS");

        app.MapPut("/api/admin/vcs-connections/{id}",
                (Guid id, IVcsConnectionService connectionService, IConfiguration config, IAuditService auditService,
                        HttpContext context, HttpRequest request) =>
                    VcsHandlers.UpdateConnection(id, connectionService, config, auditService, context, request))
            .WithTags("VCS");

        app.MapDelete("/api/admin/vcs-connections/{id}",
                (Guid id, IVcsConnectionService connectionService, IAuditService auditService, HttpContext context) =>
                    VcsHandlers.DeleteConnection(id, connectionService, auditService, context))
            .WithTags("VCS");

        app.MapGet("/api/vcs/connections", (IVcsConnectionService connectionService, HttpContext context) =>
                VcsHandlers.ListConnectionSummaries(connectionService, context))
            .WithTags("VCS");

        return app;
    }

    private static WebApplication MapGitHubWebhookEndpoint(this WebApplication app)
    {
        app.MapPost("/api/vcs/github/webhook", (IGitHubVcsService githubService, HttpContext context) =>
                VcsHandlers.HandleGitHubWebhook(githubService, context))
            .WithTags("VCS");

        return app;
    }
}
