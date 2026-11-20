using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Readers;
using SharpCompress.Readers.Rar;
using SkyCD.Plugin.Rar.Exceptions;

namespace SkyCD.Plugin.Rar;

/// <summary>
/// Scans RAR archive headers once and reports the facts the plugin needs before it lists or extracts entries.
/// </summary>
internal static class RarHeaderScan
{
    /// <summary>
    /// Classifies the archive by walking its header sequence: readable, part of a multi-volume set, or failing.
    /// </summary>
    /// <param name="source">A seekable archive stream positioned anywhere.</param>
    /// <param name="hasCompanionVolume">Whether a companion volume of the set exists next to the source.</param>
    public static RarArchiveStatus Inspect(Stream source, bool hasCompanionVolume)
    {
        var origin = source.Position;
        try
        {
            source.Position = 0;
            using var reader = RarReader.OpenReader(RarVolumeAwareStream.Wrap(source), CreateReaderOptions());
            if (reader is RarReader volumeReader && volumeReader.Volume?.IsMultiVolume == true)
            {
                return RarArchiveStatus.MultiVolume;
            }

            while (reader.MoveToNextEntry())
            {
                if (reader.Entry is RarReaderEntry entry)
                {
                    // The split-after flag marks an entry that continues in a volume the archive does not
                    // contain; listing it would report truncated data as a complete file.
                    if (entry.IsSplitAfter)
                    {
                        return RarArchiveStatus.MultiVolume;
                    }

                    if (entry.IsEncrypted)
                    {
                        return RarArchiveStatus.Encrypted;
                    }
                }
            }

            return hasCompanionVolume ? RarArchiveStatus.MultiVolume : RarArchiveStatus.Readable;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (RarFailures.IsArchiveFailure(exception))
        {
            var code = RarFailures.CodeFor(exception);
            return code == RarFailures.EncryptedCode
                ? RarArchiveStatus.Encrypted
                : RarArchiveStatus.Failed(code, exception.Message);
        }
        finally
        {
            source.Position = origin;
        }
    }

    /// <summary>Lists the entries of a readable archive. The stream stays open for the caller.</summary>
    public static async Task<List<RarListedEntry>> ListAsync(
        Stream archiveStream,
        CancellationToken cancellationToken)
    {
        var entries = new List<RarListedEntry>();
        archiveStream.Position = 0;
        using var reader = RarReader.OpenReader(RarVolumeAwareStream.Wrap(archiveStream), CreateReaderOptions());
        while (reader.MoveToNextEntry())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = reader.Entry;
            if (entry is null)
            {
                break;
            }

            entries.Add(new RarListedEntry((entry.Key ?? string.Empty).Replace('\\', '/'), entry.IsDirectory,
                TryGetLength(entry), TryGetModified(entry)));
            await Task.CompletedTask.ConfigureAwait(false);
        }

        return entries;
    }

    /// <summary>Creates reader options that never prompt for passwords and keep the stream open.</summary>
    public static ReaderOptions CreateReaderOptions() =>
        ReaderOptions.ForExternalStream with { LeaveStreamOpen = true };

    private static long TryGetLength(SharpCompress.Common.IEntry entry)
    {
        try
        {
            return Math.Max(entry.Size, 0);
        }
        catch (Exception exception) when (RarFailures.IsArchiveFailure(exception))
        {
            return 0;
        }
    }

    private static DateTimeOffset? TryGetModified(SharpCompress.Common.IEntry entry)
    {
        try
        {
            return entry.LastModifiedTime is DateTime value
                ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))
                : null;
        }
        catch (Exception exception) when (RarFailures.IsArchiveFailure(exception))
        {
            return null;
        }
    }
}

/// <summary>One entry discovered in the RAR header sequence.</summary>
internal sealed record RarListedEntry(string Key, bool IsDirectory, long SizeBytes, DateTimeOffset? ModifiedUtc);

/// <summary>The result of probing an archive's headers.</summary>
internal sealed class RarArchiveStatus
{
    private RarArchiveStatus(bool readable, bool multiVolume, bool encrypted, string? error)
    {
        IsReadable = readable;
        IsMultiVolume = multiVolume;
        IsEncrypted = encrypted;
        Error = error;
    }

    /// <summary>Gets the status of an archive whose headers can be listed.</summary>
    public static RarArchiveStatus Readable { get; } = new(true, false, false, null);

    /// <summary>Gets the status of an archive that belongs to a multi-volume set.</summary>
    public static RarArchiveStatus MultiVolume { get; } = new(false, true, false, null);

    /// <summary>Gets the status of an archive whose headers or entries are encrypted.</summary>
    public static RarArchiveStatus Encrypted { get; } = new(false, false, true, RarFailures.Format(
        RarFailures.EncryptedCode, "The archive contains encrypted entries and cannot be listed without a password."));

    /// <summary>Gets whether the archive headers or entries are encrypted.</summary>
    public bool IsEncrypted { get; }

    /// <summary>Gets whether the archive can be listed.</summary>
    public bool IsReadable { get; }

    /// <summary>Gets whether the archive needs companion volumes.</summary>
    public bool IsMultiVolume { get; }

    /// <summary>Gets the typed diagnostic for archives that cannot be read.</summary>
    public string? Error { get; }

    /// <summary>Creates a failed status that carries a typed diagnostic.</summary>
    public static RarArchiveStatus Failed(string error) => new(false, false, false, error);

    /// <summary>Creates a failed status that carries a typed diagnostic code and detail.</summary>
    public static RarArchiveStatus Failed(string code, string detail) => Failed(RarFailures.Format(code, detail));
}
