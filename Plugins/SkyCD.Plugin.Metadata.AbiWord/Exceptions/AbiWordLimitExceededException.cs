using System;

namespace SkyCD.Plugin.Metadata.AbiWord.Exceptions;

public sealed class AbiWordLimitExceededException : Exception
{
    public AbiWordLimitExceededException(string resource, long limit)
        : base($"The AbiWord document exceeds the {resource} limit of {limit}.")
    {
    }
}
