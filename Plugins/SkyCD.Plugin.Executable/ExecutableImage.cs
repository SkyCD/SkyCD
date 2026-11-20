using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Executable.Exceptions;

namespace SkyCD.Plugin.Executable;

/// <summary>One PE section, reduced to what address translation needs.</summary>
internal readonly record struct PeSection(uint VirtualAddress, uint VirtualSize, uint SizeOfRawData,
    int PointerToRawData);

/// <summary>
/// A parsed Windows portable executable: the headers and the section table. A bare DOS executable that never
/// reaches a PE header is represented too, with no sections and no resources.
/// </summary>
/// <remarks>
/// Offsets are file offsets, not virtual addresses, so only the data that is actually needed has to be
/// fetched. Every value read from the file is validated before use; the resource tree is walked separately
/// by <see cref="ExecutableResources"/>, which bounds the depth and the number of reads.
/// </remarks>
internal sealed class ExecutableImage
{
    /// <summary>The largest header window the plugin reads before parsing the section table.</summary>
    public const int MaximumHeaderBytes = 64 * 1024;

    private ExecutableImage(IReadOnlyList<PeSection> sections, PeHeader header, string container)
    {
        Sections = sections;
        Header = header;
        Container = container;
    }

    public IReadOnlyList<PeSection> Sections { get; }

    /// <summary>Optional-header values; meaningless for an <c>mz-16</c> image.</summary>
    public PeHeader Header { get; }

    /// <summary>Family of the image: <c>pe</c> for a PE image, <c>mz-16</c> for a bare DOS executable.</summary>
    public string Container { get; }

    public bool IsPortableExecutable => Container == "pe";

    public string? PeFormat => IsPortableExecutable ? (Header.IsPe32Plus ? "PE32+" : "PE32") : null;

    public bool IsExecutableImage => IsPortableExecutable && Header.IsExecutableImage;

    public bool IsDotNet => IsPortableExecutable && Header.HasClrDirectory;

    /// <summary>The RVA of the resource directory, or zero when the image has no usable resource directory.</summary>
    public uint ResourceRva => IsPortableExecutable && Header.DataDirectoryCount >= 3 ? Header.ResourceRva : 0;

    public string? MachineName { get; private set; }

    /// <summary>True when the resource root address maps into a section's raw data.</summary>
    public bool HasResources => TryGetFileOffset(ResourceRva, out _);

    /// <summary>Reads and parses the header window of an image.</summary>
    public static async Task<ExecutableImage> ReadAsync(ExecutableSource image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Length < PeHeaderReader.MinimumHeaderBytes)
        {
            throw new ExecutableReadException("the file is too small to be an executable");
        }

        var prefix = await image.ReadAsync(0, PeHeaderReader.MinimumHeaderBytes, cancellationToken)
            .ConfigureAwait(false);
        if (!PeHeaderReader.HasDosSignature(prefix))
        {
            throw new ExecutableReadException("the image does not start with a DOS signature");
        }

        var peOffset = PeHeaderReader.FindPeSignatureOffset(prefix)
                       ?? throw new ExecutableReadException("the PE header offset lies outside the file");
        if (peOffset + 4L > image.Length)
        {
            // The PE header the DOS stub points at lies beyond the available bytes, so the image is a DOS
            // executable whose stub was cut short; header facts of the PE are simply unknown.
            return new ExecutableImage([], default, "mz-16");
        }

        // The PE signature and the COFF header that follows it usually start after the initial window, so the
        // part of the image that describes the rest of the header is fetched before it is parsed.
        var signatureLength = (int)Math.Min(image.Length, peOffset + 24L);
        var signatureWindow = signatureLength <= prefix.Length
            ? prefix
            : await image.ReadAsync(0, signatureLength, cancellationToken).ConfigureAwait(false);
        if (!PeHeaderReader.HasPeSignature(signatureWindow, peOffset))
        {
            throw new ExecutableReadException("the PE header signature is missing");
        }

        var (sectionCount, headerLength) = PeHeaderReader.DescribeHeader(signatureWindow, peOffset);
        if (sectionCount is 0 or > 96 || headerLength > MaximumHeaderBytes || headerLength > image.Length)
        {
            throw new ExecutableReadException("the section table is not plausible");
        }

        var window = headerLength <= signatureWindow.Length
            ? signatureWindow
            : await image.ReadAsync(0, (int)headerLength, cancellationToken).ConfigureAwait(false);

        PeHeader header;
        try
        {
            header = PeHeaderReader.ReadOptionalHeader(window, peOffset);
        }
        catch (FormatException exception)
        {
            throw new ExecutableReadException("the optional header is not supported", exception);
        }

        var sectionTableOffset = peOffset + 24L +
                                 BinaryPrimitives.ReadUInt16LittleEndian(window.AsSpan(peOffset + 20, 2));
        var sections = new List<PeSection>(sectionCount);
        for (var index = 0; index < sectionCount; index++)
        {
            var offset = (int)(sectionTableOffset + index * 40L);
            sections.Add(new PeSection(
                BinaryPrimitives.ReadUInt32LittleEndian(window.AsSpan(offset + 12, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(window.AsSpan(offset + 16, 4)),
                BinaryPrimitives.ReadUInt32LittleEndian(window.AsSpan(offset + 20, 4)),
                BinaryPrimitives.ReadInt32LittleEndian(window.AsSpan(offset + 36, 4))));
        }

        return new ExecutableImage(sections, header, "pe")
        {
            MachineName = ResolveMachineName(PeHeaderReader.ReadMachine(window, peOffset))
        };
    }

    /// <summary>Maps an RVA to a file offset through the section table; false when it is not mapped to data.</summary>
    public bool TryGetFileOffset(uint rva, out long fileOffset)
    {
        foreach (var section in Sections)
        {
            if (section.SizeOfRawData == 0 || section.PointerToRawData < 0 || rva < section.VirtualAddress)
            {
                continue;
            }

            var delta = rva - section.VirtualAddress;
            if (delta >= section.SizeOfRawData)
            {
                continue;
            }

            fileOffset = (long)section.PointerToRawData + delta;
            return true;
        }

        fileOffset = 0;
        return false;
    }

    /// <summary>The largest file offset any section covers; resource data beyond it is not addressable.</summary>
    public long MaximumSectionEnd()
    {
        var end = 0L;
        foreach (var section in Sections)
        {
            if (section.PointerToRawData < 0 || section.SizeOfRawData == 0)
            {
                continue;
            }

            end = Math.Max(end, (long)section.PointerToRawData + section.SizeOfRawData);
        }

        return end;
    }

    private static string ResolveMachineName(ushort machine) => machine switch
    {
        0x014C => "x86",
        0x8664 => "x64",
        0xAA64 => "arm64",
        0x01C0 or 0x01C4 => "arm",
        0x0200 => "ia64",
        0x01C2 => "thumb",
        _ => $"unknown-0x{machine:X4}"
    };
}
