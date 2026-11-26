using SkyCD.Plugin.Metadata.VideoProjects.Models;

namespace SkyCD.Plugin.Metadata.VideoProjects.Parsing;

internal static class ResolveTemplateReader
{
    public static ProjectFacts? Read(Stream source, CancellationToken token) =>
        ResolveArchive.Read(source, isTemplate: true, token);
}
