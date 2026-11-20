using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SkyCD.Plugin.IWork.Tests;

/// <summary>Shape of a fixture package; each property models something a real iWork package may or may not carry.</summary>
internal sealed record PackageOptions
{
    /// <summary>Bundle layout: the component archive is stored as <c>Index.zip</c> instead of root <c>Index/</c>.</summary>
    public bool NestedIndex { get; init; }

    /// <summary>iWork '09 layout: an XML or APXL index, which this plugin deliberately does not parse.</summary>
    public bool Legacy { get; init; }

    public bool PasswordProtected { get; init; }

    public bool IncludeProperties { get; init; } = true;

    public bool IncludeBuildHistory { get; init; } = true;

    public bool IncludeIdentifier { get; init; } = true;

    public string DocumentIdentifierText { get; init; } = IWorkFixtures.DefaultDocumentIdentifier;

    /// <summary>Fallback identifier only present in <c>Properties.plist</c>, for packages without the entry.</summary>
    public string? DocumentUuidInProperties { get; init; }

    public bool IncludePreview { get; init; } = true;

    public bool JpegPreview { get; init; }

    public int SlideCount { get; init; }

    public int TemplateSlideCount { get; init; }

    /// <summary>Number of table components, the inventory marker Numbers uses instead of slides.</summary>
    public int TableCount { get; init; }

    public int DataAssets { get; init; } = 1;

    public string? FormatVersion { get; init; } = IWorkFixtures.DefaultFormatVersion;

    public string? Language { get; init; } = "en";

    public string? Locale { get; init; } = "en_US";

    public bool? IsMultiPage { get; init; } = true;

    public bool? HasExternalData { get; init; } = false;

    public string ProducerBuild { get; init; } = IWorkFixtures.DefaultProducerBuild;
}

/// <summary>
/// Builds structurally valid iWork packages in memory. The suite commits no binary blobs, and every value a
/// test asserts is written by the fixture, so nothing depends on an external document.
/// </summary>
internal static class IWorkFixtures
{
    public const string DefaultDocumentIdentifier = "0d6e2a3c-1b2f-4c3d-8e9f-0a1b2c3d4e5f";
    public const string DefaultFormatVersion = "14.4";
    public const string DefaultProducerBuild = "Pages 14.4";
    public const int PreviewWidth = 320;
    public const int PreviewHeight = 200;

    // The plugin reads the component inventory, not the IWA streams themselves; a short header is enough.
    private static readonly byte[] ComponentHeader = [0, 2, 0, 0, 8, 1];

    public static byte[] Build(PackageOptions? options = null)
    {
        options ??= new PackageOptions();
        if (options.Legacy)
        {
            return BuildLegacy();
        }

        return options.NestedIndex ? BuildBundle(options) : BuildFlat(options);
    }

    public static byte[] NotZip() => Encoding.ASCII.GetBytes("this is not an iWork package");

    public static byte[] Truncated(byte[] package) => package[..(package.Length / 2)];

    /// <summary>A ZIP that carries metadata but no component index at all.</summary>
    public static byte[] MissingIndex() => BuildZip(archive =>
        AddEntry(archive, "Metadata/Properties.plist", PropertiesPlist(new PackageOptions())));

    private static byte[] BuildFlat(PackageOptions options) => BuildZip(outer =>
    {
        AddIndex(outer, options);
        AddPackageMetadata(outer, options);
    });

    private static byte[] BuildBundle(PackageOptions options)
    {
        var index = BuildZip(nested => AddIndex(nested, options));

        return BuildZip(outer =>
        {
            AddEntry(outer, "Index.zip", index);
            AddPackageMetadata(outer, options);
        });
    }

    private static byte[] BuildLegacy() => BuildZip(archive =>
    {
        AddEntry(archive, "index.xml", Encoding.UTF8.GetBytes("<index/>"));
        AddEntry(archive, "preview.jpg", PreviewJpeg(PreviewWidth, PreviewHeight));
    });

    private static void AddIndex(ZipArchive archive, PackageOptions options)
    {
        AddEntry(archive, "Index/Document.iwa", ComponentHeader);
        AddEntry(archive, "Index/Metadata.iwa", ComponentHeader);

        for (var index = 1; index <= options.SlideCount; index++)
        {
            AddEntry(archive, $"Index/Slide-{100 + index}.iwa", ComponentHeader);
        }

        for (var index = 1; index <= options.TemplateSlideCount; index++)
        {
            AddEntry(archive, $"Index/TemplateSlide-{200 + index}.iwa", ComponentHeader);
        }

        for (var index = 1; index <= options.TableCount; index++)
        {
            AddEntry(archive, $"Index/Tables/Table-{index}.iwa", ComponentHeader);
        }
    }

    private static void AddPackageMetadata(ZipArchive archive, PackageOptions options)
    {
        if (options.IncludeProperties)
        {
            // Apple keeps this property list plain even in a protected package.
            AddEntry(archive, "Metadata/Properties.plist", PropertiesPlist(options));
        }

        if (options.IncludeIdentifier)
        {
            AddEntry(archive, "Metadata/DocumentIdentifier",
                Encoding.UTF8.GetBytes(options.DocumentIdentifierText));
        }

        // A protected package encrypts these members, so their payload is opaque rather than parseable.
        if (options.IncludeBuildHistory)
        {
            AddEntry(archive, "Metadata/BuildVersionHistory.plist", options.PasswordProtected
                ? OpaqueBlob()
                : BuildHistoryPlist(options.ProducerBuild));
        }

        for (var index = 1; index <= options.DataAssets; index++)
        {
            AddEntry(archive, $"Data/asset-{index}.bin", options.PasswordProtected ? OpaqueBlob() : [1, 2, 3]);
        }

        if (options.IncludePreview)
        {
            AddEntry(archive, options.JpegPreview ? "preview.jpg" : "preview.png", options.PasswordProtected
                ? OpaqueBlob()
                : options.JpegPreview
                    ? PreviewJpeg(PreviewWidth, PreviewHeight)
                    : PreviewPng(PreviewWidth, PreviewHeight));
        }

        if (options.PasswordProtected)
        {
            AddEntry(archive, ".iwpv2", new byte[104]);
        }
    }

    private static byte[] PropertiesPlist(PackageOptions options)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        builder.Append("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" ");
        builder.Append("\"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n");
        builder.Append("<plist version=\"1.0\">\n<dict>\n");
        AppendString(builder, "fileFormatVersion", options.FormatVersion);
        AppendString(builder, "language", options.Language);
        AppendString(builder, "locale", options.Locale);
        AppendBoolean(builder, "isMultiPage", options.IsMultiPage);
        AppendBoolean(builder, "hasExternalReferenceOrMissingOrUnmaterializedRemoteData", options.HasExternalData);
        AppendString(builder, "documentUUID", options.DocumentUuidInProperties);
        builder.Append("</dict>\n</plist>\n");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] BuildHistoryPlist(string producerBuild) => Encoding.UTF8.GetBytes(
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<plist version=\"1.0\"><array>" +
        "<string>Template: Blank</string>" +
        "<string>Pages 10.2</string>" +
        $"<string>{producerBuild}</string>" +
        "<string>Template: Custom</string>" +
        "</array></plist>");

    private static void AppendString(StringBuilder builder, string key, string? value)
    {
        if (value is not null)
        {
            builder.Append("<key>").Append(key).Append("</key><string>").Append(value).Append("</string>\n");
        }
    }

    private static void AppendBoolean(StringBuilder builder, string key, bool? value)
    {
        if (value is { } present)
        {
            builder.Append("<key>").Append(key).Append("</key>")
                .Append(present ? "<true/>" : "<false/>").Append('\n');
        }
    }

    /// <summary>PNG signature followed by an IHDR chunk carrying only the dimensions the sniffer reads.</summary>
    private static byte[] PreviewPng(int width, int height)
    {
        var bytes = new byte[26];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes, 0);
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        return bytes;
    }

    /// <summary>SOI, a baseline SOF0 segment with the frame size, and EOI.</summary>
    private static byte[] PreviewJpeg(int width, int height)
    {
        var bytes = new byte[24];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        bytes[3] = 0xC0;
        bytes[4] = 0x00;
        bytes[5] = 0x11;
        bytes[6] = 0x08;
        bytes[7] = (byte)(height >> 8);
        bytes[8] = (byte)height;
        bytes[9] = (byte)(width >> 8);
        bytes[10] = (byte)width;
        bytes[11] = 0x03;
        bytes[21] = 0xFF;
        bytes[22] = 0xD9;
        return bytes;
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static byte[] OpaqueBlob()
    {
        var blob = new byte[64];
        Array.Fill(blob, (byte)0x9A);
        return blob;
    }

    private static byte[] BuildZip(Action<ZipArchive> write)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            write(archive);
        }

        return stream.ToArray();
    }

    private static void AddEntry(ZipArchive archive, string name, byte[] content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        stream.Write(content, 0, content.Length);
    }
}
