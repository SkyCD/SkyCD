using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.Host.FileFormats;

/// <summary>
/// Resolves plugin file format routes and enforces read/write capability gates.
/// </summary>
public sealed class FileFormatRoutingService(PluginCatalog pluginCatalog)
{
    public IReadOnlyList<FileFormatRoute> GetOpenFormats()
    {
        return GetRoutesInternal()
            .Where(route => route.CanRead)
            .ToList();
    }

    public IReadOnlyList<FileFormatRoute> GetSaveFormats()
    {
        return GetRoutesInternal()
            .Where(route => route.CanWrite)
            .ToList();
    }

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
    {
        var capability = ResolveCapability(request.FormatId);
        var format = ResolveFormat(capability, request.FormatId);

        if (!format.CanRead)
        {
            throw new FileFormatRoutingException($"Format '{request.FormatId}' is not readable.");
        }

        var result = await capability.ReadAsync(request, cancellationToken);
        if (!result.Success)
        {
            throw new FileFormatRoutingException(result.Error ?? "Read operation failed.");
        }

        return result;
    }

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
    {
        var capability = ResolveCapability(request.FormatId);
        var format = ResolveFormat(capability, request.FormatId);

        if (!format.CanWrite)
        {
            throw new FileFormatRoutingException($"Format '{request.FormatId}' is read-only.");
        }

        var result = await capability.WriteAsync(request, cancellationToken);
        if (!result.Success)
        {
            throw new FileFormatRoutingException(result.Error ?? "Write operation failed.");
        }

        return result;
    }

    private IReadOnlyList<FileFormatRoute> GetRoutesInternal()
    {
        return pluginCatalog.Plugins
            .SelectMany(plugin =>
                plugin.Capabilities.OfType<IFileFormatPluginCapability>()
                    .SelectMany(capability =>
                        capability.SupportedFormats.Select(format => new FileFormatRoute(
                            plugin.Plugin.Descriptor.Id,
                            format.FormatId,
                            format.DisplayName,
                            format.Extensions,
                            format.CanRead,
                            format.CanWrite,
                            format.MimeType))))
            .OrderBy(route => route.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IFileFormatPluginCapability ResolveCapability(string formatId)
    {
        var capability = pluginCatalog.GetCapabilities<IFileFormatPluginCapability>()
            .FirstOrDefault(candidate => candidate.SupportedFormats.Any(format =>
                format.FormatId.Equals(formatId, StringComparison.OrdinalIgnoreCase)));

        return capability ?? throw new FileFormatRoutingException($"No plugin capability found for format '{formatId}'.");
    }

    private static FileFormatDescriptor ResolveFormat(IFileFormatPluginCapability capability, string formatId)
    {
        return capability.SupportedFormats
            .FirstOrDefault(format => format.FormatId.Equals(formatId, StringComparison.OrdinalIgnoreCase))
            ?? throw new FileFormatRoutingException($"Format descriptor not found for '{formatId}'.");
    }
}
