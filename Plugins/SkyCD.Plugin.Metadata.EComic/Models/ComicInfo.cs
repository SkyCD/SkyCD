namespace SkyCD.Plugin.Metadata.EComic.Models;

/// <summary>
/// Values read from a ComicRack-style <c>ComicInfo.xml</c>. A missing element stays <c>null</c> so "unknown"
/// is distinguishable from an empty value; the extraction plugin publishes only what is present.
/// </summary>
public sealed record ComicInfo(
    string? Series = null,
    string? Title = null,
    string? Number = null,
    string? Volume = null,
    string? Writer = null,
    string? Publisher = null,
    string? Genre = null,
    int? Year = null,
    int? PageCount = null,
    string? LanguageIso = null,
    string? Summary = null)
{
    public static ComicInfo Empty { get; } = new();

    /// <summary>True when the document carried no publishable value at all.</summary>
    public bool IsEmpty =>
        Series is null && Title is null && Number is null && Volume is null && Writer is null &&
        Publisher is null && Genre is null && Year is null && PageCount is null && LanguageIso is null &&
        Summary is null;
}
