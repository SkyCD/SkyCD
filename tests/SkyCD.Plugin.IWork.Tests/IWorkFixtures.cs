using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using OfficeIMO.IWork;

namespace SkyCD.Plugin.IWork.Tests;

/// <summary>Application the fixture creates.</summary>
public enum FixtureDocumentKind
{
    Pages,
    Numbers,
    Keynote
}

/// <summary>Shape of a fixture package; each property models something a real iWork package may or may not carry.</summary>
internal sealed record PackageOptions
{
    /// <summary>Which application writes the package, which drives the component archive the fixture creates.</summary>
    public FixtureDocumentKind Kind { get; init; } = FixtureDocumentKind.Pages;

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

    /// <summary>Slides created in a Keynote fixture.</summary>
    public int SlideCount { get; init; } = 1;

    /// <summary>Sheets, each carrying one table, created in a Numbers fixture.</summary>
    public int TableCount { get; init; } = 1;

    public int DataAssets { get; init; } = 1;

    public string? FormatVersion { get; init; } = IWorkFixtures.DefaultFormatVersion;

    public string? Language { get; init; } = "en";

    public string? Locale { get; init; } = "en_US";

    public bool? IsMultiPage { get; init; } = true;

    public bool? HasExternalData { get; init; } = false;

    public string ProducerBuild { get; init; } = IWorkFixtures.DefaultProducerBuild;
}

/// <summary>
/// Builds structurally valid iWork packages in memory with the same library the plugin reads them with, then
/// rewrites the plain metadata members so every value a test asserts is one the fixture wrote.
/// </summary>
/// <remarks>
/// The component archive is real IWA, so kind detection, slide counts and preview recognition exercise the
/// production parser rather than a stand-in. No binary blobs are committed: the preview images are generated
/// here, and the single JPEG is a complete baseline image encoded inline.
/// </remarks>
internal static class IWorkFixtures
{
    public const string DefaultDocumentIdentifier = "0d6e2a3c-1b2f-4c3d-8e9f-0a1b2c3d4e5f";
    public const string DefaultFormatVersion = "14.4";
    public const string DefaultProducerBuild = "Pages 14.4";
    public const int PreviewWidth = 320;
    public const int PreviewHeight = 200;
    public const int JpegPreviewWidth = 8;
    public const int JpegPreviewHeight = 6;

    private const string PropertiesPlist = "Metadata/Properties.plist";
    private const string BuildHistoryPlist = "Metadata/BuildVersionHistory.plist";
    private const string DocumentIdentifierEntry = "Metadata/DocumentIdentifier";

    public static byte[] Build(PackageOptions? options = null)
    {
        options ??= new PackageOptions();
        if (options.Legacy)
        {
            return BuildLegacy();
        }

        var package = Rewrite(CreateDocument(options), archive => ApplyMetadata(archive, options));
        return options.NestedIndex ? AsBundle(package) : package;
    }

    public static byte[] NotZip() => Encoding.ASCII.GetBytes("this is not an iWork package");

    public static byte[] Truncated(byte[] package) => package[..(package.Length / 2)];

    /// <summary>A ZIP that carries metadata but no component index at all.</summary>
    public static byte[] MissingIndex() => BuildZip(archive =>
        AddEntry(archive, PropertiesPlist, PropertiesPlistXml(new PackageOptions())));

    private static byte[] CreateDocument(PackageOptions options)
    {
        switch (options.Kind)
        {
            case FixtureDocumentKind.Keynote:
                {
                    using var document = IWorkKeynoteDocument.Create();
                    var slides = Math.Max(options.SlideCount, 1);
                    for (var index = 1; index <= slides; index++)
                    {
                        document.AddSlide(index % 2 == 0 ? "000000" : "FFFFFF")
                            .AddText($"Slide {index}", 10, 10, 400, 60);
                    }

                    return document.ToBytes();
                }

            case FixtureDocumentKind.Numbers:
                {
                    using var document = IWorkNumbersDocument.Create();
                    var sheets = Math.Max(options.TableCount, 1);
                    for (var index = 1; index <= sheets; index++)
                    {
                        document.AddSheet($"Sheet {index}").AddTable(IWorkTableData.Create(2, 2));
                    }

                    return document.ToBytes();
                }

            default:
                {
                    using var document = IWorkPagesDocument.Create();
                    document.AddParagraph("SkyCD iWork fixture");
                    return document.ToBytes();
                }
        }
    }

    private static void ApplyMetadata(ZipArchive archive, PackageOptions options)
    {
        if (options.IncludeProperties)
        {
            Replace(archive, PropertiesPlist, PropertiesPlistXml(options));
        }
        else
        {
            Delete(archive, PropertiesPlist);
        }

        if (options.IncludeBuildHistory)
        {
            // A protected package encrypts this member, so its payload is opaque rather than parseable.
            Replace(archive, BuildHistoryPlist, options.PasswordProtected
                ? OpaqueBlob()
                : BuildHistoryPlistXml(options.ProducerBuild));
        }
        else
        {
            Delete(archive, BuildHistoryPlist);
        }

        if (options.IncludeIdentifier)
        {
            Replace(archive, DocumentIdentifierEntry, Encoding.UTF8.GetBytes(options.DocumentIdentifierText));
        }
        else
        {
            Delete(archive, DocumentIdentifierEntry);
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
                    ? PreviewJpeg()
                    : PreviewPng(PreviewWidth, PreviewHeight));
        }

        if (options.PasswordProtected)
        {
            AddEntry(archive, ".iwpv2", new byte[104]);
        }
    }

    /// <summary>Flat package rewritten as a bundle: the component archive becomes a nested <c>Index.zip</c>.</summary>
    private static byte[] AsBundle(byte[] flat)
    {
        byte[] index;
        using (var source = new ZipArchive(new MemoryStream(flat), ZipArchiveMode.Read))
        {
            using var indexStream = new MemoryStream();
            using (var nested = new ZipArchive(indexStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var entry in source.Entries.Where(entry => entry.FullName.StartsWith("Index/")))
                {
                    using var input = entry.Open();
                    using var output = nested.CreateEntry(entry.FullName, CompressionLevel.Fastest).Open();
                    input.CopyTo(output);
                }
            }

            index = indexStream.ToArray();
        }

        return Rewrite(flat, archive =>
        {
            foreach (var entry in archive.Entries
                         .Where(entry => entry.FullName.StartsWith("Index/"))
                         .ToArray())
            {
                entry.Delete();
            }

            AddEntry(archive, "Index.zip", index);
        });
    }

    private static byte[] BuildLegacy() => BuildZip(archive =>
    {
        AddEntry(archive, "index.xml", Encoding.UTF8.GetBytes("<index/>"));
        AddEntry(archive, "preview.jpg", PreviewJpeg());
    });

    private static byte[] PropertiesPlistXml(PackageOptions options)
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
        AppendString(builder, "documentUUID",
            options.DocumentUuidInProperties ?? options.DocumentIdentifierText);
        builder.Append("</dict>\n</plist>\n");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] BuildHistoryPlistXml(string producerBuild) => Encoding.UTF8.GetBytes(
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

    /// <summary>A complete 24-bit PNG of the requested size, so the reader validates it like a real thumbnail.</summary>
    private static byte[] PreviewPng(int width, int height)
    {
        var stride = width * 3 + 1;
        var raw = new byte[stride * height];

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> header = stackalloc byte[13];
        WriteBigEndian(header[0..4], width);
        WriteBigEndian(header[4..8], height);
        header[8] = 8;  // bit depth
        header[9] = 2;  // RGB
        WriteChunk(png, "IHDR"u8, header);
        WriteChunk(png, "IDAT"u8, compressed.ToArray());
        WriteChunk(png, "IEND"u8, []);
        return png.ToArray();

        static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            Span<byte> length = stackalloc byte[4];
            WriteBigEndian(length, data.Length);
            output.Write(length);
            output.Write(type);
            output.Write(data);
            Span<byte> crc = stackalloc byte[4];
            WriteBigEndian(crc, unchecked((int)Crc32(type, data)));
            output.Write(crc);
        }
    }

    /// <summary>A complete baseline JPEG, 8 by 6 pixels, encoded inline as a constant.</summary>
    private static byte[] PreviewJpeg() => Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAAGAAgDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDq6KKK/os/Kj//2Q==");

    private static void WriteBigEndian(Span<byte> target, int value)
    {
        target[0] = (byte)(value >> 24);
        target[1] = (byte)(value >> 16);
        target[2] = (byte)(value >> 8);
        target[3] = (byte)value;
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in type)
        {
            crc = Step(crc, value);
        }

        foreach (var value in data)
        {
            crc = Step(crc, value);
        }

        return ~crc;

        static uint Step(uint crc, byte value)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }

            return crc;
        }
    }

    private static byte[] OpaqueBlob()
    {
        var blob = new byte[64];
        Array.Fill(blob, (byte)0x9A);
        return blob;
    }

    private static byte[] Rewrite(byte[] source, Action<ZipArchive> mutate)
    {
        using var buffer = new MemoryStream();
        buffer.Write(source);
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
        {
            mutate(archive);
        }

        return buffer.ToArray();
    }

    private static void Replace(ZipArchive archive, string name, byte[] content)
    {
        Delete(archive, name);
        AddEntry(archive, name, content);
    }

    private static void Delete(ZipArchive archive, string name) => archive.GetEntry(name)?.Delete();

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
