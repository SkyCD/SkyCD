using System;

namespace SkyCD.Plugin.CdCollect.Exceptions;

/// <summary>
/// Thrown when an unsupported database version is encountered in a CDcollect database.
/// </summary>
public sealed class CdCollectUnsupportedVersionException(int version)
    : NotSupportedException($"CDcollect database version '{version}' is not supported.");
