using System;
using System.IO;
using System.Threading;
using SkyCD.Plugin.FileFormat.AbeMeda.Native.Exceptions;
using SkyCD.Plugin.FileFormat.AbeMeda.Native.Models;

namespace SkyCD.Plugin.FileFormat.AbeMeda.Native.Parsing;

/// <summary>
/// Orchestrates generation dispatch, header validation, and record parsing.
/// The extended generation is recognized by suffix but rejected with an
/// actionable diagnostic because its item layout has not been verified.
/// </summary>
public static class AbeMedaNativeCatalogReader
{
    public static AbeMedaNativeCatalog Read(
        Stream source,
        string? fileNameOrPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        var generation = AbeMedaNativeGenerationDetector.Detect(fileNameOrPath);
        if (generation == AbeMedaNativeGeneration.Extended)
        {
            throw new AbeMedaNativeUnsupportedGenerationException("extended");
        }

        var header = AbeMedaNativeHeaderReader.Read(source, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var scan = AbeMedaNativeEntryReader.Read(source, header, cancellationToken);
        return new AbeMedaNativeCatalog(
            generation,
            header,
            source.Length,
            scan.Entries,
            scan.Warnings,
            scan.SkippedCount);
    }
}
