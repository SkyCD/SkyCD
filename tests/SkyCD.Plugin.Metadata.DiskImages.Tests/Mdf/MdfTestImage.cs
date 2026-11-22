using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using DiscUtils.Iso9660;

namespace SkyCD.Plugin.Metadata.DiskImages.Tests;

/// <summary>Builds ISO 9660 images and Alcohol MDS/MDF file sets for tests.</summary>
internal static class MdfTestImage
{
    public const string FixtureContent = "mdf fixture content";
    public const string ReadmeContent = "readme";
    public const int UserDataSize = 2048;

    public static byte[] BuildIso()
    {
        var builder = new CDBuilder { UseJoliet = true };
        builder.AddFile(@"ROOT\DEEP\FIXTURE.TXT", Encoding.UTF8.GetBytes(FixtureContent));
        builder.AddFile(@"ROOT\README.TXT", Encoding.UTF8.GetBytes(ReadmeContent));
        using var stream = new MemoryStream();
        builder.Build(stream);
        return stream.ToArray();
    }

    /// <summary>Lays the ISO user data out in <paramref name="sectorSize"/>-byte sectors.</summary>
    public static byte[] Repack(byte[] iso, int sectorSize, int dataOffset)
    {
        if (sectorSize == UserDataSize)
        {
            return iso;
        }

        var sectorCount = iso.Length / UserDataSize;
        var result = new byte[sectorCount * sectorSize];
        for (var sector = 0; sector < sectorCount; sector++)
        {
            Buffer.BlockCopy(iso, sector * UserDataSize, result, sector * sectorSize + dataOffset, UserDataSize);
        }

        return result;
    }

    /// <summary>Builds the fixed MDS descriptor header this plugin validates.</summary>
    public static byte[] BuildMds(string version = "1.20", ushort mediumType = 0x00, ushort sessions = 1)
    {
        var bytes = new byte[88];
        Encoding.ASCII.GetBytes("MEDIA DESCRIPTOR").CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes(version).CopyTo(bytes, 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(20), mediumType);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), sessions);
        return bytes;
    }

    /// <summary>Writes a descriptor and its MDF companion into <paramref name="directory"/>.</summary>
    public static string WriteSet(string directory, string baseName, byte[] image, byte[] descriptor)
    {
        Directory.CreateDirectory(directory);
        var descriptorPath = Path.Combine(directory, baseName + ".mds");
        File.WriteAllBytes(descriptorPath, descriptor);
        File.WriteAllBytes(Path.Combine(directory, baseName + ".mdf"), image);
        return descriptorPath;
    }
}
