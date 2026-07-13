using System.Text.Json;
using TerraformRegistry.API.Interfaces;
using TerraformRegistry.Models;

namespace TerraformRegistry.Services.Mirror;

public sealed class MirrorConfigService(IConfiguration configuration, IRuntimeSettingsService runtimeSettings)
    : IMirrorConfigService
{
    private const string SettingsKey = "mirror.config";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MirrorConfigResponse> GetConfigAsync(CancellationToken cancellationToken)
    {
        var startup = new MirrorOptions();
        configuration.GetSection("Mirror").Bind(startup);

        var stored = await runtimeSettings.GetAsync(SettingsKey, cancellationToken);
        if (stored == null)
        {
            MirrorConfigurationValidator.Validate(startup);
            return ToResponse(startup);
        }

        var runtime = JsonSerializer.Deserialize<MirrorConfigUpdateRequest>(stored.ValueJson, JsonOptions) ?? new();
        var effective = Merge(startup, runtime);
        MirrorConfigurationValidator.Validate(effective);
        return new MirrorConfigResponse
        {
            Effective = effective,
            OperatorEffective = MirrorOperatorOptions.From(effective),
            HasRuntimeOverride = true,
            UpdatedAt = stored.UpdatedAt,
            UpdatedBy = stored.UpdatedBy
        };
    }

    public async Task<MirrorConfigResponse> UpdateConfigAsync(
        MirrorConfigUpdateRequest request,
        string? updatedBy,
        CancellationToken cancellationToken)
    {
        var startup = new MirrorOptions();
        configuration.GetSection("Mirror").Bind(startup);
        var existing = await GetConfigAsync(cancellationToken);
        request.Providers.TrustedSigningKeyIds = [.. existing.Effective.Providers.TrustedSigningKeyIds];
        MirrorConfigurationValidator.Validate(Merge(startup, request));
        var json = JsonSerializer.Serialize(request, JsonOptions);
        await runtimeSettings.SetAsync(SettingsKey, json, updatedBy, cancellationToken);
        return await GetConfigAsync(cancellationToken);
    }

    private static MirrorOptions Merge(MirrorOptions startup, MirrorConfigUpdateRequest runtime)
    {
        startup.Enabled = runtime.Enabled;
        startup.Providers = runtime.Providers;
        startup.Modules = runtime.Modules;
        startup.Limits = runtime.Limits;
        return startup;
    }

    private static MirrorConfigResponse ToResponse(MirrorOptions effective) => new()
    {
        Effective = effective,
        OperatorEffective = MirrorOperatorOptions.From(effective)
    };
}
