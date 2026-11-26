using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Metadata.GameDev.Tests;

/// <summary>Sends a fixture through the public capability exactly as the host would and indexes the values.</summary>
internal static class GameDevTestHarness
{
    internal static async Task<Dictionary<string, MediaMetadataValue>> ExtractAsync(string extension,
        byte[] data)
    {
        var plugin = new GameDevMetadataPlugin();
        var result = await plugin
            .ExtractAsync(new MediaItem(extension, null, data.Length), new ByteArrayMediaSource(data), null,
                CancellationToken.None)
            .ConfigureAwait(false);
        return result.Values.ToDictionary(value => value.Key, StringComparer.Ordinal);
    }

    internal static async Task<Dictionary<string, MediaMetadataValue>> ExtractAsync(string extension,
        string text) => await ExtractAsync(extension, GameDevFixtures.Utf8(text)).ConfigureAwait(false);

    internal static Task<MediaPreviewResult> PreviewAsync(IMediaPreviewCapability plugin, string extension,
        byte[] data) =>
        plugin.GeneratePreviewAsync(new MediaItem(extension, null, data.Length), new ByteArrayMediaSource(data),
            null, CancellationToken.None);

    internal static string? Text(this Dictionary<string, MediaMetadataValue> values, string key) =>
        values.TryGetValue(key, out var value) ? value.Text : null;

    internal static long? Integer(this Dictionary<string, MediaMetadataValue> values, string key) =>
        values.TryGetValue(key, out var value) ? value.Integer : null;

    internal static bool? Boolean(this Dictionary<string, MediaMetadataValue> values, string key) =>
        values.TryGetValue(key, out var value) ? value.Boolean : null;

    internal static byte[] Repeat(string text, int times)
    {
        var builder = new System.Text.StringBuilder(text.Length * times);
        for (var index = 0; index < times; index++)
        {
            builder.Append(text);
        }

        return GameDevFixtures.Utf8(builder.ToString());
    }
}
