using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;

namespace SkyCD.Plugin.Runtime.Managers;

/// <summary>
/// Shared catalog facade for file-format metadata and read/write operations.
/// </summary>
public sealed class FileFormatManager(IEnumerable<IFileFormatPluginCapability> fileFormatProviders)
{
    public IReadOnlyList<FileFormatDescriptor> GetOpenFormats()
    {
        return fileFormatProviders
            .Select(static capability => capability.SupportedFormat)
            .Where(static format => format.CanRead)
            .DistinctBy(static format => format.FormatId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static format => format.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<FileFormatDescriptor> GetSaveFormats()
    {
        return fileFormatProviders
            .Select(static capability => capability.SupportedFormat)
            .Where(static format => format.CanWrite)
            .DistinctBy(static format => format.FormatId, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static format => format.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IFileFormatPluginCapability GetInstanceFor(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        foreach (var capability in fileFormatProviders)
        {
            if (capability.SupportedFormat.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return capability;
            }
        }

        throw new InvalidOperationException("Unsupported file format");
    }

    public string GetPreferredSaveExtension(string fallback = "scd")
    {
        return GetSaveFormats()
                   .SelectMany(static format => format.Extensions)
                   .Select(static extension => extension.Trim().TrimStart('.'))
                   .FirstOrDefault(static extension => !string.IsNullOrWhiteSpace(extension))
               ?? fallback;
    }

    public IReadOnlyList<FileFormatDescriptor> GetReadableFormats()
    {
        return GetOpenFormats();
    }

    public IReadOnlyList<FileFormatDescriptor> GetWritableFormats()
    {
        return GetSaveFormats();
    }

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
    {
        var formatHandler = ResolveHandler(request.FormatId, request.FileName);
        if (!formatHandler.SupportedFormat.CanRead)
        {
            throw new InvalidOperationException($"Format '{formatHandler.SupportedFormat.FormatId}' is not readable.");
        }

        var result = await formatHandler.ReadAsync(request, cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Error ?? "Read operation failed.");
        }

        return result;
    }

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
    {
        var formatHandler = ResolveHandler(request.FormatId, request.FileName);
        if (!formatHandler.SupportedFormat.CanWrite)
        {
            throw new InvalidOperationException($"Format '{formatHandler.SupportedFormat.FormatId}' is read-only.");
        }

        var result = await formatHandler.WriteAsync(request, cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Error ?? "Write operation failed.");
        }

        return result;
    }

    private IFileFormatPluginCapability ResolveHandler(string? formatId, string? fileName)
    {
        if (!string.IsNullOrWhiteSpace(formatId))
        {
            var byFormatId = fileFormatProviders.FirstOrDefault(capability =>
                capability.SupportedFormat.FormatId.Equals(formatId, StringComparison.OrdinalIgnoreCase));
            if (byFormatId is not null)
            {
                return byFormatId;
            }
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            return GetInstanceFor(fileName);
        }

        throw new InvalidOperationException("Unable to resolve file format handler.");
    }
}
