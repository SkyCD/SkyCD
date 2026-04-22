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

    public IReadOnlyCollection<FileFormatDescriptor> SupportedFormats =>
    [
        new(
            "skycd-iso",
            "ISO Image Index",
            [".iso"],
            true,
            false,
            "application/x-iso9660-image")
    ];

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "ISO image index plugin is read-only."
        });
    }

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var entries = _entryReader.ReadEntries(request.Source);
            var rows = entries
                .Select(entry => new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = entry.IsDirectory ? "folder" : "file",
                    ["fullPath"] = entry.Path.Replace('\\', '/'),
                    ["name"] = entry.Path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ??
                               string.Empty,
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

    public PluginDescriptor Descriptor => new(
        "skycd.plugin.iso",
        "ISO Index Plugin",
        new Version(1, 0, 0),
        new Version(3, 0, 0),
        "Example plugin that indexes ISO image entries.");

    public ValueTask OnLoadAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask OnInitializeAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask OnActivateAsync(PluginLifecycleContext context, CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
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
        using var reader = new CDReader(source, true);
        var entries = new List<IsoEntryInfo>();
        TraverseDirectory(reader, string.Empty, entries);
        return entries;
    }

    private static void TraverseDirectory(CDReader reader, string path, List<IsoEntryInfo> entries)
    {
        foreach (var directory in reader.GetDirectories(path))
        {
            var normalized = directory.Replace('\\', '/');
            entries.Add(new IsoEntryInfo(normalized, true, 0, null));
            TraverseDirectory(reader, normalized, entries);
        }

        foreach (var file in reader.GetFiles(path))
        {
            var normalized = file.Replace('\\', '/');
            entries.Add(new IsoEntryInfo(
                normalized,
                false,
                reader.GetFileLength(normalized),
                reader.GetLastWriteTimeUtc(normalized)));
        }
    }
}