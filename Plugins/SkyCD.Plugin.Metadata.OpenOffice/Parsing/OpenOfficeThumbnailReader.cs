using System;
using System.Buffers.Binary;
using System.Threading;
using SkyCD.Plugin.Metadata.OpenOffice.Models;

namespace SkyCD.Plugin.Metadata.OpenOffice.Parsing;

/// <summary>
/// Reads the preview picture OpenOffice and LibreOffice store in an OpenDocument or OpenOffice.org package.
/// </summary>
/// <remarks>
/// Both generations publish the preview as <c>Thumbnails/thumbnail.png</c>, so those bytes are copied after the
/// PNG structure and the pixel bounds are checked. Nothing is rendered and nothing is decrypted: a package that
/// declares encryption, a missing or damaged member, a picture that is not a readable PNG and a picture above
/// the byte or pixel bound all yield <see langword="null"/>, which the host records as an explicit "no preview".
/// </remarks>
internal static class OpenOfficeThumbnailReader
{
    private const int SignatureBytes = 8;
    private const int IhdrChunkBytes = 25;
    private const int IhdrDataBytes = 13;

    private static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Returns the bounded preview picture of a package, or <see langword="null"/> when there is none.</summary>
    public static OpenOfficeThumbnail? Read(byte[] data, OpenOfficePluginOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(options);

        using var package = OpenOfficePackage.Open(data);
        if (package.DeclaresEncryption(options.MaxPreviewBytes))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var bytes = package.TryReadThumbnail(options.MaxPreviewBytes, cancellationToken);
        return bytes is null ? null : Validate(bytes, options.MaxPreviewDimension);
    }

    /// <summary>Publishes a PNG only when its signature and IHDR chunk are readable and both edges are inside the bound.</summary>
    private static OpenOfficeThumbnail? Validate(byte[] bytes, int maxDimension)
    {
        if (bytes.Length < SignatureBytes + IhdrChunkBytes
            || !bytes.AsSpan(0, SignatureBytes).SequenceEqual(Signature)
            || BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(SignatureBytes)) is not IhdrDataBytes
            || !bytes.AsSpan(SignatureBytes + 4, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(SignatureBytes + 8));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(SignatureBytes + 12));
        if (width <= 0 || height <= 0 || width > maxDimension || height > maxDimension)
        {
            return null;
        }

        return new OpenOfficeThumbnail("image/png", bytes);
    }
}
