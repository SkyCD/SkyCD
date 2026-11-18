using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using SkyCD.Plugin.SmartCdCatalogPro.Exceptions;

namespace SkyCD.Plugin.SmartCdCatalogPro;

/// <summary>
/// Maps the application-enforced Smart CD Catalog PRO relationships onto SkyCD's flat
/// node contract. Cached counts and cached path strings are treated as advisory; the
/// hierarchy is rebuilt from IDs only.
/// </summary>
internal static class SmartCdCatalogProHierarchyBuilder
{
    private const string SourceTableCds = "CDs";
    private const string SourceTableElements = "Elements";

    public static IReadOnlyList<Dictionary<string, object?>> Build(
        SmartCdCatalogProSnapshot snapshot,
        IProgress<int>? progress,
        CancellationToken cancellationToken,
        out SmartCdCatalogProImportSummary summary)
    {
        var diagnostics = new List<string>();
        cancellationToken.ThrowIfCancellationRequested();

        var cds = ParseCds(snapshot.Cds);
        var paths = ParsePaths(snapshot.Paths, cds);
        var elements = ParseElements(snapshot.Elements, paths);
        var ownerByPath = BuildPathOwners(elements, paths);
        ValidateFolderHierarchy(elements, paths, ownerByPath);

        var categories = ParseClassifications(snapshot.Categories, "Categories");
        var boxes = ParseClassifications(snapshot.Boxes, "Boxes");
        var borrows = ParseClassifications(snapshot.Borrows, "Borrows");
        var loans = ParseLoans(snapshot.LoanHistory);
        var loansByCd = loans
            .GroupBy(loan => loan.CdId)
            .ToDictionary(group => group.Key, group => group.OrderBy(loan => loan.Start ?? DateTime.MinValue).ToList());

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(75);

        var rows = new List<Dictionary<string, object?>>(cds.Count + elements.Count);
        foreach (var (cdId, cd) in cds.OrderBy(pair => pair.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(BuildMediaRootRow(
                cdId,
                cd,
                categories,
                boxes,
                borrows,
                loansByCd,
                snapshot.AccessMode,
                diagnostics));
        }

        var folderCount = 0;
        var fileCount = 0;
        var processed = 0;
        foreach (var element in elements.Values.OrderBy(item => item.IsFolder ? 0 : 1).ThenBy(item => item.Id))
        {
            if (++processed % 1000 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var containingPath = paths[element.PathId];
            var cdId = containingPath.CdId;
            var parentId = ownerByPath.TryGetValue(containingPath.Id, out var ownerElementId)
                ? FolderNodeId(cdId, ownerElementId)
                : MediaNodeId(cdId);

            rows.Add(BuildElementRow(element, containingPath, parentId, cdId, diagnostics));
            if (element.IsFolder)
            {
                folderCount++;
            }
            else
            {
                fileCount++;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(90);

        summary = new SmartCdCatalogProImportSummary(
            MediaRootCount: cds.Count,
            FolderCount: folderCount,
            FileCount: fileCount,
            CategoryCount: categories.Count,
            BoxCount: boxes.Count,
            BorrowerCount: borrows.Count,
            LoanHistoryCount: loans.Count,
            Diagnostics: diagnostics);

        return rows;
    }

    private static Dictionary<int, SmartCdRow> ParseCds(IReadOnlyList<SmartCdRow> source)
    {
        var cds = new Dictionary<int, SmartCdRow>();
        foreach (var row in source)
        {
            var id = RequireInt32(row, "IDCD");
            if (!cds.TryAdd(id, row))
            {
                throw new SmartCdCatalogProCorruptCatalogException($"duplicate CDs.IDCD value {id}");
            }
        }

        return cds;
    }

    private static Dictionary<int, PathInfo> ParsePaths(
        IReadOnlyList<SmartCdRow> source,
        IReadOnlyDictionary<int, SmartCdRow> cds)
    {
        var paths = new Dictionary<int, PathInfo>();
        foreach (var row in source)
        {
            var id = RequireInt32(row, "IDPATH");
            var cdId = RequireInt32(row, "IDCD");
            if (!cds.ContainsKey(cdId))
            {
                throw new SmartCdCatalogProCorruptCatalogException(
                    $"Paths.IDPATH {id} belongs to missing media root {cdId}");
            }

            var path = new PathInfo(
                id,
                cdId,
                row.GetString("Path"),
                row.GetBoolean("IsArchive") ?? false);

            if (!paths.TryAdd(id, path))
            {
                throw new SmartCdCatalogProCorruptCatalogException($"duplicate Paths.IDPATH value {id}");
            }
        }

        return paths;
    }

    private static Dictionary<int, ElementInfo> ParseElements(
        IReadOnlyList<SmartCdRow> source,
        IReadOnlyDictionary<int, PathInfo> paths)
    {
        var elements = new Dictionary<int, ElementInfo>();
        foreach (var row in source)
        {
            var id = RequireInt32(row, "ID");
            var pathId = RequireInt32(row, "IDPATH");
            if (!paths.ContainsKey(pathId))
            {
                throw new SmartCdCatalogProCorruptCatalogException(
                    $"Elements.ID {id} references missing path {pathId}");
            }

            var isFolder = row.GetBoolean("IsFolder") ?? false;
            var pathSelf = row.GetInt32("IDPATHSELF");
            if (pathSelf is not null && !isFolder)
            {
                throw new SmartCdCatalogProCorruptCatalogException(
                    $"Elements.ID {id} is a file but declares a child path");
            }

            if (pathSelf is not null && !paths.ContainsKey(pathSelf.Value))
            {
                throw new SmartCdCatalogProCorruptCatalogException(
                    $"Elements.ID {id} references missing child path {pathSelf.Value}");
            }

            var element = new ElementInfo(
                id,
                pathId,
                row.GetString("ElementName"),
                isFolder,
                pathSelf,
                row);

            if (!elements.TryAdd(id, element))
            {
                throw new SmartCdCatalogProCorruptCatalogException($"duplicate Elements.ID value {id}");
            }
        }

        return elements;
    }

    private static Dictionary<int, int> BuildPathOwners(
        IReadOnlyDictionary<int, ElementInfo> elements,
        IReadOnlyDictionary<int, PathInfo> paths)
    {
        var ownerByPath = new Dictionary<int, int>();
        foreach (var element in elements.Values)
        {
            if (element.PathSelf is not { } childPathId)
            {
                continue;
            }

            var childPath = paths[childPathId];
            var containingPath = paths[element.PathId];
            if (childPath.CdId != containingPath.CdId)
            {
                throw new SmartCdCatalogProCorruptCatalogException(
                    $"Elements.ID {element.Id} links paths from different media roots");
            }

            if (!ownerByPath.TryAdd(childPathId, element.Id))
            {
                throw new SmartCdCatalogProCorruptCatalogException(
                    $"path {childPathId} is claimed by more than one folder");
            }
        }

        return ownerByPath;
    }

    private static void ValidateFolderHierarchy(
        IReadOnlyDictionary<int, ElementInfo> elements,
        IReadOnlyDictionary<int, PathInfo> paths,
        IReadOnlyDictionary<int, int> ownerByPath)
    {
        var states = new Dictionary<int, int>();

        void Visit(int folderElementId)
        {
            if (states.TryGetValue(folderElementId, out var state))
            {
                if (state == 1)
                {
                    throw new SmartCdCatalogProCorruptCatalogException(
                        $"folder/path hierarchy contains a cycle at Elements.ID {folderElementId}");
                }

                return;
            }

            states[folderElementId] = 1;
            var folder = elements[folderElementId];
            var containingPath = paths[folder.PathId];
            if (ownerByPath.TryGetValue(containingPath.Id, out var parentFolderId))
            {
                if (parentFolderId == folderElementId)
                {
                    throw new SmartCdCatalogProCorruptCatalogException(
                        $"folder/path hierarchy contains a cycle at Elements.ID {folderElementId}");
                }

                Visit(parentFolderId);
            }

            states[folderElementId] = 2;
        }

        foreach (var folderElementId in ownerByPath.Values.Distinct())
        {
            Visit(folderElementId);
        }
    }

    private static Dictionary<int, ClassificationNode> ParseClassifications(
        IReadOnlyList<SmartCdRow> source,
        string table)
    {
        var nodes = new Dictionary<int, ClassificationNode>();
        foreach (var row in source)
        {
            var id = RequireInt32(row, table, "ID");
            var node = new ClassificationNode(
                id,
                row.GetString("Name"),
                row.GetInt32("IDPARENT"));

            if (!nodes.TryAdd(id, node))
            {
                throw new SmartCdCatalogProCorruptCatalogException($"duplicate {table}.ID value {id}");
            }
        }

        return nodes;
    }

    private static List<LoanInfo> ParseLoans(IReadOnlyList<SmartCdRow> source)
    {
        var loans = new List<LoanInfo>();
        foreach (var row in source)
        {
            var cdId = row.GetInt32("IDCD");
            if (cdId is null)
            {
                continue;
            }

            loans.Add(new LoanInfo(
                cdId.Value,
                row.GetInt32("IDB"),
                row.GetBoolean("IsLend") ?? false,
                row.GetDateTime("LoanStart"),
                row.GetDateTime("LoanEnd"),
                row.GetString("LoanComment")));
        }

        return loans;
    }

    private static Dictionary<string, object?> BuildMediaRootRow(
        int cdId,
        SmartCdRow cd,
        IReadOnlyDictionary<int, ClassificationNode> categories,
        IReadOnlyDictionary<int, ClassificationNode> boxes,
        IReadOnlyDictionary<int, ClassificationNode> borrows,
        IReadOnlyDictionary<int, List<LoanInfo>> loansByCd,
        SmartCdAccessMode? accessMode,
        List<string> diagnostics)
    {
        var name = FirstNonBlank(
            cd.GetString("FolderName"),
            cd.GetString("Label"),
            cd.GetString("Drive_Folder"),
            $"Disk {cdId}")!;

        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SourceTable"] = SourceTableCds,
            ["SourceId"] = cdId
        };

        AddString(properties, "VolumeLabel", cd.GetString("Label"));
        AddString(properties, "FileSystem", cd.GetString("FileSystem"));
        AddString(properties, "DriveType", cd.GetString("DriveType"));
        AddString(properties, "SerialNumber", cd.GetString("SN"));
        AddString(properties, "ComputerName", cd.GetString("ComputerName"));
        AddString(properties, "DriveFolder", cd.GetString("Drive_Folder"));
        AddString(properties, "FolderName", cd.GetString("FolderName"));
        AddString(properties, "Comments", cd.GetString("Comment"));
        AddString(properties, "Description", cd.GetString("Description"));
        AddDouble(properties, "TotalSize", cd.GetDouble("TotalSize"), diagnostics, $"CD {cdId} TotalSize");
        AddDouble(properties, "Capacity", cd.GetDouble("Capacity"), diagnostics, $"CD {cdId} Capacity");
        AddDouble(properties, "FreeSpace", cd.GetDouble("FreeSpace"), diagnostics, $"CD {cdId} FreeSpace");
        AddDate(properties, "CreationTime", cd.GetDateTime("CreationTime"));
        AddDate(properties, "LastAccessTime", cd.GetDateTime("LastAccessTime"));
        AddDate(properties, "LastWriteTime", cd.GetDateTime("LastWriteTime"));
        AddDate(properties, "ScanDate", cd.GetDateTime("DateScan"));
        AddBool(properties, "ReadOnly", cd.GetBoolean("ReadOnly"));
        AddBool(properties, "Archive", cd.GetBoolean("Archive"));
        AddBool(properties, "Hidden", cd.GetBoolean("Hidden"));
        AddBool(properties, "System", cd.GetBoolean("System"));
        AddBool(properties, "Compressed", cd.GetBoolean("Compressed"));
        AddBool(properties, "IsFolder", cd.GetBoolean("IsFolder"));
        AddBool(properties, "IsAudio", cd.GetBoolean("IsAudio"));
        AddInt(properties, "CachedFileCount", cd.GetInt32("ContainsOfFiles"));
        AddInt(properties, "CachedFolderCount", cd.GetInt32("ContainsOfFolders"));
        AddInt(properties, "CachedFileCountSelf", cd.GetInt32("ContainsOfFilesSelf"));
        AddInt(properties, "CachedFolderCountSelf", cd.GetInt32("ContainsOfFoldersSelf"));
        AddInt(properties, "ScannedArchives", cd.GetInt32("ScannedArchives"));
        AddInt(properties, "DiskNumber", cd.GetInt32("DiskNumber"));
        AddString(properties, "DiskCase", cd.GetString("DiskCase"));
        AddBool(properties, "IsLent", cd.GetBoolean("IsLend"));
        AddDate(properties, "LoanStart", cd.GetDateTime("LoanStart"));
        AddString(properties, "LoanComment", cd.GetString("LoanComment"));

        AddClassification(properties, "Category", "CategoryId", cd.GetInt32("IDC"), categories, cdId, "category", diagnostics);
        AddClassification(properties, "Box", "BoxId", cd.GetInt32("IDX"), boxes, cdId, "box", diagnostics);

        var borrowerId = cd.GetInt32("IDB");
        if (borrowerId is not null and not -1)
        {
            AddClassification(properties, "Borrower", "BorrowerId", borrowerId, borrows, cdId, "borrower", diagnostics);
        }

        if (loansByCd.TryGetValue(cdId, out var loans) && loans.Count > 0)
        {
            properties["LoanHistoryCount"] = loans.Count;
            properties["LoanHistory"] = string.Join(
                Environment.NewLine,
                loans.Select(FormatLoan));
        }

        if (accessMode is not null)
        {
            properties["AccessModeDefaultIsFull"] = accessMode.DefaultIsFull;
            properties["AccessModeFullPasswordSet"] = accessMode.HasFullPassword;
            properties["AccessModeReadOnlyPasswordSet"] = accessMode.HasReadOnlyPassword;
        }

        return NodeRow(
            MediaNodeId(cdId),
            parentId: null,
            kind: "media",
            name,
            sizeBytes: 0,
            properties);
    }

    private static Dictionary<string, object?> BuildElementRow(
        ElementInfo element,
        PathInfo containingPath,
        string parentId,
        int cdId,
        List<string> diagnostics)
    {
        var row = element.Row;
        var realName = row.GetString("RealName");
        var isRenamed = row.GetBoolean("IsRenamed") ?? false;
        var displayName = isRenamed && !string.IsNullOrWhiteSpace(realName)
            ? realName!
            : FirstNonBlank(element.Name, realName, $"Element {element.Id}")!;

        var kind = element.IsFolder ? "folder" : "file";
        var size = element.IsFolder ? 0 : NormalizeSize(row.GetDouble("ElementSize"), element.Id, diagnostics);
        var sourcePath = CombineSourcePath(containingPath.Path, displayName);

        var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SourceTable"] = SourceTableElements,
            ["SourceId"] = element.Id,
            ["SourcePathId"] = element.PathId,
            ["SourcePath"] = sourcePath,
            ["ElementName"] = element.Name ?? string.Empty,
            ["IsRenamed"] = isRenamed,
            ["IsFolder"] = element.IsFolder,
            ["IsArchive"] = containingPath.IsArchive
        };

        if (!string.IsNullOrWhiteSpace(realName))
        {
            properties["RealName"] = realName;
        }

        AddString(properties, "Comments", row.GetString("Comment"));
        AddString(properties, "Description", row.GetString("Description"));
        AddString(properties, "Extension", row.GetString("Ext"));
        AddDate(properties, "DateCreated", row.GetDateTime("CreationTime"));
        AddDate(properties, "LastAccessTime", row.GetDateTime("LastAccessTime"));
        AddDate(properties, "DateModified", row.GetDateTime("LastWriteTime"));
        AddBool(properties, "ReadOnly", row.GetBoolean("ReadOnly"));
        AddBool(properties, "Archive", row.GetBoolean("Archive"));
        AddBool(properties, "Hidden", row.GetBoolean("Hidden"));
        AddBool(properties, "System", row.GetBoolean("System"));
        AddBool(properties, "Compressed", row.GetBoolean("Compressed"));
        AddBool(properties, "IsAudio", row.GetBoolean("IsAudio"));
        AddShort(properties, "TrackNumber", row.GetInt16("TrackN"));
        AddDuration(properties, "Duration", row.GetDateTime("Duration"));
        AddSize(properties, "UnpackedSize", row.GetDouble("UnpackedSize"), element.Id, "UnpackedSize", diagnostics);
        AddInt(properties, "ScannedArchives", row.GetInt32("ScannedArchives"));
        AddInt(properties, "CachedFileCount", row.GetInt32("ContainsOfFiles"));
        AddInt(properties, "CachedFolderCount", row.GetInt32("ContainsOfFolders"));
        AddInt(properties, "CachedFileCountSelf", row.GetInt32("ContainsOfFilesSelf"));
        AddInt(properties, "CachedFolderCountSelf", row.GetInt32("ContainsOfFoldersSelf"));

        return NodeRow(
            element.IsFolder ? FolderNodeId(cdId, element.Id) : FileNodeId(cdId, element.Id),
            parentId,
            kind,
            displayName,
            size,
            properties);
    }

    private static Dictionary<string, object?> NodeRow(
        string nodeId,
        string? parentId,
        string kind,
        string name,
        long sizeBytes,
        Dictionary<string, object?> properties) => new(StringComparer.OrdinalIgnoreCase)
        {
            ["nodeId"] = nodeId,
            ["parentId"] = parentId,
            ["kind"] = kind,
            ["name"] = name,
            ["sizeBytes"] = sizeBytes.ToString(CultureInfo.InvariantCulture),
            ["properties"] = properties
        };

    private static void AddClassification(
        Dictionary<string, object?> properties,
        string pathKey,
        string idKey,
        int? id,
        IReadOnlyDictionary<int, ClassificationNode> nodes,
        int cdId,
        string description,
        List<string> diagnostics)
    {
        if (id is null)
        {
            return;
        }

        properties[idKey] = id.Value;
        if (nodes.TryGetValue(id.Value, out var node))
        {
            var path = ResolveClassificationPath(node, nodes);
            if (!string.IsNullOrWhiteSpace(path))
            {
                properties[pathKey] = path;
            }
        }
        else
        {
            diagnostics.Add($"CD {cdId}: {description} {id.Value} is not present in its classification table");
        }
    }

    private static string? ResolveClassificationPath(
        ClassificationNode node,
        IReadOnlyDictionary<int, ClassificationNode> nodes)
    {
        var names = new List<string>();
        var visited = new HashSet<int>();
        ClassificationNode? current = node;
        while (current is not null)
        {
            if (!visited.Add(current.Id))
            {
                throw new SmartCdCatalogProCorruptCatalogException(
                    $"classification hierarchy contains a cycle at ID {current.Id}");
            }

            if (!string.IsNullOrWhiteSpace(current.Name))
            {
                names.Add(current.Name.Trim());
            }

            current = current.ParentId is { } parentId && nodes.TryGetValue(parentId, out var parent)
                ? parent
                : null;
        }

        names.Reverse();
        return names.Count == 0 ? null : string.Join(" / ", names);
    }

    private static string FormatLoan(LoanInfo loan)
    {
        var start = loan.Start?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "?";
        var end = loan.End?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "?";
        var action = loan.IsLend ? "lent" : "returned";
        var comment = string.IsNullOrWhiteSpace(loan.Comment) ? string.Empty : $": {loan.Comment}";
        return $"{start} -> {end}: {action}{comment}";
    }

    private static long NormalizeSize(double? value, int elementId, List<string> diagnostics)
    {
        if (value is null)
        {
            return 0;
        }

        var size = value.Value;
        if (!double.IsFinite(size) || size < 0 || size != Math.Truncate(size) || size > long.MaxValue)
        {
            diagnostics.Add($"Elements.ID {elementId}: ElementSize value is not a supported byte count and was omitted");
            return 0;
        }

        return (long)size;
    }

    private static void AddSize(
        Dictionary<string, object?> properties,
        string key,
        double? value,
        int elementId,
        string column,
        List<string> diagnostics)
    {
        if (value is null)
        {
            return;
        }

        var size = value.Value;
        if (!double.IsFinite(size) || size < 0 || size != Math.Truncate(size) || size > long.MaxValue)
        {
            diagnostics.Add($"Elements.ID {elementId}: {column} value is not a supported byte count and was omitted");
            return;
        }

        properties[key] = (long)size;
    }

    private static void AddDouble(
        Dictionary<string, object?> properties,
        string key,
        double? value,
        List<string> diagnostics,
        string description)
    {
        if (value is null)
        {
            return;
        }

        if (!double.IsFinite(value.Value) || value.Value < 0)
        {
            diagnostics.Add($"{description} value is not a supported size and was omitted");
            return;
        }

        properties[key] = value.Value;
    }

    private static void AddString(Dictionary<string, object?> properties, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            properties[key] = value;
        }
    }

    private static void AddDate(Dictionary<string, object?> properties, string key, DateTime? value)
    {
        if (value is not null)
        {
            properties[key] = value.Value.ToString("O", CultureInfo.InvariantCulture);
        }
    }

    private static void AddBool(Dictionary<string, object?> properties, string key, bool? value)
    {
        if (value is not null)
        {
            properties[key] = value.Value;
        }
    }

    private static void AddInt(Dictionary<string, object?> properties, string key, int? value)
    {
        if (value is not null)
        {
            properties[key] = value.Value;
        }
    }

    private static void AddShort(Dictionary<string, object?> properties, string key, short? value)
    {
        if (value is not null)
        {
            properties[key] = value.Value;
        }
    }

    private static void AddDuration(Dictionary<string, object?> properties, string key, DateTime? value)
    {
        if (value is null)
        {
            return;
        }

        var duration = TimeSpan.FromTicks(value.Value.TimeOfDay.Ticks);
        properties[key] = duration.ToString("c", CultureInfo.InvariantCulture);
    }

    private static int RequireInt32(SmartCdRow row, string column) => RequireInt32(row, row.TableName, column);

    private static int RequireInt32(SmartCdRow row, string table, string column) =>
        row.GetInt32(column) ?? throw new SmartCdCatalogProCorruptCatalogException(
            $"{table}.{column} contains a missing or non-integer value");

    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate))?.Trim();

    private static string CombineSourcePath(string? basePath, string name)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return name;
        }

        var trimmed = basePath.TrimEnd();
        return trimmed.EndsWith('\\') || trimmed.EndsWith('/')
            ? trimmed + name
            : trimmed + "\\" + name;
    }

    private static string MediaNodeId(int cdId) => $"media:scdc:{cdId}";

    private static string FolderNodeId(int cdId, int elementId) => $"folder:scdc:{cdId}:{elementId}";

    private static string FileNodeId(int cdId, int elementId) => $"file:scdc:{cdId}:{elementId}";

    private sealed record PathInfo(int Id, int CdId, string? Path, bool IsArchive);

    private sealed record ElementInfo(
        int Id,
        int PathId,
        string? Name,
        bool IsFolder,
        int? PathSelf,
        SmartCdRow Row);

    private sealed record ClassificationNode(int Id, string? Name, int? ParentId);

    private sealed record LoanInfo(
        int CdId,
        int? BorrowerId,
        bool IsLend,
        DateTime? Start,
        DateTime? End,
        string? Comment);
}
