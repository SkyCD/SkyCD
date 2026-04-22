using SkyCD.Plugin.Abstractions.Capabilities.Cli;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Host.FileFormats;

namespace SkyCD.Cli;

internal sealed class CliHostPluginApi(FileFormatRoutingService fileFormatRoutingService) : IHostCliApi
{
    public IReadOnlyList<FileFormatDescriptor> GetReadableFormats()
    {
        return fileFormatRoutingService.GetOpenFormats()
            .Select(route => new FileFormatDescriptor(
                route.FormatId,
                route.DisplayName,
                route.Extensions,
                route.CanRead,
                route.CanWrite,
                route.MimeType))
            .ToList();
    }

    public IReadOnlyList<FileFormatDescriptor> GetWritableFormats()
    {
        return fileFormatRoutingService.GetSaveFormats()
            .Select(route => new FileFormatDescriptor(
                route.FormatId,
                route.DisplayName,
                route.Extensions,
                route.CanRead,
                route.CanWrite,
                route.MimeType))
            .ToList();
    }

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
    {
        return fileFormatRoutingService.ReadAsync(request, cancellationToken);
    }

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
    {
        return fileFormatRoutingService.WriteAsync(request, cancellationToken);
    }
}
