using DiscUtils.Iso9660;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Abstractions.Lifecycle;

namespace SkyCD.Plugin.Iso;

public sealed class IsoImageIndexPlugin : IPlugin, IFileFormatPluginCapability
{
    private readonly IIsoEntryReader _entryReader;

    public IsoImageIndexPlugin() : this(new DiscUtilsIsoEntryReader())
    {
    }

    public IsoImageIndexPlugin(IIsoEntryReader entryReader)
    {
        _entryReader = entryReader;
    }

    public string Id => "skycd.plugin.iso";
    public string Name => "ISO Index Plugin";
    public Version Version => new(1, 0, 0);
    public Version MinHostVersion => new(3, 0, 0);
    public string Description => "Example plugin that indexes ISO image entries.";

    public IReadOnlyCollection<FileFormatDescriptor> SupportedFormats =>
    [
        new FileFormatDescriptor(
            "skycd-iso",
            "ISO Image Index",
            [".iso"],
            CanRead: true,
            CanWrite: false,
            MimeType: "application/x-iso9660-image")
    ];

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "ISO image index plugin is read-only."
        });
    }

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = _entryReader.ReadEntries(request.Source);
            var rows = entries
                .Select(entry => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = entry.IsDirectory ? "folder" : "file",
                    ["fullPath"] = entry.Path.Replace('\\', '/'),
                    ["name"] = entry.Path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty,
                    ["sizeBytes"] = entry.SizeBytes.ToString(),
                    ["modifiedUtc"] = entry.ModifiedUtc?.ToString("O")
                })
                .OrderBy(row => row["fullPath"]?.ToString(), StringComparer.Ordinal)
                .ThenBy(row => row["kind"]?.ToString(), StringComparer.Ordinal)
                .ToList();

            return Task.FromResult(new FileFormatReadResult
            {
                Success = true,
                Payload = rows
            });
        }
        catch (Exception exception)
        {
            return Task.FromResult(new FileFormatReadResult
            {
                Success = false,
                Error = exception.Message
            });
        }
    }
}

public interface IIsoEntryReader
{
    IReadOnlyCollection<IsoEntryInfo> ReadEntries(Stream source);
}

public sealed record IsoEntryInfo(
    string Path,
    bool IsDirectory,
    long SizeBytes,
    DateTime? ModifiedUtc);

public sealed class DiscUtilsIsoEntryReader : IIsoEntryReader
{
    public IReadOnlyCollection<IsoEntryInfo> ReadEntries(Stream source)
    {
        using var reader = new CDReader(source, joliet: true);
        var entries = new List<IsoEntryInfo>();
        TraverseDirectory(reader, path: string.Empty, entries);
        return entries;
    }

    private static void TraverseDirectory(CDReader reader, string path, List<IsoEntryInfo> entries)
    {
        foreach (var directory in reader.GetDirectories(path))
        {
            var normalized = directory.Replace('\\', '/');
            entries.Add(new IsoEntryInfo(normalized, IsDirectory: true, SizeBytes: 0, ModifiedUtc: null));
            TraverseDirectory(reader, normalized, entries);
        }

        foreach (var file in reader.GetFiles(path))
        {
            var normalized = file.Replace('\\', '/');
            entries.Add(new IsoEntryInfo(
                normalized,
                IsDirectory: false,
                SizeBytes: reader.GetFileLength(normalized),
                ModifiedUtc: reader.GetLastWriteTimeUtc(normalized)));
        }
    }
}
