namespace SkyCD.Plugin.Metadata.Models.Exceptions;

internal sealed class ModelReadException(string code) : Exception($"Invalid model data ({code}).")
{
    public string Code { get; } = code;
}
