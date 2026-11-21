using System;
using System.Runtime.InteropServices;
using SkyCD.Plugin.MediaMetadata;
using SkyCD.Plugin.MediaMetadata.Parsing;

namespace SkyCD.Plugin.FileMetadata.Parsing;

/// <summary>
/// Reads the Finder tags and Finder comment, which macOS stores as binary property lists in extended
/// attributes next to the file. Both attributes are bounded before anything is read; Finder tags carry a
/// colour suffix after a line break, which the plist reader strips.
/// </summary>
internal static class MacXattrReader
{
    private const int MaxAttributeBytes = 256 * 1024;
    private const string TagsAttribute = "com.apple.metadata:_kMDItemUserTags";
    private const string CommentAttribute = "com.apple.metadata:kMDItemFinderComment";

    public static void Read(string path, MetadataScan scan)
    {
        if (ReadAttribute(path, TagsAttribute) is { } tags)
        {
            foreach (var tag in BinaryPlistReader.ReadStrings(tags))
            {
                scan.AddKeyword(tag);
            }
        }

        if (ReadAttribute(path, CommentAttribute) is { } comment)
        {
            scan.SetComment(BinaryPlistReader.ReadString(comment));
        }
    }

    private static byte[]? ReadAttribute(string path, string name)
    {
        try
        {
            var length = GetXattr(path, name, null, 0, 0, 0);
            if (length <= 0 || length > MaxAttributeBytes)
            {
                return null;
            }

            var buffer = new byte[(int)length];
            var read = GetXattr(path, name, buffer, (nuint)buffer.Length, 0, 0);
            return read <= 0 ? null : buffer[..(int)read];
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "getxattr")]
    private static extern nint GetXattr([MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, byte[]? value, nuint size, uint position, int options);
}
