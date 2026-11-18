namespace SkyCD.Plugin.Vvv;

internal static class VvvBackupLimits
{
    internal const long MaxBackupBytes = 4L * 1024 * 1024 * 1024;

    internal const int MaxDatabaseRows = 2_000_000;

    internal const int MaxCatalogNodes = 2_000_000;

    internal const int MaxHierarchyDepth = 256;

    internal const int MinSupportedSchemaVersion = 14;

    internal const int MaxSupportedSchemaVersion = 23;
}
