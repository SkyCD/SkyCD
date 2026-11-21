using System;
using System.Collections.Generic;
using Org.BouncyCastle.Crypto.Digests;

namespace SkyCD.Plugin.Hash.Parsing;

/// <summary>
/// Incremental Tiger Tree Hash over 1024-byte leaves. Leaves are <c>Tiger(0x00 || data)</c> and internal
/// nodes <c>Tiger(0x01 || left || right)</c>; an unpaired hash is promoted unchanged until it finds a
/// sibling, exactly as the THEX specification requires.
/// </summary>
internal sealed class TigerTreeHasher
{
    internal const int LeafBytes = 1024;
    internal const int DigestBytes = 24;

    private readonly byte[] leaf = new byte[LeafBytes];
    private readonly List<byte[]?> levels = [];
    private int leafLength;
    private bool sawAnyData;

    public void Append(byte[] data, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset + count, data.Length);

        if (count == 0)
        {
            return;
        }

        sawAnyData = true;
        while (count > 0)
        {
            var take = Math.Min(count, LeafBytes - leafLength);
            Buffer.BlockCopy(data, offset, leaf, leafLength, take);
            leafLength += take;
            offset += take;
            count -= take;

            if (leafLength == LeafBytes)
            {
                AddHash(HashLeaf(leaf, 0, LeafBytes));
                leafLength = 0;
            }
        }
    }

    /// <summary>Folds the pending subtrees into the root and returns its 24-byte Tiger digest.</summary>
    public byte[] Finish()
    {
        // An empty file is one zero-length leaf, so the result is Tiger(0x00) rather than an empty value.
        if (leafLength > 0 || !sawAnyData)
        {
            AddHash(HashLeaf(leaf, 0, leafLength));
            leafLength = 0;
        }

        byte[]? root = null;
        foreach (var hash in levels)
        {
            if (hash is null)
            {
                continue;
            }

            root = root is null ? hash : HashNode(hash, root);
        }

        return root ?? throw new InvalidOperationException("The hash tree has no root.");
    }

    private void AddHash(byte[] hash)
    {
        var carry = hash;
        for (var level = 0; ; level++)
        {
            if (levels.Count == level)
            {
                levels.Add(carry);
                return;
            }

            var existing = levels[level];
            if (existing is null)
            {
                levels[level] = carry;
                return;
            }

            levels[level] = null;
            carry = HashNode(existing, carry);
        }
    }

    private static byte[] HashLeaf(byte[] data, int offset, int count)
    {
        var digest = new TigerDigest();
        digest.Update(0x00);
        digest.BlockUpdate(data, offset, count);
        var result = new byte[DigestBytes];
        digest.DoFinal(result, 0);
        return result;
    }

    private static byte[] HashNode(byte[] left, byte[] right)
    {
        var digest = new TigerDigest();
        digest.Update(0x01);
        digest.BlockUpdate(left, 0, left.Length);
        digest.BlockUpdate(right, 0, right.Length);
        var result = new byte[DigestBytes];
        digest.DoFinal(result, 0);
        return result;
    }
}
