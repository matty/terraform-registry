using System.Text.Json;
using Microsoft.Extensions.Options;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Services.ModuleExtraction;

public sealed class ModuleExtractionConfigService(
    IRuntimeSettingsService runtimeSettings,
    IOptions<ModuleExtractionOptions> options) : IModuleExtractionConfigService
{
    private const string SettingKey = "module_extraction";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ModuleExtractionOptions _options = options.Value;

    public async Task<ModuleExtractionRuntimeConfig> GetAsync(CancellationToken cancellationToken)
    {
        var setting = await runtimeSettings.GetAsync(SettingKey, cancellationToken);
        bool? persistedEnabled = null;

        if (setting != null)
        {
            using var json = JsonDocument.Parse(setting.ValueJson);
            if (json.RootElement.TryGetProperty("enabled", out var enabledElement) &&
                (enabledElement.ValueKind == JsonValueKind.True || enabledElement.ValueKind == JsonValueKind.False))
            {
                persistedEnabled = enabledElement.GetBoolean();
            }
        }

        return new ModuleExtractionRuntimeConfig
        {
            StartupEnabled = _options.Enabled,
            PersistedEnabled = persistedEnabled,
            Enabled = persistedEnabled ?? _options.Enabled,
            HasRuntimeOverride = persistedEnabled.HasValue,
            UpdatedAt = setting?.UpdatedAt,
            UpdatedBy = setting?.UpdatedBy
        };
    }

    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
    {
        return (await GetAsync(cancellationToken)).Enabled;
    }

    public async Task<ModuleExtractionRuntimeConfig> SetEnabledAsync(bool enabled, string? updatedBy,
        CancellationToken cancellationToken)
    {
        var valueJson = JsonSerializer.Serialize(new ModuleExtractionRuntimeSetting(enabled), JsonOptions);
        await runtimeSettings.SetAsync(SettingKey, valueJson, updatedBy, cancellationToken);
        return await GetAsync(cancellationToken);
    }

    private sealed record ModuleExtractionRuntimeSetting(bool Enabled);
}
