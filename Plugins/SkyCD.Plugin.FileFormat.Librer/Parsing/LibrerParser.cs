using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using MessagePack;
using SkyCD.SDK;
using SkyCD.Plugin.FileFormat.Librer.Exceptions;
using ZstdSharp;
using SkyCD.Plugin.FileFormat.Librer.Models;

namespace SkyCD.Plugin.FileFormat.Librer.Parsing;

/// <summary>
/// Parser for Librer catalog archives (.librer_data, .librer_data_compr, .dat).
/// </summary>
public sealed class LibrerParser
{
    public LibrerReportDocument Parse(Stream stream, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Copy to MemoryStream if not seekable
        MemoryStream? memoryStream = null;
        Stream seekableStream = stream;
        if (!stream.CanSeek)
        {
            memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            memoryStream.Position = 0;
            seekableStream = memoryStream;
        }

        try
        {
            ZipArchive zipArchive;
            try
            {
                zipArchive = new ZipArchive(seekableStream, ZipArchiveMode.Read, leaveOpen: true);
            }
            catch (Exception ex)
            {
                throw new LibrerInvalidArchiveException("Failed to open file as a Zip archive.", ex);
            }

            using (zipArchive)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // 1. Read and parse HEADER
                var header = ReadHeader(zipArchive);

                // 2. Read and parse filenames
                var filenames = ReadFilenames(zipArchive);

                // 3. Read and parse customdata (optional)
                var customData = ReadCustomData(zipArchive);

                // 4. Read and parse filestructure
                var filestructureObj = ReadFilestructure(zipArchive);

                // 5. Convert to CatalogDocument rows
                var rows = new List<Dictionary<string, object?>>();

                var mediaId = $"media:{Guid.NewGuid():N}";
                var mediaLabel = !string.IsNullOrWhiteSpace(header.Label)
                    ? header.Label
                    : (!string.IsNullOrWhiteSpace(header.ScanPath) ? Path.GetFileName(header.ScanPath.TrimEnd('/', '\\')) : "Librer Volume");

                if (string.IsNullOrWhiteSpace(mediaLabel))
                    mediaLabel = "Librer Volume";

                var mediaProperties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ScanPath"] = header.ScanPath,
                    ["CreationHost"] = header.CreationHost,
                    ["CreationOS"] = header.CreationOS,
                    ["FilesCount"] = header.QuantFiles.ToString(),
                    ["FoldersCount"] = header.QuantFolders.ToString(),
                    ["SumSize"] = header.SumSize.ToString()
                };

                if (!string.IsNullOrWhiteSpace(header.Info))
                    mediaProperties["Comment"] = header.Info;

                if (header.Free >= 0)
                    mediaProperties["FreeSpace"] = header.Free.ToString();

                if (header.DataFormatVersion is not null)
                    mediaProperties["DataFormatVersion"] = header.DataFormatVersion.ToString() ?? string.Empty;

                if (header.CreationTime > 0)
                {
                    try
                    {
                        var dto = DateTimeOffset.FromUnixTimeSeconds(header.CreationTime);
                        mediaProperties["CreationTime"] = dto.ToString("O");
                    }
                    catch
                    {
                        mediaProperties["CreationTime"] = header.CreationTime.ToString();
                    }
                }

                var mediaRow = new Dictionary<string, object?>
                {
                    ["Id"] = mediaId,
                    ["ParentId"] = null,
                    ["Name"] = mediaLabel,
                    ["Type"] = "Media",
                    ["Size"] = header.SumSize,
                    ["Properties"] = mediaProperties
                };
                rows.Add(mediaRow);

                // Process children
                int totalProcessed = 0;
                TraverseFilestructure(filestructureObj, mediaId, filenames, customData, rows, ref totalProcessed, progress, cancellationToken);

                var summary = new LibrerImportSummary
                {
                    CatalogLabel = mediaLabel,
                    ScanPath = header.ScanPath,
                    DataFormatVersion = header.DataFormatVersion?.ToString() ?? "1",
                    CreationHost = header.CreationHost,
                    CreationOS = header.CreationOS,
                    TotalFiles = header.QuantFiles > 0 ? header.QuantFiles : rows.Count - 1,
                    TotalFolders = header.QuantFolders,
                    TotalSizeBytes = header.SumSize,
                    FreeSizeBytes = header.Free,
                    CustomDataCount = customData.Count,
                    Comments = header.Info
                };

                if (header.CreationTime > 0)
                {
                    try
                    {
                        summary.CreationTime = DateTimeOffset.FromUnixTimeSeconds(header.CreationTime);
                    }
                    catch
                    {
                        // Ignore date conversion failure
                    }
                }

                return new LibrerReportDocument(rows, summary);
            }
        }
        finally
        {
            memoryStream?.Dispose();
        }
    }

    private static LibrerHeader ReadHeader(ZipArchive zipArchive)
    {
        var headerEntry = zipArchive.GetEntry("HEADER") ?? zipArchive.GetEntry("header");
        if (headerEntry is null)
        {
            throw new LibrerMissingHeaderException();
        }

        byte[] rawBytes;
        using (var entryStream = headerEntry.Open())
        using (var ms = new MemoryStream())
        {
            entryStream.CopyTo(ms);
            rawBytes = ms.ToArray();
        }

        if (rawBytes.Length == 0)
        {
            throw new LibrerMissingHeaderException("HEADER entry is empty.");
        }

        // Try decompressing with Zstd if it's compressed, or use raw if uncompressed
        byte[] payload = DecompressIfNeeded(rawBytes, "HEADER");

        try
        {
            var dict = DeserializeMessagePackDictionary(payload);
            return LibrerHeader.FromDictionary(dict);
        }
        catch (Exception ex)
        {
            throw new LibrerCorruptFileException("Failed to parse HEADER MessagePack dictionary.", ex);
        }
    }

    private static List<string> ReadFilenames(ZipArchive zipArchive)
    {
        var entry = zipArchive.GetEntry("filenames");
        if (entry is null)
        {
            return [];
        }

        byte[] rawBytes;
        using (var entryStream = entry.Open())
        using (var ms = new MemoryStream())
        {
            entryStream.CopyTo(ms);
            rawBytes = ms.ToArray();
        }

        if (rawBytes.Length == 0)
        {
            return [];
        }

        byte[] payload = DecompressIfNeeded(rawBytes, "filenames");

        try
        {
            var rawList = DeserializeMessagePackList(payload);
            var result = new List<string>(rawList.Count);
            foreach (var item in rawList)
            {
                result.Add(item?.ToString() ?? string.Empty);
            }
            return result;
        }
        catch (Exception ex)
        {
            throw new LibrerCorruptFileException("Failed to parse filenames entry.", ex);
        }
    }

    private static List<object?> ReadCustomData(ZipArchive zipArchive)
    {
        var entry = zipArchive.GetEntry("customdata");
        if (entry is null)
        {
            return [];
        }

        byte[] rawBytes;
        using (var entryStream = entry.Open())
        using (var ms = new MemoryStream())
        {
            entryStream.CopyTo(ms);
            rawBytes = ms.ToArray();
        }

        if (rawBytes.Length == 0)
        {
            return [];
        }

        byte[] payload = DecompressIfNeeded(rawBytes, "customdata");

        try
        {
            return DeserializeMessagePackList(payload);
        }
        catch (Exception ex)
        {
            throw new LibrerCorruptFileException("Failed to parse customdata entry.", ex);
        }
    }

    private static object? ReadFilestructure(ZipArchive zipArchive)
    {
        var entry = zipArchive.GetEntry("filestructure");
        if (entry is null)
        {
            throw new LibrerEmptyCatalogException("Archive is missing filestructure entry.");
        }

        byte[] rawBytes;
        using (var entryStream = entry.Open())
        using (var ms = new MemoryStream())
        {
            entryStream.CopyTo(ms);
            rawBytes = ms.ToArray();
        }

        if (rawBytes.Length == 0)
        {
            throw new LibrerEmptyCatalogException("filestructure entry is empty.");
        }

        byte[] payload = DecompressIfNeeded(rawBytes, "filestructure");

        try
        {
            return DeserializeMessagePackObject(payload);
        }
        catch (Exception ex)
        {
            throw new LibrerCorruptFileException("Failed to parse filestructure entry.", ex);
        }
    }

    private static byte[] DecompressIfNeeded(byte[] data, string entryName)
    {
        if (data.Length == 0)
            return data;

        // Check if data starts with Zstandard magic number: 0xFD2FB528 (little-endian: 0x28, 0xB5, 0x2F, 0xFD)
        if (data.Length >= 4 && data[0] == 0x28 && data[1] == 0xB5 && data[2] == 0x2F && data[3] == 0xFD)
        {
            try
            {
                using var decompressor = new Decompressor();
                return decompressor.Unwrap(data).ToArray();
            }
            catch (Exception ex)
            {
                throw new LibrerDecompressionException(entryName, ex);
            }
        }

        return data;
    }

    private static void TraverseFilestructure(
        object? filestructureObj,
        string rootParentId,
        List<string> filenames,
        List<object?> customData,
        List<Dictionary<string, object?>> rows,
        ref int totalProcessed,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        if (filestructureObj is null)
            return;

        // If root is a tuple/array representing the media node:
        // [name_nr, code, size, mtime, sub_list]
        if (filestructureObj is IList<object?> rootList && rootList.Count >= 5)
        {
            var code = ConvertToInt32(rootList[1]);
            var flags = new LibrerFlags(code);
            if (flags.HasFiles && rootList[4] is IList<object?> children)
            {
                foreach (var child in children)
                {
                    ProcessNode(child, rootParentId, filenames, customData, rows, ref totalProcessed, progress, cancellationToken);
                }
                return;
            }
        }

        if (filestructureObj is IList<object?> list)
        {
            foreach (var item in list)
            {
                ProcessNode(item, rootParentId, filenames, customData, rows, ref totalProcessed, progress, cancellationToken);
            }
        }
    }

    private static void ProcessNode(
        object? nodeObj,
        string parentId,
        List<string> filenames,
        List<object?> customData,
        List<Dictionary<string, object?>> rows,
        ref int totalProcessed,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (nodeObj is not IList<object?> entryList || entryList.Count < 4)
            return;

        totalProcessed++;
        if (totalProcessed % 100 == 0)
        {
            progress?.Report(totalProcessed);
        }

        var nameIndex = ConvertToInt32(entryList[0]);
        var code = ConvertToInt32(entryList[1]);
        var size = ConvertToInt64(entryList[2]);
        var mtime = ConvertToInt64(entryList[3]);

        var flags = new LibrerFlags(code);

        string name = (nameIndex >= 0 && nameIndex < filenames.Count)
            ? filenames[nameIndex]
            : (entryList[0]?.ToString() ?? $"Item_{nameIndex}");

        var nodeId = $"item:{Guid.NewGuid():N}";
        var docType = flags.IsDir ? "Folder" : "File";

        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (mtime > 0)
        {
            try
            {
                var dto = DateTimeOffset.FromUnixTimeSeconds(mtime);
                properties["MTime"] = dto.ToString("O");
            }
            catch
            {
                properties["MTime"] = mtime.ToString();
            }
        }

        if (flags.IsSymlink)
            properties["IsSymlink"] = "true";

        if (flags.IsBind)
            properties["IsBind"] = "true";

        int elemIndex = 4;
        IList<object?>? subData = null;
        if (flags.HasFiles && entryList.Count > elemIndex)
        {
            subData = entryList[elemIndex] as IList<object?>;
            elemIndex++;
        }

        if (flags.HasCd && entryList.Count > elemIndex)
        {
            var cdIndex = ConvertToInt32(entryList[elemIndex]);
            if (cdIndex >= 0 && cdIndex < customData.Count)
            {
                var cdObj = customData[cdIndex];
                ExtractCustomDataProperties(cdObj, properties);
            }
            elemIndex++;
        }

        var row = new Dictionary<string, object?>
        {
            ["Id"] = nodeId,
            ["ParentId"] = parentId,
            ["Name"] = name,
            ["Type"] = docType,
            ["Size"] = size >= 0 ? size : 0,
            ["Properties"] = properties
        };
        rows.Add(row);

        if (flags.IsDir && subData is not null)
        {
            foreach (var subEntry in subData)
            {
                ProcessNode(subEntry, nodeId, filenames, customData, rows, ref totalProcessed, progress, cancellationToken);
            }
        }
    }

    private static void ExtractCustomDataProperties(object? cdObj, Dictionary<string, object?> properties)
    {
        if (cdObj is null)
            return;

        if (cdObj is string str)
        {
            if (!string.IsNullOrWhiteSpace(str))
                properties["CustomData"] = str;
            return;
        }

        if (cdObj is IList<object?> tupleList)
        {
            // Tuple: (rule_nr, return_code, data)
            if (tupleList.Count >= 3)
            {
                var dataVal = tupleList[2];
                if (dataVal is not null)
                {
                    var dataStr = dataVal.ToString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(dataStr))
                        properties["CustomData"] = dataStr;
                }
            }
            else if (tupleList.Count > 0)
            {
                var last = tupleList[^1];
                if (last is not null)
                {
                    var strVal = last.ToString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(strVal))
                        properties["CustomData"] = strVal;
                }
            }
            return;
        }

        var genericStr = cdObj.ToString();
        if (!string.IsNullOrWhiteSpace(genericStr))
            properties["CustomData"] = genericStr;
    }

    private static int ConvertToInt32(object? val)
    {
        if (val is null) return 0;
        return val switch
        {
            sbyte sb => sb,
            byte b => b,
            short s => s,
            ushort us => us,
            int i => i,
            uint ui => (int)ui,
            long l => (int)l,
            ulong ul => (int)ul,
            string str when int.TryParse(str, out var parsed) => parsed,
            _ => 0
        };
    }

    private static long ConvertToInt64(object? val)
    {
        if (val is null) return 0;
        return val switch
        {
            sbyte sb => sb,
            byte b => b,
            short s => s,
            ushort us => us,
            int i => i,
            uint ui => ui,
            long l => l,
            ulong ul => (long)ul,
            string str when long.TryParse(str, out var parsed) => parsed,
            _ => 0
        };
    }

    private static Dictionary<string, object?> DeserializeMessagePackDictionary(byte[] bytes)
    {
        var rawObj = MessagePackSerializer.Deserialize<object>(bytes);
        return ConvertToDictionary(rawObj);
    }

    private static List<object?> DeserializeMessagePackList(byte[] bytes)
    {
        var rawObj = MessagePackSerializer.Deserialize<object>(bytes);
        return ConvertToList(rawObj);
    }

    private static object? DeserializeMessagePackObject(byte[] bytes)
    {
        var rawObj = MessagePackSerializer.Deserialize<object>(bytes);
        return NormalizeMessagePackObject(rawObj);
    }

    private static Dictionary<string, object?> ConvertToDictionary(object? raw)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (raw is IDictionary<object, object?> dictObj)
        {
            foreach (var (k, v) in dictObj)
            {
                var keyStr = k?.ToString() ?? string.Empty;
                result[keyStr] = NormalizeMessagePackObject(v);
            }
        }
        else if (raw is IDictionary<string, object?> strDict)
        {
            foreach (var (k, v) in strDict)
            {
                result[k] = NormalizeMessagePackObject(v);
            }
        }
        return result;
    }

    private static List<object?> ConvertToList(object? raw)
    {
        if (raw is IList<object?> list)
        {
            var result = new List<object?>(list.Count);
            foreach (var item in list)
            {
                result.Add(NormalizeMessagePackObject(item));
            }
            return result;
        }

        if (raw is Array arr)
        {
            var result = new List<object?>(arr.Length);
            foreach (var item in arr)
            {
                result.Add(NormalizeMessagePackObject(item));
            }
            return result;
        }

        return [];
    }

    private static object? NormalizeMessagePackObject(object? obj)
    {
        if (obj is null)
            return null;

        if (obj is IDictionary<object, object?> or IDictionary<string, object?>)
            return ConvertToDictionary(obj);

        if (obj is IList<object?> or Array)
            return ConvertToList(obj);

        return obj;
    }
}
