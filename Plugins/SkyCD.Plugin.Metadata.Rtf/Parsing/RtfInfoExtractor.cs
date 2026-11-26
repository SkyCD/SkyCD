using System;
using System.Threading;
using OfficeIMO.Rtf;
using OfficeIMO.Rtf.Syntax;
using SkyCD.Plugin.Metadata.Rtf.Exceptions;
using SkyCD.Plugin.Metadata.Rtf.Models;

namespace SkyCD.Plugin.Metadata.Rtf.Parsing;

/// <summary>
/// Reads the RTF header and information group through OfficeIMO.Rtf instead of a hand-written
/// reader (issue #1242). Text, numbers, timestamps and the code page come from the document model;
/// the three signals OfficeIMO does not surface (the <c>\rtf</c> version, the info-level
/// <c>\version</c> number and the protection words) are read off OfficeIMO's own lossless syntax
/// tree, so no custom RTF parsing remains.
/// </summary>
internal static class RtfInfoExtractor
{
    private static readonly string[] ProtectionWords =
        ["passwordhash", "formprot", "annotprot", "readprot", "revprot"];

    public static RtfInfo Read(byte[] rtf, RtfPluginOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rtf);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        // UntrustedDefaults never follows embedded references; the input bound is raised to the
        // plugin's own source bound (already enforced by RtfSourceReader) so a large but valid
        // document is not rejected by the parser's smaller default.
        var loadOptions = RtfLoadOptions.UntrustedDefaults;
        var limits = loadOptions.Limits?.Clone() ?? new RtfReadLimits();
        limits.MaxInputBytes = ToBoundedInt(options.MaxSourceBytes);
        limits.MaxInputCharacters = ToBoundedInt(options.MaxSourceBytes);
        loadOptions.Limits = limits;

        RtfReadResult result;
        try
        {
            result = RtfDocument.LoadResult(rtf, loadOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new RtfDecodeException("the document body could not be parsed", exception);
        }

        var document = result.Value
            ?? throw new RtfDecodeException("the document body could not be parsed");

        var properties = document.Properties;
        var settings = document.Settings;
        var header = ScanHeaderWords(result.SyntaxTree?.Root);

        return new RtfInfo(
            header.RtfVersion,
            settings.AnsiCodePage,
            IsProtected(settings, header),
            properties.Title,
            properties.Subject,
            properties.Author,
            properties.Manager,
            properties.Company,
            properties.Operator,
            properties.Category,
            properties.Keywords,
            properties.Comments,
            header.RevisionNumber,
            properties.EditingMinutes,
            properties.NumberOfPages,
            properties.NumberOfWords,
            properties.NumberOfCharacters,
            properties.NumberOfCharactersWithSpaces,
            ToUtcTimestamp(properties.Created),
            ToUtcTimestamp(properties.Revised),
            ToUtcTimestamp(properties.Printed));
    }

    private static int ToBoundedInt(long value) => value > int.MaxValue ? int.MaxValue : (int)value;

    /// <summary>
    /// RTF timestamps carry no timezone; they are published with a UTC offset only because the
    /// schema requires one, and the original wall-clock values are preserved.
    /// </summary>
    private static DateTimeOffset? ToUtcTimestamp(DateTime? value) =>
        value.HasValue ? new DateTimeOffset(value.Value, TimeSpan.Zero) : null;

    private static bool IsProtected(RtfDocumentSettings settings, HeaderWords header) =>
        settings.HasEditingProtection
        || settings.FormProtection.HasValue
        || settings.RevisionProtection.HasValue
        || settings.AnnotationProtection.HasValue
        || settings.ReadOnlyProtection.HasValue
        || header.HasProtectionWord;

    private static HeaderWords ScanHeaderWords(RtfGroup? root)
    {
        int? rtfVersion = null;
        int? revisionNumber = null;
        var hasProtectionWord = false;

        if (root is not null)
        {
            Walk(root, inInfo: false, ref rtfVersion, ref revisionNumber, ref hasProtectionWord);
        }

        return new HeaderWords(rtfVersion, revisionNumber, hasProtectionWord);
    }

    private static void Walk(
        RtfGroup group,
        bool inInfo,
        ref int? rtfVersion,
        ref int? revisionNumber,
        ref bool hasProtectionWord)
    {
        var insideInfo = inInfo || "info".Equals(group.Destination, StringComparison.OrdinalIgnoreCase);

        foreach (var child in group.Children)
        {
            if (child is RtfControlWord word)
            {
                if (rtfVersion is null && word.Name.Equals("rtf", StringComparison.OrdinalIgnoreCase))
                {
                    rtfVersion = word.Parameter;
                }
                else if (insideInfo && word.Name.Equals("version", StringComparison.OrdinalIgnoreCase))
                {
                    revisionNumber = word.Parameter;
                }
                else if (!hasProtectionWord && IsProtectionWord(word.Name))
                {
                    hasProtectionWord = true;
                }
            }
            else if (child is RtfGroup nested)
            {
                Walk(nested, insideInfo, ref rtfVersion, ref revisionNumber, ref hasProtectionWord);
            }
        }
    }

    private static bool IsProtectionWord(string name)
    {
        foreach (var candidate in ProtectionWords)
        {
            if (name.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record HeaderWords(int? RtfVersion, int? RevisionNumber, bool HasProtectionWord);
}
