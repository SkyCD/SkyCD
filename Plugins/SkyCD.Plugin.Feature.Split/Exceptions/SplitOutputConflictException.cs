using System.IO;

namespace SkyCD.Plugin.Feature.Split.Exceptions;

/// <summary>A planned part file already exists, so the split stops instead of overwriting it.</summary>
public sealed class SplitOutputConflictException(string path)
    : IOException($"The part file '{path}' already exists. Choose another output folder or naming pattern.");
