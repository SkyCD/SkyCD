namespace SkyCD.Plugin.DiskExplorer.Ded.Models;

/// <summary>
/// Classifies opaque payloads attached to a catalog object. Unknown kinds are
/// preserved verbatim and are never removed by transfer options.
/// </summary>
public enum DedPayloadKind
{
    Unknown = 0,
    StoredContent = 1,
    Thumbnail = 2,
    GeneratedDescription = 3
}

/// <summary>
/// Textual payload kind names used in documentation and diagnostics.
/// </summary>
public static class DedPayloadKinds
{
    public const string StoredContent = "stored-content";
    public const string Thumbnail = "thumbnail";
    public const string GeneratedDescription = "generated-description";

    public static string ToText(uint rawKind) => rawKind switch
    {
        (uint)DedPayloadKind.StoredContent => StoredContent,
        (uint)DedPayloadKind.Thumbnail => Thumbnail,
        (uint)DedPayloadKind.GeneratedDescription => GeneratedDescription,
        _ => DedObjectKinds.Unknown
    };
}
