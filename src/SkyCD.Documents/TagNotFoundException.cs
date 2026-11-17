using System;

namespace SkyCD.Documents;

public sealed class TagNotFoundException(string tagId)
    : InvalidOperationException($"Tag '{tagId}' does not exist.")
{
}
