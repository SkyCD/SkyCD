using System;

namespace SkyCD.Plugin.Metadata.YamlToml.Exceptions;

/// <summary>Exception thrown when a YAML or TOML source exceeds the plugin's own byte limit.</summary>
public sealed class YamlTomlSourceTooLargeException : Exception
{
    public YamlTomlSourceTooLargeException(long length, long maximum)
        : base($"The document is {length} bytes, above the limit of {maximum} bytes.")
    {
        Length = length;
        Maximum = maximum;
    }

    /// <summary>The source length that was rejected.</summary>
    public long Length { get; }

    /// <summary>The configured maximum.</summary>
    public long Maximum { get; }
}
