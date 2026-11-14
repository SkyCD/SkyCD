using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Katalog.CsvIdx;

/// <summary>
/// Parser for tab-separated Katalog CSV files (device.csv, storage.csv, tags.csv, parameters.csv, etc.)
/// and catalog index files (.idx, .folders.idx).
/// </summary>
public static class KatalogCsvIdxParser
{
    public static async Task<Dictionary<string, string>> ParseParametersAsync(string filePath, CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(filePath)) return parameters;

        using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var header = await reader.ReadLineAsync(ct);
        if (header is null) return parameters;

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split('\t');
            if (parts.Length >= 3)
            {
                var paramName = parts[0].Trim();
                var paramType = parts[1].Trim();
                var paramValue = parts[2].Trim();
                parameters[$"{paramType}:{paramName}"] = paramValue;
            }
        }

        return parameters;
    }

    public static async Task<List<KatalogDeviceRecord>> ParseDevicesAsync(string filePath, CancellationToken ct = default)
    {
        var devices = new List<KatalogDeviceRecord>();
        if (!File.Exists(filePath)) return devices;

        using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var headerLine = await reader.ReadLineAsync(ct);
        if (headerLine is null) return devices;

        var headerFields = headerLine.Split('\t');
        var escapedFormat = Array.Exists(headerFields, f => string.Equals(f.Trim(), "comment", StringComparison.OrdinalIgnoreCase));

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("ID\t", StringComparison.OrdinalIgnoreCase)) continue;

            var fields = line.Split('\t');
            if (fields.Length < 6) continue;

            string ReadField(int idx)
            {
                if (idx >= fields.Length) return string.Empty;
                var raw = fields[idx];
                return escapedFormat ? KatalogStringHelper.Unescape(raw) : raw;
            }

            int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var deviceId);
            int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parentId);
            var name = ReadField(2);
            var type = fields.Length > 3 ? fields[3] : string.Empty;
            int.TryParse(fields.Length > 4 ? fields[4] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var externalId);
            var path = ReadField(5);

            long.TryParse(fields.Length > 6 ? fields[6] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalFileSize);
            long.TryParse(fields.Length > 7 ? fields[7] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalFileCount);
            long.TryParse(fields.Length > 8 ? fields[8] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalSpace);
            long.TryParse(fields.Length > 9 ? fields[9] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var freeSpace);

            var active = false;
            if (fields.Length > 10)
            {
                active = string.Equals(fields[10], "true", StringComparison.OrdinalIgnoreCase) || fields[10] == "1";
            }

            int.TryParse(fields.Length > 11 ? fields[11] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var groupId);
            var dateUpdated = fields.Length > 12 ? fields[12] : string.Empty;
            int.TryParse(fields.Length > 13 ? fields[13] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var order);
            var comment = fields.Length > 14 ? ReadField(14) : string.Empty;

            devices.Add(new KatalogDeviceRecord
            {
                DeviceId = deviceId,
                ParentId = parentId,
                Name = name,
                Type = type,
                ExternalId = externalId,
                Path = path,
                TotalFileSize = totalFileSize,
                TotalFileCount = totalFileCount,
                TotalSpace = totalSpace,
                FreeSpace = freeSpace,
                Active = active,
                GroupId = groupId,
                DateUpdated = dateUpdated,
                Order = order,
                Comment = comment
            });
        }

        return devices;
    }

    public static async Task<List<KatalogStorageRecord>> ParseStorageAsync(string filePath, CancellationToken ct = default)
    {
        var storages = new List<KatalogStorageRecord>();
        if (!File.Exists(filePath)) return storages;

        using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var headerLine = await reader.ReadLineAsync(ct);
        if (headerLine is null) return storages;

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("ID\t", StringComparison.OrdinalIgnoreCase)) continue;

            var fields = line.Split('\t');
            if (fields.Length < 3) continue;

            int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var storageId);
            var name = fields.Length > 1 ? fields[1] : string.Empty;
            var type = fields.Length > 2 ? fields[2] : string.Empty;
            var location = fields.Length > 3 ? fields[3] : string.Empty;
            var path = fields.Length > 4 ? fields[4] : string.Empty;
            var label = fields.Length > 5 ? fields[5] : string.Empty;
            var fileSystem = fields.Length > 6 ? fields[6] : string.Empty;
            long.TryParse(fields.Length > 7 ? fields[7] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var total);
            long.TryParse(fields.Length > 8 ? fields[8] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var free);
            var brand = fields.Length > 9 ? fields[9] : string.Empty;
            var model = fields.Length > 10 ? fields[10] : string.Empty;
            var serialNumber = fields.Length > 11 ? fields[11] : string.Empty;
            var buildDate = fields.Length > 12 ? fields[12] : string.Empty;
            var comment1 = fields.Length > 13 ? fields[13] : string.Empty;
            var comment2 = fields.Length > 14 ? fields[14] : string.Empty;
            var comment3 = fields.Length > 15 ? fields[15] : string.Empty;
            var picturePath = fields.Length > 16 ? fields[16] : string.Empty;
            int.TryParse(fields.Length > 17 ? fields[17] : fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId);

            var commentParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(comment1)) commentParts.Add(comment1);
            if (!string.IsNullOrWhiteSpace(comment2)) commentParts.Add(comment2);
            if (!string.IsNullOrWhiteSpace(comment3)) commentParts.Add(comment3);

            storages.Add(new KatalogStorageRecord
            {
                StorageId = storageId,
                Name = name,
                Type = type,
                Location = location,
                Path = path,
                Label = label,
                FileSystem = fileSystem,
                Total = total,
                Free = free,
                Brand = brand,
                Model = model,
                SerialNumber = serialNumber,
                BuildDate = buildDate,
                Comment = string.Join(" / ", commentParts),
                PicturePath = picturePath,
                UserId = userId
            });
        }

        return storages;
    }

    public static async Task<List<KatalogTagRecord>> ParseTagsAsync(string filePath, CancellationToken ct = default)
    {
        var tags = new List<KatalogTagRecord>();
        if (!File.Exists(filePath)) return tags;

        using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var headerLine = await reader.ReadLineAsync(ct);
        if (headerLine is null) return tags;

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("ID\t", StringComparison.OrdinalIgnoreCase)) continue;

            var fields = line.Split('\t');
            if (fields.Length < 3) continue;

            int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id);
            var name = fields[1];
            var path = fields[2];
            var type = fields.Length > 3 ? fields[3] : string.Empty;
            var dateTime = fields.Length > 4 ? fields[4] : string.Empty;

            tags.Add(new KatalogTagRecord
            {
                Id = id,
                Name = name,
                Path = path,
                Type = type,
                DateTime = dateTime
            });
        }

        return tags;
    }

    public static async Task<List<string>> ParseFoldersIdxAsync(string filePath, CancellationToken ct = default)
    {
        var folders = new List<string>();
        if (!File.Exists(filePath)) return folders;

        using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split('\t');
            if (parts.Length >= 2)
            {
                folders.Add(parts[1].Trim());
            }
            else if (parts.Length == 1)
            {
                folders.Add(parts[0].Trim());
            }
        }

        return folders;
    }

    public static async Task<KatalogIdxCatalog> ParseIdxAsync(
        string filePath,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        var catalog = new KatalogIdxCatalog
        {
            FilePath = filePath,
            CatalogName = Path.GetFileNameWithoutExtension(filePath)
        };

        using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        // Read header lines (prefixed by '<tag>')
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith('<'))
            {
                var endTag = line.IndexOf('>');
                if (endTag > 0)
                {
                    var tag = line[1..endTag];
                    var val = line[(endTag + 1)..].Trim();

                    switch (tag)
                    {
                        case "catalogSourcePath":
                            catalog.SourcePath = val;
                            break;
                        case "catalogFileCount":
                            long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count);
                            catalog.FileCount = count;
                            break;
                        case "catalogTotalFileSize":
                            long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);
                            catalog.TotalFileSize = size;
                            break;
                        case "catalogIncludeHidden":
                            catalog.IncludeHidden = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "catalogFileType":
                            catalog.FileType = val;
                            break;
                        case "catalogIncludeSymblinks":
                        case "catalogIncludeSymlinks":
                            catalog.IncludeSymlinks = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "catalogIncludeSubDir":
                            catalog.IncludeSubDir = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "catalogIsFullDevice":
                            catalog.IsFullDevice = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "catalogIncludeMetadata":
                            catalog.IncludeMetadata = val;
                            break;
                        case "catalogIncludeChecksum":
                            catalog.IncludeChecksum = val;
                            break;
                        case "catalogAppVersion":
                            catalog.AppVersion = val;
                            break;
                        case "catalogID":
                            int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id);
                            catalog.CatalogId = id;
                            break;
                    }
                }
            }
            else
            {
                // First non-tag line is a file record
                ParseAndAddFileEntry(line, catalog);
                break;
            }
        }

        // Remaining lines are file records
        var lineIndex = 0;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith('<')) continue;

            ParseAndAddFileEntry(line, catalog);
            lineIndex++;
            if (lineIndex % 5000 == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
        }

        return catalog;
    }

    private static void ParseAndAddFileEntry(string line, KatalogIdxCatalog catalog)
    {
        var parts = line.Split('\t');
        if (parts.Length == 0) return;

        var fullPath = parts[0].Trim();
        if (string.IsNullOrEmpty(fullPath)) return;

        long sizeBytes = 0;
        if (parts.Length > 1)
        {
            long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out sizeBytes);
        }

        var dateUpdated = parts.Length > 2 ? parts[2].Trim() : string.Empty;
        var ext = parts.Length > 3 ? parts[3].Trim() : Path.GetExtension(fullPath).TrimStart('.');
        var fileType = parts.Length > 4 ? parts[4].Trim() : string.Empty;
        var mimeType = parts.Length > 5 ? parts[5].Trim() : string.Empty;

        int? mimeVerified = null;
        if (parts.Length > 6 && int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mv))
        {
            mimeVerified = mv;
        }

        int? typeMismatch = null;
        if (parts.Length > 7 && int.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tm))
        {
            typeMismatch = tm;
        }

        int? imgW = null;
        if (parts.Length > 8 && int.TryParse(parts[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var w)) imgW = w;

        int? imgH = null;
        if (parts.Length > 9 && int.TryParse(parts[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)) imgH = h;

        int? imgOrient = null;
        if (parts.Length > 10 && int.TryParse(parts[10], NumberStyles.Integer, CultureInfo.InvariantCulture, out var o)) imgOrient = o;

        double? vidDur = null;
        if (parts.Length > 11 && double.TryParse(parts[11], NumberStyles.Float, CultureInfo.InvariantCulture, out var vd)) vidDur = vd;

        int? vidW = null;
        if (parts.Length > 12 && int.TryParse(parts[12], NumberStyles.Integer, CultureInfo.InvariantCulture, out var vw)) vidW = vw;

        int? vidH = null;
        if (parts.Length > 13 && int.TryParse(parts[13], NumberStyles.Integer, CultureInfo.InvariantCulture, out var vh)) vidH = vh;

        var vidCodec = parts.Length > 14 ? parts[14].Trim() : string.Empty;

        double? audDur = null;
        if (parts.Length > 15 && double.TryParse(parts[15], NumberStyles.Float, CultureInfo.InvariantCulture, out var ad)) audDur = ad;

        var audArtist = parts.Length > 16 ? parts[16].Trim() : string.Empty;
        var audAlbum = parts.Length > 17 ? parts[17].Trim() : string.Empty;
        var audTitle = parts.Length > 18 ? parts[18].Trim() : string.Empty;
        var audGenre = parts.Length > 19 ? parts[19].Trim() : string.Empty;

        int? audYear = null;
        if (parts.Length > 20 && int.TryParse(parts[20], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ay)) audYear = ay;

        int? audTrack = null;
        if (parts.Length > 21 && int.TryParse(parts[21], NumberStyles.Integer, CultureInfo.InvariantCulture, out var at)) audTrack = at;

        int? audBitrate = null;
        if (parts.Length > 22 && int.TryParse(parts[22], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ab)) audBitrate = ab;

        int? audSampleRate = null;
        if (parts.Length > 23 && int.TryParse(parts[23], NumberStyles.Integer, CultureInfo.InvariantCulture, out var asr)) audSampleRate = asr;

        var metaExtended = parts.Length > 24 ? parts[24].Trim() : string.Empty;
        var metaExtDate = parts.Length > 25 ? parts[25].Trim() : string.Empty;
        var sha256 = parts.Length > 26 ? parts[26].Trim() : string.Empty;
        var sha256ExtDate = parts.Length > 27 ? parts[27].Trim() : string.Empty;

        catalog.Files.Add(new KatalogFileEntry
        {
            FullPath = fullPath,
            SizeBytes = sizeBytes,
            DateUpdated = dateUpdated,
            Extension = ext,
            FileType = fileType,
            MimeType = mimeType,
            MimeVerified = mimeVerified,
            TypeMismatch = typeMismatch,
            ImageWidth = imgW,
            ImageHeight = imgH,
            ImageOrientation = imgOrient,
            VideoDurationSeconds = vidDur,
            VideoWidth = vidW,
            VideoHeight = vidH,
            VideoCodec = vidCodec,
            AudioDurationSeconds = audDur,
            AudioArtist = audArtist,
            AudioAlbum = audAlbum,
            AudioTitle = audTitle,
            AudioGenre = audGenre,
            AudioYear = audYear,
            AudioTrackNumber = audTrack,
            AudioBitrate = audBitrate,
            AudioSampleRate = audSampleRate,
            MetadataExtended = metaExtended,
            MetadataExtractionDate = metaExtDate,
            ChecksumSha256 = sha256,
            ChecksumExtractionDate = sha256ExtDate
        });
    }
}
