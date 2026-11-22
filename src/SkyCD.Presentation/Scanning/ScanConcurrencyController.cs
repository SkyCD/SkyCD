using System;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Adaptive worker-count controller for one scan. Growth is additive and slow, load pressure shrinks
/// the pool immediately, and an EMA plus a cool-down keep the count from oscillating.
/// </summary>
public sealed class ScanConcurrencyController
{
    public const double DefaultSmoothingFactor = 0.4;
    public const int DefaultCompletionsPerStep = 32;
    public const int HysteresisPercent = 10;
    public static readonly TimeSpan DefaultCoolDown = TimeSpan.FromSeconds(2.5);

    private readonly object gate = new();
    private readonly int ceiling;
    private readonly int shrinkThresholdPercent;
    private readonly int suspendThresholdPercent;
    private readonly double smoothingFactor;
    private readonly int completionsPerStep;
    private readonly TimeSpan coolDown;
    private readonly Func<DateTimeOffset> clock;

    private int current;
    private int completionsSinceGrowth;
    private double smoothedLoad;
    private bool hasLoadSample;
    private DateTimeOffset lastChange;

    public ScanConcurrencyController(
        ScanConcurrencyPlan plan,
        ScanTraversalOptions options,
        Func<DateTimeOffset>? clock = null,
        double smoothingFactor = DefaultSmoothingFactor,
        int completionsPerStep = DefaultCompletionsPerStep,
        TimeSpan? coolDown = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);

        ceiling = Math.Max(1, plan.CeilingWorkers);
        current = Math.Clamp(plan.InitialWorkers, 1, ceiling);
        shrinkThresholdPercent = options.GetBoundedLoadShrinkThresholdPercent();
        suspendThresholdPercent = options.GetBoundedLoadSuspendThresholdPercent();
        this.smoothingFactor = Math.Clamp(smoothingFactor, 0.01, 1);
        this.completionsPerStep = Math.Max(1, completionsPerStep);
        this.coolDown = coolDown ?? DefaultCoolDown;
        this.clock = clock ?? (static () => DateTimeOffset.UtcNow);
        lastChange = this.clock() - this.coolDown;
    }

    public int Current
    {
        get
        {
            lock (gate)
            {
                return current;
            }
        }
    }

    public int SourceCeiling => ceiling;

    public double SmoothedLoad
    {
        get
        {
            lock (gate)
            {
                return smoothedLoad;
            }
        }
    }

    /// <summary>
    /// Records finished work; enough healthy completions raise the pool by one worker.
    /// </summary>
    public void RecordCompleted(int count = 1)
    {
        if (count <= 0)
        {
            return;
        }

        lock (gate)
        {
            if (current >= ceiling)
            {
                return;
            }

            completionsSinceGrowth += count;
            if (completionsSinceGrowth < completionsPerStep || InCoolDown())
            {
                return;
            }

            if (hasLoadSample && smoothedLoad * 100 >= shrinkThresholdPercent - HysteresisPercent)
            {
                return;
            }

            current = Math.Min(ceiling, current + 1);
            completionsSinceGrowth = 0;
            lastChange = clock();
        }
    }

    /// <summary>
    /// Records an I/O error, timeout or skipped/failed item; the pool halves once per cool-down.
    /// </summary>
    public void RecordBackoff()
    {
        lock (gate)
        {
            completionsSinceGrowth = 0;
            if (current <= 1 || InCoolDown())
            {
                return;
            }

            current = Math.Max(1, current / 2);
            lastChange = clock();
        }
    }

    /// <summary>
    /// Feeds a machine-wide CPU sample (0..1). Returns true when the worker target changed.
    /// </summary>
    public bool SampleLoad(double usedCpuFraction)
    {
        lock (gate)
        {
            var sample = Math.Clamp(usedCpuFraction, 0, 1);
            smoothedLoad = hasLoadSample ? smoothedLoad * (1 - smoothingFactor) + sample * smoothingFactor : sample;
            hasLoadSample = true;

            var budget = ScanConcurrencyResolver.ResolveLoadBudget(
                smoothedLoad,
                ceiling,
                shrinkThresholdPercent,
                suspendThresholdPercent);
            var desired = Math.Max(1, Math.Min(ceiling, budget));

            if (desired < current)
            {
                var emergency = smoothedLoad * 100 >= suspendThresholdPercent;
                if (!emergency && InCoolDown())
                {
                    return false;
                }

                current = desired;
                completionsSinceGrowth = 0;
                lastChange = clock();
                return true;
            }

            if (desired > current)
            {
                if (InCoolDown() || smoothedLoad * 100 >= shrinkThresholdPercent - HysteresisPercent)
                {
                    return false;
                }

                current = Math.Min(desired, current + 1);
                completionsSinceGrowth = 0;
                lastChange = clock();
                return true;
            }

            return false;
        }
    }

    private bool InCoolDown() => clock() - lastChange < coolDown;
}
