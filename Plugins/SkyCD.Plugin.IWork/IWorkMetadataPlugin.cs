using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.IWork;

/// <summary>
/// Extracts document metadata from modern Apple iWork (Pages, Numbers and Keynote) packages without altering
/// them.
/// </summary>
/// <remarks>
/// Only the package's plain members are read: the two metadata property lists, the document identifier, the
/// component inventory and the preview header. The plugin never decodes document content, never follows a
/// reference and never resolves a credential. Fields the package does not carry are omitted rather than
/// published as empty values, so "unknown" stays distinguishable from "none".
/// </remarks>
public sealed class IWorkMetadataPlugin : IMediaMetadataCapability
{
    private const string PropertiesPlist = "Metadata/Properties.plist";
    private const string BuildHistoryPlist = "Metadata/BuildVersionHistory.plist";
    private const string DocumentIdentifierEntry = "Metadata/DocumentIdentifier";

    private static readonly string[] PreviewCandidates =
    [
        "preview.jpg",
        "preview.jpeg",
        "preview.png",
        "preview-web.jpg",
        "preview-micro.jpg"
    ];

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
        progress?.Report(new MediaProgress(0.4));

        using var package = IWorkPackage.Open(data, options);

        var documentType = ResolveDocumentType(item, package);
        var values = new List<MediaMetadataValue>
        {
            new(IWorkSchema.Container, MediaMetadataType.Text, Text: "iwork"),
            new(IWorkSchema.IsPasswordProtected, MediaMetadataType.Boolean, Boolean: package.IsPasswordProtected),
            new(IWorkSchema.IwaComponentCount, MediaMetadataType.Integer,
                Integer: CountIndexComponents(package)),
            new(IWorkSchema.DataAssetCount, MediaMetadataType.Integer, Integer: CountDataAssets(package)),
            new(IWorkSchema.HasPreview, MediaMetadataType.Boolean, Boolean: HasPreview(package))
        };

        AddText(values, IWorkSchema.DocumentType, documentType);
        if (string.Equals(documentType, "keynote", StringComparison.Ordinal))
        {
            values.Add(new MediaMetadataValue(IWorkSchema.SlideCount, MediaMetadataType.Integer,
                Integer: CountSlides(package)));
        }

        var properties = ReadDictionary(package, PropertiesPlist);
        AddProperties(values, properties);
        AddProducerBuild(values, package);
        AddDocumentIdentifier(values, package, properties);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new MediaProgress(0.7));

        AddPreviewDimensions(values, package, cancellationToken);

        progress?.Report(new MediaProgress(1));
        return new MediaExtractionResult(values);
    }

    private static string? ResolveDocumentType(MediaItem item, IWorkPackage package)
    {
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

        // A mislabelled source: infer from the component inventory the same way Apple's apps distinguish the
        // Keynote and Numbers roots, which share an archive type.
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

    private static bool HasPreview(IWorkPackage package) => package.EntryNames.Any(IsPreviewEntry);

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

    private void AddProducerBuild(List<MediaMetadataValue> values, IWorkPackage package)
    {
        var bytes = ReadEntry(package, BuildHistoryPlist);
        var history = bytes is null ? null : IWorkPlist.TryReadRoot(bytes);
        AddText(values, IWorkSchema.ProducerBuild, LatestProducerBuild(history));
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

    private void AddPreviewDimensions(
        List<MediaMetadataValue> values,
        IWorkPackage package,
        CancellationToken cancellationToken)
    {
        if (package.IsPasswordProtected)
        {
            // Only the package inventory stays plain; the preview payload is encrypted like the document.
            return;
        }

        foreach (var name in PreviewCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var header = package.TryReadEntryPrefix(name, options.MaxPreviewHeaderBytes);
            if (header is null || IWorkPreviewImage.TryReadDimensions(header) is not { } dimensions)
            {
                continue;
            }

            values.Add(new MediaMetadataValue(IWorkSchema.PreviewWidth, MediaMetadataType.Integer,
                Integer: dimensions.Width));
            values.Add(new MediaMetadataValue(IWorkSchema.PreviewHeight, MediaMetadataType.Integer,
                Integer: dimensions.Height));
            return;
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

    private static string? LatestProducerBuild(PlistNode? node)
    {
        if (node is PlistArray array)
        {
            for (var index = array.Items.Count - 1; index >= 0; index--)
            {
                var candidate = array.Items[index] switch
                {
                    PlistString text => text.Value,
                    PlistDictionary dictionary => dictionary.GetString("BuildVersion")
                                                  ?? dictionary.GetString("Build Version"),
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(candidate) &&
                    !candidate.StartsWith("Template:", StringComparison.Ordinal))
                {
                    return candidate.Trim();
                }
            }

            return null;
        }

        return node is PlistString single && !string.IsNullOrWhiteSpace(single.Value)
            ? single.Value.Trim()
            : null;
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
