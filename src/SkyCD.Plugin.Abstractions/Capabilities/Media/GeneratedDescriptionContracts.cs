using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;

namespace SkyCD.Plugin.Abstractions.Capabilities.Media;

/// <summary>User-permitted input sources and aggregate context budgets.</summary>
public sealed record DescriptionContextOptions(bool Filename = true, bool Metadata = false, bool Text = false,
    bool Images = false, bool Audio = false, int MaxTextCharacters = 12000, int MaxArtifactBytes = 1048576);

/// <summary>One inert cached context item, with no filesystem location or executable content.</summary>
public sealed record DescriptionContextPart(string Kind, string ContentType, string? Text = null, byte[]? Data = null);

/// <summary>A bounded snapshot whose source and content fingerprints must still match at publication.</summary>
public sealed record DescriptionContext(string EntryId, string SourceFingerprint, string ContextFingerprint,
    DescriptionContextOptions Options, IReadOnlyList<DescriptionContextPart> Parts);

/// <summary>Plain generated text with negotiated agent identity and user-configured provenance.</summary>
public sealed record GeneratedDescription(string Text, string Agent, string? Model, string PluginVersion,
    string PromptVersion, string Language, string Origin, string ProcessingDestination, DateTimeOffset GeneratedAtUtc);

/// <summary>Explicit generation is distinct from automatic metadata extraction.</summary>
public interface IGeneratedDescriptionCapability : IPluginCapability
{
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
