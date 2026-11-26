using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AsyncKeyedLock;
using Microsoft.Extensions.Logging;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Installation.Exceptions;

namespace SkyCD.Core.Plugins.Installation;

/// <summary>
/// Catalog plugin installer: stages packages outside plugin discovery, activates exactly one pending
/// operation per plugin on the next startup before discovery, and keeps the previous version recoverable.
/// </summary>
/// <remarks>
/// Every operation for one plugin runs under a per-plugin asynchronous lock; retries of an identical staged
/// request return the existing marker without downloading again. Activation moves the active catalog folder to a
/// side backup before replacing it and restores that backup when validation or replacement fails.
/// </remarks>
public sealed class PluginInstallService(
    PluginInstallLayout layout,
    Version hostVersion,
    ILogger<PluginInstallService> logger,
    HttpMessageHandler? httpHandler = null,
    TimeProvider? timeProvider = null,
    TimeSpan? downloadTimeout = null,
    long maxPackageBytes = PluginInstallService.DefaultMaxPackageBytes)
    : IPluginPackageInstaller, IPluginInstallStateSource, IPluginPackageUninstaller
{
    public const long DefaultMaxPackageBytes = 64L * 1024 * 1024;

    public static readonly TimeSpan DefaultDownloadTimeout = TimeSpan.FromSeconds(60);

    private readonly PluginInstallStore store = new(layout);
    private readonly PluginPackageStager stager = new(httpHandler, hostVersion,
        downloadTimeout ?? DefaultDownloadTimeout, maxPackageBytes);
    private readonly AsyncKeyedLocker<string> operationLock = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim applyLock = new(1, 1);
    private readonly object stagingGate = new();
    private readonly HashSet<string> activeOperationDirectories = new(StringComparer.OrdinalIgnoreCase);

    public Version HostVersion => hostVersion;

    /// <summary>Staging and install layout used by this service.</summary>
    public PluginInstallLayout Layout => layout;

    /// <inheritdoc />
    public async Task<PluginInstallResult> StageInstallAsync(PluginPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        layout.EnsureStagingOutsidePluginRoot();

        var requestedId = request.NormalizedPackageId;
        using var operationGuard = await operationLock.LockAsync(requestedId, cancellationToken)
            .ConfigureAwait(false);

        var existingPending = store.TryReadPending(requestedId);
        if (existingPending is not null &&
            PluginInstallOperation.IsSameOperation(existingPending, request) &&
            IsStagedContentUsable(existingPending))
        {
            return IdempotentResult(existingPending);
        }

        store.CleanStaging(ProtectedStagingDirectories());

        var operationDirectory = layout.CreateOperationStagingDirectory();
        MarkOperationActive(operationDirectory);
        try
        {
            var plan = await stager.PrepareAsync(request, operationDirectory, cancellationToken)
                .ConfigureAwait(false);

            // Two package ids can resolve to the same plugin id; the second lock keeps staging serialized per
            // identity so a pending marker is never written concurrently.
            IDisposable? identityGuard = null;
            try
            {
                if (!string.Equals(plan.Entry.PluginId, requestedId, StringComparison.OrdinalIgnoreCase))
                {
                    identityGuard = await operationLock.LockAsync(plan.Entry.PluginId, cancellationToken)
                        .ConfigureAwait(false);
                }

                var existingForIdentity = store.TryReadPending(plan.Entry.PluginId);
                if (existingForIdentity is not null)
                {
                    if (PluginInstallOperation.IsSameOperation(existingForIdentity, request) &&
                        IsStagedContentUsable(existingForIdentity))
                    {
                        PluginInstallStore.TryDeleteDirectory(operationDirectory);
                        return IdempotentResult(existingForIdentity);
                    }

                    if (!string.Equals(existingForIdentity.PackageId, request.NormalizedPackageId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new PluginIdentityConflictException(plan.Entry.PluginId,
                            existingForIdentity.PackageId,
                            "another package already staged this plugin identity");
                    }
                }

                var target = ResolveTarget(plan.Entry.PluginId);
                EnsureNoConflict(request, plan, target);

                var operation = new PluginInstallOperation
                {
                    PluginId = plan.Entry.PluginId,
                    Status = PluginInstallOperationStatus.PendingRestart,
                    SourceUrl = request.serviceIndexUrl.Trim(),
                    PackageId = request.NormalizedPackageId,
                    Version = request.NormalizedVersion,
                    PreviousVersion = target.ActiveVersion,
                    EntryAssemblyName = plan.Entry.AssemblyName,
                    AssemblyVersion = plan.Entry.AssemblyVersion,
                    StagedAt = clock.GetUtcNow(),
                    StagedPath = operationDirectory,
                    EntryAssemblyPath = plan.EntryAssemblyPath,
                    ClaimedPluginIds = plan.ClaimedPluginIds
                };

                StorePreparedContent(operationDirectory);
                store.WritePending(operation);
                logger.LogInformation(
                    "Staged plugin {PluginId} version {Version}; it activates on the next host restart.",
                    operation.PluginId, operation.Version);
                return PluginInstallResult.Success(PluginInstallState.From(operation));
            }
            finally
            {
                identityGuard?.Dispose();
            }
        }
        catch (PluginInstallException exception)
        {
            PluginInstallStore.TryDeleteDirectory(operationDirectory);
            logger.LogWarning("Rejected plugin package {PackageId} {Version}: {Reason}",
                request.NormalizedPackageId, request.NormalizedVersion, exception.Reason);
            var pluginId = store.TryReadPending(requestedId)?.PluginId ?? requestedId;
            return Failure(exception.Reason, exception.Message, pluginId);
        }
        catch (OperationCanceledException)
        {
            PluginInstallStore.TryDeleteDirectory(operationDirectory);
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            var pluginId = store.TryReadPending(requestedId)?.PluginId ?? requestedId;
            return Failure(PluginInstallFailureReason.DownloadFailed, "The package download timed out.", pluginId);
        }
        finally
        {
            UnmarkOperationActive(operationDirectory);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PluginInstallResult>> ApplyPendingAsync(
        CancellationToken cancellationToken = default)
    {
        layout.EnsureStagingOutsidePluginRoot();
        await applyLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var results = new List<PluginInstallResult>();
            var pluginIds = store.ListPendingPluginIds();
            foreach (var pluginId in pluginIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var operationGuard = await operationLock.LockAsync(pluginId, cancellationToken)
                    .ConfigureAwait(false);
                results.Add(await ApplyPendingForPluginAsync(pluginId).ConfigureAwait(false));
            }

            PruneBackups();
            store.CleanStaging(ProtectedStagingDirectories());
            return results;
        }
        finally
        {
            applyLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<PluginInstallResult> CancelPendingAsync(string pluginId,
        CancellationToken cancellationToken = default)
    {
        if (!PluginInstallLayout.IsValidPluginId(pluginId))
        {
            return Failure(PluginInstallFailureReason.DestinationUnavailable,
                "The plugin id is not a safe folder name.", pluginId ?? string.Empty);
        }

        using var operationGuard = await operationLock.LockAsync(pluginId, cancellationToken)
            .ConfigureAwait(false);

        var pending = store.TryReadPending(pluginId);
        if (pending is null)
        {
            return PluginInstallResult.Success(GetInstallState(pluginId));
        }

        store.TryDeleteStaging(pending.StagedPath);
        store.DeletePending(pluginId);
        logger.LogInformation("Discarded the staged operation for plugin {PluginId} version {Version}.", pluginId,
            pending.Version);

        return PluginInstallResult.Success(PluginInstallState.From(pending with
        {
            Status = PluginInstallOperationStatus.Cancelled,
            StagedPath = null,
            Error = "The staged operation was cancelled."
        }));
    }

    /// <inheritdoc />
    /// <remarks>
    /// The target is resolved only from <see cref="PluginInstallLayout.ResolveInstallFolder(string)"/> and every
    /// path check runs before the first filesystem write. The folder is renamed out of discovery, deleted, and
    /// renamed back when the delete fails, so a failed uninstall leaves the plugin listed with its files intact.
    /// </remarks>
    public async Task<PluginInstallResult> UninstallAsync(string pluginId,
        CancellationToken cancellationToken = default)
    {
        if (!PluginInstallLayout.IsValidPluginId(pluginId))
        {
            return Failure(PluginInstallFailureReason.InvalidPluginId,
                "The plugin id is not a safe folder name.", pluginId ?? string.Empty);
        }

        using var operationGuard = await operationLock.LockAsync(pluginId, cancellationToken)
            .ConfigureAwait(false);

        var installFolder = store.GetInstallFolder(pluginId);
        if (!layout.IsDirectChildOfPluginRoot(installFolder))
        {
            logger.LogWarning(
                "Refused to uninstall plugin {PluginId}: '{Folder}' is not a folder directly below the plugin root.",
                pluginId, installFolder);
            return Failure(PluginInstallFailureReason.UnsafeInstallPath,
                "The plugin folder is outside the plugin root.", pluginId);
        }

        var receipt = Directory.Exists(installFolder) ? store.TryReadReceipt(installFolder) : null;
        if (receipt is null ||
            receipt.SchemaVersion > PluginInstallReceipt.CurrentSchemaVersion ||
            !string.Equals(receipt.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
        {
            return Failure(PluginInstallFailureReason.NotCatalogOwned,
                "The plugin folder was not installed by the catalog or its receipt is unreadable.", pluginId);
        }

        string uninstallFolder;
        try
        {
            uninstallFolder = store.MoveToUninstallFolder(pluginId, installFolder);
        }
        catch (PluginInstallException exception)
        {
            return Failure(exception.Reason, exception.Message, pluginId);
        }

        PluginInstallStore.TryDeleteDirectory(uninstallFolder);
        if (Directory.Exists(uninstallFolder))
        {
            try
            {
                Directory.Move(uninstallFolder, installFolder);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception,
                    "Uninstall of plugin {PluginId} failed and the renamed files could not be moved back to '{Folder}'.",
                    pluginId, installFolder);
            }

            return Failure(PluginInstallFailureReason.DestinationUnavailable,
                "The plugin files could not be deleted because they are in use or read-only.", pluginId);
        }

        TryCleanupAfterUninstall(pluginId);
        logger.LogInformation("Uninstalled catalog plugin {PluginId} version {Version}.", pluginId, receipt.Version);
        return PluginInstallResult.Success(PluginInstallState.NotInstalled(pluginId));
    }

    // A staged update of a plugin that no longer exists must not activate on the next start, and the hidden
    // backup copies must not keep the "uninstalled" files on disk.
    private void TryCleanupAfterUninstall(string pluginId)
    {
        try
        {
            if (store.TryReadPending(pluginId) is { } pending)
            {
                store.TryDeleteStaging(pending.StagedPath);
            }

            store.DeletePending(pluginId);
            store.DeleteBackups(pluginId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not discard the staged operation or backups of uninstalled plugin {PluginId}.",
                pluginId);
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, PluginInstallState> GetInstallStates()
    {
        var states = new Dictionary<string, PluginInstallState>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pluginId, (_, receipt)) in store.ReadOwnedReceipts())
        {
            states[pluginId] = receipt.ToState(store.TryReadPending(pluginId));
        }

        foreach (var pluginId in store.ListPendingPluginIds())
        {
            if (!states.ContainsKey(pluginId) && store.TryReadPending(pluginId) is { } pending)
            {
                states[pluginId] = PluginInstallState.From(pending);
            }
        }

        return states;
    }

    /// <inheritdoc />
    public PluginInstallState GetInstallState(string pluginId)
    {
        if (!PluginInstallLayout.IsValidPluginId(pluginId))
        {
            return PluginInstallState.NotInstalled(pluginId ?? string.Empty);
        }

        var pending = store.TryReadPending(pluginId);
        var installFolder = store.GetInstallFolder(pluginId);
        var receipt = store.HasReceipt(installFolder) ? store.TryReadReceipt(installFolder) : null;
        if (receipt is not null)
        {
            return receipt.ToState(pending);
        }

        return pending is not null
            ? PluginInstallState.From(pending)
            : PluginInstallState.NotInstalled(pluginId);
    }

    private async Task<PluginInstallResult> ApplyPendingForPluginAsync(string pluginId)
    {
        var pending = store.TryReadPending(pluginId);
        if (pending is null)
        {
            return PluginInstallResult.Success(GetInstallState(pluginId));
        }

        var installFolder = store.GetInstallFolder(pluginId);
        try
        {
            if (!PluginInstallLayout.IsValidPluginId(pending.PluginId) ||
                !string.Equals(pending.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
            {
                throw new PluginInstallDestinationException(pending.PluginId,
                    "the pending marker does not name a safe plugin folder");
            }

            var preparedPath = pending.StagedPath is not null && store.IsProtectedStaging(pending.StagedPath)
                ? ResolveStagedContent(pending.StagedPath)
                : null;
            if (preparedPath is null)
            {
                return RecoverInterruptedActivation(pending, installFolder);
            }

            var identity = ReadPendingEntryIdentity(pending, preparedPath);
            var hasOwnedInstall = store.HasReceipt(installFolder);
            EnsureTargetAvailable(pending, installFolder, hasOwnedInstall);

            string? backupFolder = null;
            var replacementStarted = false;
            try
            {
                if (hasOwnedInstall)
                {
                    backupFolder = store.MoveToBackup(pending.PluginId, installFolder);
                }

                replacementStarted = true;
                store.InstallFromStaging(pending.PluginId, preparedPath, installFolder);
                return CompleteActivation(pending, identity, installFolder, backupFolder);
            }
            catch (Exception exception) when (exception is PluginInstallException or IOException
                                                  or UnauthorizedAccessException)
            {
                var failure = exception as PluginInstallException ??
                              new PluginInstallDestinationException(pending.PluginId,
                                  "the new plugin content could not be written", exception);
                if (replacementStarted)
                {
                    RestorePrevious(pending.PluginId, installFolder, backupFolder);
                }

                store.TryDeleteStaging(pending.StagedPath);
                store.WritePending(pending with
                {
                    Status = replacementStarted
                        ? PluginInstallOperationStatus.RolledBack
                        : PluginInstallOperationStatus.Failed,
                    StagedPath = null,
                    FailureReason = failure.Reason,
                    Error = failure.Message
                });
                logger.LogError(
                    "Activation of plugin {PluginId} version {Version} failed with {Reason}; the previous version {PreviousVersion} was {Outcome}.",
                    pending.PluginId, pending.Version, failure.Reason, pending.PreviousVersion ?? "none",
                    replacementStarted ? "restored" : "left untouched");
                return Failure(failure.Reason, failure.Message, pending.PluginId);
            }
        }
        catch (PluginInstallException exception)
        {
            // Validation or conflict failure before replacement: the active version was not touched.
            store.DeletePending(pluginId);
            store.TryDeleteStaging(pending.StagedPath);
            logger.LogError("Plugin {PluginId} version {Version} failed to activate: {Reason}", pluginId,
                pending.Version, exception.Reason);
            return Failure(exception.Reason, exception.Message, pending.PluginId, pending with
            {
                Status = PluginInstallOperationStatus.Failed,
                StagedPath = null,
                FailureReason = exception.Reason,
                Error = exception.Message
            });
        }
    }

    /// <summary>
    /// Completes an operation whose staged copy disappeared before the receipt was written. The content is
    /// either already in the install folder (crash between the two renames) or still in a transient
    /// <c>.replace-</c> folder (crash between the renames); both are verified from metadata before adoption.
    /// </summary>
    private PluginInstallResult RecoverInterruptedActivation(PluginInstallOperation pending, string installFolder)
    {
        if (store.HasReceipt(installFolder))
        {
            var receipt = store.TryReadReceipt(installFolder);
            if (receipt is not null &&
                string.Equals(PackageIdentity.Normalize(receipt.PackageId), pending.PackageId,
                    StringComparison.OrdinalIgnoreCase) &&
                PackageIdentity.CompareVersions(receipt.Version, pending.Version) == 0)
            {
                // The receipt was written just before the crash; only the marker is left.
                store.DeletePending(pending.PluginId);
                return PluginInstallResult.Success(GetInstallState(pending.PluginId));
            }

            // A recorded failure from an earlier activation; keep the record for the UI and skip it.
            return PluginInstallResult.Success(GetInstallState(pending.PluginId));
        }

        if (Directory.Exists(installFolder))
        {
            var adopted = ReadPendingEntryIdentity(pending, installFolder);
            return CompleteActivation(pending, adopted, installFolder, backupFolder: null);
        }

        if (store.TryTakeInterruptedContent(pending.PluginId) is { } replacedContent)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(installFolder)!);
                Directory.Move(replacedContent, installFolder);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                PluginInstallStore.TryDeleteDirectory(replacedContent);
                throw new PluginInstallDestinationException(pending.PluginId,
                    "the interrupted plugin content could not be placed", exception);
            }

            var recovered = ReadPendingEntryIdentity(pending, installFolder);
            return CompleteActivation(pending, recovered, installFolder, backupFolder: null);
        }

        throw new PluginPackageAssemblyException(pending.PackageId, "the staged package content is missing");
    }

    private PluginInstallResult CompleteActivation(PluginInstallOperation pending,
        PluginAssemblyIdentitySnapshot identity, string installFolder, string? backupFolder)
    {
        var receipt = new PluginInstallReceipt
        {
            PluginId = pending.PluginId,
            SourceUrl = pending.SourceUrl,
            PackageId = pending.PackageId,
            Version = pending.Version,
            PreviousVersion = pending.PreviousVersion,
            EntryAssemblyPath = identity.EntryAssemblyPath,
            EntryAssemblyName = identity.AssemblyName,
            AssemblyVersion = identity.AssemblyVersion,
            MinHostVersion = identity.MinHostVersion,
            MaxHostVersion = identity.MaxHostVersion,
            ClaimedPluginIds = pending.ClaimedPluginIds,
            InstalledAt = clock.GetUtcNow()
        };
        store.WriteReceipt(installFolder, receipt);
        if (store.ResolveEntryAssemblyPath(installFolder, receipt) is null)
        {
            throw new PluginPackageAssemblyException(pending.PackageId,
                "the installed content does not contain the entry assembly");
        }

        store.DeletePending(pending.PluginId);
        store.TryDeleteStaging(pending.StagedPath);
        if (backupFolder is not null)
        {
            store.PruneBackups(pending.PluginId, backupFolder);
        }

        logger.LogInformation("Activated plugin {PluginId} version {Version}.", pending.PluginId,
            pending.Version);
        return PluginInstallResult.Success(GetInstallState(pending.PluginId));
    }

    /// <summary>Re-validates a pending operation's entry assembly from metadata before it touches the plugin root.</summary>
    private PluginAssemblyIdentitySnapshot ReadPendingEntryIdentity(PluginInstallOperation pending,
        string contentRoot)
    {
        var entryPath = pending.EntryAssemblyPath;
        if (string.IsNullOrWhiteSpace(entryPath) ||
            !PluginAssemblyMetadataInspector.IsSafeRelativeAssemblyPath(entryPath))
        {
            var assemblies = Directory.EnumerateFiles(contentRoot, "*.dll", SearchOption.AllDirectories).ToList();
            if (assemblies.Count != 1)
            {
                throw new PluginPackageAssemblyException(pending.PackageId,
                    "the staged content does not identify exactly one plugin assembly");
            }

            entryPath = Path.GetRelativePath(contentRoot, assemblies[0]);
        }

        var identity = PluginAssemblyMetadataInspector.ReadEntryIdentity(pending.PackageId, contentRoot,
            entryPath);
        stager.EnsureCompatible(pending.PackageId, pending.Version, identity);
        if (!string.Equals(identity.PluginId, pending.PluginId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(identity.AssemblyName, pending.EntryAssemblyName, StringComparison.OrdinalIgnoreCase))
        {
            throw new PluginIdentityConflictException(pending.PluginId, identity.PluginId,
                "the staged package declares a different plugin identity");
        }

        return identity;
    }

    private void EnsureTargetAvailable(PluginInstallOperation pending, string installFolder, bool hasOwnedInstall)
    {
        if (hasOwnedInstall)
        {
            var receipt = store.ReadReceipt(pending.PluginId, installFolder);
            if (!string.Equals(receipt.PluginId, pending.PluginId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(PackageIdentity.Normalize(receipt.PackageId), pending.PackageId,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new PluginIdentityConflictException(pending.PluginId, receipt.PluginId,
                    "the existing install receipt belongs to another package");
            }

            return;
        }

        if (Directory.Exists(installFolder))
        {
            throw new PluginIdentityConflictException(pending.PluginId, pending.PluginId,
                "a manually installed plugin occupies the catalog install folder");
        }
    }

    private static PluginInstallResult IdempotentResult(PluginInstallOperation pending) =>
        PluginInstallResult.Success(PluginInstallState.From(pending), isIdempotentRetry: true);

    private static bool IsStagedContentUsable(PluginInstallOperation pending) =>
        pending.StagedPath is not null && ResolveStagedContent(pending.StagedPath) is not null;

    /// <summary>Returns the prepared package directory; raw content left by an interrupted move is finished first.</summary>
    private static string? ResolveStagedContent(string? operationDirectory)
    {
        if (string.IsNullOrWhiteSpace(operationDirectory))
        {
            return null;
        }

        var prepared = Path.Combine(operationDirectory, "prepared");
        if (Directory.Exists(prepared))
        {
            return prepared;
        }

        var content = Path.Combine(operationDirectory, "content");
        return Directory.Exists(content) ? content : null;
    }

    private static void StorePreparedContent(string operationDirectory)
    {
        var content = Path.Combine(operationDirectory, "content");
        var prepared = Path.Combine(operationDirectory, "prepared");
        if (!Directory.Exists(content))
        {
            return;
        }

        if (Directory.Exists(prepared))
        {
            PluginInstallStore.TryDeleteDirectory(prepared);
        }

        Directory.Move(content, prepared);
    }

    private InstallTarget ResolveTarget(string pluginId)
    {
        var installFolder = store.GetInstallFolder(pluginId);
        var hasOwnedReceipt = store.HasReceipt(installFolder);
        var receipt = hasOwnedReceipt ? store.ReadReceipt(pluginId, installFolder) : null;
        return new InstallTarget(pluginId, installFolder, hasOwnedReceipt, receipt);
    }

    private void EnsureNoConflict(PluginPackageRequest request, PluginPackagePlan plan, InstallTarget target)
    {
        var pluginId = target.PluginId;
        if (!PluginInstallLayout.IsValidPluginId(pluginId))
        {
            throw new PluginIdentityConflictException(request.NormalizedPackageId, pluginId,
                "the plugin id is not a usable folder name");
        }

        if (target.HasOwnedReceipt && target.Receipt is null)
        {
            throw new PluginInstallDestinationException(pluginId, "the existing install receipt is unreadable");
        }

        if (Directory.Exists(target.InstallFolder) && !target.HasOwnedReceipt)
        {
            throw new PluginIdentityConflictException(request.NormalizedPackageId, pluginId,
                "a manually installed or bundled plugin already uses this identity");
        }

        if (target.Receipt is not null &&
            (!string.Equals(target.Receipt.PluginId, pluginId, StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(PackageIdentity.Normalize(target.Receipt.PackageId), request.NormalizedPackageId,
                 StringComparison.OrdinalIgnoreCase)))
        {
            throw new PluginIdentityConflictException(request.NormalizedPackageId, target.Receipt.PluginId,
                "the installed package has a different package id");
        }

        var ownedIds = store.ReadOwnedReceipts().Keys
            .Concat(store.ListPendingPluginIds())
            .Where(id => !string.Equals(id, pluginId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var identity in plan.ClaimedPluginIds)
        {
            if (!string.Equals(identity, pluginId, StringComparison.OrdinalIgnoreCase) &&
                ownedIds.Contains(identity, StringComparer.OrdinalIgnoreCase))
            {
                throw new PluginIdentityConflictException(pluginId, identity,
                    "another catalog plugin already uses this identity");
            }
        }

        foreach (var ownedId in ownedIds)
        {
            if (store.TryReadPending(ownedId) is { } otherPending &&
                PluginInstallStore.ClaimedIds(otherPending).Contains(pluginId, StringComparer.OrdinalIgnoreCase))
            {
                throw new PluginIdentityConflictException(pluginId, ownedId,
                    "another pending package already claims this identity");
            }

            var otherFolder = store.GetInstallFolder(ownedId);
            if (store.TryReadReceipt(otherFolder) is not { } otherReceipt)
            {
                continue;
            }

            if (otherReceipt.ClaimedPluginIds.Contains(pluginId, StringComparer.OrdinalIgnoreCase))
            {
                throw new PluginIdentityConflictException(pluginId, ownedId,
                    "another installed plugin already claims this identity");
            }
        }
    }

    private void RestorePrevious(string pluginId, string installFolder, string? backupFolder)
    {
        try
        {
            if (backupFolder is not null && Directory.Exists(backupFolder))
            {
                store.RestoreBackup(pluginId, backupFolder, installFolder);
            }
            else if (Directory.Exists(installFolder))
            {
                PluginInstallStore.TryDeleteDirectory(installFolder);
            }
        }
        catch (PluginInstallDestinationException)
        {
            logger.LogCritical(
                "Plugin {PluginId} could not be restored after a failed update; its backup stays at {BackupPath}.",
                pluginId, backupFolder);
        }
    }

    private List<string> ProtectedStagingDirectories()
    {
        var protectedDirectories = store.ListPendingPluginIds()
            .Select(store.TryReadPending)
            .Where(static pending => pending?.StagedPath is not null)
            .Select(static pending => pending!.StagedPath!)
            .ToList();
        lock (stagingGate)
        {
            protectedDirectories.AddRange(activeOperationDirectories);
        }

        return protectedDirectories;
    }

    private void MarkOperationActive(string operationDirectory)
    {
        lock (stagingGate)
        {
            activeOperationDirectories.Add(operationDirectory);
        }
    }

    private void UnmarkOperationActive(string operationDirectory)
    {
        lock (stagingGate)
        {
            activeOperationDirectories.Remove(operationDirectory);
        }
    }

    private void PruneBackups()
    {
        // Backups are named after the plugin folder, so pruning by receipt ids misses folders whose receipt is
        // missing or unreadable and leaves recoverable copies behind forever.
        foreach (var pluginId in store.ListPluginFolderNames())
        {
            store.PruneBackups(pluginId, currentBackup: null);
        }
    }

    private PluginInstallResult Failure(PluginInstallFailureReason reason, string message, string pluginId) =>
        Failure(reason, message, pluginId, store.TryReadPending(pluginId));

    private PluginInstallResult Failure(PluginInstallFailureReason reason, string message, string pluginId,
        PluginInstallOperation? pending)
    {
        var state = pending is not null
            ? PluginInstallState.From(pending with { FailureReason = reason, Error = message })
            : GetInstallState(pluginId) with { FailureReason = reason, Error = message };

        return PluginInstallResult.Failure(reason, message, state);
    }

    private sealed record InstallTarget(
        string PluginId,
        string InstallFolder,
        bool HasOwnedReceipt,
        PluginInstallReceipt? Receipt)
    {
        public string? ActiveVersion => Receipt?.Version;
    }
}
