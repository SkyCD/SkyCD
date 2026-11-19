using System;

namespace SkyCD.Documents;

public sealed class LocationNameRequiredException()
    : ArgumentException("A location name is required.")
{
}
