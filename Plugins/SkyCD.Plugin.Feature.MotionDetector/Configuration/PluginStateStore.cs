using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkyCD.Plugin.Feature.MotionDetector.Detectors;
using SkyCD.SDK.Capabilities.Sources;
using SkyCD.SDK.Capabilities.Storage;

namespace SkyCD.Plugin.Feature.MotionDetector.Configuration;

/// <summary>
/// Plugin-owned persistence for configured sources and the global detector settings. Records live in the open
/// catalog through <see cref="IPluginStorageApi"/>, so the host never keeps motion-detector state.
/// </summary>
internal sealed class PluginStateStore(IPluginStorageApi storage)
{
    private const string SourcesCollection = "sources";
    private const string SettingsCollection = "settings";
    private const string SettingsId = "detector-settings";
    private const string ParameterPrefix = "param.";

    public IReadOnlyList<SourceConfiguration> LoadSources()
    {
        var collection = storage.OpenCollection(SourcesCollection);
        var sources = new List<SourceConfiguration>();
        foreach (var record in collection.GetAll())
        {
            var source = ReadSource(record);
            if (source is not null)
            {
                sources.Add(source);
            }
        }

        return sources;
    }

    public void SaveSource(SourceConfiguration source)
    {
        ArgumentNullException.ThrowIfNull(source);
        storage.OpenCollection(SourcesCollection).Upsert(source.SourceId, new Dictionary<string, object?>
        {
            ["SourceId"] = source.SourceId,
            ["KindId"] = source.KindId,
            ["Location"] = source.Location,
            ["DestinationFolder"] = source.DestinationFolder,
            ["CatalogFolder"] = source.CatalogFolder,
            ["Label"] = source.Label
        });
    }

    public void DeleteSource(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        storage.OpenCollection(SourcesCollection).Delete(sourceId);
    }

    public SourceDetectorSettings LoadSettings()
    {
        var record = storage.OpenCollection(SettingsCollection).Get(SettingsId);
        if (record is null)
        {
            return DefaultSettings();
        }

        var detectorId = ReadString(record.Fields, "DetectorId");
        var parameters = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (key, value) in record.Fields)
        {
            if (key.StartsWith(ParameterPrefix, StringComparison.Ordinal) && TryReadDouble(value, out var number))
            {
                parameters[key[ParameterPrefix.Length..]] = number;
            }
        }

        return new SourceDetectorSettings(
            DetectorCatalog.Specs.Any(spec => spec.Id == detectorId) ? detectorId : null,
            DetectorCatalog.Normalize(detectorId, parameters));
    }

    public void SaveSettings(SourceDetectorSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var fields = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["DetectorId"] = settings.DetectorId
        };
        foreach (var (key, value) in DetectorCatalog.Normalize(settings.DetectorId, settings.Parameters))
        {
            fields[ParameterPrefix + key] = value;
        }

        storage.OpenCollection(SettingsCollection).Upsert(SettingsId, fields);
    }

    public static SourceDetectorSettings DefaultSettings() =>
        new(DetectorCatalog.FrameDifference,
            DetectorCatalog.Normalize(DetectorCatalog.FrameDifference, stored: null));

    private static SourceConfiguration? ReadSource(PluginDocumentRecord record)
    {
        var sourceId = ReadString(record.Fields, "SourceId") ?? record.Id;
        var kindId = ReadString(record.Fields, "KindId");
        var location = ReadString(record.Fields, "Location");
        var destination = ReadString(record.Fields, "DestinationFolder");
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(kindId) ||
            string.IsNullOrWhiteSpace(location) || string.IsNullOrWhiteSpace(destination))
        {
            return null;
        }

        return new SourceConfiguration(
            sourceId,
            kindId,
            location,
            destination,
            ReadString(record.Fields, "CatalogFolder") ?? "Motion",
            ReadString(record.Fields, "Label") ?? kindId);
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static bool TryReadDouble(object? value, out double result)
    {
        switch (value)
        {
            case double number:
                result = number;
                return true;
            case float number:
                result = number;
                return true;
            case int number:
                result = number;
                return true;
            case long number:
                result = number;
                return true;
            case string text:
                return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
            default:
                result = 0;
                return false;
        }
    }
}
