namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Describes how a catalog scan operation ended.
/// </summary>
public enum ScanOutcome
{
    /// <summary>Scan completed normally and all staged results were published.</summary>
    Success,

    /// <summary>Scan was canceled cooperatively before completion. No partial results are published by default.</summary>
    Canceled,

    /// <summary>Scan ended due to a fatal, unrecoverable error.</summary>
    Failed
}
