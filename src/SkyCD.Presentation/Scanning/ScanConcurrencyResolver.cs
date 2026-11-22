using System;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Initial and maximum worker counts for one scan, derived from the source and the CPU share ceiling.
/// </summary>
public sealed record ScanConcurrencyPlan(int InitialWorkers, int CeilingWorkers)
{
    public int Clamp(int workers) => Math.Clamp(workers, 1, Math.Max(1, CeilingWorkers));
}

/// <summary>
/// Maps a scan source profile to a safe initial worker count and a per-source ceiling.
/// </summary>
public static class ScanConcurrencyResolver
{
    public const int HardDiskMaxWorkers = 4;
    public const int NetworkMaxWorkers = 8;
    public const int NetworkInitialWorkers = 4;

    public static ScanConcurrencyPlan ResolvePlan(ScanTraversalOptions options, int processorCount)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.ConcurrencyMode == ScanConcurrencyMode.Manual)
        {
            var workers = options.GetBoundedMaxConcurrency();
            return new ScanConcurrencyPlan(workers, workers);
        }

        return ResolvePlan(options.SourceProfile, processorCount, options.GetBoundedCpuSharePercent());
    }

    public static ScanConcurrencyPlan ResolvePlan(
        ScanSourceProfile profile,
        int processorCount,
        int cpuSharePercent)
    {
        var cores = Math.Max(1, processorCount);
        var cpuCeiling = Math.Clamp(
            (int)Math.Floor(cores * Math.Clamp(cpuSharePercent, 1, 100) / 100.0),
            1,
            ScanTraversalOptions.AbsoluteMaxConcurrency);

        return profile.Kind switch
        {
            // Sequential access dominates: more workers only add seeks.
            ScanSourceKind.SlowRemovable => new ScanConcurrencyPlan(1, 1),
            ScanSourceKind.Optical => new ScanConcurrencyPlan(1, Math.Min(2, cpuCeiling)),
            ScanSourceKind.HardDisk => new ScanConcurrencyPlan(
                Math.Min(2, Math.Min(HardDiskMaxWorkers, cpuCeiling)),
                Math.Min(HardDiskMaxWorkers, cpuCeiling)),
            ScanSourceKind.SolidState => new ScanConcurrencyPlan(Math.Min(2, cpuCeiling), cpuCeiling),
            // Latency-bound: a small fixed pool, deliberately independent of core count.
            ScanSourceKind.Network => new ScanConcurrencyPlan(
                Math.Min(NetworkInitialWorkers, NetworkMaxWorkers),
                NetworkMaxWorkers),
            _ => new ScanConcurrencyPlan(1, Math.Min(2, cpuCeiling))
        };
    }

    /// <summary>
    /// Worker count for post-traversal work (metadata/hash). Honors the same source ceiling and
    /// shrinks under current system load; slow media stays at one worker to protect throughput.
    /// </summary>
    public static int ResolveMetadataWorkers(
        ScanTraversalOptions options,
        int processorCount,
        double? systemLoadFraction = null)
    {
        var plan = ResolvePlan(options, processorCount);
        if (plan.CeilingWorkers <= 1)
        {
            return 1;
        }

        if (options.ConcurrencyMode != ScanConcurrencyMode.Automatic || systemLoadFraction is null)
        {
            return plan.InitialWorkers;
        }

        var budget = ResolveLoadBudget(
            systemLoadFraction.Value,
            plan.CeilingWorkers,
            options.GetBoundedLoadShrinkThresholdPercent(),
            options.GetBoundedLoadSuspendThresholdPercent());
        return plan.Clamp(budget);
    }

    /// <summary>
    /// How many of the source's workers may run at the current machine-wide CPU load.
    /// One worker under heavy external load, half at moderate load, the full ceiling when idle.
    /// </summary>
    public static int ResolveLoadBudget(
        double usedCpuFraction,
        int sourceCeiling,
        int shrinkThresholdPercent,
        int suspendThresholdPercent)
    {
        var ceiling = Math.Max(1, sourceCeiling);
        var usedPercent = Math.Clamp(usedCpuFraction, 0, 1) * 100;
        if (usedPercent >= suspendThresholdPercent)
        {
            return 1;
        }

        return usedPercent >= shrinkThresholdPercent ? Math.Max(1, ceiling / 2) : ceiling;
    }
}
