using System;

namespace SkyCD.Presentation.Operations;

public interface IOperationProgress : IProgress<OperationProgressReport>
{
    void ReportStage(
        string stageName,
        double? stagePercentage = null,
        double? overallPercentage = null,
        long? stageCurrent = null,
        long? stageTotal = null,
        long? overallCurrent = null,
        long? overallTotal = null);

    void ReportItem(
        string currentItem,
        double? stagePercentage = null,
        double? overallPercentage = null,
        long? stageCurrent = null,
        long? stageTotal = null,
        long? overallCurrent = null,
        long? overallTotal = null);

    void ReportOverall(
        double? overallPercentage,
        long? overallCurrent = null,
        long? overallTotal = null,
        string? operationTitle = null);

    void ReportLog(
        OperationLogLevel level,
        string message,
        string? stage = null,
        string? itemPath = null,
        string? details = null);

    void ReportInfo(string message, string? itemPath = null, string? stage = null);

    void ReportWarning(string message, string? itemPath = null, string? stage = null);

    void ReportError(string message, Exception? exception = null, string? itemPath = null, string? stage = null);

    void ReportSkipped(string itemPath, string reason, string? stage = null);
}
