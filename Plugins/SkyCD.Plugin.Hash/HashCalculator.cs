using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Hash.Exceptions;

namespace SkyCD.Plugin.Hash;

/// <summary>Digests produced by one pass over a source.</summary>
internal sealed record HashResult(string Ed2k, string Tth, string Sha256);

/// <summary>
/// Reads the source once, in bounded buffers, and feeds the same bytes to all three algorithms. The
/// source is never buffered in full, so memory use does not grow with file size.
/// </summary>
internal sealed class HashCalculator : IDisposable
{
    internal const int BufferBytes = 128 * 1024;

    private readonly IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private readonly TigerTreeHasher tth = new();
    private readonly Ed2kHasher ed2k;

    private HashCalculator(long length) => ed2k = new Ed2kHasher(length);

    public static async Task<HashResult> ComputeAsync(
        IMediaSource source,
        long length,
        IProgress<MediaProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        using var calculator = new HashCalculator(length);
        var buffer = new byte[BufferBytes];
        var offset = 0L;
        var lastFraction = 0d;

        while (offset < length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var count = (int)Math.Min(buffer.Length, length - offset);
            var read = await source.ReadAsync(offset, count, cancellationToken).ConfigureAwait(false);
            if (read.Length != count)
            {
                throw new HashSourceTruncatedException(read.Length, count);
            }

            read.Span.CopyTo(buffer);
            calculator.Append(buffer, 0, count);
            offset += count;

            if (progress is not null)
            {
                var fraction = (double)offset / length;
                if (fraction - lastFraction >= 0.01 || offset == length)
                {
                    lastFraction = fraction;
                    progress.Report(new MediaProgress(fraction));
                }
            }
        }

        return calculator.Finish();
    }

    public void Dispose() => sha256.Dispose();

    private void Append(byte[] data, int offset, int count)
    {
        sha256.AppendData(data, offset, count);
        tth.Append(data, offset, count);
        ed2k.Append(data, offset, count);
    }

    private HashResult Finish()
    {
        var sha = sha256.GetHashAndReset();
        return new HashResult(
            ToLowerHex(ed2k.Finish()),
            Base32Encode(tth.Finish()),
            ToLowerHex(sha));
    }

    private static string ToLowerHex(byte[] data) => Convert.ToHexStringLower(data);

    /// <summary>RFC 4648 base32 without padding, the form P2P clients use for TTH values.</summary>
    internal static string Base32Encode(ReadOnlySpan<byte> data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var result = new StringBuilder((data.Length * 8 + 4) / 5);
        var bits = 0;
        var value = 0;

        foreach (var b in data)
        {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                result.Append(alphabet[(value >> (bits - 5)) & 0x1F]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            result.Append(alphabet[(value << (5 - bits)) & 0x1F]);
        }

        return result.ToString();
    }
}
