using System;

namespace SkyCD.Cli.Exceptions;

public sealed class CliFormatInferenceFailedException(string path)
    : InvalidOperationException($"Unable to infer format for '{path}'. Provide --format explicitly.");