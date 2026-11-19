using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Abstractions.Capabilities.Archives;

/// <summary>
/// An archive entry produced by extraction. <see cref="FullPath"/> is set only for entries written to disk.
/// </summary>
public sealed record ArchiveExtractedEntry(
    string Path,
    ArchiveEntryKind Kind,
    long SizeBytes,
    DateTimeOffset? ModifiedUtc,
    ArchiveEntrySkipReason SkipReason = ArchiveEntrySkipReason.None,
    string? Detail = null,
    string? FullPath = null);

/// <summary>
/// Result of extracting an archive. Disposing the result deletes the temporary extraction directory.
/// </summary>
public sealed class ArchiveExtractionResult : IDisposable, IAsyncDisposable
{
    private readonly string? temporaryDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArchiveExtractionResult"/> class.
    /// </summary>
    public ArchiveExtractionResult(
        ArchiveInspectionResult inspection,
        string? temporaryDirectory,
        IReadOnlyList<ArchiveExtractedEntry> entries)
    {
        Inspection = inspection ?? throw new ArgumentNullException(nameof(inspection));
        this.temporaryDirectory = temporaryDirectory;
        Entries = entries ?? [];
    }

    /// <summary>Gets the inspection outcome for the archive.</summary>
    public ArchiveInspectionResult Inspection { get; }

    /// <summary>Gets the unique temporary directory that owns the extracted files, when extraction succeeded.</summary>
    public string? TemporaryDirectory => temporaryDirectory;

    /// <summary>Gets the extracted entries, including entries that were skipped for a known reason.</summary>
    public IReadOnlyList<ArchiveExtractedEntry> Entries { get; }

    /// <summary>
    /// Creates a failed result that owns no temporary directory.
    /// </summary>
    public static ArchiveExtractionResult Failed(string error) =>
        new(new ArchiveInspectionResult { Success = false, Error = error }, null, []);

    /// <summary>
    /// Deletes the temporary extraction directory. Cleanup is best-effort and never throws.
    /// </summary>
    public void Dispose() => DeleteTemporaryDirectory(temporaryDirectory);

    /// <summary>
    /// Deletes the temporary extraction directory. Cleanup is best-effort and never throws.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Deletes a temporary extraction directory. Cleanup is best-effort and never throws.
    /// Capability implementations call this when extraction fails before a result is created.
    /// </summary>
    public static void DeleteTemporaryDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                catch (IOException)
                {
                    // Retry deletion without clearing attributes.
                }
                catch (UnauthorizedAccessException)
                {
                    // Retry deletion without clearing attributes.
                }
            }

            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup: leave the rest for OS temp cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup: leave the rest for OS temp cleanup.
        }
    }
}
