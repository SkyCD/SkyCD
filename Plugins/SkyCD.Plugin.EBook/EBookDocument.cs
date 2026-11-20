using System;
using System.Collections.Generic;

namespace SkyCD.Plugin.EBook;

/// <summary>
/// Format-neutral facts read from one e-book. Every nullable member is "the book did not carry this", which is
/// not the same as an empty value, and <see cref="Cover"/> is populated only when the caller asked for it.
/// </summary>
internal sealed class EBookDocument
{
    public required string Container { get; init; }

    public required string Format { get; init; }

    public string? Title { get; init; }

    public IReadOnlyList<string> Authors { get; init; } = [];

    public string? Publisher { get; init; }

    public string? Language { get; init; }

    public string? Subject { get; init; }

    public string? Description { get; init; }

    public string? Identifier { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public int? PublishedYear { get; init; }

    public bool IsProtected { get; init; }

    /// <summary>The embedded cover exactly as the book stored it, already checked to be a decodable image.</summary>
    public byte[]? Cover { get; init; }

    public static EBookDocument HeaderOnly(string container, string format) =>
        new() { Container = container, Format = format };
}
