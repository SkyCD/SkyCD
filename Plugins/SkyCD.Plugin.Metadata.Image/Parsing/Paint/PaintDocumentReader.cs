using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using ImageMagick;
using Microsoft.Data.Sqlite;
using SkyCD.Plugin.Metadata.Image.Models;
using SkyCD.SDK.Capabilities.Media;
using static SkyCD.Plugin.Metadata.Image.Parsing.ContainerReader;

namespace SkyCD.Plugin.Metadata.Image.Parsing.Paint;

internal static class PaintDocumentReader
{
    public static Task<ContainerValues?> ReadMetadata(Input input) => input.Extension switch
    {
        "xcf" => ReadXcf(input),
        "kra" or "ora" => Task.FromResult(ReadArchiveMetadata(input)),
        "clip" => WithClipDatabase(input, ReadClipMetadata),
        _ => ReadAffinityMetadata(input)
    };

    public static Task<MediaPreviewArtifact?> ReadPreview(Input input) => input.Extension switch
    {
        "xcf" => ReadXcfPreview(input),
        "kra" or "ora" => Task.FromResult(ReadArchivePreview(input)),
        "clip" => WithClipDatabase(input, connection => ReadClipPreview(connection, input)),
        _ => ReadAffinityPreview(input)
    };

    private static ContainerValues? ReadArchiveMetadata(Input input)
    {
        using var archive = new ZipArchive(input.OpenArchive());
        var xml = ReadEntry(archive, input.Extension == "ora" ? "stack.xml" : "maindoc.xml", input);
        if (xml is null)
        {
            return null;
        }

        using var reader = XmlReader.Create(new MemoryStream(xml, writable: false),
            new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = CandidateBytes });
        var document = XDocument.Load(reader);
        var image = document.Descendants().FirstOrDefault(node =>
            node.Name.LocalName.Equals("image", StringComparison.OrdinalIgnoreCase));
        if (image is null)
        {
            return null;
        }

        var values = new ContainerValues(input.Extension);
        values.Size(ParseInteger(Attribute(image, "w", "width")), ParseInteger(Attribute(image, "h", "height")));
        if (input.Extension == "ora")
        {
            var stack = image.Elements().FirstOrDefault(node => node.Name.LocalName == "stack");
            values.Text("Title", Attribute(stack, "name") ?? Attribute(image, "name"));
            values.Integer("LayerCount", image.Descendants().Count(node => node.Name.LocalName == "layer"));
        }
        else
        {
            var info = ReadEntry(archive, "documentinfo.xml", input);
            if (info is not null)
            {
                using var infoReader = XmlReader.Create(new MemoryStream(info, writable: false),
                    new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = CandidateBytes });
                var fields = XDocument.Load(infoReader).Descendants();
                values.Text("Title", fields.FirstOrDefault(node => node.Name.LocalName == "title")?.Value);
                values.Text("Comment", fields.FirstOrDefault(node => node.Name.LocalName is "description" or "abstract")?.Value);
            }
        }
        return values;
    }

    private static MediaPreviewArtifact? ReadArchivePreview(Input input)
    {
        using var archive = new ZipArchive(input.OpenArchive());
        var preferred = input.Extension == "ora" ? "Thumbnails/thumbnail.png" : "preview.png";
        foreach (var name in new[] { preferred, "mergedimage.png" })
        {
            var bytes = ReadEntry(archive, name, input);
            try
            {
                if (bytes is not null && Thumbnail(bytes, input.Token) is { } artifact)
                {
                    return artifact;
                }
            }
            catch (Exceptions.ImageDecodeException)
            {
            }
        }
        return null;
    }

    private static byte[]? ReadEntry(ZipArchive archive, string name, Input input)
    {
        var entry = archive.GetEntry(name);
        if (entry is null || entry.Length is <= 0 or > CandidateBytes)
        {
            return null;
        }

        using var stream = entry.Open();
        var bytes = new byte[(int)entry.Length];
        for (var offset = 0; offset < bytes.Length;)
        {
            input.Token.ThrowIfCancellationRequested();
            var count = Math.Min(64 * 1024, bytes.Length - offset);
            stream.ReadExactly(bytes.AsSpan(offset, count));
            offset += count;
        }
        return bytes;
    }

    private static async Task<ContainerValues?> ReadXcf(Input input)
    {
        var data = await input.Prefix(MiB).ConfigureAwait(false);
        if (!IsXcf(data))
        {
            return null;
        }

        var span = data.AsSpan();
        var values = new ContainerValues("xcf");
        values.Size(U32(span[14..]), U32(span[18..]));
        values.Text("ColorMode", U32(span[22..]) switch { 0 => "RGB", 1 => "Grayscale", 2 => "Indexed", _ => null });
        var version = Encoding.ASCII.GetString(span.Slice(9, 4));
        var number = version[0] == 'v' ? ParseInteger(version[1..]) : 0;
        values.Text("Software", $"XCF version {number}");
        var offset = number >= 4 ? 30 : 26;
        while (span.Length - offset >= 8)
        {
            input.Token.ThrowIfCancellationRequested();
            var property = U32(span[offset..]);
            var length = U32(span[(offset + 4)..]);
            offset += 8;
            if (length > span.Length - offset)
            {
                break;
            }
            if (property == 0)
            {
                var pointerSize = number >= 11 ? 8 : 4;
                var layerCount = 0;
                while (span.Length - offset >= pointerSize)
                {
                    var pointer = span.Slice(offset, pointerSize);
                    if (pointer.IndexOfAnyExcept((byte)0) < 0)
                    {
                        values.Integer("LayerCount", layerCount);
                        break;
                    }
                    layerCount++;
                    offset += pointerSize;
                }
                break;
            }

            if (property == 21)
            {
                ReadXcfComment(span.Slice(offset, (int)length), values);
            }
            offset += (int)length;
        }
        return values;
    }

    private static void ReadXcfComment(ReadOnlySpan<byte> data, ContainerValues values)
    {
        while (data.Length >= 12)
        {
            var nameLength = U32(data);
            if (nameLength > data.Length - 12)
            {
                return;
            }
            var name = data.Slice(4, (int)nameLength);
            data = data[(8 + (int)nameLength)..];
            var length = U32(data);
            data = data[4..];
            if (length > data.Length)
            {
                return;
            }
            if (name.SequenceEqual("gimp-comment\0"u8))
            {
                values.Text("Comment", Encoding.UTF8.GetString(data[..(int)length]).TrimEnd('\0'));
                return;
            }
            data = data[(int)length..];
        }
    }

    private static async Task<MediaPreviewArtifact?> ReadXcfPreview(Input input)
    {
        var data = await input.Whole(DecodeBytes).ConfigureAwait(false);
        return data is not null && IsXcf(data) ? NativeImageReader.Composite(data, MagickFormat.Xcf, input) : null;
    }

    private static bool IsXcf(ReadOnlySpan<byte> data) => data.Length >= 26 && data.StartsWith("gimp xcf "u8);

    private static async Task<T?> WithClipDatabase<T>(Input input, Func<SqliteConnection, T?> read) where T : class
    {
        var data = await input.Prefix(ExtendedBytes).ConfigureAwait(false);
        var offset = data.AsSpan().IndexOf("SQLite format 3\0"u8);
        if (offset < 0 || data.Length - offset < 100)
        {
            return null;
        }

        var database = data.AsSpan(offset);
        var pageSize = BinaryPrimitives.ReadUInt16BigEndian(database[16..]);
        var size = (long)(pageSize == 1 ? 65536 : pageSize) * U32(database[28..]);
        if (size < 100 || size > database.Length || size > ExtendedBytes)
        {
            return null;
        }

        var path = Path.Combine(Path.GetTempPath(), $"skycd-clip-{Guid.NewGuid():N}.sqlite");
        try
        {
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await file.WriteAsync(data.AsMemory(offset, (int)size), input.Token).ConfigureAwait(false);
            }
            var options = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
            using var connection = new SqliteConnection(options.ConnectionString);
            connection.Open();
            using var trusted = connection.CreateCommand();
            trusted.CommandText = "PRAGMA trusted_schema = OFF";
            trusted.ExecuteNonQuery();
            input.Token.ThrowIfCancellationRequested();
            return read(connection);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ContainerValues ReadClipMetadata(SqliteConnection connection)
    {
        var values = new ContainerValues("clip");
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT CanvasWidth, CanvasHeight FROM Canvas LIMIT 1";
        try
        {
            using var reader = command.ExecuteReader();
            if (reader.Read())
            {
                values.Size(reader.IsDBNull(0) ? 0 : reader.GetInt64(0), reader.IsDBNull(1) ? 0 : reader.GetInt64(1));
            }
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1)
        {
            // Some CLIP versions omit the canvas size columns.
        }
        return values;
    }

    private static MediaPreviewArtifact? ReadClipPreview(SqliteConnection connection, Input input)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ImageData FROM CanvasPreview WHERE length(ImageData) BETWEEN 1 AND $limit ORDER BY length(ImageData) DESC LIMIT 1";
        command.Parameters.AddWithValue("$limit", CandidateBytes);
        return command.ExecuteScalar() is byte[] bytes && bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71 })
            ? Thumbnail(bytes, input.Token) : null;
    }

    private static async Task<ContainerValues?> ReadAffinityMetadata(Input input)
    {
        var data = await input.Prefix(ExtendedBytes).ConfigureAwait(false);
        var png = EmbeddedPreviewReader.FindPng(data, input.Token);
        if (png is null)
        {
            return null;
        }

        var dimensions = ImageDecoder.Probe(png);
        var values = new ContainerValues(input.Extension);
        values.Size(dimensions.Width, dimensions.Height);
        return values;
    }

    private static async Task<MediaPreviewArtifact?> ReadAffinityPreview(Input input) =>
        EmbeddedPreviewReader.Png(await input.Prefix(ExtendedBytes).ConfigureAwait(false), input.Token);

    private static uint U32(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt32BigEndian(data);

    private static long ParseInteger(string? text) =>
        long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;

    private static string? Attribute(XElement? node, params string[] names) =>
        node?.Attributes().FirstOrDefault(attribute => names.Contains(attribute.Name.LocalName, StringComparer.OrdinalIgnoreCase))?.Value;
}
