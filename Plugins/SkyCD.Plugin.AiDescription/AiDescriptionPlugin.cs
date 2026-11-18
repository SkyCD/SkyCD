using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;

namespace SkyCD.Plugin.AiDescription;

public sealed class AiDescriptionPlugin : IMenuPluginCapability, IGeneratedDescriptionCapability, IDescriptionGenerationPlanner
{
    public const string CommandId = "ai-description.generate";
    public const string RegistryCommandId = "ai-description.registry";
    public const string PluginVersion = "1.0.0";
    private readonly string configurationPath;
    private readonly Func<DescriptionAgentOptions, DescriptionContext, CancellationToken, Task<GeneratedDescription>> generate;

    public AiDescriptionPlugin() : this(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "SkyCD", "ai-descriptions.json"), AcpDescriptionClient.GenerateAsync)
    { }

    public AiDescriptionPlugin(string configurationPath,
        Func<DescriptionAgentOptions, DescriptionContext, CancellationToken, Task<GeneratedDescription>> generate)
    {
        this.configurationPath = configurationPath;
        this.generate = generate;
    }

    public MediaMetadataSchema Schema => new("skycd.ai-description.metadata", 1,
        new[] { "AiDescription", "AiDescriptionAgent", "AiDescriptionModel", "AiDescriptionPromptVersion", "AiDescriptionLanguage", "AiDescriptionOrigin", "AiDescriptionProcessingDestination", "AiDescriptionContextFingerprint", "AiDescriptionSourceFingerprint" }
            .Select(key => new MediaMetadataField(key, MediaMetadataType.Text)).ToArray());

    public IReadOnlyCollection<MenuContribution> GetMenuContributions() =>
        [new(CommandId, "Generate AI description for selected file", "Tools", Order: 140),
         new("ai-description.configuration", "AI description configuration", "Tools", Order: 141),
         new(RegistryCommandId, "Browse cached ACP agent registry", "Tools", Order: 142)];

    public async Task ExecuteMenuCommandAsync(string commandId, MenuCommandContext context, CancellationToken cancellationToken = default)
    {
        if (commandId == RegistryCommandId)
        {
            if (context.HostApi is not IHostCommandApi registryHost)
                throw new DescriptionAgentException("the host cannot display ACP registry information");
            await BrowseRegistryAsync(registryHost, cancellationToken);
            return;
        }
        if (context.HostApi is not IDescriptionHostApi host)
            throw new DescriptionAgentException("select a catalog file in a host supporting explicit description generation");
        if (!File.Exists(configurationPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configurationPath)!);
            await File.WriteAllTextAsync(configurationPath,
                JsonSerializer.Serialize(new DescriptionAgentOptions(), new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        }
        if (commandId == "ai-description.configuration")
        {
            await host.NotifyAsync($"Configure the installed ACP executable, arguments, environment, optional AuthMethodId, model, language, prompt, limits, permitted sources and processing destination in {configurationPath}. Optionally download {AcpAgentRegistry.OfficialIndexUrl} to {Path.Combine(Path.GetDirectoryName(configurationPath)!, AcpAgentRegistry.FileName)} for offline browsing. Generation is disabled by default; no agent is installed automatically.", cancellationToken);
            return;
        }
        if (commandId != CommandId) return;
        var options = await LoadOptionsAsync(cancellationToken);
        var source = await host.GetDescriptionContextAsync(options.Sources, cancellationToken);
        await host.NotifyAsync($"Generating AI description; configured processing destination: {options.ProcessingDestination}. Local process transport does not imply local inference.", cancellationToken);
        var description = await generate(options, source, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await host.SaveGeneratedDescriptionAsync(source, description, cancellationToken);
        await host.NotifyAsync($"Saved AI-generated description ({description.Origin}; processing: {description.ProcessingDestination}).", cancellationToken);
    }

    public async Task<GeneratedDescription> GenerateAsync(DescriptionContext context, CancellationToken cancellationToken)
    {
        var options = await LoadOptionsAsync(cancellationToken);
        return await generate(options, context, cancellationToken);
    }

    /// <summary>
    /// Reports the locally configured provider without launching it or sending context. A missing, disabled
    /// or invalid configuration is reported as unavailable instead of failing the Properties view.
    /// </summary>
    public DescriptionGenerationPlan GetPlan()
    {
        try
        {
            var info = new FileInfo(configurationPath);
            if (!info.Exists)
            {
                return Unavailable("configuration is missing");
            }
            if (info.Length > 65536)
            {
                return Unavailable("configuration exceeds 64 KiB");
            }

            var options = JsonSerializer.Deserialize<DescriptionAgentOptions>(File.ReadAllText(configurationPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 16 });
            if (options is null)
            {
                return Unavailable("configuration is invalid");
            }

            var providerName = string.IsNullOrWhiteSpace(options.Executable)
                ? "ACP agent"
                : Path.GetFileName(options.Executable);
            if (!options.Enabled)
            {
                return new DescriptionGenerationPlan(false, providerName, PluginVersion, options.Model,
                    options.ProcessingDestination, options.Sources,
                    "generation is disabled; enable the configured ACP agent explicitly");
            }

            options.Validate();
            return new DescriptionGenerationPlan(true, providerName, PluginVersion, options.Model,
                options.ProcessingDestination, options.Sources);
        }
        catch (DescriptionAgentException exception)
        {
            return Unavailable(exception.Message);
        }
        catch (JsonException)
        {
            return Unavailable("configuration is invalid");
        }
        catch (IOException exception)
        {
            return Unavailable($"configuration cannot be read: {exception.GetType().Name}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return Unavailable($"configuration cannot be read: {exception.GetType().Name}");
        }
    }

    private static DescriptionGenerationPlan Unavailable(string reason) =>
        new(false, string.Empty, PluginVersion, null, "unknown", new DescriptionContextOptions(), reason);

    private async Task BrowseRegistryAsync(IHostCommandApi host, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetDirectoryName(configurationPath)!, AcpAgentRegistry.FileName);
        if (!File.Exists(path))
        {
            await host.NotifyAsync($"No cached ACP registry at {path}. Download {AcpAgentRegistry.OfficialIndexUrl} manually to browse agents offline. SkyCD never downloads, installs or launches registry entries automatically.", cancellationToken);
            return;
        }
        var registry = AcpAgentRegistry.Load(path);
        if (registry.Agents.Count == 0)
        {
            await host.NotifyAsync($"The cached ACP registry at {path} contains no supported entries. Configure an installed agent manually in {configurationPath}.", cancellationToken);
            return;
        }
        var listed = string.Join("; ", registry.Agents.Select(agent =>
            $"{Shorten(agent.Name, 40)} ({Shorten(agent.Id, 40)}) {agent.Version} [{string.Join('/', agent.Distributions)}]"));
        if (listed.Length > 3000) listed = listed[..3000] + "; ...";
        await host.NotifyAsync($"Cached ACP registry: {registry.Agents.Count} agents. {listed}. Browsing never installs or launches an agent; configure an installed executable in {configurationPath}.", cancellationToken);
    }

    private static string Shorten(string value, int limit) => value.Length <= limit ? value : value[..limit];

    private async Task<DescriptionAgentOptions> LoadOptionsAsync(CancellationToken cancellationToken)
    {
        var info = new FileInfo(configurationPath);
        if (!info.Exists || info.Length > 65536) throw new DescriptionAgentException("configuration is missing or exceeds 64 KiB");
        var options = JsonSerializer.Deserialize<DescriptionAgentOptions>(await File.ReadAllTextAsync(configurationPath, cancellationToken),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, MaxDepth = 16 })
            ?? throw new DescriptionAgentException("configuration is invalid");
        options.Validate();
        return options;
    }
}
