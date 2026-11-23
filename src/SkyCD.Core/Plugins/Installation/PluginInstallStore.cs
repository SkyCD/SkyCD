using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Installation.Exceptions;

namespace SkyCD.Core.Plugins.Installation;

/// <summary>
/// File-system access for receipts, pending operation markers, plugin folders and staging directories.
/// </summary>
internal sealed class PluginInstallStore(PluginInstallLayout layout)
{
    private const int MaxReceiptScanDepth = 8;

    private const int MaxInstallBackups = 2;

    private const string BackupFolderPrefix = ".backup-";

    private const string ReplaceFolderPrefix = ".replace-";

    private const string UninstallFolderPrefix = ".uninstall-";

    public PluginInstallLayout Layout { get; } = layout;

    public string GetInstallFolder(string pluginId) => Layout.ResolveInstallFolder(pluginId);

    public bool HasReceipt(string installFolder) =>
        File.Exists(Path.Combine(installFolder, PluginInstallLayout.ReceiptFileName));

    public PluginInstallReceipt? TryReadReceipt(string installFolder) =>
        PluginInstallJson.TryRead<PluginInstallReceipt>(Path.Combine(installFolder, PluginInstallLayout.ReceiptFileName));

    public PluginInstallReceipt ReadReceipt(string pluginId, string installFolder) =>
        PluginInstallJson.ReadReceipt(pluginId,
            Path.Combine(installFolder, PluginInstallLayout.ReceiptFileName));

    public void WriteReceipt(string installFolder, PluginInstallReceipt receipt) =>
        PluginInstallJson.Write(Path.Combine(installFolder, PluginInstallLayout.ReceiptFileName), receipt);

    public PluginInstallOperation? TryReadPending(string pluginId) =>
        PluginInstallJson.TryRead<PluginInstallOperation>(Layout.ResolvePendingPath(pluginId));

    public void WritePending(PluginInstallOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!PluginInstallLayout.IsValidPluginId(operation.PluginId))
        {
            throw new PluginInstallDestinationException(operation.PluginId,
                "the plugin id is not a safe folder name");
        }

        var path = Layout.ResolvePendingPath(operation.PluginId);
        if (!Layout.IsWithinStaging(path) ||
            !string.Equals(Path.GetDirectoryName(path), Path.Combine(Layout.StagingRoot, "pending"),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new PluginInstallDestinationException(operation.PluginId,
                "the pending marker would be written outside the staging area");
        }

        PluginInstallJson.Write(path, operation);
    }

    public void DeletePending(string pluginId)
    {
        var path = Layout.ResolvePendingPath(pluginId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>All pending operation markers, ordered by plugin id for deterministic startup application.</summary>
    public IReadOnlyList<string> ListPendingPluginIds()
    {
        var directory = Path.Combine(Layout.StagingRoot, "pending");
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(static path => Path.GetFileNameWithoutExtension(path))
            .OrderBy(static pluginId => pluginId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Reads every catalog receipt below the plugin root, keyed by plugin id. Receipts deeper than
    /// <see cref="MaxReceiptScanDepth"/> are ignored because they cannot be a catalog install folder itself.
    /// </summary>
    public IReadOnlyDictionary<string, (string InstallFolder, PluginInstallReceipt Receipt)> ReadOwnedReceipts()
    {
        var receipts = new Dictionary<string, (string, PluginInstallReceipt)>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(Layout.PluginRoot))
        {
            return receipts;
        }

        foreach (var receiptPath in EnumerateReceiptPaths(Layout.PluginRoot, 0))
        {
            var installFolder = Path.GetDirectoryName(receiptPath)!;
            if (!Layout.IsStrictlyWithinPluginRoot(installFolder) ||
                !string.Equals(Path.GetDirectoryName(installFolder), Layout.PluginRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                // Only the immediate plugin folder is catalog-owned; nested copies are ignored.
                continue;
            }

            var receipt = PluginInstallJson.TryRead<PluginInstallReceipt>(receiptPath);
            if (receipt is null)
            {
                continue;
            }

            receipts[Path.GetFileName(installFolder)] = (installFolder, receipt);
        }

        return receipts;
    }

    /// <summary>Lists the immediate child folder names of the plugin root (install folders and stale backups).</summary>
    public IReadOnlyList<string> ListPluginFolderNames()
    {
        if (!Directory.Exists(Layout.PluginRoot))
        {
            return [];
        }

        return Directory.EnumerateDirectories(Layout.PluginRoot, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .OfType<string>()
            .ToList();
    }

    /// <summary>Deletes every staging operation directory except the ones referenced by a pending marker in use.</summary>
    public void CleanStaging(IEnumerable<string> protectedDirectories)
    {
        if (!Directory.Exists(Layout.StagingRoot))
        {
            return;
        }

        var protectedPaths = protectedDirectories
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path!)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in Directory.EnumerateDirectories(Layout.StagingRoot))
        {
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if (string.Equals(Path.GetFileName(path), "pending", StringComparison.OrdinalIgnoreCase) ||
                protectedPaths.Contains(path))
            {
                continue;
            }

            TryDeleteDirectory(path);
        }
    }

    public bool IsProtectedStaging(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(directory);
        return Layout.IsWithinStaging(fullPath) &&
               !string.Equals(Path.TrimEndingDirectorySeparator(fullPath), Layout.StagingRoot,
                   StringComparison.OrdinalIgnoreCase);
    }

    public void TryDeleteStaging(string? directory)
    {
        if (IsProtectedStaging(directory))
        {
            TryDeleteDirectory(Path.GetFullPath(directory!));
        }
    }

    /// <summary>Moves a fully prepared directory into the install folder with a recoverable two-step rename.</summary>
    public void InstallFromStaging(string pluginId, string preparedDirectory, string installFolder)
    {
        var stagingPrefix = Path.TrimEndingDirectorySeparator(Layout.StagingRoot) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(preparedDirectory).StartsWith(stagingPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new PluginInstallDestinationException(pluginId,
                "the prepared content is outside the staging area");
        }

        if (!Layout.IsStrictlyWithinPluginRoot(installFolder) ||
            !string.Equals(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(installFolder)),
                Layout.PluginRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new PluginInstallDestinationException(pluginId, "the install folder is outside the plugin root");
        }

        var parent = Path.GetDirectoryName(installFolder)!;
        Directory.CreateDirectory(parent);
        var replaceFolder = Path.Combine(parent, $"{ReplaceFolderPrefix}{pluginId}-{Guid.NewGuid():N}");
        try
        {
            Directory.Move(preparedDirectory, replaceFolder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PluginInstallDestinationException(pluginId,
                "the staged content could not be moved into the plugin folder", exception);
        }

        try
        {
            if (Directory.Exists(installFolder))
            {
                Directory.Delete(installFolder, recursive: true);
            }

            Directory.Move(replaceFolder, installFolder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDeleteDirectory(replaceFolder);
            throw new PluginInstallDestinationException(pluginId,
                "the plugin folder could not be replaced", exception);
        }
    }

    /// <summary>
    /// Returns content left in a transient <c>.replace-</c> folder by an interrupted two-step move and removes
    /// older leftovers; <c>null</c> when no such folder exists.
    /// </summary>
    public string? TryTakeInterruptedContent(string pluginId)
    {
        if (!Directory.Exists(Layout.PluginRoot))
        {
            return null;
        }

        var prefix = $"{ReplaceFolderPrefix}{pluginId}-";
        var candidates = Directory.EnumerateDirectories(Layout.PluginRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(path => IsNonceFolderOf(path, prefix))
            .OrderByDescending(static path => Directory.GetLastWriteTimeUtc(path))
            .ToList();
        foreach (var stale in candidates.Skip(1))
        {
            TryDeleteDirectory(stale);
        }

        return candidates.FirstOrDefault();
    }

    /// <summary>Moves a catalog-owned install folder into a side backup; returns the backup path.</summary>
    public string MoveToBackup(string pluginId, string installFolder)
    {
        var parent = Path.GetDirectoryName(installFolder)!;
        var backupFolder = Path.Combine(parent, $"{BackupFolderPrefix}{pluginId}-{Guid.NewGuid():N}");
        try
        {
            Directory.Move(installFolder, backupFolder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PluginInstallDestinationException(pluginId,
                "the active plugin files are in use or cannot be moved", exception);
        }

        return backupFolder;
    }

    public void RestoreBackup(string pluginId, string backupFolder, string installFolder)
    {
        try
        {
            if (Directory.Exists(installFolder))
            {
                Directory.Delete(installFolder, recursive: true);
            }

            Directory.Move(backupFolder, installFolder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PluginInstallDestinationException(pluginId,
                "the previous plugin version could not be restored", exception);
        }
    }

    /// <summary>
    /// Renames a catalog install folder to a transient sibling so a partial delete never leaves a half-removed
    /// folder in the plugin root; the caller deletes the returned folder or moves it back.
    /// </summary>
    public string MoveToUninstallFolder(string pluginId, string installFolder)
    {
        var parent = Path.GetDirectoryName(installFolder)!;
        var trashFolder = Path.Combine(parent, $"{UninstallFolderPrefix}{pluginId}-{Guid.NewGuid():N}");
        try
        {
            Directory.Move(installFolder, trashFolder);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new PluginInstallDestinationException(pluginId,
                "the plugin files are in use or cannot be moved", exception);
        }

        return trashFolder;
    }

    /// <summary>Removes the side backups kept for replaced installs; used after the plugin is uninstalled.</summary>
    public void DeleteBackups(string pluginId)
    {
        if (!Directory.Exists(Layout.PluginRoot))
        {
            return;
        }

        var prefix = $"{BackupFolderPrefix}{pluginId}-";
        foreach (var path in Directory.EnumerateDirectories(Layout.PluginRoot, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => IsNonceFolderOf(path, prefix)))
        {
            TryDeleteDirectory(path);
        }
    }

    /// <summary>Keeps the newest <see cref="MaxInstallBackups"/> backups of the plugin and removes older ones.</summary>
    public void PruneBackups(string pluginId, string? currentBackup)
    {
        var parent = Path.GetDirectoryName(GetInstallFolder(pluginId))!;
        if (!Directory.Exists(parent))
        {
            return;
        }

        // Backup folder names are '.backup-<pluginId>-<nonce>', so a plugin id containing '-' must match the
        // nonce suffix instead of a glob that could also match a neighbouring '<pluginId>-x' plugin.
        var prefix = $"{BackupFolderPrefix}{pluginId}-";
        var backups = Directory.EnumerateDirectories(parent, "*", SearchOption.TopDirectoryOnly)
            .Where(path => IsNonceFolderOf(path, prefix))
            .OrderByDescending(static path => Directory.GetLastWriteTimeUtc(path))
            .ToList();

        for (var index = 0; index < backups.Count; index++)
        {
            var backup = backups[index];
            var isCurrent = currentBackup is not null &&
                            string.Equals(Path.GetFullPath(backup), Path.GetFullPath(currentBackup),
                                StringComparison.OrdinalIgnoreCase);
            if (!isCurrent && index >= MaxInstallBackups)
            {
                TryDeleteDirectory(backup);
            }
        }
    }

    // Matches '<prefix><nonce>' when the plugin id inside the prefix may itself contain '-'.
    private static bool IsNonceFolderOf(string path, string prefix)
    {
        var name = Path.GetFileName(path);
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // The nonce is 'N' formatted, i.e. exactly 32 hexadecimal characters.
        var suffix = name[prefix.Length..];
        return suffix.Length == 32 && suffix.All(char.IsAsciiHexDigit);
    }

    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked leftover is removed by the next startup cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // A locked leftover is removed by the next startup cleanup.
        }
    }

    /// <summary>Finds the plugin assembly for a receipt entry, following the current package layout.</summary>
    public string? ResolveEntryAssemblyPath(string installFolder, PluginInstallReceipt receipt)
    {
        var candidate = Path.Combine(installFolder,
            receipt.EntryAssemblyPath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(candidate))
        {
            return candidate;
        }

        if (!Directory.Exists(installFolder))
        {
            return null;
        }

        return Directory.EnumerateFiles(installFolder, receipt.EntryAssemblyName + ".dll", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    /// <summary>Plugin ids declared by a pending package, falling back to its entry assembly name.</summary>
    public static IReadOnlyList<string> ClaimedIds(PluginInstallOperation operation)
    {
        if (operation.ClaimedPluginIds.Count > 0)
        {
            return operation.ClaimedPluginIds;
        }

        var identity = operation.EntryAssemblyName ?? operation.PluginId;
        return [identity];
    }

    private static IEnumerable<string> EnumerateReceiptPaths(string directory, int depth)
    {
        if (depth > MaxReceiptScanDepth)
        {
            yield break;
        }

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var entry in entries)
        {
            if (Directory.Exists(entry))
            {
                foreach (var nested in EnumerateReceiptPaths(entry, depth + 1))
                {
                    yield return nested;
                }
            }
            else if (string.Equals(Path.GetFileName(entry), PluginInstallLayout.ReceiptFileName,
                         StringComparison.OrdinalIgnoreCase))
            {
                yield return entry;
            }
        }
    }
}
