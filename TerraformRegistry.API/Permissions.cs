namespace TerraformRegistry.API;

public static class RoleNames
{
    public const string Admin = "admin";
    public const string User = "user";
}

public static class Permissions
{
    public const string ModulesRead = "modules.read";
    public const string ModulesUpload = "modules.upload";
    public const string ModulesDelete = "modules.delete";
    public const string ModulesRestore = "modules.restore";
    public const string ModulesPurge = "modules.purge";
    public const string ModulesDescription = "modules.description";
    public const string ProvidersRead = "providers.read";
    public const string ProvidersPublish = "providers.publish";
    public const string ProvidersDelete = "providers.delete";
    public const string ProvidersPurge = "providers.purge";
    public const string ProvidersKeysManage = "providers.keys.manage";
    public const string ProvidersDescription = "providers.description";
    public const string WebhooksManage = "webhooks.manage";
    public const string VcsManage = "vcs.manage";
    public const string ApiKeysManage = "api_keys.manage";
    public const string ApiKeysShared = "api_keys.shared";
    public const string AnalyticsView = "analytics.view";
    public const string ModuleDocsRead = "module_docs.read";
    public const string ModuleDocsManage = "module_docs.manage";
    public const string ModuleDocsConfigure = "module_docs.configure";
    public const string AdminRoles = "admin.roles";
    public const string AdminUsers = "admin.users";
    public const string AdminAudit = "admin.audit";

    public static readonly string[] All =
    [
        ModulesRead, ModulesUpload, ModulesDelete, ModulesRestore,
        ModulesPurge, ModulesDescription,
        ProvidersRead, ProvidersPublish, ProvidersDelete, ProvidersPurge,
        ProvidersKeysManage, ProvidersDescription,
        WebhooksManage, VcsManage,
        ApiKeysManage, ApiKeysShared, AnalyticsView,
        ModuleDocsRead, ModuleDocsManage, ModuleDocsConfigure,
        AdminRoles, AdminUsers, AdminAudit
    ];

    public static readonly string[] DefaultUserPermissions =
    [
        ModulesRead, ModulesUpload, ModulesDelete, ModulesRestore,
        ModulesDescription,
        ProvidersRead,
        ApiKeysManage, AnalyticsView
    ];
}
