using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OfficeIMO.IWork;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.IWork;

/// <summary>
/// Extracts document metadata from modern Apple iWork (Pages, Numbers and Keynote) packages without altering
/// them.
/// </summary>
/// <remarks>
/// Plain metadata members are read directly, while the bounded OfficeIMO source reader parses the component
/// archive for the application kind, the producer build history and the embedded previews. A protected
/// package is not parsed because its members are encrypted; only its plain metadata is catalogued and no
/// credential is requested, guessed or stored. Fields the package does not carry are omitted rather than
/// published as empty values, so "unknown" stays distinguishable from "none".
/// </remarks>
public sealed class IWorkMetadataPlugin : IMediaMetadataCapability
{
    private const string PropertiesPlist = "Metadata/Properties.plist";
    private const string DocumentIdentifierEntry = "Metadata/DocumentIdentifier";

    private readonly IWorkPluginOptions options;

    public IWorkMetadataPlugin()
        : this(IWorkPluginOptions.Default)
    {
    }

    public IWorkMetadataPlugin(IWorkPluginOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MediaDescriptor Descriptor =>
        new(IWorkSchema.Extensions, IWorkSchema.ContentTypes, IWorkSchema.Metadata);

    public async Task<MediaExtractionResult> ExtractAsync(
        MediaItem item,
        IMediaSource source,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        var data = await IWorkSourceReader
            .ReadAllAsync(item, source, options.MaxSourceBytes, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new MediaProgress(0.3));

        using var package = IWorkPackage.Open(data, options);
        using var document = IWorkDocumentLoader.TryOpen(package, data, options, cancellationToken);

        var documentType = ResolveDocumentType(item, package, document);
        var values = new List<MediaMetadataValue>
        {
            new(IWorkSchema.Container, MediaMetadataType.Text, Text: "iwork"),
            new(IWorkSchema.IsPasswordProtected, MediaMetadataType.Boolean, Boolean: package.IsPasswordProtected),
            new(IWorkSchema.IwaComponentCount, MediaMetadataType.Integer,
                Integer: CountIndexComponents(package)),
            new(IWorkSchema.DataAssetCount, MediaMetadataType.Integer, Integer: CountDataAssets(package)),
            new(IWorkSchema.HasPreview, MediaMetadataType.Boolean, Boolean: HasPreview(package, document))
        };

        AddText(values, IWorkSchema.DocumentType, documentType);

        var properties = ReadDictionary(package, PropertiesPlist);
        AddProperties(values, properties);
        AddProducerBuild(values, document);
        AddDocumentIdentifier(values, package, properties);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new MediaProgress(0.7));

        AddPreviewDimensions(values, document);
        AddSlideCount(values, documentType, package, document, cancellationToken);

        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(values);
    }

    private static string? ResolveDocumentType(
        MediaItem item,
        IWorkPackage package,
        IWorkSourceDocument? document)
    {
        // The parsed component archive identifies the application even when the source is mislabelled.
        var detected = document?.Kind switch
        {
            IWorkDocumentKind.Pages => "pages",
            IWorkDocumentKind.Numbers => "numbers",
            IWorkDocumentKind.Keynote => "keynote",
            _ => null
        };
        if (detected is not null)
        {
            return detected;
        }

        var byExtension = item.Extension?.ToLowerInvariant() switch
        {
            ".pages" => "pages",
            ".numbers" => "numbers",
            ".key" => "keynote",
            _ => null
        };
        if (byExtension is not null)
        {
            return byExtension;
        }

        var byContentType = item.ContentType?.ToLowerInvariant() switch
        {
            "application/x-iwork-pages-sffpages" => "pages",
            "application/x-iwork-numbers-sffnumbers" => "numbers",
            "application/x-iwork-keynote-sffkey" => "keynote",
            _ => null
        };
        if (byContentType is not null)
        {
            return byContentType;
        }

        // A protected, mislabelled source: only the plain inventory is available, so the component names are
        // the same evidence Apple's applications leave for the Keynote and Numbers roots.
        if (package.IndexEntryNames.Any(name =>
                IsComponent(name, "Index/Slide") || IsComponent(name, "Index/TemplateSlide")))
        {
            return "keynote";
        }

        return package.IndexEntryNames.Any(name =>
            name.StartsWith("Index/Tables/", StringComparison.OrdinalIgnoreCase))
            ? "numbers"
            : null;
    }

    private static long CountIndexComponents(IWorkPackage package) =>
        package.IndexEntryNames.Count(name => name.EndsWith(".iwa", StringComparison.OrdinalIgnoreCase));

    private static long CountDataAssets(IWorkPackage package) =>
        package.EntryNames.Count(name => name.StartsWith("Data/", StringComparison.OrdinalIgnoreCase));

    private static bool HasPreview(IWorkPackage package, IWorkSourceDocument? document) => document is not null
        ? document.Previews.Count > 0
        : package.EntryNames.Any(IsPreviewEntry);

    private static bool IsPreviewEntry(string name)
    {
        if (name.Contains('/'))
        {
            return false;
        }

        return string.Equals(name, "preview.jpg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "preview.jpeg", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, "preview.png", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("preview-", StringComparison.OrdinalIgnoreCase);
    }

    private static long CountSlides(IWorkPackage package) =>
        package.IndexEntryNames.Count(name => IsComponent(name, "Index/Slide"));

    private static bool IsComponent(string name, string prefix)
    {
        if (string.Equals(name, prefix + ".iwa", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = name[prefix.Length..];
        return !suffix.Contains('/') &&
               suffix.EndsWith(".iwa", StringComparison.OrdinalIgnoreCase) &&
               (suffix.StartsWith('-') || (suffix.Length > 0 && char.IsAsciiDigit(suffix[0])));
    }

    private void AddProperties(List<MediaMetadataValue> values, PlistDictionary? properties)
    {
        AddText(values, IWorkSchema.FormatVersion, properties.GetString("fileFormatVersion"));
        AddText(values, IWorkSchema.Language, properties.GetString("language"));
        AddText(values, IWorkSchema.Locale, properties.GetString("locale"));
        AddBoolean(values, IWorkSchema.IsMultiPage, properties.GetBoolean("isMultiPage"));
        AddBoolean(values, IWorkSchema.HasExternalData,
            properties.GetBoolean("hasExternalReferenceOrMissingOrUnmaterializedRemoteData"));
    }

    private static void AddProducerBuild(List<MediaMetadataValue> values, IWorkSourceDocument? document)
    {
        // A protected package stores this member encrypted, and the reader never runs for it.
        if (document is null)
        {
            return;
        }

        AddText(values, IWorkSchema.ProducerBuild, LatestProducerBuild(document.BuildVersions));
    }

    private void AddDocumentIdentifier(
        List<MediaMetadataValue> values,
        IWorkPackage package,
        PlistDictionary? properties)
    {
        var identifier = NormalizeIdentifier(ReadIdentifierEntry(package))
                         ?? NormalizeIdentifier(properties.GetString("documentUUID"));
        AddText(values, IWorkSchema.DocumentIdentifier, identifier);
    }

    private static void AddPreviewDimensions(List<MediaMetadataValue> values, IWorkSourceDocument? document)
    {
        var preview = document?.PreferredRasterPreview;
        if (preview?.PixelWidth is not { } width || preview.PixelHeight is not { } height)
        {
            return;
        }

        values.Add(new MediaMetadataValue(IWorkSchema.PreviewWidth, MediaMetadataType.Integer,
            Integer: width));
        values.Add(new MediaMetadataValue(IWorkSchema.PreviewHeight, MediaMetadataType.Integer,
            Integer: height));
    }

    private static void AddSlideCount(
        List<MediaMetadataValue> values,
        string? documentType,
        IWorkPackage package,
        IWorkSourceDocument? document,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(documentType, "keynote", StringComparison.Ordinal))
        {
            return;
        }

        if (document is not null)
        {
            try
            {
                var slides = document.ReadKeynote().Slides.Count;
                values.Add(new MediaMetadataValue(IWorkSchema.SlideCount, MediaMetadataType.Integer,
                    Integer: slides));
                return;
            }
            catch (InvalidDataException)
            {
                // Only projection limits surface this way; the component inventory below still describes the
                // package in that case.
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (package.IsPasswordProtected)
        {
            // Encrypted slide components cannot be counted, and a zero count would claim the deck has none.
            return;
        }

        var components = CountSlides(package);
        if (components > 0)
        {
            values.Add(new MediaMetadataValue(IWorkSchema.SlideCount, MediaMetadataType.Integer,
                Integer: components));
        }
    }

    private byte[]? ReadEntry(IWorkPackage package, string name) =>
        package.TryReadEntry(name, options.MaxMetadataBytes);

    private string? ReadIdentifierEntry(IWorkPackage package)
    {
        var bytes = ReadEntry(package, DocumentIdentifierEntry);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    private PlistDictionary? ReadDictionary(IWorkPackage package, string name) =>
        IWorkPlist.TryReadDictionary(ReadEntry(package, name) ?? []);

    private static string? LatestProducerBuild(IReadOnlyList<string> builds)
    {
        for (var index = builds.Count - 1; index >= 0; index--)
        {
            var candidate = builds[index];
            if (!string.IsNullOrWhiteSpace(candidate) &&
                !candidate.TrimStart().StartsWith("Template:", StringComparison.Ordinal))
            {
                return candidate.Trim();
            }
        }

        return null;
    }

    private static string? NormalizeIdentifier(string? text) =>
        !string.IsNullOrWhiteSpace(text) && Guid.TryParse(text.Trim(), out var identifier)
            ? identifier.ToString("D")
            : null;

    private static void AddText(List<MediaMetadataValue> values, string key, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Text, Text: text.Trim()));
        }
    }

    private static void AddBoolean(List<MediaMetadataValue> values, string key, bool? value)
    {
        if (value is { } present)
        {
            values.Add(new MediaMetadataValue(key, MediaMetadataType.Boolean, Boolean: present));
        }
    }
}
