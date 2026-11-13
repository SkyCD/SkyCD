using System.Collections.Generic;
using SkyCD.Documents;

namespace SkyCD.Presentation.Scanning;

public sealed class CatalogScanResult
{
    public required IReadOnlyList<CatalogDocument> Documents { get; init; }

    public required ScanTraversalStatistics Statistics { get; init; }

    /// <summary>
    /// How the scan ended: <see cref="ScanOutcome.Success"/>, <see cref="ScanOutcome.Canceled"/> or
    /// <see cref="ScanOutcome.Failed"/>.  Callers must not publish documents when the outcome is
    /// <see cref="ScanOutcome.Canceled"/> or <see cref="ScanOutcome.Failed"/> unless the user
    /// explicitly opts into partial results (see issue #434).
    /// </summary>
    public ScanOutcome Outcome { get; init; } = ScanOutcome.Success;

    /// <summary>
    /// <see langword="true"/> when the scan completed and produced results that are safe to publish.
    /// Equivalent to <c>Outcome == ScanOutcome.Success</c>.
    /// </summary>
    public bool IsSuccess => Outcome == ScanOutcome.Success;
}
