using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using SkyCD.Plugin.EasyDiskCatalog.Exceptions;

namespace SkyCD.Plugin.EasyDiskCatalog;

/// <summary>
/// Reads Easy Disk Catalog (.edcdb) databases into catalog rows. Works from the documented
/// schema alone: required catalog tables are validated case-insensitively, unknown tables
/// and columns are retained as extension metadata, and every stored value is interpreted
/// defensively because an offline catalog never proves its original drive is attached.
/// </summary>
public static class EasyDiskCatalogReader
{
    internal const string CompatibilityPassword = "rVtvGfj3mLr*&!qe";

    private static readonly int[] PageSizeCandidates = [4096, 1024, 2048, 8192, 512, 16384, 32768];

    private static readonly string[] RequiredTables = ["Disks", "Folders", "Files"];

    private static readonly string[] OptionalTables =
        ["Categories", "Locations", "Contacts", "Loan"];

    private static readonly string[] DiskColumns =
        ["id", "Name", "Comment", "Folders", "Files", "Size", "FreeSpace", "Capacity",
            "Label", "DiskType", "SerialNumber", "DriveLetter", "FileSystem", "ComputerName",
            "ScanDate", "CategoryID", "LocationID", "LoanID"];

    private static readonly string[] FolderColumns =
        ["id", "DiskID", "ParentID", "Alias", "Name", "Type", "Path", "Size", "Folders", "Files",
            "Encrypted", "ReadOnly", "Hidden", "System", "Archive", "Compressed",
            "Created", "Modified", "Accessed", "Comment"];

    private static readonly string[] FileColumns =
        ["id", "DiskID", "ParentID", "Alias", "Name", "Type", "Path", "Size", "CRC",
            "Preview", "Metadata", "Encrypted", "ReadOnly", "Hidden", "System", "Archive",
            "Compressed", "Created", "Modified", "Accessed", "Comment"];

    private static readonly string[] GroupColumns = ["id", "ParentID", "Name", "Comment"];

    private static readonly string[] ContactColumns =
        ["id", "Name", "Address", "HomePhone", "MobilePhone", "Email", "Comment"];

    private static readonly string[] LoanColumns =
        ["id", "DiskID", "ContactID", "StartDate", "ReturnDate", "Lent", "Returned", "Comment"];

    private static readonly string[] AttributeFlags =
        ["Encrypted", "ReadOnly", "Hidden", "System", "Archive", "Compressed"];

    private const int MaxPropertyText = 8192;
    private const int MaxColumnText = 4096;

    public static EasyDiskCatalogReportDocument Parse(
        string filePath,
        string? previewBaseDirectory = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new EasyDiskCatalogCorruptException($"database file '{filePath}' does not exist.");
        }

        using var database = OpenDatabase(filePath);
        return ReadCatalog(database, filePath, previewBaseDirectory, progress, cancellationToken);
    }

    internal static EasyDiskCatalogDatabase OpenDatabase(string filePath)
    {
        var info = new FileInfo(filePath);
        if (info.Length < 16)
        {
            throw new EasyDiskCatalogCorruptException(
                $"database file is only {info.Length} bytes and cannot hold a database header.");
        }

        if (HasPlaintextSqliteHeader(filePath))
        {
            var plain = EasyDiskCatalogDatabase.OpenPlain(filePath);
            try
            {
                ValidateRequiredTables(plain);
                return plain;
            }
            catch
            {
                plain.Dispose();
                throw;
            }
        }

        var password = Encoding.UTF8.GetBytes(CompatibilityPassword);
        foreach (var pageSize in PageSizeCandidates)
        {
            try
            {
                var encrypted = EasyDiskCatalogDatabase.OpenEncrypted(
                    filePath, password, pageSize == 4096 ? null : pageSize);
                try
                {
                    // Decoding worked: a schema mismatch is reported as-is, not as a
                    // decryption failure, so other page sizes are not attempted.
                    ValidateRequiredTables(encrypted);
                    return encrypted;
                }
                catch
                {
                    encrypted.Dispose();
                    throw;
                }
            }
            catch (EasyDiskCatalogUnsupportedException)
            {
                throw;
            }
            catch (Exception ex) when (ex is EasyDiskCatalogCorruptException
                or EasyDiskCatalogDecryptionException)
            {
                continue;
            }
        }

        throw new EasyDiskCatalogDecryptionException(
            "no page-size candidate decoded the catalog header");
    }

    private static void ValidateRequiredTables(EasyDiskCatalogDatabase database)
    {
        var tables = database.ReadTableNames();
        var missing = RequiredTables.Where(required => !tables.Contains(required)).ToArray();
        if (missing.Length > 0)
        {
            throw new EasyDiskCatalogUnsupportedException(
                $"required catalog tables are missing: {string.Join(", ", missing)}");
        }
    }

    private static bool HasPlaintextSqliteHeader(string filePath)
    {
        Span<byte> header = stackalloc byte[16];
        using var stream = File.OpenRead(filePath);
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length)
        {
            return false;
        }

        return Encoding.ASCII.GetString(header[..15]).Equals("SQLite format 3", StringComparison.Ordinal)
            && header[15] == 0;
    }

    private static EasyDiskCatalogReportDocument ReadCatalog(
        EasyDiskCatalogDatabase database,
        string filePath,
        string? previewBaseDirectory,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        var summary = new EasyDiskCatalogImportSummary();
        var tables = database.ReadTableNames();
        var unknownTables = tables
            .Where(table => !RequiredTables.Contains(table, StringComparer.OrdinalIgnoreCase)
                && !OptionalTables.Contains(table, StringComparer.OrdinalIgnoreCase)
                && !table.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase))
            .OrderBy(table => table, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        summary.UnknownTables = unknownTables.Length;
        summary.OpenedEncrypted = !HasPlaintextSqliteHeader(filePath);

        var categories = ReadGroups(database, tables, "Categories", cancellationToken);
        var locations = ReadGroups(database, tables, "Locations", cancellationToken);
        var contacts = ReadContacts(database, tables, cancellationToken);
        summary.Categories = categories.ById.Count;
        summary.Locations = locations.ById.Count;
        summary.Contacts = contacts.Count;

        var disks = ReadTable(database, tables, "Disks", DiskColumns, null, [], cancellationToken);
        var rows = new List<Dictionary<string, object?>>(disks.Count * 4);
        var total = Math.Max(1, disks.Count);
        var diskIndex = 0;
        var contactDirectoryAttached = false;

        foreach (var disk in disks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var diskId = ReadLong(disk, "id");
            var diskRowId = $"disk:{diskId}";
            var diskFolders = ReadFolders(database, tables, diskId, cancellationToken);
            var diskFiles = ReadFiles(database, tables, diskId, cancellationToken);
            var loans = ReadLoans(database, tables, diskId, contacts, summary, cancellationToken);

            var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            CopyText(properties, disk, "Comment", "Comment");
            CopyText(properties, disk, "Label", "Label");
            CopyText(properties, disk, "DiskType", "DiskType");
            CopyText(properties, disk, "SerialNumber", "SerialNumber");
            CopyText(properties, disk, "DriveLetter", "DriveLetter");
            CopyText(properties, disk, "FileSystem", "FileSystem");
            CopyText(properties, disk, "ComputerName", "ComputerName");
            CopyDate(properties, disk, "ScanDate", "ScanDate");
            CopyLong(properties, disk, "Size", "TotalSize");
            CopyLong(properties, disk, "FreeSpace", "FreeSpace");
            CopyLong(properties, disk, "Capacity", "Capacity");
            properties["Category"] = ResolveGroupPath(categories, ReadLong(disk, "CategoryID"));
            properties["Location"] = ResolveGroupPath(locations, ReadLong(disk, "LocationID"));
            CopyUnknownColumns(properties, disk, DiskColumns, summary);
            if (unknownTables.Length > 0)
            {
                properties["ExtensionTables"] = string.Join(", ", unknownTables);
            }

            ApplyLoans(properties, loans);
            if (!contactDirectoryAttached && contacts.Count > 0)
            {
                properties["ContactDirectory"] = BuildContactDirectory(contacts);
                contactDirectoryAttached = true;
                summary.Notes.Add("Global contact directory is attached to the first disk entry.");
            }

            var diskName = ReadText(disk, "Name");
            if (string.IsNullOrWhiteSpace(diskName))
            {
                summary.SkippedRows++;
                summary.Notes.Add($"Disk id {diskId} has no name and was skipped.");
                continue;
            }

            rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = diskRowId,
                ["name"] = diskName,
                ["parentId"] = null,
                ["type"] = "media",
                ["size"] = ClampNonNegative(ReadLong(disk, "Size")),
                ["childrenCount"] = ClampNonNegative(ReadLong(disk, "Folders", "Files")),
                ["properties"] = properties
            });
            summary.Disks++;

            AppendFolderAndFileRows(rows, diskRowId, diskFolders, diskFiles,
                filePath, previewBaseDirectory, summary, cancellationToken);

            progress?.Report((int)((++diskIndex * 100L) / total));
        }

        progress?.Report(100);
        return new EasyDiskCatalogReportDocument(rows, summary);
    }

    private static void AppendFolderAndFileRows(
        List<Dictionary<string, object?>> rows,
        string diskRowId,
        List<Dictionary<string, object?>> folders,
        List<Dictionary<string, object?>> files,
        string filePath,
        string? previewBaseDirectory,
        EasyDiskCatalogImportSummary summary,
        CancellationToken cancellationToken)
    {
        var folderIds = new HashSet<long>(folders.Select(row => ReadLong(row, "id")));
        var previewDirectory = ResolvePreviewDirectory(filePath, previewBaseDirectory);

        foreach (var folder in folders.OrderBy(row => ReadLong(row, "ParentID")))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folderId = ReadLong(folder, "id");
            var name = DisplayName(folder);
            if (string.IsNullOrWhiteSpace(name))
            {
                summary.SkippedRows++;
                continue;
            }

            var parentId = ReadLong(folder, "ParentID");
            var parentRowId = !IsRootParent(parentId) && folderIds.Contains(parentId)
                ? $"folder:{parentId}"
                : diskRowId;

            rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = $"folder:{folderId}",
                ["name"] = name,
                ["parentId"] = parentRowId,
                ["type"] = "folder",
                ["size"] = ClampNonNegative(ReadLong(folder, "Size")),
                ["childrenCount"] = ClampNonNegative(ReadLong(folder, "Folders", "Files")),
                ["properties"] = BuildEntryProperties(folder, FolderColumns, summary)
            });
            summary.Folders++;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileId = ReadLong(file, "id");
            var name = DisplayName(file);
            if (string.IsNullOrWhiteSpace(name))
            {
                summary.SkippedRows++;
                continue;
            }

            var parentId = ReadLong(file, "ParentID");
            var parentRowId = folderIds.Contains(parentId) ? $"folder:{parentId}" : diskRowId;

            var properties = BuildEntryProperties(file, FileColumns, summary);
            var preview = ReadText(file, "Preview");
            if (!string.IsNullOrWhiteSpace(preview))
            {
                ResolvePreview(properties, preview, previewDirectory, summary);
            }

            var metadata = ReadText(file, "Metadata");
            if (!string.IsNullOrWhiteSpace(metadata))
            {
                properties["ExtractedMetadata"] = Truncate(metadata, MaxPropertyText);
            }

            var crc = ReadCrc(file);
            if (crc is not null)
            {
                properties["Crc32"] = crc;
            }

            rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["id"] = $"file:{fileId}",
                ["name"] = name,
                ["parentId"] = parentRowId,
                ["type"] = "file",
                ["size"] = ClampNonNegative(ReadLong(file, "Size")),
                ["childrenCount"] = 0L,
                ["properties"] = properties
            });
            summary.Files++;
        }
    }

    private static Dictionary<string, object?> BuildEntryProperties(
        Dictionary<string, object?> row,
        string[] knownColumns,
        EasyDiskCatalogImportSummary summary)
    {
        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        CopyText(properties, row, "Alias", "Alias");
        CopyText(properties, row, "Type", "FormatType");
        CopyText(properties, row, "Path", "OriginalPath");
        CopyText(properties, row, "Comment", "Comment");
        CopyDate(properties, row, "Created", "Created");
        CopyDate(properties, row, "Modified", "Modified");
        CopyDate(properties, row, "Accessed", "Accessed");

        var flags = AttributeFlags.Where(flag => ReadFlag(row, flag)).ToArray();
        if (flags.Length > 0)
        {
            properties["Attributes"] = string.Join(", ", flags);
        }

        CopyUnknownColumns(properties, row, knownColumns, summary);
        return properties;
    }

    private static List<Dictionary<string, object?>> ReadFolders(
        EasyDiskCatalogDatabase database,
        HashSet<string> tables,
        long diskId,
        CancellationToken cancellationToken)
    {
        return ReadTable(database, tables, "Folders", FolderColumns,
            "WHERE DiskID = ?1 ORDER BY ParentID;", [diskId], cancellationToken);
    }

    private static List<Dictionary<string, object?>> ReadFiles(
        EasyDiskCatalogDatabase database,
        HashSet<string> tables,
        long diskId,
        CancellationToken cancellationToken)
    {
        return ReadTable(database, tables, "Files", FileColumns,
            "WHERE DiskID = ?1;", [diskId], cancellationToken);
    }

    private static List<Dictionary<string, object?>> ReadTable(
        EasyDiskCatalogDatabase database,
        HashSet<string> tables,
        string table,
        string[] preferredColumns,
        string? whereClause,
        long[] parameters,
        CancellationToken cancellationToken)
    {
        if (!tables.Contains(table))
        {
            return [];
        }

        var actual = database.ReadColumnNames(table);
        if (actual.Count == 0)
        {
            return [];
        }

        var columns = preferredColumns.Where(column => actual.Contains(column, StringComparer.OrdinalIgnoreCase)).ToList();
        foreach (var column in actual)
        {
            if (!columns.Contains(column, StringComparer.OrdinalIgnoreCase))
            {
                columns.Add(column);
            }
        }

        return database.QueryAll(ResolveCase(tables, table) ?? table, columns, whereClause, parameters, cancellationToken);
    }

    private static string? ResolveCase(HashSet<string> tables, string table)
    {
        foreach (var candidate in tables)
        {
            if (candidate.Equals(table, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private sealed record GroupMaps(
        Dictionary<long, (string Name, long ParentId)> ById);

    private static GroupMaps ReadGroups(
        EasyDiskCatalogDatabase database,
        HashSet<string> tables,
        string table,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<long, (string Name, long ParentId)>();
        foreach (var row in ReadTable(database, tables, table, GroupColumns, null, [], cancellationToken))
        {
            var name = ReadText(row, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            map[ReadLong(row, "id")] = (name, ReadLong(row, "ParentID"));
        }

        return new GroupMaps(map);
    }

    private static string? ResolveGroupPath(GroupMaps groups, long id)
    {
        if (id == 0 || !groups.ById.TryGetValue(id, out _))
        {
            return null;
        }

        var parts = new List<string>();
        var visited = new HashSet<long>();
        var current = id;
        while (groups.ById.TryGetValue(current, out var entry) && visited.Add(current))
        {
            parts.Add(entry.Name);
            if (entry.ParentId == 0 || entry.ParentId == current)
            {
                break;
            }

            current = entry.ParentId;
        }

        parts.Reverse();
        return string.Join(" / ", parts);
    }

    private static Dictionary<long, Contact> ReadContacts(
        EasyDiskCatalogDatabase database,
        HashSet<string> tables,
        CancellationToken cancellationToken)
    {
        var contacts = new Dictionary<long, Contact>();
        foreach (var row in ReadTable(database, tables, "Contacts", ContactColumns, null, [], cancellationToken))
        {
            var name = ReadText(row, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            contacts[ReadLong(row, "id")] = new Contact(
                name,
                ReadText(row, "Address"),
                ReadText(row, "HomePhone"),
                ReadText(row, "MobilePhone"),
                ReadText(row, "Email"),
                ReadText(row, "Comment"));
        }

        return contacts;
    }

    private sealed record Contact(
        string Name, string? Address, string? HomePhone, string? MobilePhone, string? Email, string? Comment);

    private static List<LoanEntry> ReadLoans(
        EasyDiskCatalogDatabase database,
        HashSet<string> tables,
        long diskId,
        Dictionary<long, Contact> contacts,
        EasyDiskCatalogImportSummary summary,
        CancellationToken cancellationToken)
    {
        var loans = new List<LoanEntry>();
        foreach (var row in ReadTable(database, tables, "Loan", LoanColumns,
                     "WHERE DiskID = ?1 ORDER BY id;", [diskId], cancellationToken))
        {
            var contactId = ReadLong(row, "ContactID");
            contacts.TryGetValue(contactId, out var contact);
            var returned = ReadFlag(row, "Returned");
            loans.Add(new LoanEntry(
                ReadText(row, "StartDate"),
                ReadText(row, "ReturnDate"),
                contact?.Name,
                contact?.HomePhone,
                contact?.MobilePhone,
                contact?.Email,
                returned,
                ReadFlag(row, "Lent"),
                ReadText(row, "Comment")));

            if (returned)
            {
                summary.LoanHistoryEntries++;
            }
            else
            {
                summary.CurrentLoans++;
            }
        }

        return loans;
    }

    private sealed record LoanEntry(
        string? StartDate, string? ReturnDate, string? ContactName, string? HomePhone,
        string? MobilePhone, string? Email, bool Returned, bool Lent, string? Comment);

    private static void ApplyLoans(
        Dictionary<string, object?> properties,
        List<LoanEntry> loans)
    {
        var current = loans.FirstOrDefault(loan => !loan.Returned);
        if (current is not null)
        {
            CopyValue(properties, "Loan.Contact", current.ContactName);
            CopyValue(properties, "Loan.StartDate", current.StartDate);
            CopyValue(properties, "Loan.ReturnDate", current.ReturnDate);
            CopyValue(properties, "Loan.Comment", current.Comment);
            properties["Loan.Status"] = "current";
        }

        var history = loans.Where(loan => loan.Returned).ToList();
        if (history.Count > 0 || (current is null && loans.Count > 0))
        {
            var lines = loans.Select(loan =>
            {
                var contact = string.IsNullOrWhiteSpace(loan.ContactName) ? "Unknown contact" : loan.ContactName;
                var dates = $"{loan.StartDate ?? "?"} -> {loan.ReturnDate ?? "?"}";
                var status = loan.Returned ? "returned" : "current";
                return $"{contact} | {dates} | {status}";
            });
            properties["LoanHistory"] = Truncate(string.Join(Environment.NewLine, lines), MaxPropertyText);
        }
    }

    private static string BuildContactDirectory(Dictionary<long, Contact> contacts)
    {
        const int maxContacts = 50;
        var lines = contacts.Values
            .OrderBy(contact => contact.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxContacts)
            .Select(contact =>
            {
                var details = new[] { contact.HomePhone, contact.MobilePhone, contact.Email, contact.Address }
                    .Where(part => !string.IsNullOrWhiteSpace(part));
                return details.Any()
                    ? $"{contact.Name} ({string.Join(", ", details)})"
                    : contact.Name;
            });
        var directory = string.Join(Environment.NewLine, lines);
        if (contacts.Count > maxContacts)
        {
            directory += $"{Environment.NewLine}… and {contacts.Count - maxContacts} more contacts";
        }

        return directory;
    }

    private static string? ResolvePreviewDirectory(string filePath, string? previewBaseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(previewBaseDirectory) && Directory.Exists(previewBaseDirectory))
        {
            return previewBaseDirectory;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var candidate = Path.Combine(directory,
            $"{Path.GetFileNameWithoutExtension(filePath)} Previews");
        return Directory.Exists(candidate) ? candidate : null;
    }

    private static void ResolvePreview(
        Dictionary<string, object?> properties,
        string reference,
        string? previewDirectory,
        EasyDiskCatalogImportSummary summary)
    {
        properties["PreviewReference"] = reference;
        if (string.IsNullOrWhiteSpace(previewDirectory))
        {
            properties["PreviewStatus"] = "unresolved";
            summary.PreviewsUnresolved++;
            return;
        }

        var fileName = reference.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        fileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            properties["PreviewStatus"] = "missing";
            summary.PreviewsMissing++;
            return;
        }

        var fullPath = Path.Combine(previewDirectory, fileName);
        if (File.Exists(fullPath))
        {
            properties["PreviewStatus"] = "found";
            properties["PreviewFile"] = fullPath;
            summary.PreviewsFound++;
        }
        else
        {
            properties["PreviewStatus"] = "missing";
            summary.PreviewsMissing++;
        }
    }

    private static void CopyUnknownColumns(
        Dictionary<string, object?> properties,
        Dictionary<string, object?> row,
        string[] knownColumns,
        EasyDiskCatalogImportSummary summary)
    {
        foreach (var (key, value) in row)
        {
            if (value is null || knownColumns.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            properties[$"Extension.{key}"] = Truncate(
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, MaxColumnText);
            summary.UnknownColumns++;
        }
    }

    private static bool IsRootParent(long parentId) => parentId is 0 or -1;

    private static string? DisplayName(Dictionary<string, object?> row)
    {
        var name = ReadText(row, "Name");
        return string.IsNullOrWhiteSpace(name) ? ReadText(row, "Alias") : name;
    }

    private static string? ReadText(Dictionary<string, object?> row, string column)
    {
        foreach (var (key, value) in row)
        {
            if (!key.Equals(column, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return value switch
            {
                null => null,
                string text => string.IsNullOrWhiteSpace(text) ? null : text,
                long integer => integer.ToString(CultureInfo.InvariantCulture),
                double number => number.ToString(CultureInfo.InvariantCulture),
                byte[] bytes => Convert.ToBase64String(bytes),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture)
            };
        }

        return null;
    }

    private static long ReadLong(Dictionary<string, object?> row, string column)
    {
        var text = ReadText(row, column);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0L;
    }

    private static long ReadLong(Dictionary<string, object?> row, string first, string second) =>
        ReadLong(row, first) + ReadLong(row, second);

    private static bool ReadFlag(Dictionary<string, object?> row, string column)
    {
        var text = ReadText(row, column);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Trim() switch
        {
            "1" => true,
            "0" => false,
            var other when other.Equals("true", StringComparison.OrdinalIgnoreCase) => true,
            var other when other.Equals("yes", StringComparison.OrdinalIgnoreCase) => true,
            _ => false
        };
    }

    private static string? ReadCrc(Dictionary<string, object?> row)
    {
        var text = ReadText(row, "CRC");
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && uint.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
        {
            return hex.ToString(CultureInfo.InvariantCulture);
        }

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return unchecked((uint)integer).ToString(CultureInfo.InvariantCulture);
        }

        return trimmed;
    }

    private static void CopyText(
        Dictionary<string, object?> properties,
        Dictionary<string, object?> row,
        string column,
        string property)
    {
        var value = ReadText(row, column);
        if (!string.IsNullOrWhiteSpace(value))
        {
            properties[property] = value;
        }
    }

    private static void CopyDate(
        Dictionary<string, object?> properties,
        Dictionary<string, object?> row,
        string column,
        string property)
    {
        var value = ReadText(row, column);
        if (!string.IsNullOrWhiteSpace(value))
        {
            properties[property] = NormalizeDate(value);
        }
    }

    private static void CopyLong(
        Dictionary<string, object?> properties,
        Dictionary<string, object?> row,
        string column,
        string property)
    {
        var text = ReadText(row, column);
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            properties[property] = value;
        }
    }

    private static void CopyValue(Dictionary<string, object?> properties, string property, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            properties[property] = value;
        }
    }

    private static string NormalizeDate(string value)
    {
        string[] formats = ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd"];
        if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        }

        return value;
    }

    private static long ClampNonNegative(long value) => Math.Max(0L, value);

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "…[truncated]";
    }
}
