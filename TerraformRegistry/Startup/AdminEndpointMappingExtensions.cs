using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Handlers;
using TerraformRegistry.Models;
using TerraformRegistry.Services;
using TerraformRegistry.Services.ModuleExtraction;

namespace TerraformRegistry.Startup;

internal static class AdminEndpointMappingExtensions
{
    public static WebApplication MapAdminEndpoints(this WebApplication app)
    {
        app.MapWebhookEndpoints();
        app.MapRoleEndpoints();
        app.MapAuditEndpoints();
        app.MapUserEndpoints();
        app.MapNamespaceEndpoints();
        app.MapModuleDocsEndpoints();
        app.MapMirrorAdminEndpoints();

        return app;
    }

    private static WebApplication MapWebhookEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/webhooks", (IWebhookService webhookService, HttpContext context) =>
                WebhookHandlers.ListWebhooks(webhookService, context))
            .WithTags("Webhooks");

        app.MapPost("/api/admin/webhooks",
                (IWebhookService webhookService, IAuditService auditService, HttpContext context,
                        HttpRequest request) =>
                    WebhookHandlers.CreateWebhook(webhookService, auditService, context, request))
            .WithTags("Webhooks");

        app.MapPut("/api/admin/webhooks/{id}",
                (Guid id, IWebhookService webhookService, IAuditService auditService, HttpContext context,
                        HttpRequest request) =>
                    WebhookHandlers.UpdateWebhook(id, webhookService, auditService, context, request))
            .WithTags("Webhooks");

        app.MapDelete("/api/admin/webhooks/{id}",
                (Guid id, IWebhookService webhookService, IAuditService auditService, HttpContext context) =>
                    WebhookHandlers.DeleteWebhook(id, webhookService, auditService, context))
            .WithTags("Webhooks");

        app.MapPost("/api/admin/webhooks/{id}/test",
                (Guid id, IWebhookService webhookService, WebhookDispatcher dispatcher, HttpContext context) =>
                    WebhookHandlers.TestWebhook(id, webhookService, dispatcher, context))
            .WithTags("Webhooks");

        return app;
    }

    private static WebApplication MapRoleEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/roles", (IRoleService roleService, HttpContext context) =>
                AdminHandlers.ListRoles(roleService, context))
            .WithTags("Admin");

        app.MapPost("/api/admin/roles",
                (IRoleService roleService, IAuditService auditService, HttpContext context, HttpRequest request) =>
                    AdminHandlers.CreateRole(roleService, auditService, context, request))
            .WithTags("Admin");

        app.MapPut("/api/admin/roles/{id}",
                (Guid id, IRoleService roleService, IAuditService auditService, HttpContext context,
                        HttpRequest request) =>
                    AdminHandlers.UpdateRole(id, roleService, auditService, context, request))
            .WithTags("Admin");

        app.MapDelete("/api/admin/roles/{id}",
                (Guid id, IRoleService roleService, IAuditService auditService, HttpContext context) =>
                    AdminHandlers.DeleteRole(id, roleService, auditService, context))
            .WithTags("Admin");

        return app;
    }

    private static WebApplication MapAuditEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/audit",
                (IAuditService auditService, HttpContext context, string? action, string? userId,
                        string? resourceType, DateTime? from, DateTime? to, int limit = 50, int offset = 0) =>
                    AuditHandlers.ListAuditLogs(auditService, context, action, userId, resourceType, from, to, limit,
                        offset))
            .WithTags("Admin");

        app.MapGet("/api/admin/audit/{id}",
                (Guid id, IAuditService auditService, HttpContext context) =>
                    AuditHandlers.GetAuditLog(id, auditService, context))
            .WithTags("Admin");

        return app;
    }

    private static WebApplication MapUserEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/users",
                (IDatabaseService dbService, IPermissionService permService, HttpContext context) =>
                    AdminHandlers.ListUsers(dbService, permService, context))
            .WithTags("Admin");

        app.MapGet("/api/admin/users/{userId}/roles",
                (string userId, IPermissionService permService, HttpContext context) =>
                    AdminHandlers.GetUserRoles(userId, permService, context))
            .WithTags("Admin");

        app.MapPost("/api/admin/users/{userId}/roles",
                (string userId, IPermissionService permService, IAuditService auditService, HttpContext context,
                        HttpRequest request) =>
                    AdminHandlers.AssignUserRole(userId, permService, auditService, context, request))
            .WithTags("Admin");

        app.MapDelete("/api/admin/users/{userId}/roles/{roleId}",
                (string userId, Guid roleId, IPermissionService permService, IRoleService roleService,
                        IAuditService auditService, HttpContext context) =>
                    AdminHandlers.RemoveUserRole(userId, roleId, permService, roleService, auditService, context))
            .WithTags("Admin");

        return app;
    }

    private static WebApplication MapNamespaceEndpoints(this WebApplication app)
    {
        app.MapPut("/api/admin/namespaces/{namespace}/maintainer",
                (string @namespace, INamespaceMaintainerStore maintainerStore, IDatabaseService dbService,
                        IAuditService auditService, HttpContext context, HttpRequest request) =>
                    AdminHandlers.AssignNamespaceMaintainer(@namespace, maintainerStore, dbService, auditService,
                        context, request))
            .WithTags("Admin");

        return app;
    }

    private static WebApplication MapModuleDocsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/module-docs/summary",
                (IDatabaseService dbService, IModuleExtractionConfigService configService, HttpContext context) =>
                    ModuleDocsHandlers.GetSummary(dbService, configService, context))
            .WithTags("Module Docs");

        app.MapGet("/api/admin/module-docs/modules",
                (IDatabaseService dbService, HttpContext context, string? status, string? q, int limit = 50,
                        int offset = 0) =>
                    ModuleDocsHandlers.ListModules(dbService, context, status, q, limit, offset))
            .WithTags("Module Docs");

        app.MapGet("/api/admin/module-docs/modules/{namespace}/{name}/{provider}/{version}",
                (string @namespace, string name, string provider, string version, IDatabaseService dbService,
                        HttpContext context) =>
                    ModuleDocsHandlers.GetModuleDetail(@namespace, name, provider, version, dbService, context))
            .WithTags("Module Docs");

        app.MapPost("/api/admin/module-docs/modules/{namespace}/{name}/{provider}/{version}/regenerate-llm",
                (string @namespace, string name, string provider, string version,
                        IModuleExtractionService extractionService, IDatabaseService dbService,
                        IAuditService auditService, IModuleExtractionConfigService configService,
                        HttpContext context) =>
                    ModuleDocsHandlers.RegenerateLlmContext(@namespace, name, provider, version, extractionService,
                        dbService, auditService, configService, context))
            .WithTags("Module Docs");

        app.MapPost("/api/admin/module-docs/modules/{namespace}/{name}/{provider}/{version}/requeue",
                (string @namespace, string name, string provider, string version,
                        IModuleExtractionService extractionService, IDatabaseService dbService,
                        IAuditService auditService, IModuleExtractionConfigService configService,
                        HttpContext context) =>
                    ModuleDocsHandlers.Requeue(@namespace, name, provider, version, extractionService, dbService,
                        auditService, configService, context))
            .WithTags("Module Docs");

        app.MapPost("/api/admin/module-docs/backfill",
                (IModuleExtractionService extractionService, IModuleExtractionConfigService configService,
                        IAuditService auditService, HttpContext context, HttpRequest request) =>
                    ModuleDocsHandlers.Backfill(extractionService, configService, auditService, context, request))
            .WithTags("Module Docs");

        app.MapGet("/api/admin/module-docs/config",
                (IModuleExtractionConfigService configService, HttpContext context) =>
                    ModuleDocsHandlers.GetConfig(configService, context))
            .WithTags("Module Docs");

        app.MapPut("/api/admin/module-docs/config",
                (IModuleExtractionConfigService configService, IAuditService auditService, HttpContext context,
                        HttpRequest request) =>
                    ModuleDocsHandlers.UpdateConfig(configService, auditService, context, request))
            .WithTags("Module Docs");

        return app;
    }

    private static WebApplication MapMirrorAdminEndpoints(this WebApplication app)
    {
        app.MapGet("/api/admin/mirror/providers",
                (IProviderMirrorRepository providerRepository, HttpContext context, string? q, string? state,
                        int limit = 50, int offset = 0) =>
                    MirrorAdminHandlers.ListProviderCache(providerRepository, context, q, state, limit, offset))
            .WithTags("Mirror");

        app.MapGet("/api/admin/mirror/modules",
                (IModuleMirrorRepository moduleRepository, HttpContext context, string? q, string? state,
                        int limit = 50, int offset = 0) =>
                    MirrorAdminHandlers.ListModuleCache(moduleRepository, context, q, state, limit, offset))
            .WithTags("Mirror");

        app.MapGet("/api/admin/mirror/leases",
                (IMirrorLeaseRepository leaseRepository, HttpContext context, int limit = 50, int offset = 0) =>
                    MirrorAdminHandlers.ListLeases(leaseRepository, context, limit, offset))
            .WithTags("Mirror");

        app.MapGet("/api/admin/mirror/config",
                (IMirrorConfigService configService, HttpContext context) =>
                    MirrorAdminHandlers.GetConfig(configService, context))
            .WithTags("Mirror");

        app.MapPut("/api/admin/mirror/config",
                (IMirrorConfigService configService, IAuditService auditService, HttpContext context,
                        MirrorConfigUpdateRequest request) =>
                    MirrorAdminHandlers.UpdateConfig(configService, auditService, context, request))
            .WithTags("Mirror");

        return app;
    }
}
