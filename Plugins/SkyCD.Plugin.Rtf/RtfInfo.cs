using System;

namespace SkyCD.Plugin.Rtf;

/// <summary>Facts read from an RTF header and information group; absent values stay null.</summary>
internal sealed record RtfInfo(
    int? RtfVersion,
    int? AnsiCodePage,
    bool IsProtected,
    string? Title,
    string? Subject,
    string? Author,
    string? Manager,
    string? Company,
    string? Operator,
    string? Category,
    string? Keywords,
    string? Comment,
    int? RevisionNumber,
    int? EditingTimeMinutes,
    int? PageCount,
    int? WordCount,
    int? CharacterCount,
    int? CharacterCountWithSpaces,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ModifiedAt,
    DateTimeOffset? LastPrintedAt);
