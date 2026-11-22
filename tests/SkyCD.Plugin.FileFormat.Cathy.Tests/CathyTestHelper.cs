using System;
using System.IO;
using System.Text;
using SkyCD.Plugin.FileFormat.Cathy.Parsing;

namespace SkyCD.Plugin.FileFormat.Cathy.Tests;

public static class CathyTestHelper
{
    public static byte[] CreateSampleCaf(
        short version = 8,
        string? volumeName = "CD-ROM 1",
        string? comment = "Test Comment",
        uint freeSizeMb = 120,
        short archiveFlag = 0)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.Latin1);

        uint magic;
        if (version <= 2)
        {
            magic = (uint)(version * CathyParser.Modus + CathyParser.MagicBase);
            writer.Write(magic);
        }
        else
        {
            magic = (uint)(3 * CathyParser.Modus + CathyParser.MagicBase);
            writer.Write(magic);
            writer.Write(version);
        }

        // Timestamp
        var unixTime = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        writer.Write(unixTime);

        // Device
        if (version >= 2)
        {
            WriteNullTerminated(writer, "D:");
        }

        // Volume
        WriteNullTerminated(writer, volumeName ?? "MY_DISC");
        // Alias
        WriteNullTerminated(writer, string.Empty);

        // SerialNumber
        writer.Write(0x12345678u);

        // Comment
        if (version >= 4)
        {
            WriteNullTerminated(writer, comment ?? string.Empty);
        }

        // FreeSizeMb
        if (version >= 1)
        {
            writer.Write(freeSizeMb);
        }

        // ArchiveFlag
        if (version >= 6)
        {
            writer.Write(archiveFlag);
        }

        // Folders table
        // We will create 2 folders: 0 (root), 1 (Docs)
        writer.Write(2); // folderCount

        // Folder 0
        WriteNullTerminated(writer, volumeName ?? "MY_DISC");
        if (version >= 3)
        {
            writer.Write(2); // files in folder
            writer.Write((double)1024); // total size
        }

        // Folder 1
        if (version <= 3)
        {
            WriteNullTerminated(writer, "Docs");
        }
        if (version >= 3)
        {
            writer.Write(1); // files in folder
            writer.Write((double)512); // total size
        }

        // File list
        // Entry 1: Docs (Folder entry pointing to directory 1, parent 0)
        // Entry 2: Readme.txt (File in root folder 0)
        // Entry 3: Manual.pdf (File in Docs folder 1)
        writer.Write(3); // fileCount

        // Entry 1 (Folder "Docs")
        writer.Write(unixTime);
        if (version > 6) writer.Write((long)-1);
        else writer.Write((int)-1);
        if (version > 7) writer.Write((int)0);
        else writer.Write((ushort)0);
        WriteNullTerminated(writer, "Docs");

        // Entry 2 (File "Readme.txt")
        writer.Write(unixTime);
        if (version > 6) writer.Write((long)512);
        else writer.Write((int)512);
        if (version > 7) writer.Write((int)0);
        else writer.Write((ushort)0);
        WriteNullTerminated(writer, "Readme.txt");

        // Entry 3 (File "Manual.pdf")
        writer.Write(unixTime);
        if (version > 6) writer.Write((long)1024);
        else writer.Write((int)1024);
        if (version > 7) writer.Write((int)1);
        else writer.Write((ushort)1);
        WriteNullTerminated(writer, "Manual.pdf");

        writer.Flush();
        return ms.ToArray();
    }

    public static void WriteNullTerminated(BinaryWriter writer, string str)
    {
        var bytes = Encoding.Latin1.GetBytes(str);
        writer.Write(bytes);
        writer.Write((byte)0);
    }
}
