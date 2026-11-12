using System;
using System.Collections.Generic;

namespace SkyCD.Presentation.Scanning;

/// <summary>
/// Bounded traversal, retry, exclusion and link-follow policy for catalog scans.
/// </summary>
public sealed record ScanTraversalOptions
{
    public const int AbsoluteMaxConcurrency = 8;
    public const int DefaultMaxRetries = 3;
    public static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>Default policy used when callers omit traversal options.</summary>
    public static ScanTraversalOptions Default { get; } = new();

    public LinkFollowPolicy LinkFollowPolicy { get; init; } = LinkFollowPolicy.FollowDirectoryLinksOnce;

    public HiddenSystemFilePolicy HiddenSystemFilePolicy { get; init; } = HiddenSystemFilePolicy.Include;

    /// <summary>
    /// Simple wildcard exclusion patterns matched against entry names (for example <c>*.tmp</c>, <c>node_modules</c>).
    /// </summary>
    public IReadOnlyList<string> ExclusionPatterns { get; init; } = [];

    /// <summary>Maximum transient I/O retries per operation. Bounded to a non-negative value.</summary>
    public int MaxRetries { get; init; } = DefaultMaxRetries;

    public TimeSpan RetryDelay { get; init; } = DefaultRetryDelay;

    /// <summary>Optional overall operation timeout. Null means no additional timeout beyond cancellation.</summary>
    public TimeSpan? OperationTimeout { get; init; }

    /// <summary>Maximum concurrent entry processing within a directory. Clamped to 1..<see cref="AbsoluteMaxConcurrency"/>.</summary>
    public int MaxConcurrency { get; init; } = 1;

    public int GetBoundedMaxRetries() => Math.Max(0, MaxRetries);

    public int GetBoundedMaxConcurrency() => Math.Clamp(MaxConcurrency, 1, AbsoluteMaxConcurrency);

    public TimeSpan GetBoundedRetryDelay() =>
        RetryDelay < TimeSpan.Zero ? TimeSpan.Zero : RetryDelay;
}
