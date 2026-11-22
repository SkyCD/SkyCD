using System.Collections.Generic;
using SkyCD.Plugin.Abstractions.Capabilities.Media;

namespace SkyCD.Plugin.Metadata.Hash.Models;

/// <summary>
/// Stable schema identities and algorithm-qualified field keys published by the hash plugin. Values always
/// name their algorithm, so a digest can never be mistaken for a different P2P identifier.
/// </summary>
public static class HashSchema
{
    public const string MetadataSchemaId = "skycd.hash.metadata";
    public const int MetadataSchemaVersion = 1;

    /// <summary>eD2k file hash: lowercase hexadecimal, 32 characters.</summary>
    public const string Ed2k = "ed2k";

    /// <summary>Tiger Tree Hash root: RFC 4648 base32, 39 characters, as P2P clients display it.</summary>
    public const string Tth = "tth";

    /// <summary>Whole-file SHA-256: lowercase hexadecimal, 64 characters.</summary>
    public const string Sha256 = "sha256";

    /// <summary>The plugin claims every file type; the descriptor marks it opt-in so no implicit pass runs it.</summary>
    public static readonly IReadOnlyList<string> Extensions = ["*"];

    public static readonly IReadOnlyList<string> ContentTypes = ["*"];

    public static MediaMetadataSchema Metadata { get; } = new(MetadataSchemaId, MetadataSchemaVersion,
    [
        new MediaMetadataField(Ed2k, MediaMetadataType.Text),
        new MediaMetadataField(Tth, MediaMetadataType.Text),
        new MediaMetadataField(Sha256, MediaMetadataType.Text)
    ]);
}
