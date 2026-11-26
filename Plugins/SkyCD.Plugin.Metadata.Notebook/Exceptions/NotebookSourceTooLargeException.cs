using System;

namespace SkyCD.Plugin.Metadata.Notebook.Exceptions;

/// <summary>Exception thrown when a notebook source exceeds the plugin's own byte limit.</summary>
public sealed class NotebookSourceTooLargeException : Exception
{
    public NotebookSourceTooLargeException(long length, long maximum)
        : base($"The notebook is {length} bytes, above the limit of {maximum} bytes.")
    {
        Length = length;
        Maximum = maximum;
    }

    /// <summary>The source length that was rejected.</summary>
    public long Length { get; }

    /// <summary>The configured maximum.</summary>
    public long Maximum { get; }
}
