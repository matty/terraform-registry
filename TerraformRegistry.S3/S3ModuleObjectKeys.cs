namespace TerraformRegistry.S3;

internal static class S3ModuleObjectKeys
{
    public static string CreateLogicalObjectKey(string @namespace, string name, string provider, string version,
        string fileSuffix = ".zip")
    {
        return $"{@namespace}/{name}-{provider}-{version}{fileSuffix}";
    }

    public static string CreateFinalObjectKey(string logicalObjectKey)
    {
        var fileSuffix = logicalObjectKey.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            ? ".tar.gz"
            : ".zip";
        var stem = logicalObjectKey[..^fileSuffix.Length];
        return $"{stem}.{Guid.NewGuid():N}{fileSuffix}";
    }

    public static string CreateTemporaryObjectKey(string objectKey)
    {
        return $"{objectKey}.{Guid.NewGuid():N}.tmp";
    }
}
