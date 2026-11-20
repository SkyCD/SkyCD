using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.AudioCd.Sources;

/// <summary>Shared HTTP details for the online sources: identification, bounded bodies, and timeouts.</summary>
internal static class AudioCdHttp
{
    public const string UserAgent = "SkyCD.AudioCd/1.0.0 ( https://sourceforge.net/projects/skycd/ )";

    public const int MaxBodyBytes = 1024 * 1024;

    /// <summary>
    /// Links a per-request deadline to the caller's token. The caller owns and must dispose the result;
    /// a zero or negative timeout means only the caller's cancellation applies.
    /// </summary>
    public static CancellationTokenSource? BeginTimedRequest(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return null;
        }

        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        return linked;
    }

    public static async Task<string?> ReadBoundedStringAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new System.IO.MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        var charset = response.Content.Headers.ContentType?.CharSet;
        var encoding = System.Text.Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                encoding = System.Text.Encoding.GetEncoding(charset.Trim('"'));
            }
            catch (ArgumentException)
            {
                // Fall back to UTF-8 for an unknown or unusable charset.
            }
        }

        return encoding.GetString(buffer.ToArray());
    }

    public static async Task<byte[]?> ReadBoundedBytesAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > MaxBodyBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new System.IO.MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
