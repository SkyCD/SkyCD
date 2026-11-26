using System.IO;

namespace SkyCD.Plugin.Metadata.Email.Exceptions;

internal sealed class EmailDecodeException(string format)
    : IOException($"The email source is not a valid {format} file.");
