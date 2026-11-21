using System;

namespace SkyCD.Presentation.Catalog;

/// <summary>Raised when a plugin asks for storage while no catalog database is open.</summary>
public sealed class PluginStorageUnavailableException()
    : InvalidOperationException("No catalog is open, so plugin storage cannot be used.");
