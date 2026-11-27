using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkyCD.Documents;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Presentation.MediaCache;

public sealed class CatalogDescriptionService(CatalogDocumentRepository repository, IMediaCacheStore store)
{
    public const string PluginId = "skycd.ai-description";
    public const string SchemaId = "skycd.ai-description.metadata";
    public const string Capability = "generated-description";
    public const string DescriptionFieldKey = "AiDescription";

    /// <summary>Host-owned record that holds the facts an online metadata pass captured for one entry.</summary>
    public const string OnlineMetadataCapability = "online-metadata-facts";
    public const string OnlineMetadataSchemaId = "skycd.online-metadata.facts";
    public const string OnlineMetadataPartKind = "online-metadata";
    public const string OnlineMetadataSourcesKey = "OnlineMetadataSources";
    public const string OnlineMetadataSourceUrlsKey = "OnlineMetadataSourceUrls";

    public DescriptionContext BuildOriginal(string entryId, DescriptionContextOptions options)
    {
        var entry = repository.Get(entryId) ?? throw new DescriptionContextChangedException();
        if (entry.Type != CatalogDocumentType.File ||
            !entry.Properties.TryGetValue(MediaMetadataService.SourcePathPropertyKey, out var source) ||
            source?.ToString() is not { } path || !Path.IsPathFullyQualified(path))
            throw new Exceptions.AiDescriptionUnavailableException("the original file is not available locally");

        var catalog = repository.GetAll();
        var byId = catalog.ToDictionary(document => document.Id, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { entry.Id };
        for (var parentId = entry.ParentId; parentId is not null && visited.Add(parentId) &&
            byId.TryGetValue(parentId, out var parent); parentId = parent.ParentId)
        {
            if (parent.Type == CatalogDocumentType.NetworkFolder)
                throw new Exceptions.AiDescriptionUnavailableException("the original file is not available locally");
        }

        var file = new FileInfo(path);
        if (!file.Exists)
            throw new Exceptions.AiDescriptionUnavailableException("the original file is not available locally");
        if (file.Length != entry.Size || !MediaMetadataService.MatchesRecordedTimestamp(entry, file))
            throw new DescriptionContextChangedException();

        var original = new DescriptionOriginalFile(file.FullName, entry.Name, file.Length, file.LastWriteTimeUtc);
        var contextHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(original)));
        var fingerprint = MediaSourceFingerprint.ForCatalogEntry(entry, catalog);
        var parts = OnlineMetadataFactTexts(entryId, fingerprint)
            .Select(text => new DescriptionContextPart(OnlineMetadataPartKind, "text/plain", text))
            .ToList();
        return new DescriptionContext(entryId, fingerprint, contextHash, options, parts)
        {
            OriginalFile = original
        };
    }

    public DescriptionContext Build(string entryId, DescriptionContextOptions options)
    {
        var entry = repository.Get(entryId) ?? throw new DescriptionContextChangedException();
        var fingerprint = MediaSourceFingerprint.ForCatalogEntry(entry, repository.GetAll());
        var textLeft = Math.Clamp(options.MaxTextCharacters, 1, 64000);
        var bytesLeft = Math.Clamp(options.MaxArtifactBytes, 0, 4194304);
        var parts = new List<DescriptionContextPart>();
        void Text(string kind, string value)
        {
            var bounded = value[..Math.Min(value.Length, textLeft)];
            textLeft -= bounded.Length;
            if (bounded.Length > 0) parts.Add(new(kind, "text/plain", bounded));
        }
        if (options.Filename) Text("filename", entry.Name);
        foreach (var record in store.GetRecordsForEntry(entryId)
            .Where(record => record.PluginId != PluginId && !IsOnlineMetadataRecord(record) &&
                record.Fingerprint == fingerprint && record.State == MediaCacheRecordState.Captured)
            .OrderBy(record => record.Id, StringComparer.Ordinal))
        {
            foreach (var field in record.Metadata.OrderBy(field => field.Key, StringComparer.Ordinal))
            {
                var isText = field.Key.Contains("Excerpt", StringComparison.OrdinalIgnoreCase)
                    || field.Key.Contains("ExtractedText", StringComparison.OrdinalIgnoreCase);
                if ((isText && options.Text) || (!isText && options.Metadata))
                    Text(isText ? "content-text" : "metadata", field.Key + ": " + Convert.ToString(field.GetPayload(), CultureInfo.InvariantCulture));
            }
            foreach (var artifact in record.Artifacts)
            {
                var kind = artifact.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? "image"
                    : artifact.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ? "audio" : null;
                if (kind is null || (kind == "image" && !options.Images) || (kind == "audio" && !options.Audio)
                    || artifact.Length > bytesLeft || artifact.Length < 1) continue;
                if (store.TryReadArtifact(record.Id, artifact.Key, out var data) && data.Length <= bytesLeft
                    && Convert.ToHexString(SHA256.HashData(data)).Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add(new(kind, artifact.ContentType, Data: data));
                    bytesLeft -= data.Length;
                }
            }
        }
        foreach (var text in OnlineMetadataFactTexts(entryId, fingerprint))
        {
            Text(OnlineMetadataPartKind, text);
        }
        var contextHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(parts))));
        return new(entryId, fingerprint, contextHash, options, parts);
    }

    /// <summary>
    /// Stores the merged facts of one online metadata pass for the entry snapshot. Which sources answered and the
    /// URLs they fetched are kept in dedicated provenance fields, so the fetched URL never becomes a template fact.
    /// An empty pass replaces earlier facts instead of leaving a description to render stale values.
    /// </summary>
    public void SaveOnlineMetadata(string entryId, string sourceFingerprint, OnlineMetadataRun run,
        DateTimeOffset capturedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFingerprint);
        ArgumentNullException.ThrowIfNull(run);

        var values = run.Facts
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value)
                && !IsOnlineMetadataProvenanceKey(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => TextValue(pair.Key, pair.Value))
            .ToList();
        var succeeded = run.Results.Where(result => result.Succeeded).ToArray();
        var sources = succeeded.Select(result => result.SourceId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(sourceId => sourceId, StringComparer.Ordinal)
            .ToArray();
        if (sources.Length > 0)
        {
            values.Add(TextValue(OnlineMetadataSourcesKey, string.Join(", ", sources)));
        }
        var urls = succeeded.Select(result => result.SourceUrl).OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(url => url, StringComparer.Ordinal)
            .ToArray();
        if (urls.Length > 0)
        {
            values.Add(TextValue(OnlineMetadataSourceUrlsKey, string.Join(" ", urls)));
        }

        var record = new MediaCacheRecord
        {
            Id = MediaCacheRecord.CreateId(sourceFingerprint, MediaCacheRecord.HostPluginId, "1",
                OnlineMetadataCapability, OnlineMetadataSchemaId, 1),
            EntryId = entryId,
            Fingerprint = sourceFingerprint,
            PluginId = MediaCacheRecord.HostPluginId,
            PluginVersion = "1",
            Capability = OnlineMetadataCapability,
            SchemaId = OnlineMetadataSchemaId,
            SchemaVersion = 1,
            State = MediaCacheRecordState.Captured,
            CapturedAtUtc = capturedAtUtc,
            LastUsedAtUtc = capturedAtUtc,
            Metadata = values,
            SchemaFields = values.Select(value => new MediaMetadataFieldRecord { Key = value.Key, Type = value.Type })
                .ToList()
        };
        store.Save(new MediaCachePayload(record.WithRecalculatedSizes()));
    }

    /// <summary>True for the host-owned record that stores one entry's online metadata facts.</summary>
    public static bool IsOnlineMetadataRecord(MediaCacheRecord record) =>
        record is not null &&
        string.Equals(record.PluginId, MediaCacheRecord.HostPluginId, StringComparison.Ordinal) &&
        string.Equals(record.Capability, OnlineMetadataCapability, StringComparison.Ordinal);

    private static bool IsOnlineMetadataProvenanceKey(string key) =>
        string.Equals(key, OnlineMetadataSourcesKey, StringComparison.Ordinal) ||
        string.Equals(key, OnlineMetadataSourceUrlsKey, StringComparison.Ordinal);

    private IEnumerable<string> OnlineMetadataFactTexts(string entryId, string fingerprint)
    {
        foreach (var record in store.GetRecordsForEntry(entryId)
            .Where(record => IsOnlineMetadataRecord(record) && record.State == MediaCacheRecordState.Captured &&
                string.Equals(record.Fingerprint, fingerprint, StringComparison.Ordinal))
            .OrderBy(record => record.Id, StringComparer.Ordinal))
        {
            foreach (var field in record.Metadata.OrderBy(field => field.Key, StringComparer.Ordinal))
            {
                if (IsOnlineMetadataProvenanceKey(field.Key))
                {
                    continue;
                }

                yield return field.Key + ": " + Convert.ToString(field.GetPayload(), CultureInfo.InvariantCulture);
            }
        }
    }

    private static MediaMetadataValueRecord TextValue(string key, string value) =>
        new() { Key = key, Type = MediaMetadataType.Text, Text = value };

    public void Save(DescriptionContext context, GeneratedDescription description)
    {
        var current = context.OriginalFile is null ? Build(context.EntryId, context.Options) : BuildOriginal(context.EntryId, context.Options);
        if (current.SourceFingerprint != context.SourceFingerprint || current.ContextFingerprint != context.ContextFingerprint)
            throw new DescriptionContextChangedException();
        if (string.IsNullOrWhiteSpace(description.Text) || description.Text.Length > 8000)
            throw new ArgumentException("A generated description must contain 1 to 8000 characters.", nameof(description));
        var values = new Dictionary<string, string>
        {
            [DescriptionFieldKey] = description.Text,
            ["AiDescriptionAgent"] = description.Agent,
            ["AiDescriptionModel"] = description.Model ?? "unknown",
            ["AiDescriptionPromptVersion"] = description.PromptVersion,
            ["AiDescriptionLanguage"] = description.Language,
            ["AiDescriptionOrigin"] = description.Origin,
            ["AiDescriptionProcessingDestination"] = description.ProcessingDestination,
            ["AiDescriptionContextFingerprint"] = context.ContextFingerprint,
            ["AiDescriptionSourceFingerprint"] = context.SourceFingerprint
        };
        var record = new MediaCacheRecord
        {
            Id = MediaCacheRecord.CreateId(context.SourceFingerprint, PluginId, description.PluginVersion, "generated-description", SchemaId, 1),
            EntryId = context.EntryId,
            Fingerprint = context.SourceFingerprint,
            PluginId = PluginId,
            PluginVersion = description.PluginVersion,
            Capability = "generated-description",
            SchemaId = SchemaId,
            SchemaVersion = 1,
            State = MediaCacheRecordState.Captured,
            CapturedAtUtc = description.GeneratedAtUtc,
            LastUsedAtUtc = description.GeneratedAtUtc,
            Metadata = values.Select(pair => new MediaMetadataValueRecord { Key = pair.Key, Type = MediaMetadataType.Text, Text = pair.Value }).ToList(),
            SchemaFields = values.Keys.Select(key => new MediaMetadataFieldRecord { Key = key, Type = MediaMetadataType.Text }).ToList()
        };
        store.Save(new MediaCachePayload(record.WithRecalculatedSizes()));
    }

    /// <summary>True for records produced by AI description generation.</summary>
    public static bool IsGeneratedDescriptionRecord(MediaCacheRecord record) =>
        record is not null &&
        string.Equals(record.PluginId, PluginId, StringComparison.Ordinal) &&
        string.Equals(record.Capability, Capability, StringComparison.Ordinal);

    /// <summary>Reads a stored generated-description text value, or null when the record has none.</summary>
    public static string? ReadDescriptionText(MediaCacheRecord record) =>
        record?.Metadata.FirstOrDefault(value =>
            string.Equals(value.Key, DescriptionFieldKey, StringComparison.Ordinal))?.Text;

    /// <summary>Reads one stored generated-description value such as an agent or language field.</summary>
    public static string? ReadValue(MediaCacheRecord record, string key) =>
        record?.Metadata.FirstOrDefault(value => string.Equals(value.Key, key, StringComparison.Ordinal))?.Text;

    /// <summary>
    /// Removes every stored description of one entry. The catalog entry and its user comments are untouched,
    /// so clearing an AI description never edits the user's own annotation.
    /// </summary>
    public int Clear(string entryId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        var removed = 0;
        foreach (var record in store.GetRecordsForEntry(entryId).Where(IsGeneratedDescriptionRecord).ToArray())
        {
            if (store.DeleteRecord(record.Id))
            {
                removed++;
            }
        }

        return removed;
    }
}
