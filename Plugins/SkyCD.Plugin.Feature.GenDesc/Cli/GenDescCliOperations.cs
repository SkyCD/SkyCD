using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SkyCD.Plugin.Feature.GenDesc.Exceptions;
using SkyCD.Plugin.Feature.GenDesc.Models;
using SkyCD.Plugin.Feature.GenDesc.Parsing;
using SkyCD.Plugin.Feature.GenDesc.Rendering;
using SkyCD.SDK.Capabilities.GenDesc;

namespace SkyCD.Plugin.Feature.GenDesc.Cli;

/// <summary>Host-independent implementation behind the GenDesc CLI commands.</summary>
internal static class GenDescCliOperations
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static async Task<int> GenerateNfoAsync(
        string file,
        string? output,
        string? templateId,
        string? language,
        string? mimeType,
        IReadOnlyList<string>? facts,
        bool force,
        bool jsonOutput,
        TextWriter stdout,
        TextWriter stderr,
        string? templateDirectory = null)
    {
        var attempt = await RenderAsync(file, TemplateInfo.NfoKind, templateId, language, mimeType, facts,
            stderr, templateDirectory);
        if (attempt.ExitCode != GenDescCliExitCodes.Success)
        {
            return attempt.ExitCode;
        }

        if (string.Equals(output, "-", StringComparison.Ordinal))
        {
            await WriteNfoResultAsync(stdout, jsonOutput, attempt, "-");
            return GenDescCliExitCodes.Success;
        }

        string target;
        try
        {
            target = string.IsNullOrWhiteSpace(output)
                ? Path.ChangeExtension(attempt.SourcePath, ".nfo")
                : Path.GetFullPath(output);
        }
        catch (ArgumentException exception)
        {
            await stderr.WriteLineAsync(exception.Message);
            return GenDescCliExitCodes.InvalidArguments;
        }

        if (!force && File.Exists(target))
        {
            await stderr.WriteLineAsync($"Output '{target}' already exists. Pass --force to overwrite it.");
            return GenDescCliExitCodes.CommandFailed;
        }

        try
        {
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(target, attempt.Output!, Utf8NoBom);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                              or ArgumentException)
        {
            await stderr.WriteLineAsync($"Cannot write '{target}': {exception.Message}");
            return GenDescCliExitCodes.CommandFailed;
        }

        await WriteNfoResultAsync(stdout, jsonOutput, attempt, target);
        return GenDescCliExitCodes.Success;
    }

    public static async Task<int> GenerateDescriptionAsync(
        string file,
        string? templateId,
        string? language,
        string? mimeType,
        IReadOnlyList<string>? facts,
        bool jsonOutput,
        TextWriter stdout,
        TextWriter stderr,
        string? templateDirectory = null)
    {
        var attempt = await RenderAsync(file, TemplateInfo.DescriptionKind, templateId, language, mimeType, facts,
            stderr, templateDirectory);
        if (attempt.ExitCode != GenDescCliExitCodes.Success)
        {
            return attempt.ExitCode;
        }

        if (jsonOutput)
        {
            await stdout.WriteLineAsync(JsonSerializer.Serialize(new
            {
                success = true,
                command = "gendesc description",
                file = attempt.SourcePath,
                template = attempt.Rendered!.Template.Id,
                category = attempt.Rendered.Category,
                categoryTitle = attempt.Rendered.CategoryTitle,
                text = attempt.Output
            }, JsonOptions));
        }
        else
        {
            await stdout.WriteLineAsync(attempt.Output);
        }

        return GenDescCliExitCodes.Success;
    }

    private static async Task<RenderAttempt> RenderAsync(
        string file,
        string kind,
        string? templateId,
        string? language,
        string? mimeType,
        IReadOnlyList<string>? facts,
        TextWriter stderr,
        string? templateDirectory)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            await stderr.WriteLineAsync("A file path is required.");
            return RenderAttempt.Failed(GenDescCliExitCodes.InvalidArguments);
        }

        string sourcePath;
        try
        {
            sourcePath = Path.GetFullPath(file);
        }
        catch (ArgumentException exception)
        {
            await stderr.WriteLineAsync(exception.Message);
            return RenderAttempt.Failed(GenDescCliExitCodes.InvalidArguments);
        }

        GenDescEntryContext context;
        try
        {
            context = await GenDescRenderer.FromFileAsync(sourcePath);
        }
        catch (Exception exception) when (exception is FileNotFoundException or ArgumentException
                                              or NotSupportedException)
        {
            await stderr.WriteLineAsync(exception.Message);
            return RenderAttempt.Failed(GenDescCliExitCodes.CommandFailed);
        }

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in context.Facts)
        {
            merged[pair.Key] = pair.Value;
        }

        if (!TryApplyFacts(facts, merged, out var factError))
        {
            await stderr.WriteLineAsync(factError);
            return RenderAttempt.Failed(GenDescCliExitCodes.InvalidArguments);
        }

        var effectiveMime = string.IsNullOrWhiteSpace(mimeType) ? context.MimeType : mimeType.Trim();
        if (!string.IsNullOrWhiteSpace(mimeType))
        {
            merged["MimeType"] = effectiveMime;
        }

        var mergedContext = new GenDescEntryContext(context.FileName, context.Extension, effectiveMime, merged);
        try
        {
            var rendered = GenDescRenderer.Render(GenDescTemplatePaths.LoadAndSeed(templateDirectory), mergedContext,
                kind, language, templateId);
            return new RenderAttempt(sourcePath, rendered.Text, rendered, GenDescCliExitCodes.Success);
        }
        catch (Exception exception) when (exception is GenDescGenerationException or TemplateSyntaxException
                                              or IOException or UnauthorizedAccessException)
        {
            await stderr.WriteLineAsync(exception.Message);
            return RenderAttempt.Failed(GenDescCliExitCodes.CommandFailed);
        }
    }

    private static async Task WriteNfoResultAsync(TextWriter stdout, bool jsonOutput, RenderAttempt attempt,
        string target)
    {
        if (jsonOutput)
        {
            await stdout.WriteLineAsync(JsonSerializer.Serialize(new
            {
                success = true,
                command = "gendesc nfo",
                file = attempt.SourcePath,
                output = target,
                template = attempt.Rendered!.Template.Id,
                category = attempt.Rendered.Category,
                categoryTitle = attempt.Rendered.CategoryTitle,
                text = attempt.Output
            }, JsonOptions));
        }
        else if (string.Equals(target, "-", StringComparison.Ordinal))
        {
            await stdout.WriteLineAsync(attempt.Output);
        }
        else
        {
            await stdout.WriteLineAsync(target);
        }
    }

    private static bool TryApplyFacts(IReadOnlyList<string>? facts, IDictionary<string, string> target,
        out string error)
    {
        error = string.Empty;
        if (facts is null)
        {
            return true;
        }

        foreach (var fact in facts)
        {
            var separator = fact.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0 || separator == fact.Length - 1)
            {
                error = $"Each --fact must be key=value, but got '{fact}'.";
                return false;
            }

            target[fact[..separator].Trim()] = fact[(separator + 1)..].Trim();
        }

        return true;
    }

    private sealed record RenderAttempt(string SourcePath, string? Output, GenDescResult? Rendered, int ExitCode)
    {
        public static RenderAttempt Failed(int exitCode) => new(string.Empty, null, null, exitCode);
    }
}
