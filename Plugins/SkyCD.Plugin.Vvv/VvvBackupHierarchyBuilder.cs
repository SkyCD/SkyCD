using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SkyCD.Plugin.Vvv.Exceptions;

namespace SkyCD.Plugin.Vvv;

/// <summary>
/// Maps the verified VVV schema onto catalog rows: one media node per
/// physical volume, folder nodes from PATHS, file rows from FILES, and a
/// separate virtual-folder tree from VIRTUAL_PATHS/VIRTUAL_FILES. Node ids
/// derive from volume names and database ids, so repeated imports of the same
/// backup stay stable.
/// </summary>
internal static class VvvBackupHierarchyBuilder
{
    internal static VvvBackupReportDocument Build(VvvBackupSnapshot snapshot, string? sourceName)
    {
        var builder = new Builder(snapshot, CleanSourceName(sourceName));
        return builder.Build();
    }

    private static string CleanSourceName(string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return "VVV backup";
        }

        var trimmed = sourceName.Trim();
        var name = trimmed[(trimmed.LastIndexOfAny(['\\', '/']) + 1)..];
        return string.IsNullOrWhiteSpace(name) ? trimmed : name;
    }

    private sealed class Builder(VvvBackupSnapshot snapshot, string sourceName)
    {
        private const string VirtualRootNodeId = "vvvbk:virtual";

        private readonly List<Dictionary<string, object?>> _rows = [];
        private readonly HashSet<string> _usedNodeIds = new(StringComparer.Ordinal);
        private readonly HashSet<long> _resolvingPaths = [];
        private readonly HashSet<long> _resolvingVirtualPaths = [];
        private readonly List<string> _notes = [];
        private readonly Dictionary<long, string> _volumeNodeIds = [];
        private readonly Dictionary<long, VvvBackupVolume> _volumesById = [];
        private readonly Dictionary<long, string> _volumeNames = [];
        private readonly Dictionary<long, string> _volumeScopes = [];
        private readonly Dictionary<long, VvvBackupPath> _pathsById = [];
        private readonly Dictionary<long, string> _pathNodeIds = [];
        private readonly Dictionary<long, VvvBackupFile> _folderFilesByPathId = [];
        private readonly Dictionary<long, VvvBackupFile> _filesById = [];
        private readonly Dictionary<long, string?> _pathPhysicalPaths = [];
        private readonly Dictionary<long, string> _virtualPathNodeIds = [];
        private readonly Dictionary<long, long?> _virtualPathPrimaryParents = [];
        private readonly Dictionary<long, long> _virtualFolderLinkParents = [];
        private readonly HashSet<(long ParentId, long TargetId)> _virtualFolderReferences = [];
        private int _volumes;
        private int _folders;
        private int _files;
        private int _virtualFolders;
        private int _virtualFiles;
        private int _skipped;

        internal VvvBackupReportDocument Build()
        {
            IndexSnapshot();
            AddVolumes();
            foreach (var path in snapshot.Paths.OrderBy(path => path.Id))
            {
                EnsurePathNode(path.Id, depth: 0);
            }

            AddFiles();
            AddVirtualTree();

            var summary = new VvvBackupImportSummary
            {
                SchemaVersion = snapshot.SchemaVersion,
                SourceName = sourceName,
                Volumes = _volumes,
                Folders = _folders,
                Files = _files,
                VirtualFolders = _virtualFolders,
                VirtualFiles = _virtualFiles,
                SkippedRows = _skipped
            };
            summary.Notes.AddRange(_notes);
            return new VvvBackupReportDocument(_rows, summary);
        }

        private void IndexSnapshot()
        {
            foreach (var volume in snapshot.Volumes)
            {
                _volumesById[volume.Id] = volume;
                _volumeNames[volume.Id] = Clean(volume.Name) ?? $"Volume {volume.Id}";
            }

            foreach (var path in snapshot.Paths)
            {
                _pathsById.TryAdd(path.Id, path);
            }

            foreach (var file in snapshot.Files.OrderBy(file => file.Id))
            {
                _filesById.TryAdd(file.Id, file);
                if (file.FolderPathId is long folderPathId)
                {
                    _folderFilesByPathId.TryAdd(folderPathId, file);
                }
            }

            foreach (var link in snapshot.VirtualFiles
                         .Where(link => link.VirtualPathFileId is not null)
                         .OrderBy(link => link.VirtualPathId)
                         .ThenBy(link => link.FileId))
            {
                _virtualFolderLinkParents.TryAdd(link.VirtualPathFileId!.Value, link.VirtualPathId);
            }
        }

        private void AddVolumes()
        {
            foreach (var volume in snapshot.Volumes)
            {
                var name = _volumeNames[volume.Id];
                var scope = $"{StableHash(name)}-v{volume.Id.ToString(CultureInfo.InvariantCulture)}";
                _volumeScopes[volume.Id] = scope;

                var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["VvvScope"] = "Backup",
                    ["VvvVolume"] = name,
                    ["VvvVolumeId"] = volume.Id.ToString(CultureInfo.InvariantCulture),
                    ["VvvSource"] = sourceName,
                    ["VvvSchemaVersion"] = snapshot.SchemaVersion.ToString(CultureInfo.InvariantCulture)
                };
                Put(properties, "PhysicalPath", volume.PhysicalPath);
                Put(properties, "Description", volume.Description);
                PutDate(properties, "CatalogDate", volume.CatalogDate);
                PutDate(properties, "LastUpdateDate", volume.LastUpdateDate);

                var nodeId = NextNodeId($"vvvbk:{scope}:v");
                _volumeNodeIds[volume.Id] = nodeId;
                AddRow(nodeId, null, "media", name, 0, properties);
                _volumes++;
            }
        }

        private string? EnsurePathNode(long pathId, int depth)
        {
            if (_pathNodeIds.TryGetValue(pathId, out var existing))
            {
                return existing;
            }

            if (!_pathsById.TryGetValue(pathId, out var path))
            {
                return null;
            }

            if (!_volumeNodeIds.TryGetValue(path.VolumeId, out var volumeNodeId))
            {
                Skip($"Path {pathId} references missing volume {path.VolumeId}.");
                return null;
            }

            if (!_resolvingPaths.Add(pathId))
            {
                Note($"Path {pathId} has a circular parent chain; it was attached to its volume.");
                return volumeNodeId;
            }

            try
            {
                var parentNodeId = volumeNodeId;
                if (path.ParentId is long parentId)
                {
                    if (depth >= VvvBackupLimits.MaxHierarchyDepth)
                    {
                        Note($"Path {pathId} exceeds the hierarchy depth limit and was attached to its volume.");
                    }
                    else
                    {
                        var resolved = EnsurePathNode(parentId, depth + 1);
                        if (resolved is null)
                        {
                            Note($"Path {pathId} references missing parent path {parentId}; it was attached to its volume.");
                        }
                        else
                        {
                            parentNodeId = resolved;
                        }
                    }
                }

                _folderFilesByPathId.TryGetValue(pathId, out var folderFile);
                var name = Clean(path.Name) ?? Clean(folderFile?.Name) ?? $"Folder {pathId}";
                var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["VvvVolume"] = _volumeNames[path.VolumeId],
                    ["VvvPathId"] = pathId.ToString(CultureInfo.InvariantCulture)
                };
                Put(properties, "Description", path.Description ?? folderFile?.Description);
                PutDate(properties, "LastModified", folderFile?.Modified);
                Put(properties, "PhysicalPath", PhysicalPathForPath(pathId));
                properties["VvvScope"] = "Backup";

                var nodeId = NextNodeId($"vvvbk:{_volumeScopes[path.VolumeId]}:p{pathId}");
                _pathNodeIds[pathId] = nodeId;
                AddRow(nodeId, parentNodeId, "folder", name, 0, properties);
                _folders++;
                return nodeId;
            }
            finally
            {
                _resolvingPaths.Remove(pathId);
            }
        }

        private void AddFiles()
        {
            foreach (var file in snapshot.Files)
            {
                if (file.FolderPathId is not null)
                {
                    continue;
                }

                if (file.PathId is not long pathId)
                {
                    _skipped++;
                    continue;
                }

                if (!_pathNodeIds.TryGetValue(pathId, out var parentNodeId))
                {
                    Skip($"File {file.Id} references missing path {pathId}.");
                    continue;
                }

                var name = Clean(file.Name);
                if (name is null)
                {
                    Skip($"File {file.Id} has no name.");
                    continue;
                }

                var size = file.Size;
                if (size < 0)
                {
                    Note($"File {file.Id} has a negative size; it was stored as 0.");
                    size = 0;
                }

                var volumeId = _pathsById[pathId].VolumeId;
                var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["VvvScope"] = "Backup",
                    ["VvvVolume"] = _volumeNames[volumeId],
                    ["VvvFileId"] = file.Id.ToString(CultureInfo.InvariantCulture),
                    ["VvvPathId"] = pathId.ToString(CultureInfo.InvariantCulture)
                };
                Put(properties, "Extension", file.Extension);
                Put(properties, "Description", file.Description);
                PutDate(properties, "LastModified", file.Modified);
                Put(properties, "PhysicalPath", PhysicalPathForFile(pathId, name));
                AddAudioProperties(properties, file.Id);

                var nodeId = NextNodeId($"vvvbk:{_volumeScopes[volumeId]}:f{file.Id}");
                AddRow(nodeId, parentNodeId, "file", name, size, properties);
                _files++;
            }
        }

        private void AddVirtualTree()
        {
            if (snapshot.VirtualPaths.Count == 0 && snapshot.VirtualFiles.Count == 0)
            {
                return;
            }

            var virtualPathsById = new Dictionary<long, VvvBackupVirtualPath>();
            foreach (var path in snapshot.VirtualPaths)
            {
                virtualPathsById.TryAdd(path.Id, path);
            }

            var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["VvvScope"] = "Virtual",
                ["VvvSource"] = sourceName,
                ["VvvSchemaVersion"] = snapshot.SchemaVersion.ToString(CultureInfo.InvariantCulture),
                ["Comment"] = "Virtual folder tree restored from the VVV backup."
            };
            var rootNodeId = NextNodeId(VirtualRootNodeId);
            AddRow(rootNodeId, null, "media", "Virtual Folders", 0, properties);

            foreach (var path in snapshot.VirtualPaths.OrderBy(path => path.Id))
            {
                EnsureVirtualPathNode(path.Id, rootNodeId, virtualPathsById, depth: 0);
            }

            AddVirtualFolderReferences(virtualPathsById);
            AddVirtualFiles(virtualPathsById);
        }

        private string EnsureVirtualPathNode(
            long pathId,
            string rootNodeId,
            Dictionary<long, VvvBackupVirtualPath> virtualPathsById,
            int depth)
        {
            if (_virtualPathNodeIds.TryGetValue(pathId, out var existing))
            {
                return existing;
            }

            if (!virtualPathsById.TryGetValue(pathId, out var path))
            {
                return rootNodeId;
            }

            if (!_resolvingVirtualPaths.Add(pathId))
            {
                Note($"Virtual folder {pathId} has a circular parent chain; it was attached to the virtual root.");
                return rootNodeId;
            }

            try
            {
                long? primaryParentId = path.ParentId;
                if (primaryParentId is null && _virtualFolderLinkParents.TryGetValue(pathId, out var linkParent))
                {
                    primaryParentId = linkParent;
                }

                if (primaryParentId == pathId || (primaryParentId is long candidate && !virtualPathsById.ContainsKey(candidate)))
                {
                    primaryParentId = null;
                }

                var parentNodeId = rootNodeId;
                if (primaryParentId is long parentId)
                {
                    if (depth >= VvvBackupLimits.MaxHierarchyDepth)
                    {
                        Note($"Virtual folder {pathId} exceeds the hierarchy depth limit and was attached to the virtual root.");
                        primaryParentId = null;
                    }
                    else
                    {
                        parentNodeId = EnsureVirtualPathNode(parentId, rootNodeId, virtualPathsById, depth + 1);
                    }
                }

                var name = Clean(path.Name) ?? PhysicalFolderName(path.PhysicalPathId) ?? $"Virtual Folder {pathId}";
                var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["VvvScope"] = "Virtual",
                    ["VvvVirtualPathId"] = pathId.ToString(CultureInfo.InvariantCulture)
                };
                Put(properties, "PhysicalPath", PhysicalPathForPath(path.PhysicalPathId));
                Put(properties, "Description", _pathsById.TryGetValue(path.PhysicalPathId ?? 0, out var physicalPath)
                    ? physicalPath.Description
                    : null);

                var nodeId = NextNodeId($"vvvbk:vp{pathId}");
                _virtualPathNodeIds[pathId] = nodeId;
                _virtualPathPrimaryParents[pathId] = primaryParentId;
                AddRow(nodeId, parentNodeId, "folder", name, 0, properties);
                _virtualFolders++;
                return nodeId;
            }
            finally
            {
                _resolvingVirtualPaths.Remove(pathId);
            }
        }

        private void AddVirtualFolderReferences(Dictionary<long, VvvBackupVirtualPath> virtualPathsById)
        {
            foreach (var link in snapshot.VirtualFiles
                         .Where(link => link.VirtualPathFileId is not null)
                         .OrderBy(link => link.VirtualPathId)
                         .ThenBy(link => link.FileId))
            {
                var targetId = link.VirtualPathFileId!.Value;
                if (!virtualPathsById.TryGetValue(targetId, out var target) ||
                    !_virtualPathNodeIds.TryGetValue(targetId, out _) ||
                    !_virtualPathNodeIds.TryGetValue(link.VirtualPathId, out var parentNodeId))
                {
                    Skip($"Virtual folder link {link.FileId} references a missing virtual folder.");
                    continue;
                }

                if (link.VirtualPathId == targetId ||
                    (_virtualPathPrimaryParents.TryGetValue(targetId, out var primaryParent) && primaryParent == link.VirtualPathId))
                {
                    continue;
                }

                if (!_virtualFolderReferences.Add((link.VirtualPathId, targetId)))
                {
                    continue;
                }

                var name = Clean(target.Name) ?? PhysicalFolderName(target.PhysicalPathId) ?? $"Virtual Folder {targetId}";
                var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["VvvScope"] = "Virtual",
                    ["VvvVirtualPathId"] = targetId.ToString(CultureInfo.InvariantCulture),
                    ["VvvVirtualLink"] = "true"
                };
                Put(properties, "PhysicalPath", PhysicalPathForPath(target.PhysicalPathId));
                var nodeId = NextNodeId($"vvvbk:vp{link.VirtualPathId}:ref{targetId}");
                AddRow(nodeId, parentNodeId, "folder", name, 0, properties);
                _virtualFolders++;
                Note($"Virtual folder \"{name}\" is placed in more than one location; only its primary location contains nested entries.");
            }
        }

        private void AddVirtualFiles(Dictionary<long, VvvBackupVirtualPath> virtualPathsById)
        {
            foreach (var link in snapshot.VirtualFiles
                         .Where(link => link.PhysicalFileId is not null)
                         .OrderBy(link => link.VirtualPathId)
                         .ThenBy(link => link.FileId))
            {
                if (!virtualPathsById.ContainsKey(link.VirtualPathId) ||
                    !_virtualPathNodeIds.TryGetValue(link.VirtualPathId, out var parentNodeId))
                {
                    Skip($"Virtual file link {link.FileId} references missing virtual folder {link.VirtualPathId}.");
                    continue;
                }

                _filesById.TryGetValue(link.FileId, out var file);
                if (file is null && link.PhysicalFileId is long physicalFileId)
                {
                    _filesById.TryGetValue(physicalFileId, out file);
                }

                if (file is null)
                {
                    Skip($"Virtual file link {link.FileId} references a missing file.");
                    continue;
                }

                var name = Clean(file.Name);
                if (name is null)
                {
                    Skip($"Virtual file link {link.FileId} references a file without a name.");
                    continue;
                }

                var properties = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["VvvScope"] = "Virtual",
                    ["VvvVirtualPathId"] = link.VirtualPathId.ToString(CultureInfo.InvariantCulture),
                    ["VvvFileId"] = file.Id.ToString(CultureInfo.InvariantCulture),
                    ["VvvPhysicalFileId"] = (link.PhysicalFileId ?? file.Id).ToString(CultureInfo.InvariantCulture)
                };
                Put(properties, "Extension", file.Extension);
                Put(properties, "Description", file.Description);
                PutDate(properties, "LastModified", file.Modified);
                Put(properties, "PhysicalPath", file.PathId is long pathId
                    ? PhysicalPathForFile(pathId, name)
                    : null);
                AddAudioProperties(properties, file.Id);

                var nodeId = NextNodeId($"vvvbk:vp{link.VirtualPathId}:f{link.FileId}");
                AddRow(nodeId, parentNodeId, "file", name, Math.Max(file.Size, 0), properties);
                _virtualFiles++;
            }
        }

        private void AddAudioProperties(Dictionary<string, object?> properties, long fileId)
        {
            if (!snapshot.AudioByFileId.TryGetValue(fileId, out var audio))
            {
                return;
            }

            Put(properties, "Title", audio.Title);
            Put(properties, "Artist", audio.Artist);
            Put(properties, "Album", audio.Album);
            Put(properties, "Genre", audio.Genre);
            Put(properties, "Comment", audio.Comment);
            PutNumber(properties, "Year", audio.Year);
            PutNumber(properties, "TrackNumber", audio.TrackNumber);
            PutNumber(properties, "Bitrate", audio.Bitrate);
            PutNumber(properties, "SampleRate", audio.SampleRate);
            PutNumber(properties, "Channels", audio.Channels);
            PutNumber(properties, "AudioDuration", audio.LengthSeconds);
        }

        private string? PhysicalFolderName(long? physicalPathId)
        {
            if (physicalPathId is not long pathId || !_pathsById.TryGetValue(pathId, out var path))
            {
                return null;
            }

            var name = Clean(path.Name);
            if (name is not null)
            {
                return name;
            }

            return _folderFilesByPathId.TryGetValue(pathId, out var folderFile) ? Clean(folderFile.Name) : null;
        }

        private string? PhysicalPathForFile(long pathId, string fileName)
        {
            var parent = PhysicalPathForPath(pathId);
            if (parent is null)
            {
                return null;
            }

            var separator = parent.Contains('\\') ? '\\' : '/';
            return parent.TrimEnd('\\', '/') + separator + fileName;
        }

        private string? PhysicalPathForPath(long? pathId)
        {
            if (pathId is not long id)
            {
                return null;
            }

            if (_pathPhysicalPaths.TryGetValue(id, out var cached))
            {
                return cached;
            }

            if (!_pathsById.TryGetValue(id, out var path) ||
                !_volumesById.TryGetValue(path.VolumeId, out var volume))
            {
                _pathPhysicalPaths[id] = null;
                return null;
            }

            var segments = new List<string>();
            var visited = new HashSet<long>();
            var current = path;
            while (true)
            {
                var segment = Clean(current.Name);
                if (segment is not null)
                {
                    segments.Add(segment);
                }

                if (current.ParentId is not long parentId ||
                    !visited.Add(current.Id) ||
                    !_pathsById.TryGetValue(parentId, out current))
                {
                    break;
                }
            }

            segments.Reverse();
            var root = Clean(volume.PhysicalPath);
            string? result;
            if (root is null)
            {
                result = segments.Count == 0 ? null : string.Join('/', segments);
            }
            else
            {
                var separator = root.Contains('\\') ? '\\' : '/';
                var combined = root.TrimEnd('\\', '/');
                foreach (var segment in segments)
                {
                    combined += separator + segment.Trim('\\', '/');
                }

                result = combined;
            }

            _pathPhysicalPaths[id] = result;
            return result;
        }

        private string NextNodeId(string candidate)
        {
            if (_rows.Count >= VvvBackupLimits.MaxCatalogNodes)
            {
                throw new VvvBackupLimitExceededException("the backup maps to more than 2,000,000 catalog nodes");
            }

            var nodeId = candidate;
            var suffix = 2;
            while (!_usedNodeIds.Add(nodeId))
            {
                nodeId = $"{candidate}-{suffix.ToString(CultureInfo.InvariantCulture)}";
                suffix++;
            }

            return nodeId;
        }

        private void AddRow(
            string nodeId,
            string? parentId,
            string kind,
            string name,
            long size,
            Dictionary<string, object?> properties)
        {
            if (_rows.Count >= VvvBackupLimits.MaxCatalogNodes)
            {
                throw new VvvBackupLimitExceededException("the backup maps to more than 2,000,000 catalog nodes");
            }

            _rows.Add(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["nodeId"] = nodeId,
                ["parentId"] = parentId,
                ["kind"] = kind,
                ["name"] = name,
                ["sizeBytes"] = size,
                ["properties"] = properties
            });
        }

        private void Skip(string note)
        {
            _skipped++;
            Note(note);
        }

        private void Note(string note)
        {
            if (_notes.Count < 50)
            {
                _notes.Add(note);
            }
            else if (_notes.Count == 50)
            {
                _notes.Add("Additional import notes were omitted.");
            }
        }

        private static string StableHash(string value)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant();
        }

        private static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static void Put(Dictionary<string, object?> properties, string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                properties[key] = value;
            }
        }

        private static void PutDate(Dictionary<string, object?> properties, string key, DateTime? value)
        {
            if (value is not null)
            {
                properties[key] = value.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
        }

        private static void PutNumber(Dictionary<string, object?> properties, string key, int? value)
        {
            if (value is not null)
            {
                properties[key] = value.Value.ToString(CultureInfo.InvariantCulture);
            }
        }

        private static void PutNumber(Dictionary<string, object?> properties, string key, short? value)
        {
            if (value is not null)
            {
                properties[key] = value.Value.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
