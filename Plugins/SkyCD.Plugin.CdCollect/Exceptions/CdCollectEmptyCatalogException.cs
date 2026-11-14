using System;

namespace SkyCD.Plugin.CdCollect.Exceptions;

/// <summary>
/// Thrown when a CDcollect catalog database contains no CD media entries.
/// </summary>
public sealed class CdCollectEmptyCatalogException()
    : InvalidOperationException("CDcollect catalog contains no CD media entries.");
