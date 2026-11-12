using System;

namespace SkyCD.Documents;

public sealed class MediaNameRequiredException : ArgumentException
{
    public MediaNameRequiredException()
        : base("A media name is required.")
    {
    }
}
