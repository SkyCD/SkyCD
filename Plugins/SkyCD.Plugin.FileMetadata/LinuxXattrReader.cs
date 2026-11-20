using System;
using System.Runtime.InteropServices;
using SkyCD.Plugin.MediaMetadata;

namespace SkyCD.Plugin.FileMetadata;

/// <summary>
/// Reads the extended attributes XDG-aware file managers use for user-assigned metadata. KDE Dolphin keeps
/// tags in <c>user.xdg.tags</c> and the comment in <c>user.xdg.comment</c>; the values sit next to the file
/// rather than inside it. The attribute size is bounded before anything is read.
/// </summary>
internal static class LinuxXattrReader
{
    private const int MaxAttributeBytes = 64 * 1024;
    private const string TagsAttribute = "user.xdg.tags";
    private const string CommentAttribute = "user.xdg.comment";

    public static void Read(string path, MetadataScan scan)
    {
        if (ReadAttribute(path, TagsAttribute) is { } tags)
        {
            scan.AddKeywords(MetadataText.Decode(tags));
        }

        if (ReadAttribute(path, CommentAttribute) is { } comment)
        {
            scan.SetComment(MetadataText.Decode(comment));
        }
    }

    private static byte[]? ReadAttribute(string path, string name)
    {
        try
        {
            var length = GetXattr(path, name, null, 0);
            if (length <= 0 || length > MaxAttributeBytes)
            {
                return null;
            }

            var buffer = new byte[(int)length];
            var read = GetXattr(path, name, buffer, (nuint)buffer.Length);
            return read <= 0 ? null : buffer[..(int)read];
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "getxattr")]
    private static extern nint GetXattr([MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, byte[]? value, nuint size);
}
