using System;

namespace SkyCD.Plugin.Host.Media;

public sealed class MediaLimitExceededException(string limit)
    : Exception($"The media operation exceeded its {limit} limit.");
