using System;
using System.IO;

namespace SkyCD.Plugin.Runtime.Installation;

/// <summary>
/// Location rules for catalog-owned plugin folders and the bounded staging area.
/// </summary>
/// <remarks>
/// Plugin root layout (assembly discovery scans every folder recursively):
/// <code>
/// &lt;plugin root&gt;/&lt;plugin id&gt;/                    install folder of one catalog plugin
/// &lt;plugin root&gt;/&lt;plugin id&gt;/.skycd-install.json    receipt; marks the folder as catalog-owned
/// &lt;plugin root&gt;/&lt;plugin id&gt;/&lt;assembly&gt;.dll          entry assembly and bundled private dependencies
/// </code>
/// Staging layout (never below the plugin root):
/// <code>
/// &lt;staging root&gt;/pending/&lt;plugin id&gt;.json      pending operation marker
/// &lt;staging root&gt;/&lt;staging nonce&gt;/content/        package content while it is downloaded and validated
/// &lt;staging root&gt;/&lt;staging nonce&gt;/prepared/       validated content referenced by the pending marker
/// </code>
/// </remarks>
public sealed class PluginInstallLayout(string pluginRoot, string stagingRoot)
{
    public const string ReceiptFileName = ".skycd-install.json";

    public const string PendingFileName = "pending.json";

    /// <summary>Folder below the plugin root scanned by assembly discovery.</summary>
    public string PluginRoot { get; } = NormalizeRoot(pluginRoot);

    /// <summary>Hidden working area outside the plugin root; removed after activation, rollback or failure.</summary>
    public string StagingRoot { get; } = NormalizeRoot(stagingRoot);

    /// <summary>Folder that contains backups of replaced installs and atomic folder moves that must stay on one volume.</summary>
    public string WorkingRoot => PluginRoot;

    /// <summary>Resolves the install folder of a plugin whose directory name is the plugin id.</summary>
    public string ResolveInstallFolder(string pluginId) =>
        Path.GetFullPath(Path.Combine(PluginRoot, pluginId));

    public string ResolveReceiptPath(string installFolder) => Path.Combine(installFolder, ReceiptFileName);

    public string ResolvePendingPath(string pluginId) => Path.Combine(StagingRoot, "pending", pluginId + ".json");

    /// <summary>True when <paramref name="path"/> is the plugin root itself or a descendant of it.</summary>
    public bool IsWithinPluginRoot(string path) => IsWithin(PluginRoot, path);

    /// <summary>True when <paramref name="path"/> is a strict descendant of the plugin root.</summary>
    public bool IsStrictlyWithinPluginRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.Length > PluginRoot.Length && IsWithin(PluginRoot, fullPath);
    }

    /// <summary>True when <paramref name="path"/> is the staging root or a descendant of it.</summary>
    public bool IsWithinStaging(string path) => IsWithin(StagingRoot, path);

    /// <summary>Fails configuration where staging would be scanned by discovery or nested in it.</summary>
    public void EnsureStagingOutsidePluginRoot()
    {
        if (IsWithin(PluginRoot, StagingRoot) || IsWithin(StagingRoot, PluginRoot))
        {
            throw new ArgumentException(
                $"Staging root '{StagingRoot}' must be outside the plugin root '{PluginRoot}'.",
                nameof(stagingRoot));
        }
    }

    /// <summary>Creates a unique per-operation staging directory below the staging root.</summary>
    public string CreateOperationStagingDirectory()
    {
        Directory.CreateDirectory(StagingRoot);
        var directory = Path.Combine(StagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Validates a catalog plugin id as a single safe directory name below the plugin root.</summary>
    public static bool IsValidPluginId(string? pluginId) =>
        !string.IsNullOrWhiteSpace(pluginId) &&
        pluginId.Length <= 128 &&
        !pluginId.TrimStart().StartsWith('.') &&
        pluginId.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        pluginId.IndexOfAny(['/', '\\', ':']) < 0 &&
        pluginId.Trim() == pluginId &&
        pluginId != "." &&
        pluginId != "..";

    internal static string NormalizeRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A plugin root path is required.", nameof(path));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static bool IsWithin(string root, string path)
    {
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
               fullPath.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
