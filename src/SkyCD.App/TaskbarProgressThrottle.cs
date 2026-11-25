using System;

namespace SkyCD.App;

/// <summary>
/// Bounds how often progress reaches the taskbar so frequent progress reports do not flicker the bar:
/// state changes always pass, while repeated value updates inside the minimum interval are dropped.
/// The clock is injectable for tests.
/// </summary>
internal sealed class TaskbarProgressThrottle
{
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(200);

    private readonly TimeProvider clock;
    private DateTimeOffset lastReportAt = DateTimeOffset.MinValue;
    private TaskbarProgressState lastState = TaskbarProgressState.NoProgress;
    private ulong lastCompleted;
    private bool hasReported;

    public TaskbarProgressThrottle(TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
    }

    public bool ShouldReport(TaskbarProgressState state, ulong completed)
    {
        if (!hasReported || state != lastState)
        {
            Record(state, completed);
            return true;
        }

        if (completed != lastCompleted && clock.GetUtcNow() - lastReportAt >= MinimumInterval)
        {
            Record(state, completed);
            return true;
        }

        return false;
    }

    private void Record(TaskbarProgressState state, ulong completed)
    {
        hasReported = true;
        lastState = state;
        lastCompleted = completed;
        lastReportAt = clock.GetUtcNow();
    }
}
