using System.Collections.Generic;
using SkyCD.Couchbase.Attributes;
using SkyCD.Documents.Collections;
using SkyCD.Documents.Enum;
using SkyCD.Documents.Repository;
using SkyCD.Formatting;

namespace SkyCD.Documents;

[CouchbaseDocument("catalog", repositoryType: typeof(CatalogDocumentRepository))]
public sealed class CatalogDocument
{
    [Id] public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    [ParentId] public string? ParentId { get; init; }

    public CatalogDocumentType Type { get; init; } = CatalogDocumentType.File;

    public long Size { get; init; }

    public long ChildrenCount { get; init; }

    public CatalogContentsState ContentsState { get; init; } = CatalogContentsState.Known;

    public string? Status { get; set; }

    public PropertiesCollection Properties { get; init; } = new();

    private (string Borrower, bool Overdue)? lendingState;

    public string LentTo => lendingState?.Borrower ?? string.Empty;

    public bool IsLent => lendingState.HasValue;

    public bool IsLoanOverdue => lendingState?.Overdue == true;

    public void SetLendingState((string Borrower, bool Overdue)? state) => lendingState = state;

    private string? locationName;

    /// <summary>
    /// Resolved physical storage location name, projected from the assigned location id. Persisted
    /// assignments live in <c>Properties["LocationId"]</c> and are inherited from ancestors, so this
    /// value is computed at projection time and is never serialized.
    /// </summary>
    public string Location => locationName ?? string.Empty;

    public bool HasLocation => !string.IsNullOrEmpty(locationName);

    public void SetLocationName(string? name) => locationName = name;

    public string DisplayType => Type.ToDisplayName();

    public bool HasKnownSize => !Properties.TryGetValue("SizeKnown", out var value) ||
                                !bool.TryParse(value?.ToString(), out var known) || known;

    public string DisplaySize => HasKnownSize ? SizeFormatting.FormatBytes(Size, "0.##") : string.Empty;

    public string IconGlyph => ResolveIconGlyph();

    private string ResolveIconGlyph()
    {
        if (Properties.TryGetValue("Icon", out var iconValue) && iconValue is not null)
        {
            if (iconValue is byte[] bytes)
            {
                return $"data:image/x-icon;base64,{System.Convert.ToBase64String(bytes)}";
            }

            var iconStr = iconValue.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(iconStr))
            {
                return iconStr;
            }
        }

        if (Type == CatalogDocumentType.File)
        {
            var ext = System.IO.Path.GetExtension(Name);
            if (!string.IsNullOrWhiteSpace(ext))
            {
                return $"ext:{ext.TrimStart('.').ToLowerInvariant()}";
            }
        }

        return Type.ResolveIconGlyph();
    }
}
