using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Menu;

namespace SkyCD.SDK.Capabilities.Media;

/// <summary>User-permitted input sources and aggregate context budgets.</summary>
public sealed record DescriptionContextOptions(bool Filename = true, bool Metadata = false, bool Text = false,
    bool Images = false, bool Audio = false, int MaxTextCharacters = 12000, int MaxArtifactBytes = 1048576);

/// <summary>One inert cached context item, with no filesystem location or executable content.</summary>
public sealed record DescriptionContextPart(string Kind, string ContentType, string? Text = null, byte[]? Data = null);

/// <summary>A bounded snapshot whose source and content fingerprints must still match at publication.</summary>
public sealed record DescriptionContext(string EntryId, string SourceFingerprint, string ContextFingerprint,
    DescriptionContextOptions Options, IReadOnlyList<DescriptionContextPart> Parts)
{
    /// <summary>The original file explicitly attached by a catalog-add operation.</summary>
    public DescriptionOriginalFile? OriginalFile { get; init; }
}

/// <summary>A snapshot of the original local file supplied directly to the provider.</summary>
public sealed record DescriptionOriginalFile(string FullPath, string Name, long Length, DateTime LastWriteTimeUtc);

/// <summary>Plain generated text with negotiated agent identity and user-configured provenance.</summary>
public sealed record GeneratedDescription(string Text, string Agent, string? Model, string PluginVersion,
    string PromptVersion, string Language, string Origin, string ProcessingDestination, DateTimeOffset GeneratedAtUtc);

/// <summary>
/// Explicit generation is distinct from automatic metadata extraction. Whether a generator is AI-backed or
/// template-backed is an implementation detail; the host only sees the stable identity and the contract.
/// </summary>
public interface IDescriptionGeneratorCapability : IPluginCapability
{
    /// <summary>Stable identifier persisted by the host; it must not change between versions.</summary>
    string ImplementationId { get; }
    /// <summary>Short user-facing name shown in the Options implementation picker.</summary>
    string ImplementationName { get; }
    /// <summary>Versioned generated-metadata schema, independent from user comments.</summary>
    MediaMetadataSchema Schema { get; }
    /// <summary>Runs an explicitly requested generation pass over permitted cached context.</summary>
    Task<GeneratedDescription> GenerateAsync(DescriptionContext context, CancellationToken cancellationToken);
}

/// <summary>No filesystem, shell or arbitrary catalog mutation is exposed to the generator.</summary>
public interface IDescriptionHostApi : IHostCommandApi
{
    /// <summary>Returns bounded context for the selection captured when the command started.</summary>
    Task<DescriptionContext> GetDescriptionContextAsync(DescriptionContextOptions options, CancellationToken cancellationToken);
    /// <summary>Publishes generated metadata only while the source/context snapshot remains current.</summary>
    Task SaveGeneratedDescriptionAsync(DescriptionContext context, GeneratedDescription description, CancellationToken cancellationToken);
}
