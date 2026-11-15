namespace SkyCD.Presentation.Staging;

public enum ScanStagingSessionStatus
{
    Active = 0,
    Committed = 1,
    Cancelled = 2,
    Failed = 3,
    Abandoned = 4
}
