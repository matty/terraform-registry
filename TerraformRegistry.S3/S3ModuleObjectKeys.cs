namespace TerraformRegistry.S3;

internal static class S3ModuleObjectKeys
{
    public static string CreateLogicalObjectKey(string @namespace, string name, string provider, string version)
    {
        return $"{@namespace}/{name}-{provider}-{version}.zip";
    }

    public static string CreateFinalObjectKey(string logicalObjectKey)
    {
        return $"{logicalObjectKey}.{Guid.NewGuid():N}";
    }

    public static string CreateTemporaryObjectKey(string objectKey)
    {
        return $"{objectKey}.{Guid.NewGuid():N}.tmp";
    }
}
