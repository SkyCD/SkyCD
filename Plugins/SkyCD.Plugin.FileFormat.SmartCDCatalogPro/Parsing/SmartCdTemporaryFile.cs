using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.FileFormat.SmartCDCatalogPro.Exceptions;

namespace SkyCD.Plugin.FileFormat.SmartCDCatalogPro.Parsing;

/// <summary>
/// Materializes the supplied stream into a private temporary file. The source is never
/// written to and no lock file is created next to it.
/// </summary>
internal static class SmartCdTemporaryFile
{
    internal const long MaxDatabaseBytes = 2L * 1024 * 1024 * 1024;

    public static async Task<string> CopyAsync(Stream source, CancellationToken cancellationToken)
    {
        if (source.CanSeek && source.Length - source.Position > MaxDatabaseBytes)
        {
            throw new SmartCdCatalogProInvalidFormatException(
                $"the database exceeds the supported {MaxDatabaseBytes} byte limit");
        }

        var path = Path.Combine(Path.GetTempPath(), $"skycd-scdc-{Guid.NewGuid():N}.scdc");
        try
        {
            await using (var target = new FileStream(
                             path,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaxDatabaseBytes)
                    {
                        throw new SmartCdCatalogProInvalidFormatException(
                            $"the database exceeds the supported {MaxDatabaseBytes} byte limit");
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            return path;
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
