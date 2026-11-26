using System;
using System.Threading.Tasks;
using CommandDotNet;
using SkyCD.SDK.Capabilities.Cli;

namespace SkyCD.Plugin.Feature.GenDesc.Cli;

/// <summary>Prints the torrent-site description generated for a local file.</summary>
[Command("description", Description = "Print a site description generated from a local file's facts.")]
public sealed class GenDescDescriptionCommand : ICliPluginCapability, ISupportsJsonOutput
{
    public bool JsonOutput { get; set; }

    [DefaultCommand]
    public Task<int> Execute(
        string file,
        [Option("template", Description = "Pin a template by its id.")]
        string? template = null,
        [Option("language", Description = "Template language code; defaults to the current UI language.")]
        string? language = null,
        [Option("mime", Description = "Override the MIME type used for template matching.")]
        string? mime = null,
        [Option("fact", Description = "Add or override a fact as key=value; repeatable.")]
        string[]? facts = null) =>
        GenDescCliOperations.GenerateDescriptionAsync(file, template, language, mime, facts, JsonOutput,
            Console.Out, Console.Error);
}
