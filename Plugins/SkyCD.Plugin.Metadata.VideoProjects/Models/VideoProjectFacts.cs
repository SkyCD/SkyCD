using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Metadata.VideoProjects.Models;

/// <summary>
/// Facts one reader recovered from a project container. Absent optional members are simply not published;
/// <paramref name="Truncated"/> records that a bounded walk stopped early but the facts so far are usable.
/// </summary>
internal sealed record VideoProjectFacts
{
    public VideoProjectFacts(string kind, string format, string application)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentException.ThrowIfNullOrWhiteSpace(application);

        Kind = kind;
        Format = format;
        Application = application;
    }

    public string Kind { get; }

    public string Format { get; }

    public string Application { get; }

    public string? ApplicationVersion { get; init; }

    public string? ProjectTitle { get; init; }

    public long? TimelineCount { get; init; }

    public IReadOnlyList<string> TimelineNames { get; init; } = [];

    public long? TrackCount { get; init; }

    public long? ClipCount { get; init; }

    public long? ItemCount { get; init; }

    public long? MediaFileCount { get; init; }

    public double? FrameRate { get; init; }

    public double? DurationSeconds { get; init; }

    public string? Resolution { get; init; }

    public bool? IsTemplate { get; init; }

    public bool Truncated { get; init; }
}
