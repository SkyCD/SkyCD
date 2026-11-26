using System.Buffers.Binary;
using SkyCD.Plugin.Metadata.VideoProjects.Models;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

internal sealed class AfterEffectsFacts(string format)
{
    private int compositions;
    private int footage;
    private int folders;
    private bool readComposition;
    private bool readItem;

    public ProjectFacts Facts { get; } = new("AfterEffects", format, "Adobe After Effects");

    public void ReadChunk(string tag, ReadOnlySpan<byte> data)
    {
        switch (tag)
        {
            case "head" when data.Length >= 4:
                Facts.ApplicationVersion = FormattableString.Invariant($"{ReadUInt16(data, 0)}.{ReadUInt16(data, 2)}");
                break;
            case "idta" when data.Length >= 2:
                readItem = true;
                switch (ReadUInt16(data, 0))
                {
                    case 1: folders++; break;
                    case 7: footage++; break;
                }
                break;
            case "cdta":
                compositions++;
                if (!readComposition)
                {
                    readComposition = true;
                    ReadComposition(data);
                }
                break;
        }
    }

    public ProjectFacts Complete()
    {
        Facts.TimelineCount = compositions;
        if (readItem)
        {
            Facts.MediaFileCount = footage;
            Facts.FolderCount = folders;
            Facts.ItemCount = compositions + footage + folders;
        }
        return Facts;
    }

    private void ReadComposition(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 0x34 && ReadUInt32(data, 0x30) is > 0 and var scale)
            Facts.Duration = (double)ReadUInt32(data, 0x2c) / scale;
        if (data.Length >= 0x90 && ReadUInt16(data, 0x8c) is > 0 and var width &&
            ReadUInt16(data, 0x8e) is > 0 and var height)
            Facts.Resolution = FormattableString.Invariant($"{width} × {height}");
        if (data.Length >= 0xa0 && ReadUInt32(data, 0x9c) is > 0 and var rate)
            Facts.FrameRate = rate / 65536.0;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
}
