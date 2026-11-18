using System;
using Org.BouncyCastle.Crypto.Digests;

namespace SkyCD.Plugin.Hash;

/// <summary>
/// Incremental eD2k hash. A source of at most one 9500 KiB chunk is hashed with MD4 directly; larger
/// sources are split into those chunks, each chunk is hashed with MD4, and the concatenated chunk digests
/// are hashed again. No trailing empty chunk is added for a source that ends on a chunk boundary.
/// </summary>
internal sealed class Ed2kHasher
{
    internal const int ChunkBytes = 9_728_000;

    private readonly bool singleChunk;
    private readonly MD4Digest whole = new();
    private readonly MD4Digest outer = new();
    private MD4Digest part = new();
    private int partLength;

    public Ed2kHasher(long length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        singleChunk = length <= ChunkBytes;
    }

    public void Append(byte[] data, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset + count, data.Length);

        if (singleChunk)
        {
            whole.BlockUpdate(data, offset, count);
            return;
        }

        while (count > 0)
        {
            var take = Math.Min(count, ChunkBytes - partLength);
            part.BlockUpdate(data, offset, take);
            partLength += take;
            offset += take;
            count -= take;

            if (partLength == ChunkBytes)
            {
                EndChunk();
            }
        }
    }

    public byte[] Finish()
    {
        var result = new byte[16];
        if (singleChunk)
        {
            whole.DoFinal(result, 0);
            return result;
        }

        if (partLength > 0)
        {
            EndChunk();
        }

        outer.DoFinal(result, 0);
        return result;
    }

    private void EndChunk()
    {
        var digest = new byte[16];
        part.DoFinal(digest, 0);
        outer.BlockUpdate(digest, 0, digest.Length);
        part = new MD4Digest();
        partLength = 0;
    }
}
