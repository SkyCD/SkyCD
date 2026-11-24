using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Indexing;
using SkyCD.Plugin.Metadata.Archives.Ace.Import;
using SkyCD.Plugin.Metadata.Archives.Arc.Import;
using SkyCD.Plugin.Metadata.Archives.Arj.Import;
using SkyCD.Plugin.Metadata.Archives.Bzip2.Import;
using SkyCD.Plugin.Metadata.Archives.Cab.Import;
using SkyCD.Plugin.Metadata.Archives.Gzip.Import;
using SkyCD.Plugin.Metadata.Archives.Lha.Import;
using SkyCD.Plugin.Metadata.Archives.Models;
using SkyCD.Plugin.Metadata.Archives.Rar.Import;
using SkyCD.Plugin.Metadata.Archives.SevenZip.Import;
using SkyCD.Plugin.Metadata.Archives.Tar.Import;
using SkyCD.Plugin.Metadata.Archives.Xz.Import;
using SkyCD.Plugin.Metadata.Archives.Zip.Import;
using SkyCD.Plugin.Metadata.Archives.Zoo.Import;
using SkyCD.Plugin.Metadata.Archives.Zstd.Import;

namespace SkyCD.Plugin.Metadata.Archives;

/// <summary>
/// Archive metadata provider. When the host scans a path and metadata providers are enabled, it contributes
/// the entries of supported archives as extra work items so they can be inspected. The plugin dispatches to
/// one format reader per extension; readers extract into a temporary directory with bounded resource usage
/// and the returned set removes those files when disposed.
/// </summary>
public sealed class ArchivesIndexPlugin : IIndexWorkItemProvider
{
    private readonly ArchiveIndexLimits limits;

    public ArchivesIndexPlugin() : this(ArchiveIndexLimits.Default)
    {
    }

    public ArchivesIndexPlugin(ArchiveIndexLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        this.limits = limits;
    }

    /// <summary>Gets the typed diagnostics for entries the last call skipped while contributing work items.</summary>
    public IReadOnlyList<string> LastDiagnostics { get; private set; } = [];

    public async Task<IndexWorkItemSet> GetWorkItemsAsync(IndexWorkItemRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        LastDiagnostics = [];

        if (HasExtension(request.FileName, ".tar")
            || IsTarCompoundName(request.FileName)
            || IsTarCompoundName(request.SourcePath))
        {
            return await TarMetadataWorkItems.CreateAsync(request, limits, cancellationToken).ConfigureAwait(false);
        }

        if (IsGzipFile(request.FileName))
        {
            return await GzipIndexWorkItems.CreateAsync(request, limits, cancellationToken).ConfigureAwait(false);
        }

        if (IsBzip2File(request.FileName))
        {
            return await Bzip2IndexWorkItems.CreateAsync(request, limits, cancellationToken).ConfigureAwait(false);
        }

        if (IsXzFile(request.FileName))
        {
            return await XzIndexWorkItems.CreateAsync(request, limits, cancellationToken).ConfigureAwait(false);
        }

        if (IsZstdFile(request.FileName))
        {
            return await ZstdIndexWorkItems.CreateAsync(request, limits, cancellationToken).ConfigureAwait(false);
        }

        if (HasExtension(request.FileName, ".zip"))
        {
            return await ZipIndexWorkItems.CreateAsync(request, limits, cancellationToken).ConfigureAwait(false);
        }

        if (IsSevenZipFile(request.FileName))
        {
            return await SevenZipIndexWorkItems.CreateAsync(request, limits, cancellationToken).ConfigureAwait(false);
        }

        if (IsRarFile(request.FileName))
        {
            return await RarIndexWorkItems.CreateAsync(request, limits, cancellationToken).ConfigureAwait(false);
        }

        if (IsLhaFile(request.FileName))
        {
            return await LhaIndexWorkItems.CreateAsync(request, limits, cancellationToken).ConfigureAwait(false);
        }

        if (HasExtension(request.FileName, ".cab"))
        {
            var result = await CabIndexWorkItems.CreateAsync(request, limits, cancellationToken)
                .ConfigureAwait(false);
            LastDiagnostics = result.Skipped
                .Select(entry => string.IsNullOrEmpty(entry.Name)
                    ? $"{entry.Code}: {entry.Reason}"
                    : $"{entry.Code}: {entry.Name}: {entry.Reason}")
                .ToList();
            return result.WorkItems;
        }

        if (HasExtension(request.FileName, ".arj"))
        {
            return await ArjIndexWorkItems.CreateAsync(request, limits, cancellationToken).ConfigureAwait(false);
        }

        if (HasExtension(request.FileName, ".arc"))
        {
            return await ArcIndexWorkItems.CreateAsync(request, limits, cancellationToken).ConfigureAwait(false);
        }

        if (HasExtension(request.FileName, ".zoo"))
        {
            return await ZooIndexWorkItems.CreateAsync(request, limits, cancellationToken).ConfigureAwait(false);
        }

        if (HasExtension(request.FileName, ".ace"))
        {
            var result = await AceIndexWorkItems.CreateAsync(request, limits, cancellationToken).ConfigureAwait(false);
            LastDiagnostics = result.Skipped
                .Select(entry => string.IsNullOrEmpty(entry.Name)
                    ? $"{entry.Code}: {entry.Reason}"
                    : $"{entry.Code}: {entry.Name}: {entry.Reason}")
                .ToList();
            return result.WorkItems;
        }

        return IndexWorkItemSet.Empty;
    }

    private static bool HasExtension(string fileName, string extension) =>
        string.Equals(Path.GetExtension(fileName), extension, StringComparison.OrdinalIgnoreCase);

    private static bool IsSevenZipFile(string fileName) => HasExtension(fileName, ".7z");

    private static bool IsRarFile(string fileName) => HasExtension(fileName, ".rar");

    private static bool IsLhaFile(string fileName) =>
        EndsWith(fileName, ".lha") || EndsWith(fileName, ".lzh");

    private static bool IsBzip2File(string fileName) =>
        EndsWith(fileName, ".bz2") || EndsWith(fileName, ".bz") || EndsWith(fileName, ".tbz")
        || EndsWith(fileName, ".tbz2");

    private static bool IsGzipFile(string fileName) =>
        (EndsWith(fileName, ".gz") || EndsWith(fileName, ".gzip"))
        && !EndsWith(fileName, ".tar.gz")
        && !EndsWith(fileName, ".tar.gzip");

    private static bool IsXzFile(string fileName) =>
        EndsWith(fileName, ".xz") || EndsWith(fileName, ".txz");

    private static bool IsZstdFile(string fileName) =>
        EndsWith(fileName, ".zst") || EndsWith(fileName, ".tzst");

    private static bool IsTarCompoundName(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && (EndsWith(fileName, ".tar.gz") || EndsWith(fileName, ".tgz"));

    private static bool EndsWith(string fileName, string suffix) =>
        fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
}
