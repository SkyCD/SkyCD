using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Abstractions.Capabilities.Indexing;

/// <summary>Source facts offered to <see cref="IIndexWorkItemProvider"/> implementations.</summary>
public sealed class IndexWorkItemRequest
{
    /// <summary>Gets the source file name, including its extension.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the source length in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>Gets the factory that opens a fresh readable stream for the source.</summary>
    public required Func<CancellationToken, Task<Stream>> OpenSource { get; init; }

    /// <summary>Gets optional per-source progress, from zero to one hundred.</summary>
    public IProgress<int>? Progress { get; init; }
}
