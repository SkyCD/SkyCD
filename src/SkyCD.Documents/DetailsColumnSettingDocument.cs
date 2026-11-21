namespace SkyCD.Documents;

public sealed class DetailsColumnSettingDocument
{
    public string Id { get; set; } = string.Empty;

    public bool IsVisible { get; set; } = true;

    /// <summary>Saved pixel width; <c>null</c> means the catalog default width is used.</summary>
    public double? Width { get; set; }
}
