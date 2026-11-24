using System;

namespace SkyCD.Plugin.Ai.GenDesc.Exceptions;

/// <summary>Raised when a template body contains syntax the engine cannot compile.</summary>
public sealed class TemplateSyntaxException : Exception
{
    public TemplateSyntaxException(string message)
        : base($"Template syntax error: {message}")
    {
    }

    public TemplateSyntaxException(string message, int line)
        : base($"Template syntax error at line {line.ToString(System.Globalization.CultureInfo.InvariantCulture)}: {message}")
    {
    }
}
