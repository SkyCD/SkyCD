using System;
using OfficeIMO;
using OfficeIMO.Access;
using OfficeIMO.Excel;
using OfficeIMO.OneNote;
using OfficeIMO.PowerPoint;
using OfficeIMO.Project;
using OfficeIMO.Visio;
using OfficeIMO.Word;
using SkyCD.Plugin.Metadata.Office.Models;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Builds the per-format load options the readers hand to the reading library.
/// </summary>
/// <remarks>
/// <para>
/// Every load is read-only, never persisted, and bounded by the same source limit the plugin prefights, so a
/// document cannot ask the library for more bytes than the host already charged. Nothing here takes a path: the
/// readers hand the library a stream over the bytes the host supplied, which is what keeps a document that
/// references another file from reaching it. The decompression budget is a multiple of the compressed source
/// because a metadata part is small while a text-heavy document part is not, and refusing a large document part
/// would cost real documents their metadata.
/// </para>
/// <para>
/// The content policies deliberately stay permissive, unlike the library's own <c>UntrustedDefaults</c>. Those
/// reject a package for <em>containing</em> a macro part, an embedded payload or an external relationship, and
/// all three are ordinary in the formats this plugin must cover: <c>.docm</c>, <c>.xlsm</c>, <c>.pptm</c> and
/// <c>.vsdm</c> carry a VBA project, and any document with a hyperlink carries an external relationship. This
/// plugin only reads the property parts, so it never executes a macro, never renders a payload and never opens
/// the target of a relationship: nothing in a document reaches a second file or a network address, because the
/// only bytes it ever sees are the ones the host handed it. A document is therefore judged by its size, not by
/// which optional parts it happens to store.
/// </para>
/// <para>
/// No option here is ever given a password. A protected document is reported, not opened.
/// </para>
/// </remarks>
internal static class OfficeLoadBounds
{
    private const int ExpansionFactor = 8;

    public static WordLoadOptions Word(OfficePluginOptions options) =>
        new()
        {
            AccessMode = DocumentAccessMode.ReadOnly,
            PersistenceMode = DocumentPersistenceMode.Explicit,
            MaxInputBytes = options.MaxSourceBytes,
            MaxLegacyInputBytes = BoundedInt(options.MaxSourceBytes),
            PackageSecurity = Security(options)
        };

    public static ExcelLoadOptions Workbook(OfficePluginOptions options) =>
        new()
        {
            AccessMode = DocumentAccessMode.ReadOnly,
            PersistenceMode = DocumentPersistenceMode.Explicit,
            MaxInputBytes = options.MaxSourceBytes,
            MaxLegacyInputBytes = BoundedInt(options.MaxSourceBytes),
            MaxPackageBytes = options.MaxSourceBytes,
            MaxPartBytes = BoundedInt(options.MaxSourceBytes),
            MaxBytes = options.MaxSourceBytes,
            PreserveExternalWorkbookLinks = false,
            PackageSecurity = Security(options)
        };

    public static PowerPointLoadOptions Presentation(OfficePluginOptions options) =>
        new()
        {
            AccessMode = DocumentAccessMode.ReadOnly,
            PersistenceMode = DocumentPersistenceMode.Explicit,
            MaxInputBytes = options.MaxSourceBytes,
            MaxLegacyInputBytes = BoundedInt(options.MaxSourceBytes),
            PackageSecurity = Security(options)
        };

    public static VisioLoadOptions Drawing(OfficePluginOptions options) =>
        new()
        {
            PackageSecurity = Security(options),
            Limits = new VisioLoadLimits
            {
                MaxInputBytes = options.MaxSourceBytes,
                MaxPackageXmlCharacters = options.MaxSourceBytes * ExpansionFactor,
                MaxLegacyXmlCharacters = options.MaxSourceBytes * ExpansionFactor
            }
        };

    public static OneNoteLoadOptions Note(OfficePluginOptions options) =>
        new()
        {
            MaxInputBytes = options.MaxSourceBytes,
            MaxAssetBytes = options.MaxPreviewBytes,
            MaxTotalAssetBytes = options.MaxPreviewBytes,

            // A revision store keeps history; metadata describes the current state, so history is not decoded.
            PreserveUnknownData = false
        };

    public static OneNoteNotebookReaderOptions Notebook(OfficePluginOptions options) =>
        new()
        {
            OneNoteOptions = Note(options),

            // Only the table of contents and the section list are read; page content would be decoded for nothing.
            LoadSectionContent = false,
            IncludeRecycleBin = false,
            ContinueOnSectionError = true,
            MaxPackageEntryBytes = options.MaxSourceBytes,
            MaxPackageExpandedBytes = options.MaxSourceBytes * ExpansionFactor
        };

    public static AccessLoadOptions Database(OfficePluginOptions options) =>
        new()
        {
            AccessMode = DocumentAccessMode.ReadOnly,
            PersistenceMode = DocumentPersistenceMode.Explicit,
            MaxInputBytes = options.MaxSourceBytes,
            MaxPages = BoundedInt(options.MaxSourceBytes / 1024),
            MaxMetadataBytes = options.MaxMetadataPartBytes,

            // The catalog names the tables and queries; application objects would carry definitions to decode.
            DecodeCatalog = true,
            DecodeApplicationObjects = false

            // No package-security policy: a database is a paged file rather than a package, and the library
            // refuses the pairing outright. Its own page and metadata budgets above bound the read instead.
        };

    public static ProjectLoadOptions Plan(OfficePluginOptions options) =>
        new()
        {
            AccessMode = DocumentAccessMode.ReadOnly,
            PersistenceMode = DocumentPersistenceMode.Explicit,
            MaxInputBytes = options.MaxSourceBytes,
            MaxCharacters = options.MaxSourceBytes * ExpansionFactor,
            MaxCompoundEntries = 4096,
            MaxCompoundStreams = 4096
        };

    private static OfficePackageSecurityOptions Security(OfficePluginOptions options) =>
        new()
        {
            MaxPackageBytes = options.MaxSourceBytes,
            MaxPartUncompressedBytes = options.MaxSourceBytes,
            MaxTotalUncompressedBytes = options.MaxSourceBytes * ExpansionFactor,
            MaxXmlCharactersInPart = options.MaxSourceBytes * ExpansionFactor,
            MaxCompressionRatio = 100
        };

    private static int BoundedInt(long value) => value > int.MaxValue ? int.MaxValue : (int)value;
}
