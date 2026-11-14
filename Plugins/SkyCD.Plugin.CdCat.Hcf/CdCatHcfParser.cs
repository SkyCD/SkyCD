using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml;
using SkyCD.Plugin.Abstractions;
using SkyCD.Plugin.CdCat.Hcf.Exceptions;

namespace SkyCD.Plugin.CdCat.Hcf;

/// <summary>
/// Parser for CdCat .hcf (Hyper Catalog File) gzip/XML catalogs.
/// </summary>
public static class CdCatHcfParser
{
    public const float MaxSupportedVersion = 2.1f;
    public const long DefaultMaxDecompressedBytes = 512L * 1024L * 1024L; // 512 MB

    private static readonly string[] DateTimeFormats =
    [
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd",
        "yyyy/MM/dd HH:mm:ss",
        "yyyy/MM/dd"
    ];

    public static CdCatHcfReportDocument Parse(
        Stream stream,
        long maxDecompressedBytes = DefaultMaxDecompressedBytes,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        progress?.Report(5);
        cancellationToken.ThrowIfCancellationRequested();

        var initialPosition = stream.CanSeek ? stream.Position : -1;
        bool isGzip;
        try
        {
            var header = new byte[2];
            var read = stream.Read(header, 0, 2);
            if (read < 2)
            {
                throw new CdCatCorruptFileException("File is empty or truncated.");
            }

            isGzip = (header[0] == 0x1F && header[1] == 0x8B);

            if (stream.CanSeek)
            {
                stream.Position = initialPosition;
            }
            else
            {
                throw new CdCatCorruptFileException("Stream does not support seeking.");
            }
        }
        catch (CdCatCorruptFileException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CdCatCorruptFileException(ex.Message);
        }

        Stream decompressStream;
        if (isGzip)
        {
            try
            {
                decompressStream = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
            }
            catch (Exception ex)
            {
                throw new CdCatCorruptFileException($"Failed to initialize GZip decompression: {ex.Message}");
            }
        }
        else
        {
            decompressStream = stream;
        }

        var countingStream = new CountingBoundedStream(decompressStream, maxDecompressedBytes);

        var xmlSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 100_000_000,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true
        };

        XmlDocument xmlDoc = new();
        try
        {
            using var xmlReader = XmlReader.Create(countingStream, xmlSettings);
            xmlDoc.Load(xmlReader);
        }
        catch (XmlException ex) when (ex.InnerException is CdCatDecompressionLimitException)
        {
            throw ex.InnerException;
        }
        catch (XmlException ex) when (ex.InnerException is InvalidDataException or IOException)
        {
            throw new CdCatCorruptFileException(ex.InnerException.Message);
        }
        catch (XmlException ex) when (isGzip && countingStream.Position == 0)
        {
            throw new CdCatCorruptFileException($"GZip stream is corrupt or unreadable: {ex.Message}");
        }
        catch (XmlException ex)
        {
            throw new CdCatMalformedXmlException(ex.Message);
        }
        catch (InvalidDataException ex)
        {
            throw new CdCatCorruptFileException(ex.Message);
        }
        catch (CdCatDecompressionLimitException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new CdCatCorruptFileException(ex.Message);
        }

        progress?.Report(25);
        cancellationToken.ThrowIfCancellationRequested();

        var root = xmlDoc.DocumentElement;
        if (root is null)
        {
            throw new CdCatMalformedXmlException("Catalog XML has no root element.");
        }

        string catalogVersion = "2.1";
        if (string.Equals(root.Name, "datafile", StringComparison.OrdinalIgnoreCase))
        {
            var versionAttr = root.Attributes["version"]?.Value;
            if (!string.IsNullOrWhiteSpace(versionAttr))
            {
                catalogVersion = versionAttr.Trim();
                if (float.TryParse(catalogVersion, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedVersion))
                {
                    if (parsedVersion > MaxSupportedVersion + 0.0001f)
                    {
                        throw new CdCatUnsupportedVersionException(catalogVersion);
                    }
                }
            }
        }

        XmlNode? catalogNode = null;
        if (string.Equals(root.Name, "catalog", StringComparison.OrdinalIgnoreCase))
        {
            catalogNode = root;
        }
        else
        {
            catalogNode = root.SelectSingleNode("catalog");
        }

        if (catalogNode is null)
        {
            throw new CdCatEmptyCatalogException();
        }

        var mediaNodes = catalogNode.SelectNodes("media");
        if (mediaNodes is null || mediaNodes.Count == 0)
        {
            throw new CdCatEmptyCatalogException();
        }

        progress?.Report(40);
        cancellationToken.ThrowIfCancellationRequested();

        var rows = new List<Dictionary<string, object?>>();
        var diagnostics = new List<string>();
        var unsupportedFeatures = new List<string>
        {
            "Direct catalog export/write (read-only format)"
        };

        var totalMediaCount = 0;
        var totalDirectoryCount = 0;
        var totalFileCount = 0;
        long totalSizeBytes = 0;

        for (var mediaIndex = 0; mediaIndex < mediaNodes.Count; mediaIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mediaNode = mediaNodes[mediaIndex]!;
            totalMediaCount++;

            var rawMediaName = mediaNode.Attributes?["name"]?.Value ?? string.Empty;
            var mediaName = string.IsNullOrWhiteSpace(rawMediaName) ? $"Media_{mediaIndex + 1}" : rawMediaName.Trim();
            var mediaNumber = mediaNode.Attributes?["number"]?.Value?.Trim();
            var rawType = mediaNode.Attributes?["type"]?.Value?.Trim() ?? string.Empty;
            var rawTime = mediaNode.Attributes?["time"]?.Value?.Trim();

            var driveType = MapDriveType(rawType);
            var mediaNodeId = $"media:{mediaIndex + 1}_{mediaName}";

            var mediaProps = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["DriveType"] = driveType,
                ["VolumeType"] = driveType,
                ["CdCatMediaType"] = rawType
            };

            if (!string.IsNullOrWhiteSpace(mediaNumber))
            {
                mediaProps["VolumeNumber"] = mediaNumber;
                mediaProps["MediaNumber"] = mediaNumber;
            }

            if (TryParseDateTime(rawTime, out var mediaDate))
            {
                mediaProps["DateAdded"] = mediaDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                mediaProps["DateModified"] = mediaDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }

            var mediaCommentNode = mediaNode.SelectSingleNode("comment");
            if (mediaCommentNode is not null && !string.IsNullOrWhiteSpace(mediaCommentNode.InnerText))
            {
                mediaProps["Comments"] = mediaCommentNode.InnerText.Trim();
            }

            var mediaBorrowNode = mediaNode.SelectSingleNode("borrow");
            if (mediaBorrowNode is not null && !string.IsNullOrWhiteSpace(mediaBorrowNode.InnerText))
            {
                mediaProps["Borrow"] = mediaBorrowNode.InnerText.Trim();
                mediaProps["Loan"] = mediaBorrowNode.InnerText.Trim();
            }

            var mediaRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = mediaNodeId,
                ["parentId"] = null,
                ["kind"] = "media",
                ["name"] = mediaName,
                ["sizeBytes"] = "0",
                ["properties"] = mediaProps
            };

            rows.Add(mediaRow);

            long mediaTotalSize = 0;

            // Process child directories and files recursively
            ProcessContainerChildren(
                mediaNode,
                mediaIndex + 1,
                mediaNodeId,
                string.Empty,
                rows,
                diagnostics,
                ref totalDirectoryCount,
                ref totalFileCount,
                ref mediaTotalSize,
                cancellationToken);

            totalSizeBytes += mediaTotalSize;
            mediaRow["sizeBytes"] = mediaTotalSize.ToString(CultureInfo.InvariantCulture);

            var progressPercent = (int)(40 + (50.0 * (mediaIndex + 1) / mediaNodes.Count));
            progress?.Report(progressPercent);
        }

        var summary = new CdCatHcfImportSummary(
            catalogVersion,
            totalMediaCount,
            totalDirectoryCount,
            totalFileCount,
            totalSizeBytes,
            diagnostics,
            unsupportedFeatures);

        progress?.Report(100);

        return new CdCatHcfReportDocument(rows, summary);
    }

    private static void ProcessContainerChildren(
        XmlNode containerNode,
        int mediaIndex,
        string parentNodeId,
        string currentPath,
        List<Dictionary<string, object?>> rows,
        List<string> diagnostics,
        ref int totalDirectoryCount,
        ref int totalFileCount,
        ref long containerTotalSize,
        CancellationToken cancellationToken)
    {
        foreach (XmlNode child in containerNode.ChildNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.Equals(child.Name, "directory", StringComparison.OrdinalIgnoreCase))
            {
                totalDirectoryCount++;
                var dirName = child.Attributes?["name"]?.Value?.Trim() ?? $"Folder_{totalDirectoryCount}";
                var dirTime = child.Attributes?["time"]?.Value?.Trim();
                var dirRelPath = string.IsNullOrEmpty(currentPath) ? dirName : $"{currentPath}/{dirName}";
                var dirNodeId = $"folder:{mediaIndex}_{dirRelPath}";

                var dirProps = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                if (TryParseDateTime(dirTime, out var dirDate))
                {
                    dirProps["DateModified"] = dirDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    dirProps["DateCreated"] = dirDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                }

                var dirCommentNode = child.SelectSingleNode("comment");
                if (dirCommentNode is not null && !string.IsNullOrWhiteSpace(dirCommentNode.InnerText))
                {
                    dirProps["Comments"] = dirCommentNode.InnerText.Trim();
                }

                var dirRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nodeId"] = dirNodeId,
                    ["parentId"] = parentNodeId,
                    ["kind"] = "folder",
                    ["name"] = dirName,
                    ["sizeBytes"] = "0",
                    ["properties"] = dirProps
                };

                rows.Add(dirRow);

                long dirTotalSize = 0;

                ProcessContainerChildren(
                    child,
                    mediaIndex,
                    dirNodeId,
                    dirRelPath,
                    rows,
                    diagnostics,
                    ref totalDirectoryCount,
                    ref totalFileCount,
                    ref dirTotalSize,
                    cancellationToken);

                dirRow["sizeBytes"] = dirTotalSize.ToString(CultureInfo.InvariantCulture);
                containerTotalSize += dirTotalSize;
            }
            else if (string.Equals(child.Name, "file", StringComparison.OrdinalIgnoreCase))
            {
                totalFileCount++;
                var fileName = child.Attributes?["name"]?.Value?.Trim() ?? $"File_{totalFileCount}";
                var rawSize = child.Attributes?["size"]?.Value?.Trim();
                var fileTime = child.Attributes?["time"]?.Value?.Trim();
                var fileRelPath = string.IsNullOrEmpty(currentPath) ? fileName : $"{currentPath}/{fileName}";
                var fileNodeId = $"file:{mediaIndex}_{fileRelPath}";

                var sizeBytes = CdCatSizeParser.ParseSizeBytes(rawSize);
                containerTotalSize += sizeBytes;

                var fileProps = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

                if (TryParseDateTime(fileTime, out var fileDate))
                {
                    fileProps["DateModified"] = fileDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                    fileProps["DateCreated"] = fileDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                }

                var commentNode = child.SelectSingleNode("comment");
                if (commentNode is not null && !string.IsNullOrWhiteSpace(commentNode.InnerText))
                {
                    CdCatMetadataExtractor.ExtractCommentMetadata(commentNode.InnerText, fileProps);
                }

                var borrowNode = child.SelectSingleNode("borrow");
                if (borrowNode is not null && !string.IsNullOrWhiteSpace(borrowNode.InnerText))
                {
                    fileProps["Borrow"] = borrowNode.InnerText.Trim();
                    fileProps["Loan"] = borrowNode.InnerText.Trim();
                }

                var contentNode = child.SelectSingleNode("content");
                if (contentNode is not null && !string.IsNullOrWhiteSpace(contentNode.InnerText))
                {
                    fileProps["Content"] = contentNode.InnerText.Trim();
                    fileProps["ArchiveContent"] = contentNode.InnerText.Trim();
                }

                var mp3TagNode = child.SelectSingleNode("mp3tag");
                if (mp3TagNode is not null)
                {
                    var mp3Attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (mp3TagNode.Attributes is not null)
                    {
                        foreach (XmlAttribute attr in mp3TagNode.Attributes)
                        {
                            mp3Attrs[attr.Name] = attr.Value;
                        }
                    }

                    CdCatMetadataExtractor.ExtractMp3Tag(mp3Attrs, mp3TagNode.InnerText, fileProps);
                }

                var fileRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["nodeId"] = fileNodeId,
                    ["parentId"] = parentNodeId,
                    ["kind"] = "file",
                    ["name"] = fileName,
                    ["sizeBytes"] = sizeBytes.ToString(CultureInfo.InvariantCulture),
                    ["properties"] = fileProps
                };

                rows.Add(fileRow);
            }
        }
    }

    private static string MapDriveType(string rawType) =>
        rawType.ToLowerInvariant() switch
        {
            "cd" or "dvd" => "CDRom",
            "harddisc" or "harddisk" or "hdd" or "fixed" => "Fixed",
            "networkplace" or "network" => "Network",
            "floppy" or "flashdrive" or "usb" or "flash" or "removable" or "other" => "Removable",
            _ => "Unknown"
        };

    private static bool TryParseDateTime(string? rawTime, out DateTime parsedDate)
    {
        parsedDate = DateTime.MinValue;
        if (string.IsNullOrWhiteSpace(rawTime))
        {
            return false;
        }

        if (DateTime.TryParseExact(rawTime, DateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate))
        {
            return true;
        }

        return DateTime.TryParse(rawTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate);
    }

    private sealed class CountingBoundedStream(Stream innerStream, long maxAllowedBytes) : Stream
    {
        private long bytesRead;

        public override bool CanRead => innerStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => innerStream.Length;
        public override long Position
        {
            get => bytesRead;
            set => throw new NotSupportedException();
        }

        public override void Flush() => innerStream.Flush();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = innerStream.Read(buffer, offset, count);
            if (read > 0)
            {
                bytesRead += read;
                if (bytesRead > maxAllowedBytes)
                {
                    throw new CdCatDecompressionLimitException(maxAllowedBytes);
                }
            }
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
