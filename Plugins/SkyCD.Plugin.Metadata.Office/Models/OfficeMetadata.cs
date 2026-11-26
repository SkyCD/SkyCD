using System;

namespace SkyCD.Plugin.Metadata.Office.Models;

/// <summary>
/// Metadata facts a reader collected for one document. Readers fill the fields they can reach and leave the
/// rest <see langword="null"/>, so an unreadable optional property costs only its own field.
/// </summary>
/// <remarks>
/// Values arrive exactly as the reading library reports them. Whitespace folding, blank-to-<see langword="null"/>
/// and the omit-non-positive rule belong to the capability that publishes the record, which keeps every reader
/// free to repeat them.
/// </remarks>
internal sealed record OfficeMetadata
{
    public required string Container { get; init; }

    public string? DocumentType { get; init; }

    public bool IsEncrypted { get; set; }

    public string? ProtectionReason { get; set; }

    public bool IsTemplate { get; set; }

    public string? Title { get; set; }
    public string? Subject { get; set; }
    public string? Author { get; set; }
    public string? Keywords { get; set; }
    public string? Comments { get; set; }
    public string? Category { get; set; }
    public string? ContentStatus { get; set; }
    public string? Template { get; set; }
    public string? LastSavedBy { get; set; }
    public string? RevisionNumber { get; set; }
    public string? Language { get; set; }

    public string? ApplicationName { get; set; }
    public string? ApplicationVersion { get; set; }
    public string? Company { get; set; }
    public string? Manager { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }
    public DateTimeOffset? LastPrintedAt { get; set; }

    public long? PageCount { get; set; }
    public long? WordCount { get; set; }
    public long? CharacterCount { get; set; }
    public long? SlideCount { get; set; }
    public long? SheetCount { get; set; }
    public long? SectionCount { get; set; }
    public string? PageTitles { get; set; }
    public long? TableCount { get; set; }
    public long? QueryCount { get; set; }
    public long? TaskCount { get; set; }
    public long? TopLevelTaskCount { get; set; }

    public DateTimeOffset? PlanStartAt { get; set; }
    public DateTimeOffset? PlanFinishAt { get; set; }

    public string? FormatProfile { get; set; }

    /// <summary>
    /// A container the plugin recognized but deliberately left unread because it is protected, together with the
    /// error the reading library gave. No credential is requested, guessed or stored to get past it.
    /// </summary>
    public static OfficeMetadata Protected(string container, string reason) =>
        new() { Container = container, IsEncrypted = true, ProtectionReason = reason };
}
