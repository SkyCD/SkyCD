using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommandDotNet;
using Couchbase.Lite;
using SkyCD.Cli.Command;
using SkyCD.Cli.Console;
using SkyCD.Cli.DependencyInjection;
using SkyCD.Cli.Exceptions;
using SkyCD.Cli.Execution;
using SkyCD.Documents;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Runtime.DependencyInjection;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Managers;

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
            SkyCD.Plugin.Runtime.DependencyInjection.ServiceProvider.RebuildGlobal();
            var lightweightServiceProvider = SkyCD.Plugin.Runtime.DependencyInjection.ServiceProvider.Instance;
            lightweightServiceProvider.Register(static registrator =>
                registrator.AddRegistrator<CliRuntimeServiceRegistrator>());
            var lightweightFileFormatManager = lightweightServiceProvider.GetRequiredService<FileFormatManager>();
            var lightweightRegistry = lightweightServiceProvider.GetRequiredService<CliContributionRegistry>();
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

        SkyCD.Plugin.Runtime.DependencyInjection.ServiceProvider.RebuildGlobal();
        var runtimeServiceProvider = SkyCD.Plugin.Runtime.DependencyInjection.ServiceProvider.Instance;
        var pluginList = discoveredPlugins.ToList();
        runtimeServiceProvider.Register(static registrator =>
            registrator.AddRegistrator<CliRuntimeServiceRegistrator>());
        runtimeServiceProvider.Register(registrator => CliRuntimeServiceRegistrator.RegisterPluginServices(registrator, pluginList));
        var fileFormatManager = runtimeServiceProvider.GetRequiredService<FileFormatManager>();
        var registry = runtimeServiceProvider.GetRequiredService<CliContributionRegistry>();
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
            var exitCode = await PluginCommandExecutor.ExecutePluginCommandAsync(
                ConsoleRedirectLock,
                stdout,
                stderr,
                jsonOptions,
                pluginCommand,
                pluginArgs,
                jsonOutput,
                cancellationToken);
            return new CliRunResult { Handled = true, ExitCode = exitCode };
        }

        return new CliRunResult { Handled = false, ExitCode = CliExitCodes.Success };
    }

    internal static IReadOnlyList<string> GetPluginDirectories(string? appDataRoot = null)
    {
        var pluginPath = TryReadPluginPathFromAppSettings(appDataRoot);
        return string.IsNullOrWhiteSpace(pluginPath)
            ? []
            : [pluginPath];
    }

    internal static string? TryReadPluginPathFromAppSettings(string? appDataRoot = null)
    {
        var root = appDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            var defaultPathFromEmptyRoot = GetInstalledPluginsPath();
            return string.IsNullOrWhiteSpace(defaultPathFromEmptyRoot) ? null : defaultPathFromEmptyRoot;
        }

        try
        {
            var optionsDirectory = Path.Combine(root, "SkyCD");
            Directory.CreateDirectory(optionsDirectory);
            var configuration = new DatabaseConfiguration
            {
                Directory = optionsDirectory
            };
            using var database = new Database("skycd", configuration);
            var settings = database.GetCollection("settings", Collection.DefaultScopeName)
                           ?? database.CreateCollection("settings", Collection.DefaultScopeName);
            var appOptionsRepository = new AppOptionsDocumentRepository();
            appOptionsRepository.Initialize(typeof(AppOptionsDocument), "settings", settings);
            var resolvedPath = appOptionsRepository.GetOrCreateAppOptions().PluginPath;
            return string.IsNullOrWhiteSpace(resolvedPath) ? null : resolvedPath;
        }
        catch
        {
            var fallback = GetInstalledPluginsPath();
            return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
        }
    }

    private static string GetInstalledPluginsPath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Plugins"));
    }

    private async Task<CliExitCodes> ExecuteSystemCommandAsync(
        IReadOnlyList<string> args,
        bool jsonOutput,
        FileFormatManager fileFormatManager,
        CliContributionRegistry registry,
        IReadOnlyList<DiscoveredPlugin> discoveredPlugins,
        IReadOnlyList<string> pluginDirectories,
        CancellationToken cancellationToken)
    {
        var runnerArgs = NormalizeSystemRunnerArgs(args);
        var context = new CliCommandExecutionContext(
            this,
            jsonOutput,
            fileFormatManager,
            registry,
            discoveredPlugins,
            pluginDirectories,
            cancellationToken);

        try
        {
            CliCommandExecutionContextScope.Current = context;
            var appRunner = new AppRunner<RootCommand>().UseDefaultMiddleware();
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

            return System.Enum.IsDefined(typeof(CliExitCodes), exitCode)
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
            CliCommandExecutionContextScope.Current = null;
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

        var resolvedFormat = fileFormatManager.ResolveFormatId(formatId, fullPath, forWrite: false);
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

        var resolvedInputFormat = fileFormatManager.ResolveFormatId(inputFormat, fullInputPath, forWrite: false);
        var resolvedOutputFormat = fileFormatManager.ResolveFormatId(outputFormat, fullOutputPath, forWrite: true);

        await using var source = File.OpenRead(fullInputPath);
        var readResult = await fileFormatManager.ReadAsync(new FileFormatReadRequest
        {
            FormatId = resolvedInputFormat,
            Source = source,
            FileName = Path.GetFileName(fullInputPath)
        }, cancellationToken);

        var payload = readResult.Payload
            ?? throw new CliSourcePayloadMissingException();
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

    private static SystemCommandNamespace[] DiscoverSystemCommandNamespaces()
    {
        var rootCommandType = typeof(RootCommand);
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

    private static Task<IReadOnlyList<DiscoveredPlugin>> LoadDiscoveredPluginsAsync(
        Version hostVersion,
        CancellationToken cancellationToken = default)
    {
        var pluginDirectories = GetPluginDirectories();
        SkyCD.Plugin.Runtime.DependencyInjection.ServiceProvider.RebuildGlobal();
        var serviceProvider = SkyCD.Plugin.Runtime.DependencyInjection.ServiceProvider.Instance;
        var pluginManager = serviceProvider.GetRequiredService<PluginManager>();
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
