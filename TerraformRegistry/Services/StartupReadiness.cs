using System.Threading;
using TerraformRegistry.API.Interfaces;

namespace TerraformRegistry.Services;

public sealed class StartupReadiness : IStartupReadiness
{
    private int _storageInitialized;

    public bool IsStorageInitialized => Volatile.Read(ref _storageInitialized) == 1;

    public void MarkStorageInitialized() => Volatile.Write(ref _storageInitialized, 1);
}
