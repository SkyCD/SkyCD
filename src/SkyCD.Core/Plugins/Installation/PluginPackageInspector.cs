using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Installation.Exceptions;

namespace SkyCD.Core.Plugins.Installation;

/// <summary>
/// Extracts and validates a NuGet package without executing plugin code.
/// </summary>
/// <remarks>
/// The archive is rejected when it is not a readable zip, contains entry names that are absolute, rooted,
/// traverse outside the package root, use link attributes (symlink/hardlink), repeat a path, or exceed the entry
/// count, per-entry size or total uncompressed size limits. Only <c>lib/&lt;tfm&gt;/</c> and
/// <c>tools/&lt;tfm&gt;/any/</c> content is extracted; package scripts, build targets and metadata folders are ignored.
/// </remarks>
internal sealed class PluginPackageInspector
{
    public const int MaxEntries = 4096;

    public const long MaxEntryBytes = 64L * 1024 * 1024;

    public const long MaxTotalUncompressedBytes = 256L * 1024 * 1024;

    private static readonly string[] ExcludedPrefixes = ["_rels/", "package/", "build/", "buildTransitive/"];

    private static readonly string[] ExcludedRootFiles = ["[Content_Types].xml", ".rels", ".signature.p7s"];

    /// <summary>Validates every entry and extracts plugin content below <paramref name="targetDirectory"/>.</summary>
    public PluginPackageContent Extract(string packageId, string packagePath, string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(packagePath);
        }
        catch (InvalidDataException)
        {
            throw new PluginArchiveCorruptException(packageId, "the archive is not a valid zip package");
        }
        catch (IOException)
        {
            throw new PluginArchiveCorruptException(packageId, "the archive could not be read");
        }

        using (archive)
        {
            var entries = ValidateEntries(packageId, archive);
            var nuspecEntry = SelectNuspec(packageId, entries);
            var manifest = ReadManifest(packageId, nuspecEntry, entries);
            var extractedFiles = ExtractContent(packageId, entries, targetDirectory);

            return new PluginPackageContent
            {
                Manifest = manifest,
                ExtractedFiles = extractedFiles,
                ManifestFileName = nuspecEntry.FullName
            };
        }
    }

    private static List<ZipArchiveEntry> ValidateEntries(string packageId, ZipArchive archive)
    {
        if (archive.Entries.Count > MaxEntries)
        {
            throw new PluginArchiveCorruptException(packageId,
                $"the archive has more than {MaxEntries} entries");
        }

        var validated = new List<ZipArchiveEntry>(archive.Entries.Count);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;

        foreach (var entry in archive.Entries)
        {
            var path = NormalizeEntryPath(packageId, entry.FullName);
            if (path.Length == 0)
            {
                continue;
            }

            if (!seenPaths.Add(path))
            {
                throw new PluginUnsafeArchivePathException(packageId, entry.FullName);
            }

            if (IsSymbolicLink(entry))
            {
                throw new PluginUnsafeArchivePathException(packageId, entry.FullName);
            }

            var isDirectory = path.EndsWith('/');
            if (!isDirectory)
            {
                if (entry.Length > MaxEntryBytes)
                {
                    throw new PluginArchiveCorruptException(packageId,
                        $"entry '{entry.FullName}' exceeds the {MaxEntryBytes} byte limit");
                }

                totalBytes += entry.Length;
                if (totalBytes > MaxTotalUncompressedBytes)
                {
                    throw new PluginArchiveCorruptException(packageId,
                        $"the archive expands beyond the {MaxTotalUncompressedBytes} byte limit");
                }
            }

            validated.Add(entry);
        }

        return validated;
    }

    private static string NormalizeEntryPath(string packageId, string entryName)
    {
        if (string.IsNullOrEmpty(entryName) ||
            entryName.IndexOf('\0') >= 0 ||
            entryName.Contains('\\', StringComparison.Ordinal) ||
            entryName.Contains(':', StringComparison.Ordinal) ||
            entryName.StartsWith('/'))
        {
            throw new PluginUnsafeArchivePathException(packageId, entryName);
        }

        var segments = entryName.Split('/');
        var normalized = new List<string>(segments.Length);
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var isLast = index == segments.Length - 1;
            if (!isLast && segment.Length == 0)
            {
                throw new PluginUnsafeArchivePathException(packageId, entryName);
            }

            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                throw new PluginUnsafeArchivePathException(packageId, entryName);
            }

            if (isLast && segment.Length == 0)
            {
                break;
            }

            normalized.Add(segment);
        }

        if (normalized.Count == 0)
        {
            return string.Empty;
        }

        return string.Join('/', normalized) + (entryName.EndsWith('/') ? "/" : string.Empty);
    }

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        // ZipArchive exposes the external attributes; Unix symlinks store the S_IFLNK mode bits in the high 16 bits.
        var unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
        return (unixMode & 0xF000) == 0xA000;
    }

    private static ZipArchiveEntry SelectNuspec(string packageId, List<ZipArchiveEntry> entries)
    {
        var manifests = entries
            .Where(static entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) &&
                                   entry.FullName.IndexOf('/') < 0)
            .ToList();

        return manifests.Count switch
        {
            1 => manifests[0],
            > 1 => throw new PluginArchiveCorruptException(packageId, "the archive contains several manifests"),
            _ => throw new PluginArchiveCorruptException(packageId, "the archive contains no manifest")
        };
    }

    private static PluginPackageManifest ReadManifest(string packageId, ZipArchiveEntry entry,
        IReadOnlyList<ZipArchiveEntry> entries)
    {
        XDocument document;
        try
        {
            using var stream = entry.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 1024 * 1024
            });
            document = XDocument.Load(reader);
        }
        catch (XmlException)
        {
            throw new PluginArchiveCorruptException(packageId, "the manifest is not valid XML");
        }
        catch (IOException)
        {
            throw new PluginArchiveCorruptException(packageId, "the manifest could not be read");
        }

        var metadata = document.Root is { } root && root.Name.LocalName == "package"
            ? root.Elements().FirstOrDefault(static element => element.Name.LocalName == "metadata")
            : null;

        var id = metadata?.Elements().FirstOrDefault(static element => element.Name.LocalName == "id")?.Value;
        var version = metadata?.Elements().FirstOrDefault(static element => element.Name.LocalName == "version")?.Value;

        // The plugin entry assembly is declared in the optional <files> section (a sibling of <metadata>) as a
        // 'lib/<tfm>/<file>' source with an empty target, so it keeps its package-relative path. Older packages
        // without the section fall back to the first lib/<tfm>/ assembly file.
        var entryAssembly = ReadDeclaredEntryAssembly(document) ?? entries
            .Select(static entry => entry.FullName.Replace('\\', '/'))
            .Where(static path =>
                path.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                path.Count(static character => character == '/') >= 2)
            .Select(static path => path[(path.LastIndexOf('/') + 1)..])
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(version))
        {
            throw new PluginArchiveCorruptException(packageId, "the manifest does not declare an id and version");
        }

        return new PluginPackageManifest(
            PackageIdentity.Normalize(id),
            PackageIdentity.Normalize(version),
            string.IsNullOrWhiteSpace(entryAssembly) ? null : entryAssembly.Trim());
    }

    private static string? ReadDeclaredEntryAssembly(XDocument document) => document.Root?
        .Elements()
        .FirstOrDefault(static element => element.Name.LocalName == "files")?
        .Elements()
        .Where(static element => element.Name.LocalName == "file")
        .Select(static element => (string?)element.Attribute("src"))
        .Where(static src => !string.IsNullOrWhiteSpace(src))
        .Select(static src => src!.Trim().Replace('\\', '/'))
        .FirstOrDefault(static src =>
            src.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) &&
            src.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
            src.Count(static character => character == '/') >= 2);

    /// <summary>
    /// Extracts lib/&lt;tfm&gt;/ content (or tools/&lt;tfm&gt;/any/ when a package has no lib folder) into
    /// <paramref name="targetDirectory"/>. Assembly discovery scans the folder recursively, so private
    /// dependencies keep their sub-folders and the entry assembly stays resolvable by path.
    /// </summary>
    private static IReadOnlyList<string> ExtractContent(string packageId, List<ZipArchiveEntry> entries,
        string targetDirectory)
    {
        var files = entries
            .Where(static entry => !entry.FullName.EndsWith('/') && entry.Length > 0)
            .Select(entry => (Entry: entry, Path: entry.FullName))
            .Where(static item =>
                !ExcludedRootFiles.Contains(item.Path, StringComparer.OrdinalIgnoreCase) &&
                !ExcludedPrefixes.Any(prefix => item.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var libFiles = files
            .Where(static item => item.Path.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) &&
                                  item.Path.Count(static character => character == '/') >= 2)
            .ToList();
        var selected = libFiles.Count > 0
            ? libFiles
            : files
                .Where(static item => item.Path.StartsWith("tools/", StringComparison.OrdinalIgnoreCase) &&
                                      item.Path.Count(static character => character == '/') >= 3 &&
                                      item.Path.Split('/')[2].Equals("any", StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (selected.Count == 0)
        {
            throw new PluginPackageAssemblyException(packageId,
                "the package contains no lib/<tfm>/ or tools/<tfm>/any/ content");
        }

        var extracted = new List<string>(selected.Count);
        var root = Path.GetFullPath(targetDirectory);
        var rootPrefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);

        foreach (var (entry, path) in selected)
        {
            var relativePath = path.Replace('/', Path.DirectorySeparatorChar);
            var destinationPath = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!destinationPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new PluginUnsafeArchivePathException(packageId, entry.FullName);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            try
            {
                using var source = entry.Open();
                using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None);
                long written = 0;
                var buffer = new byte[81920];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    written += read;
                    if (written > MaxEntryBytes)
                    {
                        throw new PluginArchiveCorruptException(packageId,
                            $"entry '{entry.FullName}' exceeds the {MaxEntryBytes} byte limit");
                    }

                    destination.Write(buffer, 0, read);
                }
            }
            catch (InvalidDataException)
            {
                throw new PluginArchiveCorruptException(packageId,
                    $"entry '{entry.FullName}' cannot be decompressed");
            }

            extracted.Add(relativePath);
        }

        return extracted;
    }
}

internal sealed record PluginPackageManifest(string PackageId, string Version, string? EntryAssembly);

internal sealed record PluginPackageContent
{
    public required PluginPackageManifest Manifest { get; init; }

    /// <summary>Extracted files as paths relative to the extraction directory, using the platform separator.</summary>
    public required IReadOnlyList<string> ExtractedFiles { get; init; }

    public required string ManifestFileName { get; init; }
}
