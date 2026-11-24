using System;

namespace SkyCD.Plugin.Abstractions.Capabilities.Catalog;

/// <summary>Base type for location API failures, so plugins can handle them as one family.</summary>
public abstract class LocationApiException : Exception
{
    /// <summary>Creates the exception with the supplied message.</summary>
    protected LocationApiException(string message) : base(message)
    {
    }
}
