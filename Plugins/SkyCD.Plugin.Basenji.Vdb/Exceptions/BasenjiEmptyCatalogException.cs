using System;

namespace SkyCD.Plugin.Basenji.Vdb.Exceptions;

/// <summary>
/// Thrown when a Basenji catalog file contains no volumes or items.
/// </summary>
public sealed class BasenjiEmptyCatalogException()
    : InvalidOperationException("Basenji catalog is empty and contains no volumes.");
