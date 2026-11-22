using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.FileFormat.Ccd.Exceptions;

namespace SkyCD.Plugin.FileFormat.Ccd.Parsing;

/// <summary>Reads a CCD descriptor from a text stream with a hard size bound.</summary>
internal static class CcdDescriptorReader
{
    private const int ReadBufferBytes = 8_192;

    public static async Task<CcdDescriptor> ReadAsync(Stream source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.CanSeek)
        {
            source.Position = 0;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[ReadBufferBytes];
        int read;
        while ((read = await source.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > CcdDescriptor.MaxDescriptorBytes)
            {
                throw new CcdImageFormatException("the descriptor is too large.");
            }

            buffer.Write(chunk, 0, read);
        }

        return CcdDescriptor.Parse(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }
}
