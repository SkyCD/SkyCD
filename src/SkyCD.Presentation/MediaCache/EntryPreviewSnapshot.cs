using System;
using System.Collections.Generic;
using SkyCD.UI.Controls.Preview;

namespace SkyCD.Presentation.MediaCache;

/// <summary>
/// Everything the catalog browser needs to display one entry offline: the resolved availability, the
/// projected typed fields and a reference to the stored preview payload. A snapshot is built only from
/// cache records, so producing one never reconnects media and never runs a plugin decoder.
/// </summary>
public sealed record EntryPreviewSnapshot
{
    /// <summary>The snapshot shown while nothing is selected.</summary>
    public static EntryPreviewSnapshot NoSelection { get; } = new()
    {
        State = MediaPreviewState.NoSelection
    };

    public string EntryId { get; init; } = string.Empty;

    public MediaPreviewState State { get; init; } = MediaPreviewState.Unavailable;

    public PreviewPayloadKind PayloadKind { get; init; } = PreviewPayloadKind.None;

    /// <summary>The record the preview payload comes from, used as the decode-cache key.</summary>
    public string? RecordId { get; init; }

    public MediaArtifactReference? Artifact { get; init; }

    /// <summary>Sanitized failure classification carried by the record; never plugin exception text.</summary>
    public string? FailureReason { get; init; }

    /// <summary>Plugin that produced the selected payload, kept for diagnostics while it is disabled.</summary>
    public string? ProviderId { get; init; }

    public DateTimeOffset? CapturedAtUtc { get; init; }

    /// <summary>Bounded decoded text, present only for <see cref="PreviewPayloadKind.Text"/> payloads.</summary>
    public string? TextPreview { get; init; }

    /// <summary>True when the bounded text preview was cut short.</summary>
    public bool IsTextPreviewTruncated { get; init; }

    public IReadOnlyList<MediaMetadataFieldView> Fields { get; init; } = [];

    public bool HasPayload => PayloadKind != PreviewPayloadKind.None;

    public bool IsStale => State == MediaPreviewState.Stale;

    public bool HasFields => Fields.Count > 0;
}
