using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using OfficeIMO.OneNote;
using SkyCD.Plugin.Metadata.Office.Exceptions;
using SkyCD.Plugin.Metadata.Office.Models;

namespace SkyCD.Plugin.Metadata.Office.Parsing;

/// <summary>
/// Reads a OneNote section, table of contents or notebook package.
/// </summary>
/// <remarks>
/// <para>
/// A OneNote file is a revision store, so the reader asks for the current structure only: the section name, the
/// pages it lists and their timestamps. Page bodies, embedded files and ink are never decoded, because none of
/// them is a catalog fact and a section can hold a great deal of them.
/// </para>
/// <para>
/// A section whose current revision is encrypted is published as protected and the older plaintext revision is
/// never used instead, because the older revision describes a document the user no longer has.
/// </para>
/// <para>
/// A table of contents and a notebook package carry no notebook title of their own: what a reader can call the
/// notebook is the name of the folder or file it was stored in, which is where the catalog already keeps it.
/// Publishing that as <c>Title</c> would report a file name as a document property, so these two publish the
/// section list instead.
/// </para>
/// </remarks>
internal static class OfficeOneNoteReader
{
    private const int MaxListedTitles = 200;
    private const int MaxTitlesLength = 4096;

    public static OfficeMetadata Read(
        string? extension,
        byte[] data,
        OfficePluginOptions options,
        CancellationToken cancellationToken)
    {
        // A notebook package is a cabinet, while a section and its table of contents are revision stores.
        var container = OfficeFamilyResolver.Normalize(extension) is ".onepkg"
            ? OfficeContainers.Cab
            : OfficeContainers.OneNote;

        return OfficeProtection.Read(container,
            () => ReadOpened(container, extension, data, options, cancellationToken));
    }

    private static OfficeMetadata ReadOpened(
        string container,
        string? extension,
        byte[] data,
        OfficePluginOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return container switch
        {
            OfficeContainers.Cab => ReadNotebook(container, data, options),
            _ when OfficeFamilyResolver.Normalize(extension) is ".onetoc2" => ReadNotebook(container, data, options),
            _ => ReadSection(container, data, options, cancellationToken)
        };
    }

    private static OfficeMetadata ReadSection(
        string container,
        byte[] data,
        OfficePluginOptions options,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var section = OneNoteSection.Load(stream, OfficeLoadBounds.Note(options));

        var protectedSection = OfficeProtection.FromDiagnostics(
            container, Diagnostics(section.Diagnostics));

        return protectedSection ?? new OfficeMetadata
        {
            Container = container,
            DocumentType = OfficeFamilyResolver.DocumentType(OfficeFamily.Note),
            Title = OfficeGuard.Try(() => OfficeText.Normalize(section.Name)),
            SheetCount = OfficeGuard.TryCount(() => section.Pages.Count),
            PageTitles = OfficeGuard.Try(() => Titles(section.Pages.Select(page => page.Title))),
            CreatedAt = OfficeGuard.TryValue(() => Earliest(section.Pages.Select(page => page.CreatedUtc))),
            ModifiedAt = OfficeGuard.TryValue(() => Latest(section.Pages.Select(page => page.LastModifiedUtc)))
        };
    }

    private static OfficeMetadata ReadNotebook(string container, byte[] data, OfficePluginOptions options)
    {
        using var stream = new MemoryStream(data, writable: false);
        var notebook = container == OfficeContainers.Cab
            ? OneNotePackageReader.Read(stream, "notebook.onepkg", OfficeLoadBounds.Notebook(options))
            : OneNoteNotebookReader.Read(stream, "notebook.onetoc2", OfficeLoadBounds.Notebook(options));

        var protectedNotebook = OfficeProtection.FromDiagnostics(
            container, Diagnostics(notebook.Diagnostics));

        return protectedNotebook ?? new OfficeMetadata
        {
            Container = container,
            DocumentType = OfficeFamilyResolver.DocumentType(OfficeFamily.Note),
            SectionCount = OfficeGuard.TryCount(() => notebook.Sections.Count),
            PageTitles = OfficeGuard.Try(() => Titles(notebook.Sections.Select(section => section.Name)))
        };
    }

    private static IEnumerable<string?> Diagnostics(IEnumerable<OneNoteDiagnostic>? diagnostics) =>
        diagnostics is null
            ? []
            : diagnostics.SelectMany(diagnostic => new[] { diagnostic.Code, diagnostic.Message });

    private static DateTimeOffset? Earliest(IEnumerable<DateTime?> timestamps) =>
        MinMax(timestamps, oldest: true);

    private static DateTimeOffset? Latest(IEnumerable<DateTime?> timestamps) =>
        MinMax(timestamps, oldest: false);

    private static DateTimeOffset? MinMax(IEnumerable<DateTime?> timestamps, bool oldest)
    {
        DateTime? chosen = null;
        foreach (var timestamp in timestamps)
        {
            if (timestamp is not { } value)
            {
                continue;
            }

            if (chosen is null || (oldest ? value < chosen : value > chosen))
            {
                chosen = value;
            }
        }

        return OfficeText.FromDateTime(chosen);
    }

    private static string? Titles(IEnumerable<string?> titles)
    {
        var builder = new StringBuilder();
        var listed = 0;
        foreach (var title in titles)
        {
            var value = OfficeText.Normalize(title);
            if (value is null || ++listed > MaxListedTitles)
            {
                continue;
            }

            if (builder.Length + value.Length > MaxTitlesLength)
            {
                break;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(value);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}
