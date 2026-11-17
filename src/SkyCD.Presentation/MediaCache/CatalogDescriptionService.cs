using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SkyCD.Documents;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Presentation.MediaCache;

public sealed class CatalogDescriptionService(CatalogDocumentRepository repository, IMediaCacheStore store)
{
    public const string PluginId = "skycd.ai-description";
    public const string SchemaId = "skycd.ai-description.metadata";

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
            .Where(record => record.PluginId != PluginId && record.Fingerprint == fingerprint
                && record.State is MediaCacheRecordState.Captured or MediaCacheRecordState.Stale)
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
        var contextHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(parts))));
        return new(entryId, fingerprint, contextHash, options, parts);
    }

    public void Save(DescriptionContext context, GeneratedDescription description)
    {
        var current = Build(context.EntryId, context.Options);
        if (current.SourceFingerprint != context.SourceFingerprint || current.ContextFingerprint != context.ContextFingerprint)
            throw new DescriptionContextChangedException();
        if (string.IsNullOrWhiteSpace(description.Text) || description.Text.Length > 8000)
            throw new ArgumentException("A generated description must contain 1 to 8000 characters.", nameof(description));
        var values = new Dictionary<string, string>
        {
            ["AiDescription"] = description.Text,
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
}
