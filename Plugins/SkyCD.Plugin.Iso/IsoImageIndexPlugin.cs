using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Iso9660;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.Iso;

public sealed class IsoImageIndexPlugin : IFileFormatPluginCapability
{
    private readonly IIsoEntryReader _entryReader;

    public IsoImageIndexPlugin() : this(new DiscUtilsIsoEntryReader())
    {
    }

    public IsoImageIndexPlugin(IIsoEntryReader entryReader)
    {
        _entryReader = entryReader;
    }

    public FileFormatDescriptor SupportedFormat =>
        new FileFormatDescriptor(
            "skycd-iso",
            "ISO Image Index",
            [".iso"],
            CanRead: true,
            CanWrite: false,
            MimeType: "application/x-iso9660-image");

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

