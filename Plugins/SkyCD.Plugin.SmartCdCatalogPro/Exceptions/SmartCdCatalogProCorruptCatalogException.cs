using System;

namespace SkyCD.Plugin.SmartCdCatalogPro.Exceptions;

public sealed class SmartCdCatalogProCorruptCatalogException(string reason)
    : InvalidOperationException($"Corrupt Smart CD Catalog PRO catalog: {reason}")
{
}
