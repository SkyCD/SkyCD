using System.IO;

namespace SkyCD.Plugin.Metadata.Email.Exceptions;

internal sealed class EmailSourceTooLargeException(long size, long limit)
    : IOException($"Email source size {size} bytes exceeds the {limit}-byte metadata read limit.");
