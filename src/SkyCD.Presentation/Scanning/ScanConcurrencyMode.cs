namespace SkyCD.Presentation.Scanning;

/// <summary>How the scan worker count is chosen.</summary>
public enum ScanConcurrencyMode
{
    /// <summary>Derive the worker count from the scan source and the current system load.</summary>
    Automatic,

    /// <summary>Use the explicit worker count from <see cref="ScanTraversalOptions.MaxConcurrency"/>.</summary>
    Manual
}
