using System.IO;

namespace SkyCD.Plugin.Feature.Split.Exceptions;

internal sealed class SplitOutputConflictException(string path)
    : IOException($"The part file '{path}' already exists. Choose another output folder or naming pattern.");
