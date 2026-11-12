using System.Collections.Generic;

namespace SkyCD.Presentation.Operations;

public sealed record OperationProgressReport
{
    public string? OperationTitle { get; init; }
    public string? StageName { get; init; }
    public string? CurrentItem { get; init; }

    public double? OverallProgressPercentage { get; init; }
    public bool? IsOverallIndeterminate { get; init; }
    public long? OverallWorkCurrent { get; init; }
    public long? OverallWorkTotal { get; init; }

    public double? StageProgressPercentage { get; init; }
    public bool? IsStageIndeterminate { get; init; }
    public long? StageWorkCurrent { get; init; }
    public long? StageWorkTotal { get; init; }

    public OperationLogEntry? LogEntry { get; init; }
    public IReadOnlyList<OperationLogEntry>? LogEntries { get; init; }

    public static OperationProgressReport CreateStage(
        string stageName,
        double? stagePercentage = null,
        double? overallPercentage = null,
        long? stageCurrent = null,
        long? stageTotal = null,
        long? overallCurrent = null,
        long? overallTotal = null) => new()
        {
            StageName = stageName,
            StageProgressPercentage = stagePercentage,
            IsStageIndeterminate = stagePercentage is null ? true : false,
            OverallProgressPercentage = overallPercentage,
            IsOverallIndeterminate = overallPercentage.HasValue ? false : null,
            StageWorkCurrent = stageCurrent,
            StageWorkTotal = stageTotal,
            OverallWorkCurrent = overallCurrent,
            OverallWorkTotal = overallTotal
        };

    public static OperationProgressReport CreateItem(
        string currentItem,
        double? stagePercentage = null,
        double? overallPercentage = null,
        long? stageCurrent = null,
        long? stageTotal = null,
        long? overallCurrent = null,
        long? overallTotal = null) => new()
        {
            CurrentItem = currentItem,
            StageProgressPercentage = stagePercentage,
            IsStageIndeterminate = stagePercentage.HasValue ? false : null,
            OverallProgressPercentage = overallPercentage,
            IsOverallIndeterminate = overallPercentage.HasValue ? false : null,
            StageWorkCurrent = stageCurrent,
            StageWorkTotal = stageTotal,
            OverallWorkCurrent = overallCurrent,
            OverallWorkTotal = overallTotal
        };

    public static OperationProgressReport CreateLog(OperationLogEntry logEntry) => new()
    {
        LogEntry = logEntry
    };
}
