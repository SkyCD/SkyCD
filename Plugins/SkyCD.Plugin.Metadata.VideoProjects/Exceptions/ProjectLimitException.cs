namespace SkyCD.Plugin.Metadata.VideoProjects.Exceptions;

internal sealed class ProjectLimitException(string limit) : IOException($"Video project exceeds the {limit} limit.");
