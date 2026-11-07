using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Exceptions;
using SkyCD.Plugin.Abstractions;

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
        foreach (var capability in fileFormatProviders.OrderByDescending(capability =>
                     capability.SupportedFormat.Extensions.Where(extension => MatchesExtension(fileName, extension))
                         .Select(extension => extension.Length).DefaultIfEmpty(0).Max()))
        {
            if (capability.SupportedFormat.Extensions
                .Any(extension => MatchesExtension(fileName, extension)))
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
            return formats.Any(format => format.FormatId.Equals(explicitFormatId, StringComparison.OrdinalIgnoreCase))
                ? explicitFormatId
                : throw new FileFormatHandlerResolutionException();
        }

        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new FileFormatHandlerResolutionException();
        }

        var byExtension = formats
            .OrderByDescending(format => format.Extensions.Where(candidate => MatchesExtension(path, candidate))
                .Select(candidate => candidate.Length).DefaultIfEmpty(0).Max())
            .FirstOrDefault(format => format.Extensions.Any(candidate => MatchesExtension(path, candidate)));

        return byExtension is null ? throw new UnsupportedFileFormatException(path) : byExtension.FormatId;
    }

    public async Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        var formatHandler = ResolveHandler(request.FormatId, request.FileName);
        if (!formatHandler.SupportedFormat.CanRead)
        {
            throw new FileFormatNotReadableException(formatHandler.SupportedFormat.FormatId);
        }

        FileFormatReadResult result;
        try
        {
            result = await formatHandler.ReadAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PluginFailure.Publish(formatHandler.GetType(), "read", exception);
            throw;
        }
        if (!result.Success)
        {
            var failure = new FileFormatReadFailedException(result.Error);
            PluginFailure.Publish(formatHandler.GetType(), "read", failure);
            throw failure;
        }

        return result;
    }

    public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var formatHandler = ResolveHandler(request.FormatId, request.FileName);
        if (!formatHandler.SupportedFormat.CanWrite)
        {
            throw new FileFormatReadOnlyException(formatHandler.SupportedFormat.FormatId);
        }

        FileFormatWriteResult result;
        try
        {
            result = await formatHandler.WriteAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PluginFailure.Publish(formatHandler.GetType(), "write", exception);
            throw;
        }

        if (!result.Success)
        {
            var failure = new FileFormatWriteFailedException(result.Error);
            PluginFailure.Publish(formatHandler.GetType(), "write", failure);
            throw failure;
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

        return !string.IsNullOrWhiteSpace(fileName)
            ? GetInstanceFor(fileName)
            : throw new FileFormatHandlerResolutionException();
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

    private static bool MatchesExtension(string fileName, string extension)
    {
        var normalized = NormalizeExtension(extension);
        return normalized.Length > 1 && fileName.EndsWith(normalized, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var trimmed = extension.Trim();
        if (trimmed.StartsWith("*.", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }
        else if (trimmed.StartsWith("*", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        return trimmed.StartsWith(".", StringComparison.Ordinal) ? trimmed : $".{trimmed}";
    }
}
