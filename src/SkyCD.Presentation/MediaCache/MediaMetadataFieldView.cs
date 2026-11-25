using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Presentation.MediaCache;

/// <summary>One metadata field of an entry with its provenance, for display and for edit protection.</summary>
public sealed class MediaMetadataFieldView
{
    public required string Key { get; init; }

    public required MediaMetadataOrigin Origin { get; init; }

    /// <summary>The effective value: the user annotation when present, otherwise the captured value.</summary>
    public object? Value { get; init; }

    public MediaMetadataType? Type { get; init; }

    public string? Unit { get; init; }

    public string? PluginId { get; init; }

    public string? PluginVersion { get; init; }

    public string? SchemaId { get; init; }

    public int SchemaVersion { get; init; }

    public MediaCacheRecordState? RecordState { get; init; }

    /// <summary>The captured value a user annotation replaced, kept so it can be shown or restored.</summary>
    public object? CapturedValue { get; init; }

    public bool IsUserEdited => Origin == MediaMetadataOrigin.UserEdited;

    /// <summary>True when the value comes from a record that is no longer current.</summary>
    public bool IsFromStaleRecord => RecordState is MediaCacheRecordState.Stale or MediaCacheRecordState.Missing;

    /// <summary>True when the provider tried to capture this field and failed, so there is no value.</summary>
    public bool IsFailed => RecordState == MediaCacheRecordState.Failed;
}
