using System;
using System.IO;

namespace SkyCD.Plugin.Runtime.Exceptions;

/// <summary>
/// Saving failed because the destination folder denied an operation. Carries the folder — never the
/// internal temporary file name — so the host can explain the failure and offer a way forward.
/// </summary>
public sealed class CatalogSaveIOException(string directoryPath, Exception? innerException = null)
    : IOException($"Cannot save into '{directoryPath}'.", innerException)
{
    /// <summary>Folder that denied the write; user-facing messages name this, never a temp file.</summary>
    public string DirectoryPath { get; } = directoryPath;
}
