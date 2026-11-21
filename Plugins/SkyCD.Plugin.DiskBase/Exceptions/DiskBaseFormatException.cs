using System.IO;

namespace SkyCD.Plugin.DiskBase.Exceptions;

public sealed class DiskBaseFormatException(string reason) : IOException($"Invalid or unsupported DiskBase QDR database: {reason}");
