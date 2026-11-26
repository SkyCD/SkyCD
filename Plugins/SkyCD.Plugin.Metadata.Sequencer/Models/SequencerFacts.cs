using SkyCD.SDK.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Sequencer.Models;

internal sealed class SequencerFacts(string kind, string format)
{
    private readonly Dictionary<string, MediaMetadataValue> values = new()
    {
        ["Kind"] = new("Kind", MediaMetadataType.Text, Text: kind),
        ["Format"] = new("Format", MediaMetadataType.Text, Text: format)
    };

    internal IReadOnlyCollection<MediaMetadataValue> Values => values.Values;
    internal List<string> Names { get; } = [];

    internal void Text(string key, string? value)
    {
        if (value is null)
            return;

        var cleaned = Clean(value);
        if (cleaned.Length > 0)
            values[key] = new(key, MediaMetadataType.Text, Text: cleaned);
    }

    internal void Integer(string key, long value) => values[key] = new(key, MediaMetadataType.Integer, Integer: value);

    internal void Number(string key, double value)
    {
        if (double.IsFinite(value) && value >= 0)
            values[key] = new(key, MediaMetadataType.Number, Number: value);
    }

    internal void Boolean(string key, bool value) => values[key] = new(key, MediaMetadataType.Boolean, Boolean: value);

    internal void AddNames(IEnumerable<string?> names, int limit) =>
        Names.AddRange(names.Where(name => name is not null).Select(name => Clean(name!))
            .Where(name => name.Length > 0).Take(limit));

    private static string Clean(string text) => new string(text.Where(c => !char.IsControl(c)).Take(120).ToArray()).Trim();
}
