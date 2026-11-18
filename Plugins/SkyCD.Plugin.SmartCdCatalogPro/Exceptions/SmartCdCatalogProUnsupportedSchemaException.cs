using System;

namespace SkyCD.Plugin.SmartCdCatalogPro.Exceptions;

public sealed class SmartCdCatalogProUnsupportedSchemaException(string reason)
    : InvalidOperationException($"Unsupported Smart CD Catalog PRO catalog schema: {reason}")
{
}
