using System;
using System.Collections.Generic;
using Org.BouncyCastle.Crypto.Digests;

namespace SkyCD.Plugin.Metadata.Hash.Tests;

/// <summary>
/// Straightforward whole-buffer reference implementations of eD2k and TTH. They share only the primitive
/// digest library with the plugin, so a streaming or tree-folding mistake in the plugin is still caught.
/// </summary>
internal static class HashReference
{
    internal const int Ed2kChunkBytes = 9_728_000;
    internal const int TthLeafBytes = 1024;

    public static string Ed2k(byte[] data)
    {
        if (data.Length <= Ed2kChunkBytes)
        {
            return Convert.ToHexStringLower(Md4(data, 0, data.Length));
        }

        var chunkDigests = new List<byte>();
        for (var offset = 0; offset < data.Length; offset += Ed2kChunkBytes)
        {
            var length = Math.Min(Ed2kChunkBytes, data.Length - offset);
            chunkDigests.AddRange(Md4(data, offset, length));
        }

        var digest = chunkDigests.ToArray();
        return Convert.ToHexStringLower(Md4(digest, 0, digest.Length));
    }

    public static string Tth(byte[] data)
    {
        var leaves = new List<byte[]>();
        for (var offset = 0; offset < data.Length; offset += TthLeafBytes)
        {
            leaves.Add(Tiger(0x00, data, offset, Math.Min(TthLeafBytes, data.Length - offset)));
        }

        if (leaves.Count == 0)
        {
            leaves.Add(Tiger(0x00, [], 0, 0));
        }

        while (leaves.Count > 1)
        {
            var next = new List<byte[]>((leaves.Count + 1) / 2);
            for (var index = 0; index < leaves.Count; index += 2)
            {
                if (index + 1 == leaves.Count)
                {
                    next.Add(leaves[index]);
                    continue;
                }

                var pair = new byte[leaves[index].Length + leaves[index + 1].Length];
                leaves[index].CopyTo(pair, 0);
                leaves[index + 1].CopyTo(pair, leaves[index].Length);
                next.Add(Tiger(0x01, pair, 0, pair.Length));
            }

            leaves = next;
        }

        return Base32(leaves[0]);
    }

    public static string Sha256(byte[] data) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(data));

    private static byte[] Md4(byte[] data, int offset, int count)
    {
        var digest = new MD4Digest();
        digest.BlockUpdate(data, offset, count);
        var result = new byte[digest.GetDigestSize()];
        digest.DoFinal(result, 0);
        return result;
    }

    private static byte[] Tiger(byte prefix, byte[] data, int offset, int count)
    {
        var digest = new TigerDigest();
        digest.Update(prefix);
        digest.BlockUpdate(data, offset, count);
        var result = new byte[digest.GetDigestSize()];
        digest.DoFinal(result, 0);
        return result;
    }

    private static string Base32(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new System.Text.StringBuilder((data.Length * 8 + 4) / 5);
        var bits = 0;
        var value = 0;
        foreach (var b in data)
        {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                output.Append(alphabet[(value >> (bits - 5)) & 0x1F]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            output.Append(alphabet[(value << (5 - bits)) & 0x1F]);
        }

        return output.ToString();
    }
}
