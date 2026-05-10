using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Exceptions;

namespace SkyCD.Plugin.Runtime.Managers;

/// <summary>
/// Shared catalog facade for file-format metadata and read/write operations.
/// </summary>
public sealed class FileFormatManager(IEnumerable<IFileFormatPluginCapability> fileFormatProviders)
{
    public FileFormatFilterCollection GetOpenFilters()
    {
        return BuildFilters(GetOpenFormats());
    }

    public FileFormatFilterCollection GetSaveFilters()
    {
        return BuildFilters(GetSaveFormats());
    }

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

        throw new UnsupportedFileFormatException(fileName);
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

    public string ResolveFormatId(string? explicitFormatId, string path, bool forWrite)
    {
        var formats = forWrite ? GetSaveFormats() : GetOpenFormats();

        if (!string.IsNullOrWhiteSpace(explicitFormatId))
        {
            return formats.Any(format => format.FormatId.Equals(explicitFormatId, StringComparison.OrdinalIgnoreCase)) ? explicitFormatId : throw new FileFormatHandlerResolutionException();
        }

        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new FileFormatHandlerResolutionException();
        }

        var byExtension = formats.FirstOrDefault(format =>
            format.Extensions.Any(candidate => candidate.Equals(extension, StringComparison.OrdinalIgnoreCase)));

        return byExtension is null ? throw new UnsupportedFileFormatException(path) : byExtension.FormatId;
    }

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
    {
        var formatHandler = ResolveHandler(request.FormatId, request.FileName);
        if (!formatHandler.SupportedFormat.CanRead)
        {
            throw new FileFormatNotReadableException(formatHandler.SupportedFormat.FormatId);
        }

        var result = await formatHandler.ReadAsync(request, cancellationToken);
        if (!result.Success)
        {
            throw new FileFormatReadFailedException(result.Error);
        }

        return result;
    }

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
    {
        var formatHandler = ResolveHandler(request.FormatId, request.FileName);
        if (!formatHandler.SupportedFormat.CanWrite)
        {
            throw new FileFormatReadOnlyException(formatHandler.SupportedFormat.FormatId);
        }

        var result = await formatHandler.WriteAsync(request, cancellationToken);

        return !result.Success ? throw new FileFormatWriteFailedException(result.Error) : result;
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

        return !string.IsNullOrWhiteSpace(fileName) ? GetInstanceFor(fileName) : throw new FileFormatHandlerResolutionException();
    }

    private static FileFormatFilterCollection BuildFilters(IReadOnlyList<FileFormatDescriptor> formats)
    {
        var filters = formats
            .Select(format => new FilePickerFileType(format.DisplayName)
            {
                Patterns = format.Extensions
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(static extension => NormalizePattern(extension))
                    .ToArray(),
                MimeTypes = format.MimeTypes
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            })
            .ToArray();

        return new FileFormatFilterCollection(filters);
    }

    private static string NormalizePattern(string extension)
    {
        var trimmed = extension.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return "*.*";
        }

        var normalized = trimmed.StartsWith('.') ? trimmed : $".{trimmed}";
        return $"*{normalized}";
    }
}
