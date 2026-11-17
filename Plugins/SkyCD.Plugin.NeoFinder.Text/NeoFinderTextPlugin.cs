using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Csv;
using SkyCD.Plugin.NeoFinder.Text.Exceptions;

namespace SkyCD.Plugin.NeoFinder.Text;

/// <summary>
/// File format capability plugin for importing NeoFinder configurable text exports.
/// </summary>
/// <remarks>
/// NeoFinder ("Text with Tab") exports are header-driven: columns, delimiters, date formats and
/// encodings are user-configurable, so recognition maps the header row against
/// <see cref="NeoFinderTextProfile"/> and rejects ambiguous layouts instead of guessing.
/// The recommended export configuration is documented in <c>docs/plugins/neofinder-text.md</c>.
/// </remarks>
public sealed class NeoFinderTextPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    private static readonly string[] GenericSkyCdHeaders = ["NodeId", "ParentId", "Kind", "Name", "SizeBytes"];

    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "neofinder-text",
            DisplayName: "NeoFinder Text Export",
            Extensions: [".txt", ".tsv"],
            MimeTypes: ["text/tab-separated-values", "text/plain"],
            CanRead: true,
            CanWrite: false);

    /// <summary>
    /// Inspects the stream content to verify if it is a NeoFinder text export.
    /// Preserves the stream position.
    /// </summary>
    /// <remarks>
    /// The export is decoded with the same profile resolution the import uses, so an export is never
    /// recognised under one encoding and imported under another.
    /// </remarks>
    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        if (!stream.CanRead)
        {
            return false;
        }

        var initialPosition = stream.CanSeek ? stream.Position : -1;
        try
        {
            var data = ReadAllBytes(stream);
            var profile = NeoFinderTextEncodingDetector.Detect(data);
            if (profile.Kind == NeoFinderTextEncodingKind.Unknown)
            {
                return false;
            }

            var text = profile.Decode(data);
            var headerLine = ReadFirstNonEmptyLine(text);
            if (headerLine is null)
            {
                return false;
            }

            var delimiter = CsvParser.DetectDelimiter(headerLine);
            var headerFields = CsvParser.ParseLine(headerLine, delimiter);
            if (headerFields.Count == 0)
            {
                return false;
            }

            // Must NOT match the SkyCD generic CSV layout.
            if (GenericSkyCdHeaders.SequenceEqual(headerFields, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            // Must NOT match WinCatalog CSV reports (they carry their own path columns).
            if (headerFields.Any(field =>
                    field.Equals("Catalog Path", StringComparison.OrdinalIgnoreCase) ||
                    field.Equals("File Path", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var columns = headerFields.Select(NeoFinderTextProfile.MapColumn).ToList();
            var hasName = columns.Contains(NeoFinderTextColumn.Name);
            var hasLocation = columns.Contains(NeoFinderTextColumn.Path)
                || columns.Contains(NeoFinderTextColumn.Volume)
                || columns.Contains(NeoFinderTextColumn.Catalog);

            return hasName && hasLocation;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (initialPosition >= 0 && stream.CanSeek)
            {
                stream.Position = initialPosition;
            }
        }
    }

    public async Task<FileFormatReadResult> ReadAsync(
        FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var data = await ReadAllBytesAsync(request.Source, cancellationToken);
            var profile = NeoFinderTextEncodingDetector.Detect(data);
            if (profile.Kind == NeoFinderTextEncodingKind.Unknown)
            {
                throw new NeoFinderTextUnsupportedEncodingException();
            }

            request.Progress?.Report(10);

            var text = profile.Decode(data);

            using var reader = new StringReader(text);
            var parseResult = await CsvParser.ReadCsvAsync(reader, null, cancellationToken);
            request.Progress?.Report(50);

            if (parseResult.Rows.Count == 0)
            {
                throw new NeoFinderTextCorruptHeaderException();
            }

            var fileStem = Path.GetFileNameWithoutExtension(
                request.FileName ?? request.SourcePath ?? "NeoFinder");
            var document = NeoFinderTextHierarchyBuilder.Build(
                parseResult.Rows, parseResult.Delimiter, profile, fileStem, cancellationToken);
            request.Progress?.Report(100);

            return new FileFormatReadResult
            {
                Success = true,
                Payload = document
            };
        }
        catch (DecoderFallbackException)
        {
            return new FileFormatReadResult
            {
                Success = false,
                Error = new NeoFinderTextUnsupportedEncodingException().Message
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new FileFormatReadResult
            {
                Success = false,
                Error = exception.Message
            };
        }
    }

    public Task<FileFormatWriteResult> WriteAsync(
        FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "NeoFinder text export is not supported. Format is read-only."
        });
    }

    private static string? ReadFirstNonEmptyLine(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                return line;
            }
        }

        return null;
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }
}
