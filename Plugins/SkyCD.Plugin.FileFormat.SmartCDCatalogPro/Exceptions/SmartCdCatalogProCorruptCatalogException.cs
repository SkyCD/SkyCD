using System;

namespace SkyCD.Plugin.FileFormat.SmartCDCatalogPro.Exceptions;

public sealed class SmartCdCatalogProCorruptCatalogException(string reason)
    : InvalidOperationException($"Corrupt Smart CD Catalog PRO catalog: {reason}")
{
}
