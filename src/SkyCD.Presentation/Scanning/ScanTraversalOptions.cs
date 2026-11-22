using System;
using System.Collections.Generic;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Bounded traversal, retry, exclusion and concurrency policy for catalog scans.
/// </summary>
public sealed record ScanTraversalOptions
{
    /// <summary>Hard upper bound for any worker count, including manual overrides and fast SSDs.</summary>
    public const int AbsoluteMaxConcurrency = 32;
    public const int DefaultMaxRetries = 3;
    public const int DefaultCpuSharePercent = 80;
    public const int DefaultLoadShrinkThresholdPercent = 70;
    public const int DefaultLoadSuspendThresholdPercent = 85;
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan DefaultLoadSampleInterval = TimeSpan.FromSeconds(1.5);

    /// <summary>Default policy used when callers omit traversal options.</summary>
    public static ScanTraversalOptions Default { get; } = new();

    public LinkFollowPolicy LinkFollowPolicy { get; init; } = LinkFollowPolicy.FollowDirectoryLinksOnce;

    public HiddenSystemFilePolicy HiddenSystemFilePolicy { get; init; } = HiddenSystemFilePolicy.Include;

    /// <summary>
    /// Simple wildcard exclusion patterns matched against entry names (for example <c>*.tmp</c>, <c>node_modules</c>).
    /// </summary>
    public IReadOnlyList<string> ExclusionPatterns { get; init; } = [];

    /// <summary>
    /// Entry-type aware exclusion rules matched against entry names. Kept alongside
    /// <see cref="ExclusionPatterns"/> for callers that only need simple wildcards.
    /// </summary>
    public IReadOnlyList<ScanExclusionRule> ExclusionRules { get; init; } = [];

    /// <summary>
    /// Include-by-mask (whitelist) rules: when non-empty, an entry is accepted only when it
    /// matches at least one inclusion rule for its entry kind.
    /// </summary>
    public IReadOnlyList<ScanExclusionRule> InclusionRules { get; init; } = [];

    /// <summary>Skip recycle bin / trash entries during traversal.</summary>
    public bool SkipRecycleBins { get; init; } = true;

    /// <summary>Skip well-known system folders and files during traversal.</summary>
    public bool SkipWellKnownSystemFolders { get; init; } = true;

    /// <summary>Do not emit folder documents that end up without children.</summary>
    public bool SkipEmptyFolders { get; init; } = false;

    /// <summary>Skip files marked offline (online-only cloud placeholders).</summary>
    public bool SkipOnlineOnlyCloudFiles { get; init; } = false;

    /// <summary>Maximum transient I/O retries per operation. Bounded to a non-negative value.</summary>
    public int MaxRetries { get; init; } = DefaultMaxRetries;

    public TimeSpan RetryDelay { get; init; } = DefaultRetryDelay;

    /// <summary>Optional overall operation timeout. Null means no additional timeout beyond cancellation.</summary>
    public TimeSpan? OperationTimeout { get; init; }

    /// <summary>How the effective worker count is chosen.</summary>
    public ScanConcurrencyMode ConcurrencyMode { get; init; } = ScanConcurrencyMode.Automatic;

    /// <summary>
    /// Manual worker count used when <see cref="ConcurrencyMode"/> is <see cref="ScanConcurrencyMode.Manual"/>.
    /// Zero is treated as one. Automatic mode ignores this value.
    /// </summary>
    public int MaxConcurrency { get; init; }

    /// <summary>Maximum share of logical processors automatic scanning may use on an idle machine.</summary>
    public int CpuSharePercent { get; init; } = DefaultCpuSharePercent;

    /// <summary>System-wide CPU usage at which the automatic pool starts to shrink.</summary>
    public int LoadShrinkThresholdPercent { get; init; } = DefaultLoadShrinkThresholdPercent;

    /// <summary>System-wide CPU usage at which the automatic pool drops to one worker.</summary>
    public int LoadSuspendThresholdPercent { get; init; } = DefaultLoadSuspendThresholdPercent;

    /// <summary>How often machine-wide CPU load is sampled while scanning. Zero disables sampling.</summary>
    public TimeSpan LoadSampleInterval { get; init; } = DefaultLoadSampleInterval;

    /// <summary>
    /// Classification of the scan root. Populated when the scan is created so the traversal never has
    /// to guess from <see cref="Environment.ProcessorCount"/> at the call site.
    /// </summary>
    public ScanSourceProfile SourceProfile { get; init; } = ScanSourceProfile.Unknown;

    public int GetBoundedMaxRetries() => Math.Max(0, MaxRetries);

    public int GetBoundedMaxConcurrency() => Math.Clamp(MaxConcurrency, 1, AbsoluteMaxConcurrency);

    public int GetBoundedCpuSharePercent() => Math.Clamp(CpuSharePercent, 1, 100);

    public int GetBoundedLoadShrinkThresholdPercent() =>
        Math.Clamp(LoadShrinkThresholdPercent, 1, GetBoundedLoadSuspendThresholdPercent() - 1);

    public int GetBoundedLoadSuspendThresholdPercent() =>
        Math.Clamp(LoadSuspendThresholdPercent, 2, 100);

    public TimeSpan GetBoundedRetryDelay() =>
        RetryDelay < TimeSpan.Zero ? TimeSpan.Zero : RetryDelay;
}
