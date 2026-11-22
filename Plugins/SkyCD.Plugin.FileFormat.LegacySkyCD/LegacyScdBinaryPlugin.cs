using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.FileFormat.LegacySkyCD.Export;
using SkyCD.Plugin.FileFormat.LegacySkyCD.Models;
using SkyCD.Plugin.FileFormat.LegacySkyCD.Parsing;

namespace SkyCD.Plugin.FileFormat.LegacySkyCD;

/// <summary>Exports SCD catalogs using the VB6 binary layout.</summary>
public sealed class LegacyScdBinaryPlugin : IFileFormatPluginCapability, IDataLossAwareFileFormatCapability
{
    public FileFormatDescriptor SupportedFormat =>
        new(
            FormatId: "legacy-scd-binary",
            DisplayName: "SkyCD Legacy Binary Format",
            Extensions: [".scd"],
            MimeTypes: ["application/vnd.skycd.scd"],
            CanRead: false,
            CanWrite: true);

    public bool PreservesCatalogScope => false;

    public FileFormatDataLossAssessment AssessDataLoss(FileFormatDataLossRequest request) =>
        new(FileFormatDataLossAssessmentBuilder.AssessEntries(request,
            FileFormatEntryFieldSupport.Kind | FileFormatEntryFieldSupport.Size));

    public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new FileFormatReadResult { Success = false, Error = "Binary SCD import is provided by the combined SCD reader." });

    public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        var catalog = ResolveCatalog(request.Payload);
        if (catalog is null)
        {
            return Task.FromResult(new FileFormatWriteResult { Success = false, Error = "Payload must be LegacyScdCatalog." });
        }

        try
        {
            LegacyScdBinaryWriter.Write(request.Target, catalog, request.Progress, cancellationToken);
            return Task.FromResult(new FileFormatWriteResult { Success = true });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Task.FromResult(new FileFormatWriteResult { Success = false, Error = exception.Message });
        }
    }

    private static LegacyScdCatalog? ResolveCatalog(object? payload)
    {
        if (payload is LegacyScdCatalog typed)
        {
            return typed;
        }

        if (payload is not IEnumerable<Dictionary<string, object?>> rows)
        {
            return null;
        }

        var catalog = new LegacyScdCatalog();
        foreach (var entry in LegacyRowPaths.Build(rows))
        {
            catalog.Entries.Add(new LegacyScdEntry
            {
                Path = entry.Path,
                SizeBytes = entry.SizeBytes,
                IsDirectory = entry.IsDirectory
            });
        }

        return catalog;
    }
}
