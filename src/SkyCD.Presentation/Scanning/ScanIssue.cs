namespace SkyCD.Presentation.Scanning;

public enum ScanIssueSeverity
{
    Warning,
    Error
}

/// <summary>One error or warning reported while a catalog scan was running.</summary>
public sealed record ScanIssue(ScanIssueSeverity Severity, string? ItemPath, string Message);
