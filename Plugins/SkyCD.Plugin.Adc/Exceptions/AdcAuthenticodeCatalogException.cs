using System;

namespace SkyCD.Plugin.Adc.Exceptions;

/// <summary>
/// Raised when a Windows Authenticode security catalog is offered instead of an
/// ElcomSoft Advanced Disk Catalog database.
/// </summary>
public sealed class AdcAuthenticodeCatalogException()
    : InvalidOperationException("The file is a Windows Authenticode security catalog, not an ElcomSoft Advanced Disk Catalog database.");
