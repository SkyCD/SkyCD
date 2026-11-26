using System;
using System.IO;
using System.Threading;
using OfficeIMO;
using OfficeIMO.Word.Legacy;
using SkyCD.Plugin.Metadata.WordPerfect.Exceptions;

namespace SkyCD.Plugin.Metadata.WordPerfect.Parsing;

/// <summary>
/// Recovers the leading body text of a structured WordPerfect 5.x or 6.x stream through OfficeIMO's
/// read-only legacy importer.
/// </summary>
/// <remarks>
/// The importer walks the prefix packets and body records of both generations, so WordPerfect 5 documents
/// that this plugin cannot read on its own now contribute text. Only structured recovery feeds the
/// thumbnail: a salvage-quality import is text guessed from the document area, so it is reported as "no
/// preview" instead of being drawn. Nothing is executed and source links are never resolved. An encrypted
/// header is already rejected by <see cref="Models.WordPerfectDocument.Parse"/> before the importer runs;
/// the mapping here is a backstop so the failure classification cannot drift. Scan cost grows with the
/// source: the importer projects a full editable model, so the preview pass is bounded by the source size
/// the host already charged and by the graphic limits below.
/// </remarks>
internal static class WordPerfectBodyTextImporter
{
    /// <summary>
    /// Total raster pixels the importer may produce for cached WPG1 graphics, about four 96-DPI US-letter
    /// pages. Reached only by graphic-heavy documents, which stop the preview instead of costing unbounded CPU.
    /// </summary>
    private const int MaxGraphicPixels = 16 * 1024 * 1024;

    /// <summary>Ceiling for embedded graphic source bytes plus their rasterized PNG bytes.</summary>
    private const int MaxGraphicBytes = 64 * 1024 * 1024;

    /// <summary>
    /// Returns the trimmed leading body text capped at <paramref name="maxCharacters"/>, or null when the
    /// import is salvage-quality or carries no text.
    /// </summary>
    public static string? ReadStructuredText(byte[] data, int maxCharacters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);

        try
        {
            using var result = LegacyWordImporter.Import(data, CreateOptions(data.Length), cancellationToken);
            if (result.Report.Quality != OfficeLegacyImportQuality.Structured)
            {
                return null;
            }

            var text = result.PlainText.Trim();
            if (text.Length == 0)
            {
                return null;
            }

            return text.Length > maxCharacters ? text[..maxCharacters].TrimEnd() : text;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidDataException exception) when (DeclaresEncryption(data))
        {
            throw new WordPerfectAuthenticationRequiredException("the document header declares encryption", exception);
        }
        catch (InvalidDataException exception)
        {
            throw new WordPerfectDecodeException("the document body could not be imported", exception);
        }
    }

    private static LegacyWordImportOptions CreateOptions(int sourceBytes) => new()
    {
        // The WPC header already established the family, so detection probing is redundant.
        FormatHint = LegacyWordFormat.WordPerfect,
        // The quality check above decides between a preview and the explicit "no preview" state; a salvage
        // import is not a failure of the capability.
        RequireStructured = false,
        Limits = new OfficeLegacyImportLimits
        {
            MaxInputBytes = sourceBytes,
            // Recovered text, its paragraph separators and note markers cannot exceed a few times the bytes
            // they are decoded from, so this bound follows the source instead of rejecting a long document.
            MaxTextCharacters = TextCharacterLimit(sourceBytes),
            // Records, items and the source part of graphic resources are all consumed per source byte.
            MaxRecords = Math.Max(sourceBytes, 1),
            MaxItems = Math.Max(sourceBytes, 1),
            // The preview hands over one already-extracted stream, never a compound file.
            MaxCompoundStreams = 1,
            MaxResourceBytes = Math.Max(MaxGraphicBytes, sourceBytes),
            MaxImagePixels = MaxGraphicPixels
        }
    };

    private static int TextCharacterLimit(int sourceBytes) =>
        (int)Math.Min((long)sourceBytes * 4, int.MaxValue);

    private static bool DeclaresEncryption(byte[] data) =>
        data.Length >= 14 && (data[12] != 0 || data[13] != 0);
}