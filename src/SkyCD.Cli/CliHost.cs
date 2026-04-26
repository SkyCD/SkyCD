using System.Reflection;
using System.Text.Json;
using CommandDotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Runtime.Factories;
using PluginServiceProvider = SkyCD.Plugin.Runtime.DependencyInjection.ServiceProvider;

namespace SkyCD.Cli;

public sealed class CliHost(
    TextWriter stdout,
    TextWriter stderr,
    Func<Version, CancellationToken, Task<IReadOnlyList<DiscoveredPlugin>>>? pluginLoader = null)
{
    private sealed record SystemCommandNamespace(
        string BasePath,
        string[]? Subcommands = null);

    private static readonly SystemCommandNamespace[] SystemCommandNamespaces = DiscoverSystemCommandNamespaces();
    private static readonly string[] SystemCommandPaths = BuildSystemCommandPaths();
    private static readonly HashSet<string> SystemCommandPathSet = SystemCommandPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock ConsoleRedirectLock = new();
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true
    };
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
        var routedTokens = ExpandCompositeCommandToken(normalized);

        if (routedTokens.Count == 1 && IsVersionToken(routedTokens[0]))
        {
            await stdout.WriteLineAsync(GetVersionText());
            return new CliRunResult { Handled = true, ExitCode = CliExitCodes.Success };
        }

        if (TryGetConcatenatedSubcommandHint(routedTokens, out var invalidCommandEarly, out var suggestedCommandEarly))
        {
            await stderr.WriteLineAsync($"Unknown command '{invalidCommandEarly}'. Did you mean '{suggestedCommandEarly}'?");
            return new CliRunResult { Handled = true, ExitCode = CliExitCodes.InvalidArguments };
        }

        if (ShouldHandleWithSystemRunner(routedTokens) && CanRunWithoutPluginRuntime(routedTokens))
        {
            var systemRunnerTokens = NormalizeImplicitNamespaceHelp(routedTokens);
            var lightweightFileFormatManager = new FileFormatManager(Array.Empty<IFileFormatPluginCapability>());
            using var lightweightRegistry = new CliContributionRegistry();
            lightweightRegistry.Register([]);
            var exitCode = await ExecuteSystemCommandAsync(
                systemRunnerTokens,
                jsonOutput,
                lightweightFileFormatManager,
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

        if (ShouldHandleWithSystemRunner(routedTokens))
        {
            var systemRunnerTokens = NormalizeImplicitNamespaceHelp(routedTokens);
            var exitCode = await ExecuteSystemCommandAsync(
                systemRunnerTokens,
                jsonOutput,
                fileFormatManager,
                registry,
                discoveredPlugins,
                pluginDirectories,
                cancellationToken);
            return new CliRunResult { Handled = true, ExitCode = exitCode };
        }

        var pluginCommand = registry.ResolveCommand(routedTokens, out var consumedTokens);
        if (pluginCommand is not null)
        {
            var pluginArgs = routedTokens.Skip(consumedTokens).ToArray();
            var exitCode = await ExecutePluginCommandAsync(pluginCommand, pluginArgs, jsonOutput, cancellationToken);
            return new CliRunResult { Handled = true, ExitCode = exitCode };
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
        return SystemCommandPathSet;
    }

    private static bool CanRunWithoutPluginRuntime(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return true;
        }

        if (args.Any(IsHelpToken))
        {
            return true;
        }

        if (args.Any(IsVersionToken))
        {
            return true;
        }

        if (args.Count == 1)
        {
            return SystemCommandNamespaces.Any(systemNamespace =>
                systemNamespace.Subcommands is { Length: > 0 }
                && systemNamespace.BasePath.Equals(args[0], StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    internal async Task<CliExitCodes> ExecuteOpenAsync(
        string? file,
        string? formatId,
        bool jsonOutput,
        FileFormatManager fileFormatManager,
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

        var payload = readResult.Payload
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

    private async Task<CliExitCodes> ExecutePluginCommandAsync(
        RegisteredCliContribution command,
        IReadOnlyList<string> pluginArgs,
        bool jsonOutput,
        CancellationToken cancellationToken)
    {
        var executionResult = await ExecuteWithTimeoutAsync(
            token => InvokePluginCommandAsync(command, pluginArgs, token),
            cancellationToken);

        if (!executionResult.Success)
        {
            await stderr.WriteLineAsync(executionResult.Error ?? "Plugin command failed.");
            return CliExitCodes.CommandFailed;
        }

        if (!string.IsNullOrWhiteSpace(executionResult.Output))
        {
            await stdout.WriteLineAsync(executionResult.Output);
        }
        else if (jsonOutput)
        {
            await stdout.WriteLineAsync(JsonSerializer.Serialize(new
            {
                success = true,
                command = command.CommandPath
            }, jsonOptions));
        }

        return executionResult.ExitCode;
    }

    private async Task<PluginCommandExecutionResult> InvokePluginCommandAsync(
        RegisteredCliContribution command,
        IReadOnlyList<string> pluginArgs,
        CancellationToken cancellationToken)
    {
        try
        {
            var runnerType = typeof(AppRunner<>).MakeGenericType(command.CommandInstance.GetType());
            var runner = Activator.CreateInstance(runnerType, new AppSettings(), new Resources())
                         ?? throw new InvalidOperationException($"Failed to create CLI runner for '{command.CommandPath}'.");

            var runMethod = runner.GetType().GetMethod("Run", [typeof(string[])])
                           ?? throw new InvalidOperationException($"Could not resolve Run(string[]) for '{command.CommandPath}'.");

            var canonicalPluginArgs = CanonicalizePluginCommandTokens(command.CommandInstance.GetType(), pluginArgs);
            var normalizedArgs = NormalizeSystemRunnerArgs(canonicalPluginArgs);
            var exitCode = await Task.Run(() =>
            {
                lock (ConsoleRedirectLock)
                {
                    var previousOut = System.Console.Out;
                    var previousError = System.Console.Error;
                    try
                    {
                        System.Console.SetOut(TextWriter.Synchronized(stdout));
                        System.Console.SetError(TextWriter.Synchronized(stderr));
                        return (int)(runMethod.Invoke(runner, [normalizedArgs]) ?? (int)CliExitCodes.Success);
                    }
                    finally
                    {
                        System.Console.SetOut(previousOut);
                        System.Console.SetError(previousError);
                    }
                }
            }, cancellationToken);

            var mappedExitCode = Enum.IsDefined(typeof(CliExitCodes), exitCode)
                ? (CliExitCodes)exitCode
                : CliExitCodes.InvalidArguments;
            return mappedExitCode == CliExitCodes.Success
                ? new PluginCommandExecutionResult(true, null, null, mappedExitCode)
                : new PluginCommandExecutionResult(false, null, $"Plugin command returned {mappedExitCode}.", mappedExitCode);
        }
        catch (TargetInvocationException exception)
        {
            return new PluginCommandExecutionResult(false, null, exception.InnerException?.Message ?? exception.Message, CliExitCodes.CommandFailed);
        }
        catch (Exception exception)
        {
            return new PluginCommandExecutionResult(false, null, exception.Message, CliExitCodes.CommandFailed);
        }
    }

    private static async Task<PluginCommandExecutionResult> ExecuteWithTimeoutAsync(
        Func<CancellationToken, Task<PluginCommandExecutionResult>> executor,
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
            return new PluginCommandExecutionResult(false, null, "Plugin CLI handler timed out after 5 seconds.", CliExitCodes.CommandFailed);
        }
        catch (Exception exception)
        {
            return new PluginCommandExecutionResult(false, null, exception.Message, CliExitCodes.CommandFailed);
        }
    }

    private sealed record PluginCommandExecutionResult(
        bool Success,
        string? Output,
        string? Error,
        CliExitCodes ExitCode);


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
        if (first.Equals("list-formats", StringComparison.OrdinalIgnoreCase))
        {
            invalidCommand = first;
            suggestedCommand = "fileformats list";
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

    private static IReadOnlyList<string> NormalizeImplicitNamespaceHelp(IReadOnlyList<string> args)
    {
        if (args.Count != 1)
        {
            return args;
        }

        var commandNamespace = SystemCommandNamespaces.FirstOrDefault(systemNamespace =>
            systemNamespace.Subcommands is { Length: > 0 }
            && systemNamespace.BasePath.Equals(args[0], StringComparison.OrdinalIgnoreCase));
        if (commandNamespace is null)
        {
            return args;
        }

        return [args[0], "--help"];
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

    private static IReadOnlyList<string> ExpandCompositeCommandToken(IReadOnlyList<string> args)
    {
        if (args.Count != 1 || !args[0].Contains(' ', StringComparison.Ordinal))
        {
            return args;
        }

        return args[0]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
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

    private static IReadOnlyList<string> CanonicalizePluginCommandTokens(Type rootCommandType, IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return args;
        }

        var canonical = args.ToArray();
        var currentType = rootCommandType;

        for (var index = 0; index < canonical.Length; index++)
        {
            var token = canonical[index];
            if (token.StartsWith("-", StringComparison.Ordinal))
            {
                break;
            }

            var subcommands = GetSubcommandTypes(currentType)
                .Select(subcommandType => new
                {
                    Name = GetDeclaredCommandName(subcommandType),
                    Type = subcommandType
                })
                .Where(static item => !string.IsNullOrWhiteSpace(item.Name))
                .ToList();

            if (subcommands.Count == 0)
            {
                break;
            }

            var match = subcommands.FirstOrDefault(item =>
                item.Name.Equals(token, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                break;
            }

            canonical[index] = match.Name;
            currentType = match.Type;
        }

        return canonical;
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

    private static string[] BuildSystemCommandPaths()
    {
        var commandPaths = new List<string>();

        foreach (var systemNamespace in SystemCommandNamespaces)
        {
            commandPaths.Add(systemNamespace.BasePath);

            if (systemNamespace.Subcommands is not { Length: > 0 })
            {
                continue;
            }

            commandPaths.AddRange(systemNamespace.Subcommands!.Select(subcommand =>
                $"{systemNamespace.BasePath} {subcommand}"));
        }

        return commandPaths.ToArray();
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
}
