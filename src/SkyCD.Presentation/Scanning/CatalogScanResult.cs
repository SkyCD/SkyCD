using System.Collections.Generic;
using SkyCD.Documents;

namespace SkyCD.Presentation.Scanning;

public sealed class CatalogScanResult
{
    public required IReadOnlyList<CatalogDocument> Documents { get; init; }

    public required ScanTraversalStatistics Statistics { get; init; }
}
