using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SkyCD.Plugin.CdCatalogExpert.Exceptions;

namespace SkyCD.Plugin.CdCatalogExpert;

/// <summary>
/// Converts between <see cref="ZdcDatabase"/> containers and the shared flat
/// node rows.
/// </summary>
/// <remarks>
/// <para>
/// Domain mapping is structural only: the container becomes the media root
/// and every member becomes a file row. The ZDC profile stores no
/// parent/child relations between members (parent entries are <c>-1</c> roots),
/// so disc XML payloads are preserved opaquely instead of being expanded into
/// deeper hierarchies.
/// </para>
/// <para>
/// All preserved raw fields travel as plain strings so rows survive host
/// serialization: timestamps and counts as invariant integers, reserved blobs,
/// keys, name bytes, and payloads as Base64.
/// </para>
/// </remarks>
public static class ZdcReportBuilder
{
    private const string SourceKey = "ZdcSource";
    private const string SourceValue = "cdcatalogexpert-native";
    private const string CscKey = "ZdcCsc";
    private const string OofKey = "ZdcOof";
    private const string ProfileKeyPrefix = "ZdcProfile";
    private const string ReservedProfileKey = "ZdcReservedProfile";
    private const string StoredLengthKey = "ZdcStoredLength";
    private const string TimestampAKey = "ZdcTimestampA";
    private const string TimestampBKey = "ZdcTimestampB";
    private const string TimestampCKey = "ZdcTimestampC";
    private const string FileTypeKey = "ZdcFileType";
    private const string ParentEntryKey = "ZdcParentEntry";
    private const string ReservedKey = "ZdcReserved";
    private const string EntryTypeKey = "ZdcEntryType";
    private const string FileNameBytesKey = "ZdcFileNameBytes";
    private const string PayloadKey = "ZdcPayload";
    private const string PayloadShaKey = "ZdcPayloadSha256";
    private const string AacsProfileKey = "ZdcAacsProfile";

    /// <summary>
    /// Flattens a container into node rows.
    /// </summary>
    public static ZdcReportDocument Build(ZdcDatabase database, string fileStem, int totalRecordsRead)
    {
        ArgumentNullException.ThrowIfNull(database);

        var mediaName = string.IsNullOrWhiteSpace(fileStem) ? "CD Catalog Expert" : fileStem.Trim();
        var rows = new List<Dictionary<string, object?>>();
        var unsupported = new SortedSet<string>(StringComparer.Ordinal);

        var mediaId = $"media:{mediaName}";
        var mediaProperties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [SourceKey] = SourceValue,
            [CscKey] = database.Csc.ToString(CultureInfo.InvariantCulture),
            [OofKey] = database.Oof.ToString(CultureInfo.InvariantCulture),
            [ReservedProfileKey] = Convert.ToBase64String(database.ReservedProfile)
        };
        for (var i = 0; i < database.ProfileValues.Length; i++)
        {
            mediaProperties[$"{ProfileKeyPrefix}{(char)('A' + i)}"] =
                database.ProfileValues[i].ToString(CultureInfo.InvariantCulture);
        }

        rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["nodeId"] = mediaId,
            ["parentId"] = null,
            ["kind"] = "media",
            ["name"] = mediaName,
            ["sizeBytes"] = "0",
            ["properties"] = mediaProperties
        });

        var counter = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var member in database.Members)
        {
            var nodeId = $"file:{mediaName}/{member.FileName}";
            if (!counter.TryAdd(nodeId, 0))
            {
                counter[nodeId]++;
                nodeId = $"{nodeId}#{counter[nodeId]}";
            }

            CollectUnsupported(member, unsupported);
            var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                [StoredLengthKey] = member.StoredLength.ToString(CultureInfo.InvariantCulture),
                [TimestampAKey] = member.TimestampA.ToString(CultureInfo.InvariantCulture),
                [TimestampBKey] = member.TimestampB.ToString(CultureInfo.InvariantCulture),
                [TimestampCKey] = member.TimestampC.ToString(CultureInfo.InvariantCulture),
                [FileTypeKey] = member.FileType.ToString(CultureInfo.InvariantCulture),
                [ParentEntryKey] = member.ParentEntry.ToString(CultureInfo.InvariantCulture),
                [ReservedKey] = Convert.ToBase64String(member.Reserved),
                [EntryTypeKey] = member.EntryType.ToString(CultureInfo.InvariantCulture),
                [FileNameBytesKey] = Convert.ToBase64String(member.FileNameBytes),
                [PayloadKey] = Convert.ToBase64String(member.Payload),
                [PayloadShaKey] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(member.Payload)),
                [AacsProfileKey] = member.AacsProfile.ToString(CultureInfo.InvariantCulture)
            };
            rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = nodeId,
                ["parentId"] = mediaId,
                ["kind"] = "file",
                ["name"] = member.FileName,
                ["sizeBytes"] = member.Payload.Length.ToString(CultureInfo.InvariantCulture),
                ["properties"] = properties
            });
        }

        var summary = new ZdcImportSummary(
            TotalRecordsRead: totalRecordsRead,
            ImportedRecordsCount: database.Members.Count,
            SkippedRecordsCount: 0,
            IsPartialReport: false,
            SkippedReasons: [],
            UnsupportedFields: unsupported.ToList(),
            DetectedVersion: "9.x");

        return new ZdcReportDocument(rows, summary);
    }

    private static void CollectUnsupported(ZdcMember member, SortedSet<string> unsupported)
    {
        if (member.FileType != 0)
        {
            unsupported.Add($"FileType=0x{member.FileType:X8}");
        }

        if (member.EntryType != 0)
        {
            unsupported.Add($"EntryType=0x{member.EntryType:X8}");
        }
    }

    /// <summary>
    /// Rebuilds a container from flat node rows (round-trip and fresh writes).
    /// </summary>
    /// <remarks>
    /// Rows carrying preserved <c>Zdc*</c> properties restore their raw
    /// fields verbatim, including payload bytes and AACS profile; other rows
    /// are written with neutral defaults. Orphaned rows (dangling parent
    /// links) attach to the container root instead of being dropped.
    /// </remarks>
    public static ZdcDatabase ToDatabase(IReadOnlyList<Dictionary<string, object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var database = new ZdcDatabase();
        var mediaIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (string.Equals(row.TryGetValue("kind", out var kindValue) ? kindValue?.ToString() : null, "media", StringComparison.OrdinalIgnoreCase) &&
                row.TryGetValue("nodeId", out var mediaIdValue) && mediaIdValue?.ToString() is { } mediaId)
            {
                mediaIds.Add(mediaId);
            }
        }

        var mediaRow = rows.FirstOrDefault(r =>
            string.Equals(r.TryGetValue("kind", out var kindValue) ? kindValue?.ToString() : null, "media", StringComparison.OrdinalIgnoreCase) &&
            r.TryGetValue("nodeId", out var mediaIdValue) && mediaIdValue?.ToString() is { } mediaId &&
            mediaIds.Contains(mediaId));
        if (mediaRow is not null)
        {
            ReadContainerProfile(mediaRow, database);
        }

        foreach (var row in rows)
        {
            var kind = row.TryGetValue("kind", out var kindValue) ? kindValue?.ToString() : null;
            if (!string.Equals(kind, "file", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = row.TryGetValue("name", out var nameValue) ? nameValue?.ToString() ?? string.Empty : string.Empty;
            var preserved = row.TryGetValue("properties", out var preservedValue) &&
                preservedValue is Dictionary<string, object?> preservedDict
                    ? preservedDict
                    : new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            database.Members.Add(new ZdcMember
            {
                FileName = name,
                TimestampA = ReadUInt64(preserved, TimestampAKey),
                TimestampB = ReadUInt64(preserved, TimestampBKey),
                TimestampC = ReadUInt64(preserved, TimestampCKey),
                FileType = (uint)ReadUInt64(preserved, FileTypeKey),
                ParentEntry = (int)ReadInt64(preserved, ParentEntryKey, -1),
                Reserved = ReadBytes(preserved, ReservedKey, ZdcFormat.MemberReservedSize),
                EntryType = (uint)ReadUInt64(preserved, EntryTypeKey),
                FileNameBytes = ReadBytes(preserved, FileNameBytesKey, 0),
                Payload = ReadBytes(preserved, PayloadKey, 0),
                AacsProfile = (uint)ReadUInt64(preserved, AacsProfileKey)
            });
        }

        return database;
    }

    private static void ReadContainerProfile(Dictionary<string, object?> mediaRow, ZdcDatabase database)
    {
        if (mediaRow.TryGetValue("properties", out var propsValue) &&
            propsValue is Dictionary<string, object?> properties)
        {
            database.Csc = (uint)ReadUInt64(properties, CscKey);
            database.Oof = (uint)ReadUInt64(properties, OofKey);
            for (var i = 0; i < database.ProfileValues.Length; i++)
            {
                database.ProfileValues[i] = (uint)ReadUInt64(properties, $"{ProfileKeyPrefix}{(char)('A' + i)}");
            }

            database.ReservedProfile = ReadBytes(properties, ReservedProfileKey, ZdcFormat.ReservedProfileSize);
        }
    }

    private static ulong ReadUInt64(Dictionary<string, object?> properties, string key)
    {
        if (properties.TryGetValue(key, out var value) &&
            ulong.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static long ReadInt64(Dictionary<string, object?> properties, string key, long fallback)
    {
        if (properties.TryGetValue(key, out var value) &&
            long.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static byte[] ReadBytes(Dictionary<string, object?> properties, string key, int expectedLength)
    {
        if (properties.TryGetValue(key, out var value))
        {
            try
            {
                var raw = Convert.FromBase64String(value?.ToString() ?? string.Empty);
                if (expectedLength == 0 || raw.Length == expectedLength)
                {
                    return raw;
                }
            }
            catch (FormatException)
            {
                // Leniency on the write path only: unreadable preserved values are dropped.
            }
        }

        return new byte[expectedLength];
    }
}
