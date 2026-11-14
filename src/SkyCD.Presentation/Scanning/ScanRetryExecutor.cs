using System;
using System.IO;
using System.Threading;

namespace SkyCD.Presentation.Scanning;

internal static class ScanRetryExecutor
{
    public static T Execute<T>(
        ScanOperationContext context,
        string itemPath,
        string stage,
        Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(action);

        var maxRetries = context.Traversal.GetBoundedMaxRetries();
        var attempts = 0;
        Exception? lastException = null;

        while (true)
        {
            context.ThrowIfCancellationRequested();
            attempts++;

            try
            {
                return action();
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                lastException = ex;
                if (attempts > maxRetries)
                {
                    break;
                }

                context.Progress?.ReportWarning(
                    $"Transient failure during '{stage}' for '{itemPath}' (attempt {attempts}/{maxRetries + 1}): {ex.Message}",
                    itemPath: itemPath,
                    stage: stage);
                context.WaitForRetryDelay();
            }
        }

        throw new ScanRetryExhaustedException(itemPath, stage, attempts, lastException);
    }

    internal static bool IsTransient(Exception exception)
    {
        // Permanent FTP-domain failures: rejected credentials, an unsupported scheme and a
        // vanished directory must never be retried. A dropped control connection
        // (FtpConnectionException) is transient — it is the FTP disconnect that the scan has
        // to survive by reconnecting (#433).
        if (exception is FtpAuthenticationException
            or FtpSchemeNotSupportedException
            or FtpDirectoryNotFoundException)
        {
            return false;
        }

        if (exception is OperationCanceledException
            or UnauthorizedAccessException
            or PathTooLongException
            or DirectoryNotFoundException
            or FileNotFoundException
            or ArgumentException)
        {
            return false;
        }

        if (exception is TimeoutException)
        {
            return true;
        }

        if (exception is IOException)
        {
            return true;
        }

        var name = exception.GetType().Name;
        return name.Contains("Socket", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Ftp", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Network", StringComparison.OrdinalIgnoreCase);
    }
}
