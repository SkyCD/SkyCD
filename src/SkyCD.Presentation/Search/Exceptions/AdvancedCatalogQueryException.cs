using System;

namespace SkyCD.Presentation.Search.Exceptions;

public sealed class AdvancedCatalogQueryException : InvalidOperationException
{
    public AdvancedCatalogQueryException(string template, params object?[] args)
        : base(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Invalid advanced catalog query: " + template,
            args))
    {
    }
}
