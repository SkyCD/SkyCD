using System;
using System.Threading.Tasks;
using CommandDotNet;
using SkyCD.SDK.Capabilities.Cli;

namespace SkyCD.Plugin.Feature.GenDesc.Cli;

/// <summary>Generates an .nfo file for a local file using the matching template.</summary>
[Command("nfo", Description = "Write an .nfo file generated from a local file's facts.")]
public sealed class GenDescNfoCommand : ICliPluginCapability, ISupportsJsonOutput
{
    public bool JsonOutput { get; set; }

    [DefaultCommand]
    public Task<int> Execute(
        string file,
        [Option("output", Description = "Write the .nfo to this path instead of next to the file; use - for stdout.")]
        string? output = null,
        [Option("template", Description = "Pin a template by its id.")]
        string? template = null,
        [Option("language", Description = "Template language code; defaults to the current UI language.")]
        string? language = null,
        [Option("mime", Description = "Override the MIME type used for template matching.")]
        string? mime = null,
        [Option("fact", Description = "Add or override a fact as key=value; repeatable.")]
        string[]? facts = null,
        [Option("force", Description = "Overwrite an existing output file.")]
        bool force = false) =>
        GenDescCliOperations.GenerateNfoAsync(file, output, template, language, mime, facts, force, JsonOutput,
            Console.Out, Console.Error);
}
