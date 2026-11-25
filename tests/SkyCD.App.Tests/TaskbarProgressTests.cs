using System;
using System.Runtime.Versioning;
using Xunit;

namespace SkyCD.App.Tests;

// The interop surface is Windows-only by design; the zero-handle probes below stay safe on every
// platform because the implementation guards before touching any native API.
[SupportedOSPlatform("windows")]
public class TaskbarProgressTests
{
    [Fact]
    public void TaskbarCalls_WithZeroHandle_NeverThrow()
    {
        var handle = IntPtr.Zero;

        WindowsTaskbarProgress.SetIndeterminate(handle);
        WindowsTaskbarProgress.SetProgress(handle, 42);
        WindowsTaskbarProgress.Clear(handle);
    }

    [Fact]
    public void SetProgress_WithZeroHandle_AcceptsOutOfRangeValues()
    {
        WindowsTaskbarProgress.SetProgress(IntPtr.Zero, -5);
        WindowsTaskbarProgress.SetProgress(IntPtr.Zero, 250);
    }

    [Fact]
    public void Throttle_FirstReport_AlwaysPasses()
    {
        var throttle = new TaskbarProgressThrottle(new ManualTimeProvider());

        Assert.True(throttle.ShouldReport(TaskbarProgressState.Indeterminate, 0));
    }

    [Fact]
    public void Throttle_SameValueTwice_SuppressesSecond()
    {
        var throttle = new TaskbarProgressThrottle(new ManualTimeProvider());

        Assert.True(throttle.ShouldReport(TaskbarProgressState.Normal, 10));
        Assert.False(throttle.ShouldReport(TaskbarProgressState.Normal, 10));
    }

    [Fact]
    public void Throttle_StateChange_PassesImmediately()
    {
        var throttle = new TaskbarProgressThrottle(new ManualTimeProvider());

        Assert.True(throttle.ShouldReport(TaskbarProgressState.Normal, 10));
        Assert.True(throttle.ShouldReport(TaskbarProgressState.Indeterminate, 0));
        Assert.True(throttle.ShouldReport(TaskbarProgressState.NoProgress, 0));
    }

    [Fact]
    public void Throttle_ValueChangeWithinInterval_IsSuppressedUntilIntervalPasses()
    {
        var clock = new ManualTimeProvider();
        var throttle = new TaskbarProgressThrottle(clock);

        Assert.True(throttle.ShouldReport(TaskbarProgressState.Normal, 10));
        Assert.False(throttle.ShouldReport(TaskbarProgressState.Normal, 11));

        clock.Advance(TaskbarProgressThrottle.MinimumInterval);

        Assert.True(throttle.ShouldReport(TaskbarProgressState.Normal, 11));
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan delta) => now += delta;
    }
}
