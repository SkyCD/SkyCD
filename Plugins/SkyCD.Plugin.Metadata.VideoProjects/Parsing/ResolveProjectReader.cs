using SkyCD.Plugin.Metadata.VideoProjects.Models;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

internal static class ResolveProjectReader
{
    public static ProjectFacts? Read(Stream source, CancellationToken token) =>
        ResolveArchive.Read(source, isTemplate: false, token);
}
