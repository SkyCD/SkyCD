using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Cathy.Exceptions;

namespace SkyCD.Plugin.Cathy;

/// <summary>
/// File format capability plugin for importing Cathy disk catalog (.caf) files.
/// </summary>
public sealed class CathyPlugin : IFileFormatPluginCapability, IContentAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "cathy-caf",
            DisplayName: "Cathy Catalog (.caf)",
            Extensions: [".caf"],
            MimeTypes: ["application/x-cathy-catalog"],
            CanRead: true,
            CanWrite: false);

    /// <summary>
    /// Inspects the stream content to verify if it represents a Cathy (.caf) catalog file.
    /// Preserves stream position.
    /// </summary>
    public bool CanHandleContent(Stream stream, string? fileName = null)
    {
        return CathyParser.IsCathyStream(stream);
    }

    public Task<FileFormatReadResult> ReadAsync(
        FileFormatReadRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            request.Progress?.Report(10);
            var catalog = CathyParser.Parse(request.Source);
            request.Progress?.Report(50);

            var (flatEntries, summary) = CathyHierarchyBuilder.BuildHierarchy(catalog);
            request.Progress?.Report(80);

            if (flatEntries.Count == 0 || (flatEntries.Count == 1 && catalog.Entries.Count == 0))
            {
                throw new CathyEmptyCatalogException();
            }

            var payloadRows = new List<Dictionary<string, object?>>(flatEntries.Count);
            foreach (var node in flatEntries)
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["fullPath"] = node.FullPath,
                    ["size"] = node.Size,
                    ["sizeBytes"] = node.Size,
                    ["kind"] = node.IsDirectory ? "folder" : "file",
                    ["icon"] = node.IsDirectory ? "folder" : "file"
                };

                if (!string.IsNullOrWhiteSpace(node.ModifiedDate))
                {
                    row["modifiedDate"] = node.ModifiedDate;
                }

                payloadRows.Add(row);
            }

            var document = new CathyCatalogDocument(payloadRows, summary);
            request.Progress?.Report(100);

            return Task.FromResult(new FileFormatReadResult
            {
                Success = true,
                Payload = document
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new FileFormatReadResult
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    public Task<FileFormatWriteResult> WriteAsync(
        FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new FileFormatWriteResult
        {
            Success = false,
            Error = "Cathy (.caf) catalog export is not supported. Format is read-only."
        });
    }
}
