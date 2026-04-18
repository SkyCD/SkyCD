namespace SkyCD.Plugin.Host.FileFormats;

/// <summary>
/// Typed routing exception for host-level file format operations.
/// </summary>
public sealed class FileFormatRoutingException(string message) : Exception(message)
{
}
