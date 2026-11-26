using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Docnet.Core;
using Docnet.Core.Models;
using ImageMagick;
using SkiaSharp;
using SkyCD.Plugin.Metadata.Image.Models;
using SkyCD.SDK.Capabilities.Media;
using Svg.Skia;
using UglyToad.PdfPig;
using static SkyCD.Plugin.Metadata.Image.Parsing.ContainerReader;

namespace SkyCD.Plugin.Metadata.Image.Parsing.Vector;

internal static class VectorImageReader
{
    public static async Task<ContainerValues?> ReadMetadata(Input input)
    {
        if (input.Extension is "svg" or "svgz")
        {
            return SvgMetadata(await ReadSvg(input).ConfigureAwait(false), input.Extension);
        }
        if (input.Extension is "wmf" or "emf")
        {
            return Metafile(await input.Prefix(108).ConfigureAwait(false), input.Extension);
        }

        var header = await input.Prefix(64 * 1024).ConfigureAwait(false);
        if (input.Extension != "ai" || !header.AsSpan().StartsWith("%PDF-"u8))
        {
            return Dsc(header, input.Extension);
        }

        var data = await input.Whole(DecodeBytes).ConfigureAwait(false);
        var values = new ContainerValues("ai");
        if (data is not null)
        {
            using var pdf = PdfDocument.Open(data);
            var info = pdf.Information;
            values.Text("Title", info.Title);
            values.Text("Author", info.Author);
            values.Text("Software", info.Creator ?? info.Producer);
            var date = info.CreationDate ?? info.ModifiedDate;
            if (date?.StartsWith("D:", StringComparison.Ordinal) == true)
            {
                date = date[2..].Replace("'", "", StringComparison.Ordinal);
            }
            if (DateTimeOffset.TryParseExact(date, ["yyyyMMddHHmmsszzz", "yyyyMMddHHmmss'Z'", "yyyyMMddHHmmss", "yyyyMMdd"],
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
            {
                values.Timestamp("CapturedAt", timestamp);
            }
            if (pdf.NumberOfPages > 0)
            {
                var page = pdf.GetPage(1);
                values.Size(Dimension(page.Width), Dimension(page.Height));
            }
        }
        return values;
    }

    public static async Task<MediaPreviewArtifact?> ReadPreview(Input input)
    {
        if (input.Extension is "svg" or "svgz")
        {
            return RenderSvg(await ReadSvg(input).ConfigureAwait(false), input);
        }
        if (input.Extension is "wmf" or "emf")
        {
            var header = await input.Prefix(108).ConfigureAwait(false);
            return Metafile(header, input.Extension) is null ? null :
                NativeImageReader.Composite(await input.Whole(DecodeBytes).ConfigureAwait(false),
                    input.Extension == "wmf" ? MagickFormat.Wmf : MagickFormat.Emf, input);
        }
        if (input.Extension == "ai" && (await input.Prefix(5).ConfigureAwait(false)).AsSpan().SequenceEqual("%PDF-"u8))
        {
            var pdf = await input.Whole(DecodeBytes).ConfigureAwait(false);
            return pdf is null ? null : RenderPdf(pdf, input);
        }
        return null;
    }

    private static async Task<XElement?> ReadSvg(Input input)
    {
        var bytes = await input.Whole(HeaderBytes).ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }
        using var source = new MemoryStream(bytes, writable: false);
        using var gzip = input.Extension == "svgz" ? new GZipStream(source, CompressionMode.Decompress) : null;
        using var reader = XmlReader.Create(gzip ?? (Stream)source,
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = HeaderBytes });
        var root = XDocument.Load(reader).Root;
        return root?.Name.LocalName == "svg" ? root : null;
    }

    private static ContainerValues? SvgMetadata(XElement? root, string extension)
    {
        if (root is null)
        {
            return null;
        }

        var values = new ContainerValues(extension);
        var box = Numbers((string?)root.Attribute("viewBox"));
        values.Size(Length((string?)root.Attribute("width")) ?? (box.Length == 4 ? Dimension(box[2]) : 0),
            Length((string?)root.Attribute("height")) ?? (box.Length == 4 ? Dimension(box[3]) : 0));
        values.Text("Title", root.Elements().FirstOrDefault(node => node.Name.LocalName == "title")?.Value);
        var comment = root.Elements().FirstOrDefault(node => node.Name.LocalName == "desc")?.Value;
        var rdf = root.Descendants(XName.Get("RDF", "http://www.w3.org/1999/02/22-rdf-syntax-ns#")).FirstOrDefault();
        if (rdf is not null && SkyCD.Plugin.MediaMetadata.Parsing.XmpMetadataReader.TryRead(
            Encoding.UTF8.GetBytes(rdf.ToString(SaveOptions.DisableFormatting)), out var xmp))
        {
            var scan = new SkyCD.Plugin.MediaMetadata.Parsing.MetadataScan();
            foreach (var keyword in xmp.Keywords)
            {
                scan.AddKeyword(keyword);
            }
            scan.SetComment(xmp.Comment ?? comment);
            scan.SetRating(xmp.Rating);
            values.AddEmbedded(new EmbeddedMetadata(scan, null));
        }
        else
        {
            values.Text("Comment", comment);
        }
        return values;
    }

    private static MediaPreviewArtifact? RenderSvg(XElement? root, Input input)
    {
        if (root is null)
        {
            return null;
        }

        // Keep rendering offline, including when the SVG contains resource or stylesheet references.
        foreach (var node in root.DescendantsAndSelf().ToArray())
        {
            input.Token.ThrowIfCancellationRequested();
            if (node != root && node.Name.LocalName is "script" or "foreignObject" or "style")
            {
                node.Remove();
                continue;
            }
            foreach (var attribute in node.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration).ToArray())
            {
                var value = attribute.Value.Trim();
                if ((attribute.Name.LocalName is "href" or "src" && !value.StartsWith('#')) ||
                    attribute.Name.LocalName is "base" or "style" ||
                    (value.Contains("url", StringComparison.OrdinalIgnoreCase) &&
                        (!value.StartsWith("url(#", StringComparison.OrdinalIgnoreCase) ||
                        value.AsSpan(5).Contains('('))))
                {
                    attribute.Remove();
                }
            }
        }
        using var svg = new SKSvg();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(root.ToString(SaveOptions.DisableFormatting)));
        var picture = svg.Load(stream);
        if (picture is null)
        {
            return null;
        }
        var bounds = picture.CullRect;
        if (Dimension(bounds.Width) == 0 || Dimension(bounds.Height) == 0)
        {
            return null;
        }

        var scale = Math.Min(1, ThumbnailEdge / Math.Max(bounds.Width, bounds.Height));
        using var bitmap = new SKBitmap((int)Math.Ceiling(bounds.Width * scale), (int)Math.Ceiling(bounds.Height * scale));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Flush();
        return Encode(bitmap);
    }

    private static ContainerValues? Dsc(byte[] data, string extension)
    {
        if (!data.AsSpan().StartsWith("%!PS"u8))
        {
            return null;
        }

        var values = new ContainerValues(extension);
        using var reader = new StringReader(Encoding.Latin1.GetString(data));
        string? title = null;
        string? creator = null;
        string? date = null;
        double[] box = [];
        double[] preciseBox = [];
        while (reader.ReadLine() is { } line)
        {
            var separator = line.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }
            var value = line[(separator + 1)..].Trim();
            if (value == "(atend)")
            {
                continue;
            }

            switch (line[..separator])
            {
                case "%%Title": title ??= value.Trim('(', ')'); break;
                case "%%Creator": creator ??= value.Trim('(', ')'); break;
                case "%%CreationDate": date ??= value.Trim('(', ')'); break;
                case "%%BoundingBox" when box.Length == 0: box = Numbers(value); break;
                case "%%HiResBoundingBox" when preciseBox.Length == 0: preciseBox = Numbers(value); break;
            }
        }
        values.Text("Title", title);
        values.Text("Software", creator);
        if (DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
        {
            values.Timestamp("CapturedAt", timestamp);
        }
        box = preciseBox.Length == 4 ? preciseBox : box;
        if (box.Length == 4)
        {
            values.Size(Dimension(box[2] - box[0]), Dimension(box[3] - box[1]));
        }
        return values;
    }

    private static ContainerValues? Metafile(ReadOnlySpan<byte> data, string extension)
    {
        var values = new ContainerValues(extension);
        if (extension == "wmf")
        {
            if (data.Length >= 22 && BinaryPrimitives.ReadUInt32LittleEndian(data) == 0x9ac6cdd7)
            {
                var inch = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
                if (inch > 0)
                {
                    values.Size((I16(data[10..]) - I16(data[6..])) * 1440L / inch,
                        (I16(data[12..]) - I16(data[8..])) * 1440L / inch);
                }
                return values;
            }
            return data.Length >= 18 && I16(data) is 1 or 2 && I16(data[2..]) == 9 ? values : null;
        }

        if (data.Length < 88 || I32(data) != 1 || !data.Slice(40, 4).SequenceEqual(" EMF"u8))
        {
            return null;
        }
        values.Size(Dimension((I32(data[32..]) - (long)I32(data[24..])) * 1440d / 2540),
            Dimension((I32(data[36..]) - (long)I32(data[28..])) * 1440d / 2540));
        return values;
    }

    private static MediaPreviewArtifact? RenderPdf(byte[] data, Input input)
    {
        using var document = PdfDocument.Open(data);
        if (document.NumberOfPages == 0)
        {
            return null;
        }

        var first = document.GetPage(1);
        var width = Dimension(first.Width);
        var height = Dimension(first.Height);
        if (width == 0 || height == 0)
        {
            return null;
        }
        var (targetWidth, targetHeight) = ImageDecoder.ScaleTo((int)width, (int)height, ThumbnailEdge);
        using var pdf = DocLib.Instance.GetDocReader(data,
            new PageDimensions(Math.Min(targetWidth, targetHeight), Math.Max(targetWidth, targetHeight)));
        using var page = pdf.GetPageReader(0);
        var rasterWidth = page.GetPageWidth();
        var rasterHeight = page.GetPageHeight();
        if (rasterWidth is <= 0 or > ThumbnailEdge || rasterHeight is <= 0 or > ThumbnailEdge)
        {
            return null;
        }

        input.Token.ThrowIfCancellationRequested();
        var pixels = page.GetImage();
        var size = rasterWidth * rasterHeight * 4;
        using var bitmap = new SKBitmap(rasterWidth, rasterHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        if (pixels.Length < size || bitmap.GetPixels() == IntPtr.Zero)
        {
            return null;
        }
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), size);
        return Encode(bitmap);
    }

    private static short I16(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadInt16LittleEndian(data);
    private static int I32(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadInt32LittleEndian(data);
    private static long Dimension(double value) => value is > 0 and <= int.MaxValue ? (long)Math.Ceiling(value) : 0;

    private static long? Length(string? text)
    {
        text = text?.Trim();
        if (text?.EndsWith("px", StringComparison.OrdinalIgnoreCase) == true)
        {
            text = text[..^2];
        }
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Dimension(value) : null;
    }

    private static double[] Numbers(string? text)
    {
        var parts = text?.Split([' ', '\t', '\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (parts.Length != 4)
        {
            return [];
        }
        var values = new double[4];
        for (var i = 0; i < values.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
                !double.IsFinite(values[i]))
            {
                return [];
            }
        }
        return values;
    }
}
