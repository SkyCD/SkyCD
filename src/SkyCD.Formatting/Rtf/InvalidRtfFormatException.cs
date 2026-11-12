using System;

namespace SkyCD.Formatting.Rtf;

public class InvalidRtfFormatException : FormatException
{
    private const string DefaultMessageTemplate = "The provided RTF content is malformed: {0}";

    public InvalidRtfFormatException(string details)
        : base(string.Format(DefaultMessageTemplate, details))
    {
        Details = details;
    }

    public InvalidRtfFormatException(string details, Exception innerException)
        : base(string.Format(DefaultMessageTemplate, details), innerException)
    {
        Details = details;
    }

    public string Details { get; }
}
