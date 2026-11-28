using System.Globalization;
using System.Numerics;
using System.Text;
using SkyCD.Plugin.Metadata.Models3D.Exceptions;

namespace SkyCD.Plugin.Metadata.Models3D.Parsing;

internal static class ModelText
{
    public static StreamReader Open(byte[] data) => new(new MemoryStream(data), Encoding.UTF8);

    public static string[] Words(string line) => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    public static int Integer(string text) => int.TryParse(text, CultureInfo.InvariantCulture, out var value)
        ? value : throw new ModelReadException("InvalidInteger");

    public static int Count(string text)
    {
        var count = Integer(text);
        return count >= 0 ? count : throw new ModelReadException("InvalidCount");
    }

    public static float Number(string text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)
            ? value : throw new ModelReadException("InvalidNumber");

    public static Vector3 Position(string[] words, int start)
    {
        if (words.Length < start + 3)
            throw new ModelReadException("InvalidVertex");
        return new(Number(words[start]), Number(words[start + 1]), Number(words[start + 2]));
    }

    public static string WithoutComment(string line)
    {
        var index = line.IndexOf('#');
        return index < 0 ? line : line[..index];
    }
}
