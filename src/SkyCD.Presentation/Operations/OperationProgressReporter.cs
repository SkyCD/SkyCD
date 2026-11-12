using System;

namespace SkyCD.Presentation.Operations;

public sealed class OperationProgressReporter : IOperationProgress
{
    private readonly IProgress<OperationProgressReport> target;
    private string? currentStage;

    public OperationProgressReporter(IProgress<OperationProgressReport> target)
    {
        this.target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public void Report(OperationProgressReport value) => target.Report(value);

    public void ReportStage(
        string stageName,
        double? stagePercentage = null,
        double? overallPercentage = null,
        long? stageCurrent = null,
        long? stageTotal = null,
        long? overallCurrent = null,
        long? overallTotal = null)
    {
        currentStage = stageName;
        target.Report(OperationProgressReport.CreateStage(
            stageName,
            stagePercentage,
            overallPercentage,
            stageCurrent,
            stageTotal,
            overallCurrent,
            overallTotal));
    }

    public void ReportItem(
        string currentItem,
        double? stagePercentage = null,
        double? overallPercentage = null,
        long? stageCurrent = null,
        long? stageTotal = null,
        long? overallCurrent = null,
        long? overallTotal = null)
    {
        target.Report(OperationProgressReport.CreateItem(
            currentItem,
            stagePercentage,
            overallPercentage,
            stageCurrent,
            stageTotal,
            overallCurrent,
            overallTotal));
    }

    public void ReportOverall(
        double? overallPercentage,
        long? overallCurrent = null,
        long? overallTotal = null,
        string? operationTitle = null)
    {
        target.Report(new OperationProgressReport
        {
            OperationTitle = operationTitle,
            OverallProgressPercentage = overallPercentage,
            OverallWorkCurrent = overallCurrent,
            OverallWorkTotal = overallTotal
        });
    }

    public void ReportLog(
        OperationLogLevel level,
        string message,
        string? stage = null,
        string? itemPath = null,
        string? details = null)
    {
        var entry = new OperationLogEntry(level, message, stage ?? currentStage, itemPath, details);
        target.Report(OperationProgressReport.CreateLog(entry));
    }

    public void ReportInfo(string message, string? itemPath = null, string? stage = null) =>
        ReportLog(OperationLogLevel.Information, message, stage, itemPath);

    public void ReportWarning(string message, string? itemPath = null, string? stage = null) =>
        ReportLog(OperationLogLevel.Warning, message, stage, itemPath);

    public void ReportError(string message, Exception? exception = null, string? itemPath = null, string? stage = null) =>
        ReportLog(OperationLogLevel.Error, message, stage, itemPath, exception?.ToString());

    public void ReportSkipped(string itemPath, string reason, string? stage = null) =>
        ReportLog(OperationLogLevel.ItemSkipped, $"Skipped: {reason}", stage, itemPath, reason);

    public void ReportFailed(string itemPath, string reason, string? stage = null, Exception? exception = null) =>
        ReportLog(
            OperationLogLevel.ItemFailed,
            $"Failed: {reason}",
            stage,
            itemPath,
            exception?.ToString() ?? reason);
}
