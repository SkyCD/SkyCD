using System;

namespace SkyCD.Plugin.AbiWord.Models;

public sealed record AbiWordPluginOptions
{
    public AbiWordPluginOptions(
        int maxSourceBytes = 16 * 1024 * 1024,
        int maxDecompressedBytes = 32 * 1024 * 1024,
        int maxMetadataCharacters = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSourceBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDecompressedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMetadataCharacters);
        MaxSourceBytes = maxSourceBytes;
        MaxDecompressedBytes = maxDecompressedBytes;
        MaxMetadataCharacters = maxMetadataCharacters;
    }

    public int MaxSourceBytes { get; }
    public int MaxDecompressedBytes { get; }
    public int MaxMetadataCharacters { get; }

    public static AbiWordPluginOptions Default { get; } = new();
}
