using System.Reflection;
using System.Text.Json;
using CommandDotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Runtime.Factories;
using PluginServiceProvider = SkyCD.Plugin.Runtime.DependencyInjection.ServiceProvider;

namespace SkyCD.Cli;

public sealed class CliHost(
    TextWriter stdout,
    TextWriter stderr,
    Func<Version, CancellationToken, Task<IReadOnlyList<DiscoveredPlugin>>>? pluginLoader = null,
    Func<string>? executableNameProvider = null)
{
    private const string DeprecatedListFormatsAlias = "list-formats";

    private sealed record SystemCommandNamespace(
        string BasePath,
        bool SupportsExtensions = false,
        string[]? Subcommands = null);

    private sealed record SystemCommandDefinition(
        string Path,
        bool HasSubcommands = false,
        bool SupportsExtensions = false);

    private static readonly string[] ExtensionPointBaseCommands =
    [
        "open",
        "convert"
    ];

    private static readonly Dictionary<string, string> DeprecatedCommandAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        [DeprecatedListFormatsAlias] = "fileformats list"
    };

    private static readonly SystemCommandNamespace[] SystemCommandNamespaces = DiscoverSystemCommandNamespaces();

    private static readonly SystemCommandDefinition[] SystemCommandDefinitions = BuildSystemCommandDefinitions();
    private static readonly HashSet<string> SystemCommandPathSet = SystemCommandDefinitions
        .Select(static definition => definition.Path)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly int MaxSystemCommandTokenCount = SystemCommandDefinitions
        .Select(static definition => definition.Path.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length)
        .DefaultIfEmpty(1)
        .Max();
    private static readonly Lock ConsoleRedirectLock = new();
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true
    };
    private readonly Func<string> executableNameProvider = executableNameProvider ?? ResolveExecutableName;
    private readonly Func<Version, CancellationToken, Task<IReadOnlyList<DiscoveredPlugin>>> pluginLoaderFactory =
        pluginLoader ?? LoadDiscoveredPluginsAsync;

    public async Task<CliRunResult> TryRunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0)
        {
            return new CliRunResult { Handled = false, ExitCode = CliExitCodes.Success };
        }

        var (jsonOutput, commandTokens) = ExtractJsonFlag(args);
        var normalized = Normalize(commandTokens);

        if (normalized.Count == 1 && IsHelpToken(normalized[0]))
        {
            await WriteHelpAsync(Array.Empty<string>(), jsonOutput);
            return new CliRunResult { Handled = true, ExitCode = CliExitCodes.Success };
        }

        if (TryGetBuiltInHelpCommand(normalized, out var helpCommand))
        {
            await WriteCommandHelpAsync(helpCommand, jsonOutput);
            return new CliRunResult { Handled = true, ExitCode = CliExitCodes.Success };
        }

        if (TryGetImplicitBuiltInHelpCommand(normalized, out var implicitHelpCommand))
        {
            await WriteCommandHelpAsync(implicitHelpCommand, jsonOutput);
            return new CliRunResult { Handled = true, ExitCode = CliExitCodes.Success };
        }

        if (normalized.Count == 1 && IsVersionToken(normalized[0]))
        {
            await stdout.WriteLineAsync(GetVersionText());
            return new CliRunResult { Handled = true, ExitCode = CliExitCodes.Success };
        }

        if (ShouldHandleWithSystemRunner(normalized) && !RequiresPluginRuntime(normalized))
        {
            var lightweightFileFormatManager = new FileFormatManager(Array.Empty<IFileFormatPluginCapability>());
            IHostCliApi lightweightHostApi = new FileFormatHostCliApi(lightweightFileFormatManager);
            using var lightweightRegistry = new CliContributionRegistry();
            lightweightRegistry.Register([]);
            var exitCode = await ExecuteSystemCommandAsync(
                normalized,
                jsonOutput,
                lightweightFileFormatManager,
                lightweightHostApi,
                lightweightRegistry,
                [],
                [],
                cancellationToken);
            return new CliRunResult { Handled = true, ExitCode = exitCode };
        }

        var pluginDirectories = GetPluginDirectories();
        var discoveredPlugins = await pluginLoaderFactory(new Version(3, 0, 0), cancellationToken);

        var serviceCollectionFactory = new ServiceCollectionFactory();
        var serviceProvider = BuildGlobalServiceProvider(
            discoveredPlugins,
            serviceCollectionFactory);
        using var _ = serviceProvider;
        var fileFormatManager = serviceProvider.GetRequiredService<FileFormatManager>();
        var hostApi = serviceProvider.GetRequiredService<IHostCliApi>();
        using var registry = new CliContributionRegistry();
        registry.Register(discoveredPlugins);

        if (registry.Errors.Count > 0)
        {
            foreach (var error in registry.Errors)
            {
                await stderr.WriteLineAsync(error);
            }

            return new CliRunResult { Handled = true, ExitCode = CliExitCodes.ConfigurationError };
        }

        if (ShouldHandleWithSystemRunner(normalized))
        {
            var exitCode = await ExecuteSystemCommandAsync(
                normalized,
                jsonOutput,
                fileFormatManager,
                hostApi,
                registry,
                discoveredPlugins,
                pluginDirectories,
                cancellationToken);
            return new CliRunResult { Handled = true, ExitCode = exitCode };
        }

        var pluginCommand = registry.ResolveCommand(normalized, out var consumedTokens);
        if (pluginCommand is not null)
        {
            var pluginArgs = normalized.Skip(consumedTokens).ToArray();
            var exitCode = await ExecutePluginCommandAsync(pluginCommand, pluginArgs, jsonOutput, hostApi, cancellationToken);
            return new CliRunResult { Handled = true, ExitCode = exitCode };
        }

        if (TryGetConcatenatedSubcommandHint(normalized, out var invalidCommand, out var suggestedCommand))
        {
            await stderr.WriteLineAsync($"Unknown command '{invalidCommand}'. Did you mean '{suggestedCommand}'?");
            return new CliRunResult { Handled = true, ExitCode = CliExitCodes.InvalidArguments };
        }

        return new CliRunResult { Handled = false, ExitCode = CliExitCodes.Success };
    }

    internal static IReadOnlyList<string> GetPluginDirectories()
    {
        var configured = Environment.GetEnvironmentVariable("SKYCD_PLUGIN_PATH");
        var fromAppSettings = TryReadPluginPathFromAppSettings();
        return BuildPluginDirectories(configured, fromAppSettings);
    }

    internal static IReadOnlyList<string> BuildPluginDirectories(string? configuredPluginPaths, string? appSettingsPluginPath)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(configuredPluginPaths))
        {
            foreach (var segment in configuredPluginPaths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                candidates.Add(Path.GetFullPath(segment));
            }
        }

        if (!string.IsNullOrWhiteSpace(appSettingsPluginPath))
        {
            candidates.Add(Path.GetFullPath(appSettingsPluginPath));
        }

        return candidates
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string? TryReadPluginPathFromAppSettings(string? appDataRoot = null)
    {
        var root = appDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var optionsPath = Path.Combine(root, "SkyCD", "options.json");
        if (!File.Exists(optionsPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(optionsPath));
            if (!document.RootElement.TryGetProperty("PluginPath", out var pluginPathElement))
            {
                return null;
            }

            var pluginPath = pluginPathElement.GetString();
            return string.IsNullOrWhiteSpace(pluginPath) ? null : pluginPath.Trim();
        }
        catch
        {
            return null;
        }
    }

    private async Task<CliExitCodes> ExecuteSystemCommandAsync(
        IReadOnlyList<string> args,
        bool jsonOutput,
        FileFormatManager fileFormatManager,
        IHostCliApi hostApi,
        CliContributionRegistry registry,
        IReadOnlyList<SkyCD.Plugin.Runtime.Discovery.DiscoveredPlugin> discoveredPlugins,
        IReadOnlyList<string> pluginDirectories,
        CancellationToken cancellationToken)
    {
        var runnerArgs = NormalizeSystemRunnerArgs(args);
        var context = new SkyCD.Cli.Execution.CliCommandExecutionContext(
            this,
            jsonOutput,
            fileFormatManager,
            hostApi,
            registry,
            discoveredPlugins,
            pluginDirectories,
            cancellationToken);

        try
        {
            SkyCD.Cli.Execution.CliCommandExecutionContextScope.Current = context;
            var appRunner = new AppRunner<SkyCD.Cli.Console.RootCommand>().UseDefaultMiddleware();
            int exitCode;
            lock (ConsoleRedirectLock)
            {
                var previousOut = System.Console.Out;
                var previousError = System.Console.Error;
                try
                {
                    System.Console.SetOut(TextWriter.Synchronized(stdout));
                    System.Console.SetError(TextWriter.Synchronized(stderr));
                    exitCode = appRunner.Run(runnerArgs);
                }
                finally
                {
                    System.Console.SetOut(previousOut);
                    System.Console.SetError(previousError);
                }
            }

            return Enum.IsDefined(typeof(CliExitCodes), exitCode)
                ? (CliExitCodes)exitCode
                : CliExitCodes.InvalidArguments;
        }
        catch (OperationCanceledException)
        {
            await stderr.WriteLineAsync("Operation cancelled.");
            return CliExitCodes.Cancelled;
        }
        catch (Exception exception)
        {
            await stderr.WriteLineAsync($"Command failed: {exception.Message}");
            return CliExitCodes.CommandFailed;
        }
        finally
        {
            SkyCD.Cli.Execution.CliCommandExecutionContextScope.Current = null;
        }
    }

    private static string[] NormalizeSystemRunnerArgs(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return [];
        }

        var normalized = new string[args.Count];
        for (var index = 0; index < args.Count; index++)
        {
            normalized[index] = args[index].Equals("/?", StringComparison.OrdinalIgnoreCase)
                ? "--help"
                : args[index];
        }

        return normalized;
    }

    private static bool ShouldHandleWithSystemRunner(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return false;
        }

        var first = args[0];
        return SystemCommandNamespaces.Any(command =>
                   command.BasePath.Equals(first, StringComparison.OrdinalIgnoreCase))
               || IsHelpToken(first)
               || IsVersionToken(first);
    }

    internal static IReadOnlySet<string> GetSystemCommandPaths()
    {
        return SystemCommandDefinitions
            .Select(static command => command.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static IReadOnlySet<string> GetExtensionPointPaths()
    {
        return SystemCommandDefinitions
            .Where(static command => command.SupportsExtensions || command.HasSubcommands)
            .Select(static command => command.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool RequiresPluginRuntime(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return false;
        }

        if (args.Any(IsHelpToken))
        {
            return false;
        }

        if (args.Any(IsVersionToken))
        {
            return false;
        }

        if (!TryFindMatchedSystemCommandPath(args, out var matchedCommandPath, out var consumedTokens))
        {
            return false;
        }

        var owningNamespace = SystemCommandNamespaces.FirstOrDefault(systemNamespace =>
            systemNamespace.BasePath.Equals(
                matchedCommandPath.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0],
                StringComparison.OrdinalIgnoreCase));

        if (owningNamespace is not null
            && owningNamespace.Subcommands is { Length: > 0 }
            && consumedTokens == 1)
        {
            return false;
        }

        if (SystemCommandDefinitions.Any(definition =>
                definition.Path.Equals(matchedCommandPath, StringComparison.OrdinalIgnoreCase)
                && definition.SupportsExtensions))
        {
            return true;
        }

        return true;
    }

    internal async Task<CliExitCodes> ExecuteOpenAsync(
        string? file,
        string? formatId,
        bool jsonOutput,
        FileFormatManager fileFormatManager,
        IHostCliApi hostApi,
        CliContributionRegistry registry,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            await stderr.WriteLineAsync("Missing required argument: <file>");
            return CliExitCodes.InvalidArguments;
        }

        var fullPath = Path.GetFullPath(file);
        if (!File.Exists(fullPath))
        {
            await stderr.WriteLineAsync($"File not found: {fullPath}");
            return CliExitCodes.InvalidArguments;
        }

        var resolvedFormat = ResolveFormatId(formatId, fullPath, fileFormatManager.GetOpenFormats(), "read");
        await using var source = File.OpenRead(fullPath);
        var readResult = await fileFormatManager.ReadAsync(new FileFormatReadRequest
        {
            FormatId = resolvedFormat,
            Source = source,
            FileName = Path.GetFileName(fullPath)
        }, cancellationToken);

        var extensionResult = await ExecuteExtensionsAsync(
            "open",
            BuildOpenExtensionArguments(file, formatId),
            jsonOutput,
            readResult.Payload,
            hostApi,
            registry,
            cancellationToken);
        if (!extensionResult.Success)
        {
            await stderr.WriteLineAsync(extensionResult.Error ?? "Plugin extension failed.");
            return CliExitCodes.CommandFailed;
        }

        if (jsonOutput)
        {
            await stdout.WriteLineAsync(JsonSerializer.Serialize(new
            {
                success = true,
                command = "open",
                file = fullPath,
                formatId = resolvedFormat
            }, jsonOptions));
        }
        else
        {
            await stdout.WriteLineAsync($"Opened '{fullPath}' as {resolvedFormat}.");
        }

        return CliExitCodes.Success;
    }

    internal async Task<CliExitCodes> ExecuteConvertAsync(
        string? inputPath,
        string? outputPath,
        string? inputFormat,
        string? outputFormat,
        bool jsonOutput,
        FileFormatManager fileFormatManager,
        IHostCliApi hostApi,
        CliContributionRegistry registry,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(outputPath))
        {
            await stderr.WriteLineAsync("Missing required options: --in <file> --out <file>");
            return CliExitCodes.InvalidArguments;
        }

        var fullInputPath = Path.GetFullPath(inputPath);
        var fullOutputPath = Path.GetFullPath(outputPath);

        if (!File.Exists(fullInputPath))
        {
            await stderr.WriteLineAsync($"Input file not found: {fullInputPath}");
            return CliExitCodes.InvalidArguments;
        }

        var resolvedInputFormat = ResolveFormatId(inputFormat, fullInputPath, fileFormatManager.GetOpenFormats(), "read");
        var resolvedOutputFormat = ResolveFormatId(outputFormat, fullOutputPath, fileFormatManager.GetSaveFormats(), "write");

        await using var source = File.OpenRead(fullInputPath);
        var readResult = await fileFormatManager.ReadAsync(new FileFormatReadRequest
        {
            FormatId = resolvedInputFormat,
            Source = source,
            FileName = Path.GetFileName(fullInputPath)
        }, cancellationToken);

        var extensionResult = await ExecuteExtensionsAsync(
            "convert",
            BuildConvertExtensionArguments(inputPath, outputPath, inputFormat, outputFormat),
            jsonOutput,
            readResult.Payload,
            hostApi,
            registry,
            cancellationToken);
        if (!extensionResult.Success)
        {
            await stderr.WriteLineAsync(extensionResult.Error ?? "Plugin extension failed.");
            return CliExitCodes.CommandFailed;
        }

        var payload = extensionResult.Payload ?? readResult.Payload
            ?? throw new InvalidOperationException("Source format returned empty payload.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath) ?? Directory.GetCurrentDirectory());

        await using var target = File.Create(fullOutputPath);
        await fileFormatManager.WriteAsync(new FileFormatWriteRequest
        {
            FormatId = resolvedOutputFormat,
            Target = target,
            FileName = Path.GetFileName(fullOutputPath),
            Payload = payload
        }, cancellationToken);

        if (jsonOutput)
        {
            await stdout.WriteLineAsync(JsonSerializer.Serialize(new
            {
                success = true,
                command = "convert",
                inputPath = fullInputPath,
                outputPath = fullOutputPath,
                inputFormatId = resolvedInputFormat,
                outputFormatId = resolvedOutputFormat
            }, jsonOptions));
        }
        else
        {
            await stdout.WriteLineAsync($"Converted '{fullInputPath}' ({resolvedInputFormat}) -> '{fullOutputPath}' ({resolvedOutputFormat}).");
        }

        return CliExitCodes.Success;
    }

    private static IReadOnlyList<string> BuildOpenExtensionArguments(string file, string? formatId)
    {
        var args = new List<string> { file };
        if (!string.IsNullOrWhiteSpace(formatId))
        {
            args.Add("--format");
            args.Add(formatId);
        }

        return args;
    }

    private static IReadOnlyList<string> BuildConvertExtensionArguments(
        string inputPath,
        string outputPath,
        string? inputFormat,
        string? outputFormat)
    {
        var args = new List<string>
        {
            "--in", inputPath,
            "--out", outputPath
        };

        if (!string.IsNullOrWhiteSpace(inputFormat))
        {
            args.Add("--in-format");
            args.Add(inputFormat);
        }

        if (!string.IsNullOrWhiteSpace(outputFormat))
        {
            args.Add("--format");
            args.Add(outputFormat);
        }

        return args;
    }

    internal async Task<CliExitCodes> ExecuteListFormatsAsync(
        bool jsonOutput,
        FileFormatManager fileFormatManager,
        IReadOnlyList<string> pluginDirectories)
    {
        var formats = fileFormatManager.GetOpenFormats()
            .Concat(fileFormatManager.GetSaveFormats())
            .GroupBy(static format => format.FormatId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static format => format.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (jsonOutput)
        {
            await stdout.WriteLineAsync(JsonSerializer.Serialize(formats, jsonOptions));
            return CliExitCodes.Success;
        }

        if (formats.Count == 0)
        {
            await stdout.WriteLineAsync("No file format plugins were found.");
            await stdout.WriteLineAsync($"Plugin directories checked: {string.Join(", ", pluginDirectories)}");
            return CliExitCodes.Success;
        }

        foreach (var format in formats)
        {
            await stdout.WriteLineAsync($"{format.FormatId,-16} {format.DisplayName} [{string.Join(", ", format.Extensions)}]");
        }

        return CliExitCodes.Success;
    }

    internal async Task<CliExitCodes> ExecutePluginsListAsync(
        bool jsonOutput,
        CliContributionRegistry registry,
        FileFormatManager fileFormatManager,
        IReadOnlyList<SkyCD.Plugin.Runtime.Discovery.DiscoveredPlugin> discoveredPlugins,
        IReadOnlyList<string> pluginDirectories)
    {
        var availableFormatIds = fileFormatManager.GetOpenFormats()
            .Concat(fileFormatManager.GetSaveFormats())
            .Select(static format => format.FormatId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var formatsByPlugin = discoveredPlugins
            .ToDictionary(
                static plugin => plugin.Id,
                plugin => plugin.Capabilities
                    .OfType<IFileFormatPluginCapability>()
                    .Select(static capability => capability.SupportedFormat.FormatId)
                    .Where(formatId => availableFormatIds.Contains(formatId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static id => id)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase)
            .Where(static item => item.Value.Length > 0)
            .ToDictionary(
                static item => item.Key,
                static item => item.Value,
                StringComparer.OrdinalIgnoreCase);

        var pluginInfo = discoveredPlugins
            .Select(plugin => new
            {
                PluginId = plugin.Id,
                DisplayName = plugin.Name,
                Capabilities = plugin.Capabilities.Select(static capability => capability.GetType().Name).OrderBy(static name => name).ToArray(),
                Formats = formatsByPlugin.TryGetValue(plugin.Id, out var formats)
                    ? formats
                    : Array.Empty<string>()
            })
            .OrderBy(static plugin => plugin.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (jsonOutput)
        {
            await stdout.WriteLineAsync(JsonSerializer.Serialize(new
            {
                plugins = pluginInfo,
                cliCommands = registry.CommandPaths.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
                pluginDirectories = pluginDirectories
            }, jsonOptions));
            return CliExitCodes.Success;
        }

        if (pluginInfo.Count == 0)
        {
            await stdout.WriteLineAsync("No plugins loaded.");
        }
        else
        {
            foreach (var plugin in pluginInfo)
            {
                var formats = plugin.Formats.Length == 0 ? "-" : string.Join(", ", plugin.Formats);
                await stdout.WriteLineAsync($"{plugin.PluginId} ({plugin.DisplayName})");
                await stdout.WriteLineAsync($"  capabilities: {string.Join(", ", plugin.Capabilities)}");
                await stdout.WriteLineAsync($"  formats: {formats}");
            }
        }

        var pluginCommands = registry.CommandPaths.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (pluginCommands.Length > 0)
        {
            await stdout.WriteLineAsync("Plugin CLI commands:");
            foreach (var path in pluginCommands)
            {
                await stdout.WriteLineAsync($"  {path}");
            }
        }

        await stdout.WriteLineAsync($"Plugin directories checked: {string.Join(", ", pluginDirectories)}");

        return CliExitCodes.Success;
    }

    private async Task<CliExitCodes> WriteBuiltInHelpAsync(string command, bool jsonOutput)
    {
        await WriteCommandHelpAsync(command, jsonOutput);
        return CliExitCodes.Success;
    }

    private async Task<CliExitCodes> ExecutePluginCommandAsync(
        RegisteredCliContribution command,
        IReadOnlyList<string> pluginArgs,
        bool jsonOutput,
        IHostCliApi hostApi,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteWithTimeoutAsync(
            token => command.Capability.ExecuteCliCommandAsync(new CliCommandContext
            {
                CommandPath = command.Contribution.CommandPath,
                CommandId = command.Contribution.CommandId,
                Arguments = pluginArgs,
                JsonOutput = jsonOutput,
                HostApi = hostApi
            }, token),
            cancellationToken);

        if (!result.Success)
        {
            await stderr.WriteLineAsync(result.Error ?? "Plugin command failed.");
            return CliExitCodes.CommandFailed;
        }

        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            await stdout.WriteLineAsync(result.Output);
        }
        else if (jsonOutput)
        {
            await stdout.WriteLineAsync(JsonSerializer.Serialize(new
            {
                success = true,
                command = command.Contribution.CommandPath
            }, jsonOptions));
        }

        return CliExitCodes.Success;
    }

    private async Task<CliCommandResult> ExecuteExtensionsAsync(
        string extensionPoint,
        IReadOnlyList<string> args,
        bool jsonOutput,
        object? payload,
        IHostCliApi hostApi,
        CliContributionRegistry registry,
        CancellationToken cancellationToken)
    {
        var currentPayload = payload;
        foreach (var extension in registry.ResolveExtensions(extensionPoint))
        {
            var result = await ExecuteWithTimeoutAsync(
                token => extension.Capability.ExecuteCliCommandAsync(new CliCommandContext
                {
                    CommandPath = extension.Contribution.CommandPath,
                    CommandId = extension.Contribution.CommandId,
                    Arguments = args,
                    JsonOutput = jsonOutput,
                    Payload = currentPayload,
                    HostApi = hostApi
                }, token),
                cancellationToken);

            if (!result.Success)
            {
                return result;
            }

            if (result.Payload is not null)
            {
                currentPayload = result.Payload;
            }
        }

        return new CliCommandResult
        {
            Success = true,
            Payload = currentPayload
        };
    }

    private static async Task<CliCommandResult> ExecuteWithTimeoutAsync(
        Func<CancellationToken, Task<CliCommandResult>> executor,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            return await executor(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CliCommandResult
            {
                Success = false,
                Error = "Plugin CLI handler timed out after 5 seconds."
            };
        }
        catch (Exception exception)
        {
            return new CliCommandResult
            {
                Success = false,
                Error = exception.Message
            };
        }
    }

    private async Task WriteHelpAsync(IEnumerable<string> pluginCommands, bool jsonOutput)
    {
        var executableName = NormalizeExecutableName(executableNameProvider());
        var builtIn = new[]
        {
            new { Name = "open", Description = "Open and validate a catalog file." },
            new { Name = "convert", Description = "Convert a catalog between supported formats." },
            new { Name = "fileformats", Description = "Work with file format handlers." },
            new { Name = "plugins", Description = "Inspect loaded plugins and capabilities." }
        };

        if (jsonOutput)
        {
            await stdout.WriteLineAsync(JsonSerializer.Serialize(new
            {
                description = "SkyCD command line interface",
                usage = $"{executableName} [options] [command]",
                options = new[]
                {
                    "--help     Display this help",
                    "--version  Display application version",
                    "--json     Use JSON output where supported"
                },
                builtInCommands = builtIn.Select(static command => new { command.Name, command.Description }).ToArray(),
                commandHelpHint = $"Use `{executableName} <command> --help` for command-specific options.",
                pluginCommands = pluginCommands.OrderBy(static command => command, StringComparer.OrdinalIgnoreCase).ToArray()
            }, jsonOptions));
            return;
        }

        await stdout.WriteLineAsync("Description:");
        await stdout.WriteLineAsync("  SkyCD command line interface");
        await stdout.WriteLineAsync("");
        await stdout.WriteLineAsync("Usage:");
        await stdout.WriteLineAsync($"  {executableName} [options] [command]");
        await stdout.WriteLineAsync("");
        await stdout.WriteLineAsync("Options:");
        await stdout.WriteLineAsync("  --help       Display this help");
        await stdout.WriteLineAsync("  --version    Display application version");
        await stdout.WriteLineAsync("  --json       Use JSON output where supported");
        await stdout.WriteLineAsync("");
        await stdout.WriteLineAsync("Commands:");
        foreach (var command in builtIn)
        {
            await stdout.WriteLineAsync($"  {command.Name,-12} {command.Description}");
        }

        await stdout.WriteLineAsync("");
        await stdout.WriteLineAsync("Notes:");
        await stdout.WriteLineAsync($"  Use `{executableName} <command> --help` for command-specific options.");

        var contributedCommands = pluginCommands.OrderBy(static command => command, StringComparer.OrdinalIgnoreCase).ToArray();
        if (contributedCommands.Length > 0)
        {
            await stdout.WriteLineAsync("");
            await stdout.WriteLineAsync("Plugin Commands:");
        }

        foreach (var pluginCommand in contributedCommands)
        {
            await stdout.WriteLineAsync($"  {pluginCommand}");
        }
    }

    private async Task WriteCommandHelpAsync(string command, bool jsonOutput)
    {
        var executableName = NormalizeExecutableName(executableNameProvider());
        var (usage, options, notes) = command switch
        {
            "open" => (
                $"{executableName} open <file> [--format <id>] [--json]",
                new[]
                {
                    "--format <id>  Override inferred format id",
                    "--json         Use JSON output where supported",
                    "--help         Display this help"
                },
                new[]
                {
                    "open infers format from file extension by default."
                }),
            "convert" => (
                $"{executableName} convert --in <file> --out <file> [--in-format <id>] [--format <id>] [--json]",
                new[]
                {
                    "--in <file>        Input file path (required)",
                    "--out <file>       Output file path (required)",
                    "--in-format <id>   Override inferred input format id",
                    "--format <id>      Override inferred output format id",
                    "--json             Use JSON output where supported",
                    "--help             Display this help"
                },
                new[]
                {
                    $"Format ids are listed by `{executableName} fileformats list`."
                }),
            "fileformats" => (
                $"{executableName} fileformats <subcommand> [options]",
                new[]
                {
                    "list     List available read/write format handlers",
                    "--help   Display this help"
                },
                Array.Empty<string>()),
            "fileformats list" => (
                $"{executableName} fileformats list [--json]",
                new[]
                {
                    "--json   Use JSON output where supported",
                    "--help   Display this help"
                },
                Array.Empty<string>()),
            "plugins" => (
                $"{executableName} plugins <subcommand> [options]",
                new[]
                {
                    "list     List loaded plugins, capabilities, and commands",
                    "--help   Display this help"
                },
                Array.Empty<string>()),
            "plugins list" => (
                $"{executableName} plugins list [--json]",
                new[]
                {
                    "--json   Use JSON output where supported",
                    "--help   Display this help"
                },
                Array.Empty<string>()),
            _ => throw new InvalidOperationException($"Unsupported help command: {command}")
        };

        if (jsonOutput)
        {
            await stdout.WriteLineAsync(JsonSerializer.Serialize(new
            {
                command,
                usage,
                options,
                notes
            }, jsonOptions));
            return;
        }

        await stdout.WriteLineAsync("Description:");
        await stdout.WriteLineAsync($"  {command} command");
        await stdout.WriteLineAsync("");
        await stdout.WriteLineAsync("Usage:");
        await stdout.WriteLineAsync($"  {usage}");
        await stdout.WriteLineAsync("");
        await stdout.WriteLineAsync("Options:");
        foreach (var option in options)
        {
            await stdout.WriteLineAsync($"  {option}");
        }

        if (notes.Length == 0)
        {
            return;
        }

        await stdout.WriteLineAsync("");
        await stdout.WriteLineAsync("Notes:");
        foreach (var note in notes)
        {
            await stdout.WriteLineAsync($"  {note}");
        }
    }

    private static bool TrySplitBuiltIn(
        IReadOnlyList<string> args,
        out string command,
        out IReadOnlyList<string> remaining)
    {
        command = string.Empty;
        remaining = [];

        if (args.Count == 1 && IsHelpToken(args[0]))
        {
            command = "help";
            return true;
        }

        if (args.Count == 1 && IsVersionToken(args[0]))
        {
            command = "version";
            return true;
        }

        if (args.Count > 0 && args[0].Equals("open", StringComparison.OrdinalIgnoreCase))
        {
            command = "open";
            remaining = args.Skip(1).ToArray();
            return true;
        }

        if (args.Count > 0 && args[0].Equals("convert", StringComparison.OrdinalIgnoreCase))
        {
            command = "convert";
            remaining = args.Skip(1).ToArray();
            return true;
        }

        if (args.Count == 1 && args[0].Equals("plugins", StringComparison.OrdinalIgnoreCase))
        {
            command = "plugins";
            return true;
        }

        if (args.Count >= 2 &&
            args[0].Equals("plugins", StringComparison.OrdinalIgnoreCase) &&
            args[1].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            command = "plugins list";
            remaining = args.Skip(2).ToArray();
            return true;
        }

        if (args.Count == 1 && args[0].Equals("fileformats", StringComparison.OrdinalIgnoreCase))
        {
            command = "fileformats";
            return true;
        }

        if (args.Count >= 2 &&
            args[0].Equals("fileformats", StringComparison.OrdinalIgnoreCase) &&
            args[1].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            command = "fileformats list";
            remaining = args.Skip(2).ToArray();
            return true;
        }

        return false;
    }

    private static bool TryGetBuiltInHelpCommand(IReadOnlyList<string> args, out string command)
    {
        command = string.Empty;

        foreach (var definition in SystemCommandDefinitions.OrderByDescending(static item =>
                     item.Path.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length))
        {
            var commandTokens = definition.Path.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (args.Count <= commandTokens.Length || !StartsWithTokens(args, commandTokens))
            {
                continue;
            }

            if (!ContainsOnlyHelpTokens(args.Skip(commandTokens.Length)))
            {
                continue;
            }

            command = definition.Path;
            return true;
        }

        return false;
    }

    private static bool TryGetImplicitBuiltInHelpCommand(IReadOnlyList<string> args, out string command)
    {
        command = string.Empty;
        if (args.Count != 1)
        {
            return false;
        }

        var matchingNamespace = SystemCommandNamespaces.FirstOrDefault(systemNamespace =>
            systemNamespace.Subcommands is { Length: > 0 }
            && systemNamespace.BasePath.Equals(args[0], StringComparison.OrdinalIgnoreCase));
        if (matchingNamespace is not null)
        {
            command = matchingNamespace.BasePath;
            return true;
        }

        return false;
    }

    private static bool TryGetConcatenatedSubcommandHint(
        IReadOnlyList<string> args,
        out string invalidCommand,
        out string suggestedCommand)
    {
        invalidCommand = string.Empty;
        suggestedCommand = string.Empty;

        if (args.Count == 0)
        {
            return false;
        }

        var first = args[0];
        if (DeprecatedCommandAliases.TryGetValue(first, out var deprecatedAliasTarget))
        {
            invalidCommand = first;
            suggestedCommand = deprecatedAliasTarget;
            return true;
        }

        if (args.Count != 1)
        {
            return false;
        }

        var candidate = first;
        foreach (var systemNamespace in SystemCommandNamespaces.Where(static ns => ns.Subcommands is { Length: > 0 }))
        {
            foreach (var subcommand in systemNamespace.Subcommands!)
            {
                var concatenated = $"{systemNamespace.BasePath}{subcommand}";
                if (!candidate.Equals(concatenated, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                invalidCommand = candidate;
                suggestedCommand = $"{systemNamespace.BasePath} {subcommand}";
                return true;
            }
        }

        return false;
    }

    private static (bool JsonOutput, IReadOnlyList<string> Tokens) ExtractJsonFlag(IReadOnlyList<string> args)
    {
        var json = false;
        var tokens = new List<string>(args.Count);

        foreach (var token in args)
        {
            if (token.Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                json = true;
                continue;
            }

            tokens.Add(token);
        }

        return (json, tokens);
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string> args)
    {
        return args
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .Select(static token => token.Trim())
            .ToArray();
    }

    private static bool IsHelpToken(string token)
    {
        return token.Equals("--help", StringComparison.OrdinalIgnoreCase)
               || token.Equals("-h", StringComparison.OrdinalIgnoreCase)
               || token.Equals("/?", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVersionToken(string token)
    {
        return token.Equals("--version", StringComparison.OrdinalIgnoreCase)
               || token.Equals("-v", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsOnlyHelpTokens(IEnumerable<string> args)
    {
        var sawAny = false;
        foreach (var token in args)
        {
            if (!IsHelpToken(token))
            {
                return false;
            }

            sawAny = true;
        }

        return sawAny;
    }

    private static string ResolveExecutableName()
    {
        var arg0 = Environment.GetCommandLineArgs().FirstOrDefault();
        return NormalizeExecutableName(arg0);
    }

    private static string NormalizeExecutableName(string? executableNameCandidate)
    {
        if (string.IsNullOrWhiteSpace(executableNameCandidate))
        {
            return "skycd";
        }

        var executableName = Path.GetFileName(executableNameCandidate);
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return "skycd";
        }

        if (OperatingSystem.IsWindows())
        {
            if (executableName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return Path.ChangeExtension(executableName, ".exe");
            }

            if (!Path.HasExtension(executableName))
            {
                return $"{executableName}.exe";
            }
        }

        return executableName;
    }

    private static bool TryFindMatchedSystemCommandPath(
        IReadOnlyList<string> args,
        out string matchedCommandPath,
        out int consumedTokens)
    {
        matchedCommandPath = string.Empty;
        consumedTokens = 0;

        if (args.Count == 0)
        {
            return false;
        }

        var maxTokens = Math.Min(args.Count, MaxSystemCommandTokenCount);
        for (var tokenCount = maxTokens; tokenCount >= 1; tokenCount--)
        {
            var candidate = string.Join(' ', args.Take(tokenCount));
            if (!SystemCommandPathSet.Contains(candidate))
            {
                continue;
            }

            matchedCommandPath = candidate;
            consumedTokens = tokenCount;
            return true;
        }

        return false;
    }

    private static bool StartsWithTokens(IReadOnlyList<string> args, IReadOnlyList<string> expectedPrefix)
    {
        if (args.Count < expectedPrefix.Count)
        {
            return false;
        }

        for (var index = 0; index < expectedPrefix.Count; index++)
        {
            if (!args[index].Equals(expectedPrefix[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static SystemCommandNamespace[] DiscoverSystemCommandNamespaces()
    {
        var rootCommandType = typeof(SkyCD.Cli.Console.RootCommand);
        var discoveredNamespaces = new List<SystemCommandNamespace>();

        foreach (var subcommandType in GetSubcommandTypes(rootCommandType))
        {
            var basePath = GetDeclaredCommandName(subcommandType);
            if (string.IsNullOrWhiteSpace(basePath))
            {
                continue;
            }

            var subcommands = GetSubcommandTypes(subcommandType)
                .Select(GetDeclaredCommandName)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .ToArray()!;

            discoveredNamespaces.Add(new SystemCommandNamespace(
                basePath,
                SupportsExtensions: ExtensionPointBaseCommands.Contains(basePath, StringComparer.OrdinalIgnoreCase),
                Subcommands: subcommands.Length == 0 ? null : subcommands));
        }

        return discoveredNamespaces
            .OrderBy(static systemNamespace => systemNamespace.BasePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<Type> GetSubcommandTypes(Type commandType)
    {
        return commandType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(static property => property.GetCustomAttribute<SubcommandAttribute>() is not null)
            .Select(static property => property.PropertyType);
    }

    private static string GetDeclaredCommandName(Type commandType)
    {
        var attributeData = commandType.CustomAttributes.FirstOrDefault(attribute =>
            attribute.AttributeType == typeof(CommandAttribute));
        if (attributeData is null)
        {
            return string.Empty;
        }

        if (attributeData.ConstructorArguments.Count > 0
            && attributeData.ConstructorArguments[0].ArgumentType == typeof(string)
            && attributeData.ConstructorArguments[0].Value is string ctorValue
            && !string.IsNullOrWhiteSpace(ctorValue))
        {
            return ctorValue.Trim();
        }

        var commandAttribute = commandType.GetCustomAttribute<CommandAttribute>();
        var namedName = commandAttribute?.Name;
        return string.IsNullOrWhiteSpace(namedName) ? string.Empty : namedName.Trim();
    }

    private static SystemCommandDefinition[] BuildSystemCommandDefinitions()
    {
        var definitions = new List<SystemCommandDefinition>();

        foreach (var systemNamespace in SystemCommandNamespaces)
        {
            var hasSubcommands = systemNamespace.Subcommands is { Length: > 0 };
            definitions.Add(new SystemCommandDefinition(
                systemNamespace.BasePath,
                HasSubcommands: hasSubcommands,
                SupportsExtensions: systemNamespace.SupportsExtensions));

            if (!hasSubcommands)
            {
                continue;
            }

            definitions.AddRange(systemNamespace.Subcommands!.Select(subcommand =>
                new SystemCommandDefinition($"{systemNamespace.BasePath} {subcommand}")));
        }

        return definitions.ToArray();
    }

    private static string ResolveFormatId(
        string? explicitFormatId,
        string path,
        IReadOnlyList<FileFormatDescriptor> formats,
        string operation)
    {
        if (!string.IsNullOrWhiteSpace(explicitFormatId))
        {
            if (formats.Any(format => format.FormatId.Equals(explicitFormatId, StringComparison.OrdinalIgnoreCase)))
            {
                return explicitFormatId;
            }

            throw new InvalidOperationException($"Format '{explicitFormatId}' does not support {operation}.");
        }

        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new InvalidOperationException($"Unable to infer format for '{path}'. Provide --format explicitly.");
        }

        var byExtension = formats.FirstOrDefault(format =>
            format.Extensions.Any(candidate => candidate.Equals(extension, StringComparison.OrdinalIgnoreCase)));
        if (byExtension is null)
        {
            throw new InvalidOperationException($"No format handler registered for '{extension}' ({operation}).");
        }

        return byExtension.FormatId;
    }

    private static PluginServiceProvider BuildGlobalServiceProvider(
        IReadOnlyList<DiscoveredPlugin> plugins,
        ServiceCollectionFactory serviceCollectionFactory)
    {
        var pluginList = plugins.ToList();
        var pluginById = pluginList.ToDictionary(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
        IServiceCollection mergedServices = serviceCollectionFactory.BuildCommonServiceCollection();
        mergedServices.AddSingleton<IHostCliApi, FileFormatHostCliApi>();

        mergedServices.AddSingleton<IReadOnlyList<DiscoveredPlugin>>(pluginList);
        mergedServices.AddSingleton<IReadOnlyCollection<DiscoveredPlugin>>(pluginList);
        mergedServices.AddSingleton<IReadOnlyDictionary<string, DiscoveredPlugin>>(pluginById);

        foreach (var plugin in pluginList)
        {
            var pluginDescriptors = serviceCollectionFactory.BuildPluginServiceCollection(plugin);
            foreach (var descriptor in pluginDescriptors)
            {
                mergedServices.Add(descriptor);
            }
        }

        PluginServiceProvider.Instance.Import(mergedServices);
        return PluginServiceProvider.Instance;
    }

    private static Task<IReadOnlyList<DiscoveredPlugin>> LoadDiscoveredPluginsAsync(
        Version hostVersion,
        CancellationToken cancellationToken = default)
    {
        var pluginDirectories = GetPluginDirectories();
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddSimpleConsole(options =>
            {
                options.ColorBehavior = LoggerColorBehavior.Disabled;
                options.SingleLine = true;
                options.TimestampFormat = string.Empty;
            });
        });

        var pluginManager = new PluginManager(
            loggerFactory.CreateLogger<PluginManager>(),
            new AssembliesListFactory(loggerFactory.CreateLogger("SkyCD.Plugin.Runtime.Factories.AssembliesListFactory")),
            new DiscoveredPluginFactory());
        pluginManager.Discover(string.Join(Path.PathSeparator, pluginDirectories), hostVersion);
        return Task.FromResult<IReadOnlyList<DiscoveredPlugin>>(pluginManager.Plugins.ToList());
    }

    private static string GetVersionText()
    {
        var version = typeof(CliHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? typeof(CliHost).Assembly.GetName().Version?.ToString()
                      ?? "unknown";
        return $"SkyCD {version}";
    }

    private sealed class FileFormatHostCliApi(FileFormatManager fileFormatManager) : IHostCliApi
    {
        public IReadOnlyList<FileFormatDescriptor> GetReadableFormats()
        {
            return fileFormatManager.GetReadableFormats();
        }

        public IReadOnlyList<FileFormatDescriptor> GetWritableFormats()
        {
            return fileFormatManager.GetWritableFormats();
        }

        public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request, CancellationToken cancellationToken = default)
        {
            return fileFormatManager.ReadAsync(request, cancellationToken);
        }

        public Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request, CancellationToken cancellationToken = default)
        {
            return fileFormatManager.WriteAsync(request, cancellationToken);
        }
    }
}
