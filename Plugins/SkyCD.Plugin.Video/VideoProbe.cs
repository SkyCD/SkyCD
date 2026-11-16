using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.Video;

/// <summary>One stream inside the container, video or audio.</summary>
internal sealed record VideoStreamInfo(string Kind, string? Codec, double? FrameRate, int? Width, int? Height, int? Channels);

/// <summary>Everything the plugin learned about one source, in the terms the schema publishes.</summary>
internal sealed class VideoProbe
{
    /// <summary>Container detected from the bytes, not the file name.</summary>
    public required string Container { get; init; }

    public double? DurationSeconds { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public int? RotationDegrees { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public double? FrameRate { get; set; }
    public int? BitrateKbps { get; set; }
    public string? Title { get; set; }
    public List<VideoStreamInfo> Streams { get; } = [];

    public int VideoStreamCount
    {
        get
        {
            var count = 0;
            foreach (var stream in Streams)
            {
                if (stream.Kind == "video")
                {
                    count++;
                }
            }

            return count;
        }
    }

    public int AudioStreamCount
    {
        get
        {
            var count = 0;
            foreach (var stream in Streams)
            {
                if (stream.Kind == "audio")
                {
                    count++;
                }
            }

            return count;
        }
    }
}
