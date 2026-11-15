using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// One durable extraction result, keyed by source fingerprint plus provider and schema identity.
/// Records are stored separately from <see cref="SkyCD.Documents.CatalogDocument"/> so that captured
/// metadata and previews stay available offline and never overwrite user annotations.
/// </summary>
public sealed class MediaCacheRecord
{
    /// <summary>Identity used for host-owned markers that no plugin produced.</summary>
    public const string HostPluginId = "skycd.host";

    /// <summary>Identity used for the host-owned marker recorded when no provider supports a source.</summary>
    public const string UnsupportedCapability = "unsupported";

    public string Id { get; set; } = string.Empty;

    /// <summary>The catalog entry (<c>CatalogDocument.Id</c>) this result was captured for.</summary>
    public string EntryId { get; set; } = string.Empty;

    public string Fingerprint { get; set; } = string.Empty;

    public string PluginId { get; set; } = string.Empty;

    public string PluginVersion { get; set; } = string.Empty;

    public string Capability { get; set; } = string.Empty;

    public string? SchemaId { get; set; }

    public int SchemaVersion { get; set; }

    /// <summary>Descriptor priority reported by the provider, used to order competing values.</summary>
    public int Priority { get; set; }

    public MediaCacheRecordState State { get; set; } = MediaCacheRecordState.Captured;

    public DateTimeOffset CapturedAtUtc { get; set; }

    /// <summary>Last time the record was written or served; drives LRU eviction.</summary>
    public DateTimeOffset LastUsedAtUtc { get; set; }

    public long SourceLength { get; set; }

    public long MetadataBytes { get; set; }

    public long ArtifactBytes { get; set; }

    public bool TimedOut { get; set; }

    /// <summary>Sanitized failure classification; never plugin exception text.</summary>
    public string? FailureReason { get; set; }

    public List<MediaMetadataValueRecord> Metadata { get; set; } = [];

    /// <summary>Field definitions captured with the values, so metadata stays describable offline.</summary>
    public List<MediaMetadataFieldRecord> SchemaFields { get; set; } = [];

    public List<MediaArtifactReference> Artifacts { get; set; } = [];

    /// <summary>Deterministic identity: the same source and provider version always maps to one record.</summary>
    public static string CreateId(
        string fingerprint,
        string pluginId,
        string pluginVersion,
        string capability,
        string? schemaId,
        int schemaVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);

        return "mcr-" + MediaCacheHashing.Sha256Hex(string.Join(
            '|',
            fingerprint,
            pluginId,
            pluginVersion ?? string.Empty,
            capability,
            schemaId ?? string.Empty,
            schemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    public static string CreateUnsupportedId(string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        return CreateId(fingerprint, HostPluginId, string.Empty, UnsupportedCapability, null, 0);
    }

    /// <summary>Provider identity within one fingerprint; a change means previously captured data is stale.</summary>
    public string CreateIdentityKey() => CreateIdentityKey(PluginId, PluginVersion, Capability, SchemaId, SchemaVersion);

    public static string CreateIdentityKey(
        string pluginId,
        string pluginVersion,
        string capability,
        string? schemaId,
        int schemaVersion) => string.Join(
        '|',
        pluginId,
        pluginVersion ?? string.Empty,
        capability,
        schemaId ?? string.Empty,
        schemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public long CalculateMetadataBytes() => Metadata.Sum(value =>
        (long)Encoding.UTF8.GetByteCount(value.Key ?? string.Empty) +
        (long)Encoding.UTF8.GetByteCount(value.Text ?? string.Empty) + 32);

    public long CalculateArtifactBytes() => Artifacts.Sum(artifact => artifact.Length);

    /// <summary>Refreshes the stored size counters; called before a record is written.</summary>
    public MediaCacheRecord WithRecalculatedSizes()
    {
        MetadataBytes = CalculateMetadataBytes();
        ArtifactBytes = CalculateArtifactBytes();
        return this;
    }

    public MediaCacheRecord Clone()
    {
        var clone = (MediaCacheRecord)MemberwiseClone();
        clone.Metadata = [.. Metadata];
        clone.SchemaFields = [.. SchemaFields];
        clone.Artifacts = [.. Artifacts];
        return clone;
    }
}
