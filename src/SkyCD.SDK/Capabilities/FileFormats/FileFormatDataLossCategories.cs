namespace SkyCD.SDK.Capabilities.FileFormats;

/// <summary>
/// Stable category identifiers used by <see cref="FileFormatDataLossItem.Category"/>.
/// The host localizes known identifiers and falls back to the raw value.
/// </summary>
public static class FileFormatDataLossCategories
{
    /// <summary>Per-entry field loss such as status, size or custom properties.</summary>
    public const string Entries = "entries";

    /// <summary>Virtual collection definitions.</summary>
    public const string Collections = "collections";

    /// <summary>Custom status definitions.</summary>
    public const string Statuses = "statuses";

    /// <summary>Loan records contributed by a feature plugin.</summary>
    public const string Loans = "loans";

    /// <summary>Borrower or contact records contributed by a feature plugin.</summary>
    public const string Borrowers = "borrowers";

    /// <summary>User-defined scan exclusion rules.</summary>
    public const string ScanExclusions = "scanExclusions";

    /// <summary>User-added AI scan exclusion MIME types.</summary>
    public const string AiScanExclusions = "aiScanExclusions";
}

/// <summary>
/// Stable per-entry field identifiers used as <see cref="FileFormatDataLossItem.Name"/>
/// inside the <see cref="FileFormatDataLossCategories.Entries"/> category.
/// </summary>
public static class FileFormatDataLossFields
{
    /// <summary>Entry identifiers.</summary>
    public const string Id = "id";

    /// <summary>Entry parent relationships.</summary>
    public const string ParentId = "parentId";

    /// <summary>Entry kinds.</summary>
    public const string Kind = "kind";

    /// <summary>Entry sizes in bytes.</summary>
    public const string Size = "size";

    /// <summary>Assigned statuses.</summary>
    public const string Status = "status";

    /// <summary>Custom entry properties. Property keys use the <see cref="PropertyKeyPrefix"/> prefix.</summary>
    public const string Properties = "properties";

    /// <summary>Prefix used for a concrete property key item name.</summary>
    public const string PropertyKeyPrefix = "property:";
}
