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

    /// <summary>
    /// Returns a copy with a different name. The document is immutable, and the resolved location and
    /// feature decoration projections are carried over so a rename does not drop them in the same session.
    /// </summary>
    public CatalogDocument WithName(string name) => new()
    {
        Id = Id,
        Name = name,
        ParentId = ParentId,
        Type = Type,
        Size = Size,
        ChildrenCount = ChildrenCount,
        ContentsState = ContentsState,
        Status = Status,
        Properties = new PropertiesCollection(Properties),
        locationName = locationName,
        displayName = displayName,
        badgeText = badgeText,
        badgeIcon = badgeIcon,
        badgeTooltip = badgeTooltip,
        badgeAttention = badgeAttention
    };

    private string? displayName;

    /// <summary>
    /// Name shown in the browser when a plugin captured a preferred title (for example a comic series).
    /// Computed by the browser projection and never serialized, so the catalogued file name — which merges,
    /// renames and deletions rely on — is untouched.
    /// </summary>
    public string DisplayName => displayName ?? Name;

    public void SetDisplayName(string? name) =>
        displayName = string.IsNullOrWhiteSpace(name) || string.Equals(name, Name, System.StringComparison.Ordinal)
            ? null
            : name;

    private string? badgeText;
    private string? badgeIcon;
    private string? badgeTooltip;
    private bool badgeAttention;

    /// <summary>
    /// Short badge text projected from an enabled feature plugin, or null when the entry has none.
    /// Computed at projection time and never serialized.
    /// </summary>
    [CouchbaseTransient]
    public string? BadgeText => badgeText;

    /// <summary>Optional badge glyph projected from a feature plugin.</summary>
    [CouchbaseTransient]
    public string? BadgeIcon => badgeIcon;

    /// <summary>Optional badge tooltip projected from a feature plugin.</summary>
    [CouchbaseTransient]
    public string? BadgeTooltip => badgeTooltip;

    /// <summary>Whether the projected badge asks for attention (for example an overdue state).</summary>
    [CouchbaseTransient]
    public bool BadgeAttention => badgeAttention;

    /// <summary>Sets the feature-plugin decoration shown on this entry. Pass nulls to clear it.</summary>
    public void SetDecoration(string? text, string? icon, string? tooltip, bool attention)
    {
        badgeText = text;
        badgeIcon = icon;
        badgeTooltip = tooltip;
        badgeAttention = attention;
    }

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

    /// <summary>
    /// Property key that marks a stored size as unknown. Importers that cannot read a size, and manually
    /// created entries that have never been scanned, write <c>false</c> so the surfaces can leave the size
    /// empty instead of presenting the default zero as a real total.
    /// </summary>
    public const string SizeKnownPropertyKey = "SizeKnown";

    public bool HasKnownSize => !Properties.TryGetValue(SizeKnownPropertyKey, out var value) ||
                                !bool.TryParse(value?.ToString(), out var known) || known;

    public string DisplaySize => HasKnownSize ? SizeFormatting.FormatBytes(Size, "0.##") : string.Empty;

    /// <summary>
    /// Returns a copy carrying a maintained size. A known size drops the unknown marker, because the stored
    /// total now describes the entry; an unknown size keeps it and leaves the last stored value untouched so
    /// nothing is silently zeroed.
    /// </summary>
    public CatalogDocument WithSize(long size, bool sizeKnown)
    {
        var properties = new PropertiesCollection(Properties);
        if (sizeKnown)
        {
            properties.Remove(SizeKnownPropertyKey);
        }
        else
        {
            properties[SizeKnownPropertyKey] = false;
        }

        return new CatalogDocument
        {
            Id = Id,
            Name = Name,
            ParentId = ParentId,
            Type = Type,
            Size = size,
            ChildrenCount = ChildrenCount,
            ContentsState = ContentsState,
            Status = Status,
            Properties = properties,
            locationName = locationName,
            displayName = displayName,
            badgeText = badgeText,
            badgeIcon = badgeIcon,
            badgeTooltip = badgeTooltip,
            badgeAttention = badgeAttention
        };
    }

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
