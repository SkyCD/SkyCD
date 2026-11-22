namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Facts about a scan root that influence how much parallel I/O is safe.
/// </summary>
public sealed record ScanSourceProfile(
    ScanSourceKind Kind,
    long? CapacityBytes = null,
    string? FileSystemFormat = null)
{
    public static ScanSourceProfile Unknown { get; } = new(ScanSourceKind.Unknown);

    public static ScanSourceProfile Network { get; } = new(ScanSourceKind.Network);

    public static ScanSourceProfile FromDrive(MediaDriveInfo? drive) => ScanSourceClassifier.FromDrive(drive);

    public static ScanSourceProfile FromPath(string? path) => ScanSourceClassifier.FromPath(path);
}
