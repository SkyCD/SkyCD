using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DiscUtils.Iso9660;

namespace SkyCD.Plugin.Metadata.Ccd.Tests;

/// <summary>Builds ISO 9660 images and CloneCD descriptor/IMG file sets for tests.</summary>
internal static class CcdTestImage
{
    public const string FixtureContent = "ccd fixture content";
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

    /// <summary>Writes a raw 2352-byte track preceded by empty audio sectors.</summary>
    public static byte[] WithAudioPrefix(byte[] iso, int audioSectors)
    {
        var data = Repack(iso, sectorSize: 2352, dataOffset: 16);
        var result = new byte[audioSectors * 2352 + data.Length];
        Buffer.BlockCopy(data, 0, result, audioSectors * 2352, data.Length);
        return result;
    }

    public static string BuildDescriptor(IReadOnlyList<(string Mode, long Index1)> tracks,
        string? imageFile = null, int? scrambled = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[CloneCD]");
        builder.AppendLine("Version=3");
        builder.AppendLine();
        builder.AppendLine("[Disc]");
        builder.AppendLine($"TocEntries={tracks.Count}");
        if (imageFile is not null)
        {
            builder.AppendLine($"ImageFile={imageFile}");
        }

        if (scrambled is not null)
        {
            builder.AppendLine($"DataTracksScrambled={scrambled}");
        }

        for (var index = 0; index < tracks.Count; index++)
        {
            builder.AppendLine();
            builder.AppendLine($"[TRACK {index + 1}]");
            builder.AppendLine($"MODE={tracks[index].Mode}");
            builder.AppendLine($"INDEX 1={tracks[index].Index1}");
        }

        return builder.ToString();
    }

    /// <summary>Writes a descriptor and its IMG companion into <paramref name="directory"/>.</summary>
    public static string WriteSet(string directory, string baseName, byte[] image, string descriptor,
        string? imageFileName = null)
    {
        Directory.CreateDirectory(directory);
        var descriptorPath = Path.Combine(directory, baseName + ".ccd");
        File.WriteAllText(descriptorPath, descriptor);
        File.WriteAllBytes(Path.Combine(directory, imageFileName ?? baseName + ".img"), image);
        return descriptorPath;
    }
}
