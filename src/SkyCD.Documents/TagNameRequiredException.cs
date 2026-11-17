using System;

namespace SkyCD.Documents;

public sealed class TagNameRequiredException()
    : ArgumentException("A tag name is required.")
{
}
