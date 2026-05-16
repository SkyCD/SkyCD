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
using DryIoc;
using SkyCD.Cli.DependencyInjection;
using SkyCD.Cli.Console;
using SkyCD.Cli.Console.FileFormats;
using SkyCD.Cli.Console.Plugins;
using SkyCD.Cli.Enum;
using SkyCD.Cli.Extensions;
using SkyCD.Cli.Exceptions;
using SkyCD.Cli.Execution;
using SkyCD.Couchbase;
using SkyCD.Documents;
using SkyCD.Documents.Repository;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Core.DependencyInjection;
using SkyCD.Core.DependencyInjection.Registrators;
using SkyCD.Plugin.Runtime.Discovery;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.Core.Versioning;

namespace SkyCD.Cli;

public sealed class CliHost(
    TextWriter stdout,
    TextWriter stderr)
{
    private sealed record SystemCommandNamespace(
        string BasePath,
        string[]? Subcommands = null);

    private static readonly SystemCommandNamespace[] SystemCommandNamespaces = DiscoverSystemCommandNamespaces();
    private static readonly string[] SystemCommandPaths = BuildSystemCommandPaths();

    private static readonly HashSet<string> SystemCommandPathSet =
        SystemCommandPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly Lock ConsoleRedirectLock = new();

    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true
    };

    internal TextWriter Stdout => stdout;
    internal TextWriter Stderr => stderr;
    internal JsonSerializerOptions JsonOptions => jsonOptions;

    public async Task<CliRunResult> TryRunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        using var cliServiceProvider = CreateCliExecutionServiceProvider();

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
            await stderr.WriteLineAsync(
                $"Unknown command '{invalidCommandEarly}'. Did you mean '{suggestedCommandEarly}'?");
            return new CliRunResult { Handled = true, ExitCode = CliExitCodes.InvalidArguments };
        }

        if (ShouldHandleWithSystemRunner(routedTokens) && CanRunWithoutPluginRuntime(routedTokens))
        {
            var systemRunnerTokens = NormalizeImplicitNamespaceHelp(routedTokens);
            var lightweightFileFormatManager = cliServiceProvider.Resolve<FileFormatManager>();
            using var lightweightRegistry = new CliContributionRegistry();
            lightweightRegistry.Register(GetSystemCapabilities());
            var exitCode = await ExecuteSystemCommandAsync(
                systemRunnerTokens,
                jsonOutput,
                lightweightFileFormatManager,
                lightweightRegistry,
                [],
                null,
                cancellationToken);
            return new CliRunResult { Handled = true, ExitCode = exitCode };
        }

        var pluginPath = TryReadPluginPathFromAppSettings(cliServiceProvider);
        var pluginManager = ServiceProvider.Resolve<PluginManager>();
        var hostVersionProvider = ServiceProvider.Resolve<HostVersionProvider>();
        pluginManager.Discover(pluginPath ?? string.Empty, hostVersionProvider.Current);
        IReadOnlyList<DiscoveredPlugin> discoveredPlugins = DiscoverPluginsForCli(
            cliServiceProvider,
            pluginPath,
            hostVersionProvider.Current);
        var fileFormatManager = new FileFormatManager(
            discoveredPlugins
                .SelectMany(static plugin => plugin.Capabilities)
                .OfType<IFileFormatPluginCapability>()
                .ToArray());
        using var registry = new CliContributionRegistry();
        var pluginCapabilities = discoveredPlugins
            .SelectMany(static plugin => plugin.Capabilities)
            .OfType<ICliPluginCapability>();
        registry.Register(GetSystemCapabilities().Concat(pluginCapabilities));

        if (registry.Errors.Count > 0)
        {
            foreach (var error in registry.Errors)
            {
                await stderr.WriteLineAsync(error);
            }

            return new CliRunResult { Handled = true, ExitCode = CliExitCodes.ConfigurationError };
        }

        var pluginCommand = registry.ResolveCommand(routedTokens, out var consumedTokens);
        if (pluginCommand is not null)
        {
            var pluginArgs = routedTokens.Skip(consumedTokens).ToArray();
            var context = new CliCommandExecutionContext(
                this,
                jsonOutput,
                fileFormatManager,
                registry,
                discoveredPlugins,
                pluginPath,
                cancellationToken);
            try
            {
                CliCommandExecutionContextScope.Current = context;
                var exitCode = await ExecuteContributionCommandAsync(
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
            finally
            {
                CliCommandExecutionContextScope.Current = null;
            }
        }

        return new CliRunResult { Handled = false, ExitCode = CliExitCodes.Success };
    }

    internal static string? TryReadPluginPathFromAppSettings()
    {
        using var cliServiceProvider = CreateCliExecutionServiceProvider();
        return TryReadPluginPathFromAppSettings(cliServiceProvider);
    }

    internal static string? TryReadPluginPathFromAppSettings(IResolverContext resolver)
    {
        return ResolveInstalledPluginPath();
    }

    private static IContainer CreateCliExecutionServiceProvider()
    {
        return ServiceProvider.RegisterChildContainer(static registrator =>
        {
            new CliRuntimeServiceRegistrator().RegisterServices(registrator);
        });
    }

    private async Task<CliExitCodes> ExecuteSystemCommandAsync(
        IReadOnlyList<string> args,
        bool jsonOutput,
        FileFormatManager fileFormatManager,
        CliContributionRegistry registry,
        IReadOnlyList<DiscoveredPlugin> discoveredPlugins,
        string? pluginDirectory,
        CancellationToken cancellationToken)
    {
        var runnerArgs = NormalizeSystemRunnerArgs(args);
        var context = new CliCommandExecutionContext(
            this,
            jsonOutput,
            fileFormatManager,
            registry,
            discoveredPlugins,
            pluginDirectory,
            cancellationToken);

        try
        {
            CliCommandExecutionContextScope.Current = context;
            var systemContribution = new RegisteredCliContribution(
                OwnerId: "skycd-host",
                CommandPath: "skycd",
                CommandInstance: new RootCommand());
            return await ExecuteContributionCommandAsync(
                ConsoleRedirectLock,
                stdout,
                stderr,
                jsonOptions,
                systemContribution,
                runnerArgs,
                jsonOutput,
                cancellationToken);
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

    private static IReadOnlyList<ICliPluginCapability> GetSystemCapabilities()
    {
        return [new OpenCommand(), new ConvertCommand(), new FileFormatsCommand(), new PluginsCommand()];
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

    private static string GetVersionText()
    {
        var version = typeof(CliHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                          ?.InformationalVersion
                      ?? typeof(CliHost).Assembly.GetName().Version?.ToString()
                      ?? "unknown";
        return $"SkyCD {version}";
    }

    private static string? ResolveInstalledPluginPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "Plugins");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static IReadOnlyList<DiscoveredPlugin> DiscoverPluginsForCli(
        IResolverContext resolver,
        string? pluginPath,
        Version hostVersion)
    {
        var assembliesListFactory = resolver.Resolve<AssembliesListFactory>();
        var discoveredPluginFactory = resolver.Resolve<DiscoveredPluginFactory>();
        var normalizedDirectories = (pluginPath ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return assembliesListFactory
            .BuildFromPaths(normalizedDirectories)
            .Select(assembly =>
            {
                try
                {
                    return discoveredPluginFactory.BuildFromAssembly(assembly);
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            })
            .Where(static plugin => plugin is not null)
            .Select(static plugin => plugin!)
            .Where(plugin =>
                PluginCompatibilityEvaluator.IsCompatible(plugin.MinHostVersion, plugin.MaxHostVersion, hostVersion))
            .GroupBy(static plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
    }

    private static async Task<CliExitCodes> ExecuteContributionCommandAsync(
        Lock consoleRedirectLock,
        TextWriter stdout,
        TextWriter stderr,
        JsonSerializerOptions jsonOptions,
        RegisteredCliContribution command,
        IReadOnlyList<string> pluginArgs,
        bool jsonOutput,
        CancellationToken cancellationToken)
    {
        var executionResult = await ExecuteWithTimeoutAsync(
            token => InvokeContributionCommandAsync(consoleRedirectLock, stdout, stderr, command, pluginArgs, token),
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
            await stdout.WriteJsonAsync(new
            {
                success = true,
                command = command.CommandPath
            }, jsonOptions);
        }

        return executionResult.ExitCode;
    }

    private static async Task<ContributionCommandExecutionResult> InvokeContributionCommandAsync(
        Lock consoleRedirectLock,
        TextWriter stdout,
        TextWriter stderr,
        RegisteredCliContribution command,
        IReadOnlyList<string> pluginArgs,
        CancellationToken cancellationToken)
    {
        try
        {
            var runnerType = typeof(AppRunner<>).MakeGenericType(command.CommandInstance.GetType());
            var runner = Activator.CreateInstance(runnerType, new AppSettings(), new Resources())
                         ?? throw new CliRunnerCreationException(command.CommandPath);

            var runMethod = runner.GetType().GetMethod("Run", [typeof(string[])])
                            ?? throw new CliRunnerMethodResolutionException(command.CommandPath);

            var canonicalPluginArgs = CanonicalizeCommandTokens(command.CommandInstance.GetType(), pluginArgs);
            var normalizedArgs = NormalizeSystemRunnerArgs(canonicalPluginArgs);
            var exitCode = await Task.Run(() =>
            {
                lock (consoleRedirectLock)
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

            var mappedExitCode = System.Enum.IsDefined(typeof(CliExitCodes), exitCode)
                ? (CliExitCodes)exitCode
                : CliExitCodes.InvalidArguments;
            return mappedExitCode == CliExitCodes.Success
                ? new ContributionCommandExecutionResult(true, null, null, mappedExitCode)
                : new ContributionCommandExecutionResult(false, null, $"Plugin command returned {mappedExitCode}.",
                    mappedExitCode);
        }
        catch (TargetInvocationException exception)
        {
            return new ContributionCommandExecutionResult(false, null,
                exception.InnerException?.Message ?? exception.Message, CliExitCodes.CommandFailed);
        }
        catch (Exception exception)
        {
            return new ContributionCommandExecutionResult(false, null, exception.Message, CliExitCodes.CommandFailed);
        }
    }

    private static async Task<ContributionCommandExecutionResult> ExecuteWithTimeoutAsync(
        Func<CancellationToken, Task<ContributionCommandExecutionResult>> executor,
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
            return new ContributionCommandExecutionResult(false, null, "Plugin CLI handler timed out after 5 seconds.",
                CliExitCodes.CommandFailed);
        }
        catch (Exception exception)
        {
            return new ContributionCommandExecutionResult(false, null, exception.Message, CliExitCodes.CommandFailed);
        }
    }

    private static IReadOnlyList<string> CanonicalizeCommandTokens(Type rootCommandType, IReadOnlyList<string> args)
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

    private sealed record ContributionCommandExecutionResult(
        bool Success,
        string? Output,
        string? Error,
        CliExitCodes ExitCode);
}

