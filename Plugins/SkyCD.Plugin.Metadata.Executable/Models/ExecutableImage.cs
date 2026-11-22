using System;
using System.Collections.Generic;
using System.Reflection.PortableExecutable;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Metadata.Executable.Exceptions;
using SkyCD.Plugin.Metadata.Executable.Parsing;

namespace SkyCD.Plugin.Metadata.Executable.Models;

/// <summary>One PE section, reduced to what address translation needs.</summary>
internal readonly record struct PeSection(uint VirtualAddress, uint VirtualSize, uint SizeOfRawData,
    int PointerToRawData);

/// <summary>
/// A parsed Windows portable executable: the headers and the section table. A bare DOS executable that never
/// reaches a PE header is represented too, with no sections and no resources.
/// </summary>
/// <remarks>
/// The COFF and optional headers and the section table are parsed by the in-box
/// <see cref="PEHeaders"/> reader, so no parsing code is duplicated here. Offsets are file offsets, not virtual
/// addresses, so only the data that is actually needed has to be fetched; the resource tree is walked separately
/// by <see cref="ExecutableResources"/>, which bounds the depth and the number of reads.
/// </remarks>
internal sealed class ExecutableImage
{
    /// <summary>Section count above which an image is treated as crafted rather than real.</summary>
    private const int MaximumSectionCount = 96;

    private const int ResourceDirectoryIndex = 2;
    private const int ClrDirectoryIndex = 14;

    private ExecutableImage(IReadOnlyList<PeSection> sections, string container, bool isPe32Plus,
        bool isExecutableImage, uint resourceRva, bool hasClrDirectory, string? machineName)
    {
        Sections = sections;
        Container = container;
        IsPe32Plus = isPe32Plus;
        IsExecutableImage = isExecutableImage;
        ResourceRva = resourceRva;
        IsDotNet = hasClrDirectory;
        MachineName = machineName;
    }

    public IReadOnlyList<PeSection> Sections { get; }

    /// <summary>Family of the image: <c>pe</c> for a PE image, <c>mz-16</c> for a bare DOS executable.</summary>
    public string Container { get; }

    public bool IsPortableExecutable => Container == "pe";

    public string? PeFormat => IsPortableExecutable ? (IsPe32Plus ? "PE32+" : "PE32") : null;

    public bool IsExecutableImage { get; }

    public bool IsDotNet { get; }

    /// <summary>The RVA of the resource directory, or zero when the image has no usable resource directory.</summary>
    public uint ResourceRva { get; }

    public string? MachineName { get; }

    /// <summary>True when the resource root address maps into a section's raw data.</summary>
    public bool HasResources => TryGetFileOffset(ResourceRva, out _);

    private bool IsPe32Plus { get; }

    /// <summary>Reads the headers and the section table of an image.</summary>
    public static async Task<ExecutableImage> ReadAsync(ExecutableSource image, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Length < PeContainerProbe.MinimumHeaderBytes)
        {
            throw new ExecutableReadException("the file is too small to be an executable");
        }

        var prefix = await image.ReadAsync(0, PeContainerProbe.MinimumHeaderBytes, cancellationToken)
            .ConfigureAwait(false);
        if (!PeContainerProbe.HasDosSignature(prefix))
        {
            throw new ExecutableReadException("the image does not start with a DOS signature");
        }

        var peOffset = PeContainerProbe.FindPeSignatureOffset(prefix)
                       ?? throw new ExecutableReadException("the PE header offset lies outside the file");
        if (peOffset + 4L > image.Length)
        {
            // The PE header the DOS stub points at lies beyond the available bytes, so the image is a DOS
            // executable whose stub was cut short; header facts of the PE are simply unknown.
            return new ExecutableImage([], "mz-16", isPe32Plus: false, isExecutableImage: false, resourceRva: 0,
                hasClrDirectory: false, machineName: null);
        }

        // The PE signature and the COFF header that follows it usually start after the initial window, so the
        // bytes that hold the signature and the section count are fetched before the container is classified.
        var signatureLength = (int)Math.Min(image.Length, peOffset + 24L);
        var signatureWindow = signatureLength <= prefix.Length
            ? prefix
            : await image.ReadAsync(0, signatureLength, cancellationToken).ConfigureAwait(false);
        if (!PeContainerProbe.HasPeSignature(signatureWindow, peOffset))
        {
            throw new ExecutableReadException("the PE header signature is missing");
        }

        var sectionCount = PeContainerProbe.ReadSectionCount(signatureWindow, peOffset);
        if (sectionCount is 0 or > MaximumSectionCount)
        {
            throw new ExecutableReadException("the section table is not plausible");
        }

        try
        {
            using var stream = new ExecutableSourceStream(image, cancellationToken);
            var headers = new PEHeaders(stream);
            if (headers.PEHeader is not { } header)
            {
                throw new ExecutableReadException("the optional header is not supported");
            }

            var sections = new List<PeSection>(sectionCount);
            foreach (var section in headers.SectionHeaders)
            {
                sections.Add(new PeSection((uint)section.VirtualAddress, (uint)section.VirtualSize,
                    (uint)section.SizeOfRawData, section.PointerToRawData));
            }

            return new ExecutableImage(sections, "pe", header.Magic == PEMagic.PE32Plus,
                (headers.CoffHeader.Characteristics & Characteristics.ExecutableImage) != 0,
                (uint)header.NumberOfRvaAndSizes > ResourceDirectoryIndex
                    ? (uint)header.ResourceTableDirectory.RelativeVirtualAddress
                    : 0,
                (uint)header.NumberOfRvaAndSizes > ClrDirectoryIndex &&
                header.CorHeaderTableDirectory.RelativeVirtualAddress != 0,
                ResolveMachineName(headers.CoffHeader.Machine));
        }
        catch (BadImageFormatException exception)
        {
            throw new ExecutableReadException("the PE headers are not supported", exception);
        }
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

    private static string ResolveMachineName(Machine machine) => (ushort)machine switch
    {
        0x014C => "x86",
        0x8664 => "x64",
        0xAA64 => "arm64",
        0x01C0 or 0x01C4 => "arm",
        0x0200 => "ia64",
        0x01C2 => "thumb",
        _ => $"unknown-0x{(ushort)machine:X4}"
    };
}
