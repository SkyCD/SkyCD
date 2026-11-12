using System;

namespace SkyCD.Presentation.Operations;

public sealed class OperationProgressAdapter : IOperationProgress
{
    private readonly IProgress<(string Status, int Percentage)> target;

    public OperationProgressAdapter(IProgress<(string Status, int Percentage)> target)
    {
        this.target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public void Report(OperationProgressReport value)
    {
        var status = value.StageName ?? value.OperationTitle ?? string.Empty;
        if (!string.IsNullOrEmpty(status))
        {
            var percentage = (int)Math.Round(value.OverallProgressPercentage ?? value.StageProgressPercentage ?? 0);
            target.Report((status, percentage));
        }
    }

    public void ReportStage(
        string stageName,
        double? stagePercentage = null,
        double? overallPercentage = null,
        long? stageCurrent = null,
        long? stageTotal = null,
        long? overallCurrent = null,
        long? overallTotal = null)
    {
        var percent = (int)Math.Round(overallPercentage ?? stagePercentage ?? 0);
        target.Report((stageName, percent));
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
    }

    public void ReportOverall(
        double? overallPercentage,
        long? overallCurrent = null,
        long? overallTotal = null,
        string? operationTitle = null)
    {
        if (!string.IsNullOrEmpty(operationTitle))
        {
            var percent = (int)Math.Round(overallPercentage ?? 0);
            target.Report((operationTitle, percent));
        }
    }

    public void ReportLog(
        OperationLogLevel level,
        string message,
        string? stage = null,
        string? itemPath = null,
        string? details = null)
    {
    }

    public void ReportInfo(string message, string? itemPath = null, string? stage = null) { }

    public void ReportWarning(string message, string? itemPath = null, string? stage = null) { }

    public void ReportError(string message, Exception? exception = null, string? itemPath = null, string? stage = null) { }

    public void ReportSkipped(string itemPath, string reason, string? stage = null) { }
}
