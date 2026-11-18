using System;

namespace SkyCD.Presentation.Scanning;

public sealed record RemoteEntry(
    string Name,
    string FullName,
    string DirectoryPath,
    bool IsDirectory,
    long Size,
    string? Permission,
    DateTime? ModifiedUtc);
