using System;
using System.Collections.Generic;
using OfficeIMO;
using OfficeIMO.OneNote;
using SkyCD.Plugin.Metadata.Office.Models;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Recognises a source the plugin must not read because it is protected, and turns what the library reported
/// into the published reason.
/// </summary>
/// <remarks>
/// Protection is only reported when the content was actually left unread: a document that opened and merely
/// records that its author preferred a password is an ordinary document, and publishing it as protected would
/// hide the fields the plugin did read. The reason text is the library's own message, folded and shortened, so
/// the value a user sees is the error that was really raised rather than a restatement of it.
/// </remarks>
internal static class OfficeProtection
{
    private const int MaxReasonLength = 240;

    private static readonly string[] Signals =
    [
        "encrypt", "decrypt", "password", "protected", "protection", "permission"
    ];

    /// <summary>OneNote and the shared diagnostic vocabulary report protection as a code, not an exception.</summary>
    private static readonly string[] DiagnosticSignals =
    [
        "encrypt", "password", "protected", "permission"
    ];

    public static bool IsProtection(Exception exception) => Classify(exception) is not null;

    /// <summary>
    /// Runs one library read, publishing the protected record when the read was refused because the document is
    /// protected instead of because it is unreadable.
    /// </summary>
    public static OfficeMetadata Read(string container, Func<OfficeMetadata> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (IsProtection(exception))
        {
            return Describe(container, exception);
        }
    }

    /// <summary>The protected reading of a source that could not be opened, carrying the library's error.</summary>
    public static OfficeMetadata Describe(string container, Exception exception) =>
        OfficeMetadata.Protected(container, Reason(exception));

    /// <summary>The library's message for the protection, shortened to a field value.</summary>
    public static string Reason(Exception exception)
    {
        var message = OfficeText.Normalize(Classify(exception)?.Message ?? exception.Message)
            ?? "the document is protected";

        return message.Length <= MaxReasonLength ? message : message[..MaxReasonLength].TrimEnd();
    }

    /// <summary>
    /// True when a load that succeeded still refused the current revision, which must be published as protected
    /// rather than as the older plaintext the reader fell back to.
    /// </summary>
    public static OfficeMetadata? FromDiagnostics(string container, IEnumerable<string?>? diagnostics)
    {
        if (diagnostics is null)
        {
            return null;
        }

        foreach (var diagnostic in diagnostics)
        {
            if (Matches(diagnostic, DiagnosticSignals))
            {
                return OfficeMetadata.Protected(container, OfficeText.Normalize(diagnostic)!);
            }
        }

        return null;
    }

    private static Exception? Classify(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is OfficePackageSecurityException)
            {
                // A security rule can also be a size limit, which is a damaged or oversized source, not a lock.
                return Matches(current.Message, Signals) ? current : null;
            }

            if (Matches(current.Message, Signals) || Matches(Code(current), Signals))
            {
                return current;
            }
        }

        return null;
    }

    private static string? Code(Exception exception) =>
        exception is OneNoteFormatException note ? note.Code : null;

    private static bool Matches(string? text, string[] signals)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (var signal in signals)
        {
            if (text.Contains(signal, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
