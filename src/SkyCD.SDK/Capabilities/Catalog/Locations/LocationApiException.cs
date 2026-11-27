using System;

namespace SkyCD.SDK.Capabilities.Catalog.Locations;

/// <summary>Base type for location API failures, so plugins can handle them as one family.</summary>
public abstract class LocationApiException : Exception
{
    /// <summary>Creates the exception with the supplied message.</summary>
    protected LocationApiException(string message) : base(message)
    {
    }
}
