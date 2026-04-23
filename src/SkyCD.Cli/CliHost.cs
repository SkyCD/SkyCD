using System.Reflection;
using System.Text.Json;
using SkyCD.Plugin.Abstractions.Capabilities.Cli;
using SkyCD.Plugin.Abstractions.Capabilities.FileFormats;
using SkyCD.Plugin.Host;
using SkyCD.Plugin.Host.FileFormats;

namespace SkyCD.Cli;

public sealed class CliHost(
    TextWriter stdout,
    TextWriter stderr,
    Func<Version, CancellationToken, Task<CliPluginRuntime>>? runtimeLoader = null,
    Func<string>? executableNameProvider = null)
{
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true
    };
    private readonly Func<string> executableNameProvider = executableNameProvider ?? ResolveExecutableName;
    private readonly Func<Version, CancellationToken, Task<CliPluginRuntime>> runtimeLoaderFactory =
        runtimeLoader ?? CliPluginRuntime.LoadAsync;

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

        await using var runtime = await runtimeLoaderFactory(new Version(3, 0, 0), cancellationToken);

        foreach (var diagnostic in runtime.Diagnostics.Where(static entry => entry.StartsWith("info:", StringComparison.OrdinalIgnoreCase)))
        {
            await stderr.WriteLineAsync(diagnostic);
        }

        var catalog = new PluginCatalog();
        catalog.SetPlugins(runtime.DiscoveredPlugins);
        var routing = new FileFormatRoutingService(catalog);
        var pluginApi = new CliHostPluginApi(routing);
        using var registry = new CliContributionRegistry();
        registry.Register(runtime.DiscoveredPlugins);

        if (registry.Errors.Count > 0)
        {
            foreach (var error in registry.Errors)
            {
                await stderr.WriteLineAsync(error);
            }

            return new CliRunResult { Handled = true, ExitCode = CliExitCodes.ConfigurationError };
        }

        if (TrySplitBuiltIn(normalized, out var builtInCommand, out var builtInArguments))
        {
            var exitCode = await ExecuteBuiltInAsync(
                builtInCommand,
                builtInArguments,
                jsonOutput,
                routing,
                pluginApi,
                registry,
                runtime.DiscoveredPlugins,
                runtime.PluginDirectories,
                cancellationToken);
            return new CliRunResult { Handled = true, ExitCode = exitCode };
        }

        var pluginCommand = registry.ResolveCommand(normalized, out var consumedTokens);
        if (pluginCommand is not null)
        {
            var pluginArgs = normalized.Skip(consumedTokens).ToArray();
            var exitCode = await ExecutePluginCommandAsync(pluginCommand, pluginArgs, jsonOutput, pluginApi, cancellationToken);
            return new CliRunResult { Handled = true, ExitCode = exitCode };
        }

        return new CliRunResult { Handled = false, ExitCode = CliExitCodes.Success };
    }

    private async Task<int> ExecuteBuiltInAsync(
        string command,
        IReadOnlyList<string> args,
        bool jsonOutput,
        FileFormatRoutingService routing,
        IHostCliApi hostApi,
        CliContributionRegistry registry,
        IReadOnlyList<SkyCD.Plugin.Runtime.Discovery.DiscoveredPlugin> discoveredPlugins,
        IReadOnlyList<string> pluginDirectories,
        CancellationToken cancellationToken)
    {
        try
        {
            return command switch
            {
                "open" => await ExecuteOpenAsync(args, jsonOutput, routing, hostApi, registry, cancellationToken),
                "convert" => await ExecuteConvertAsync(args, jsonOutput, routing, hostApi, registry, cancellationToken),
                "fileformats" => await WriteBuiltInHelpAsync("fileformats", jsonOutput),
                "fileformats list" => await ExecuteListFormatsAsync(jsonOutput, routing, pluginDirectories),
                "plugins" => await WriteBuiltInHelpAsync("plugins", jsonOutput),
                "plugins list" => await ExecutePluginsListAsync(jsonOutput, registry, routing, discoveredPlugins, pluginDirectories),
                _ => CliExitCodes.InvalidArguments
            };
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
    }

    private async Task<int> ExecuteOpenAsync(
        IReadOnlyList<string> args,
        bool jsonOutput,
        FileFormatRoutingService routing,
        IHostCliApi hostApi,
        CliContributionRegistry registry,
        CancellationToken cancellationToken)
    {
        var file = args.FirstOrDefault(static token => !token.StartsWith("--", StringComparison.Ordinal));
        var formatId = ReadOption(args, "--format");

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

        var resolvedFormat = ResolveFormatId(formatId, fullPath, routing.GetOpenFormats(), "read");
        await using var source = File.OpenRead(fullPath);
        var readResult = await routing.ReadAsync(new FileFormatReadRequest
        {
            FormatId = resolvedFormat,
            Source = source,
            FileName = Path.GetFileName(fullPath)
        }, cancellationToken);

        var extensionResult = await ExecuteExtensionsAsync("open", args, jsonOutput, readResult.Payload, hostApi, registry, cancellationToken);
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

    private async Task<int> ExecuteConvertAsync(
        IReadOnlyList<string> args,
        bool jsonOutput,
        FileFormatRoutingService routing,
        IHostCliApi hostApi,
        CliContributionRegistry registry,
        CancellationToken cancellationToken)
    {
        var inputPath = ReadOption(args, "--in");
        var outputPath = ReadOption(args, "--out");
        var inputFormat = ReadOption(args, "--in-format");
        var outputFormat = ReadOption(args, "--format");

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

        var resolvedInputFormat = ResolveFormatId(inputFormat, fullInputPath, routing.GetOpenFormats(), "read");
        var resolvedOutputFormat = ResolveFormatId(outputFormat, fullOutputPath, routing.GetSaveFormats(), "write");

        await using var source = File.OpenRead(fullInputPath);
        var readResult = await routing.ReadAsync(new FileFormatReadRequest
        {
            FormatId = resolvedInputFormat,
            Source = source,
            FileName = Path.GetFileName(fullInputPath)
        }, cancellationToken);

        var extensionResult = await ExecuteExtensionsAsync("convert", args, jsonOutput, readResult.Payload, hostApi, registry, cancellationToken);
        if (!extensionResult.Success)
        {
            await stderr.WriteLineAsync(extensionResult.Error ?? "Plugin extension failed.");
            return CliExitCodes.CommandFailed;
        }

        var payload = extensionResult.Payload ?? readResult.Payload
            ?? throw new InvalidOperationException("Source format returned empty payload.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath) ?? Directory.GetCurrentDirectory());

        await using var target = File.Create(fullOutputPath);
        await routing.WriteAsync(new FileFormatWriteRequest
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

    private async Task<int> ExecuteListFormatsAsync(
        bool jsonOutput,
        FileFormatRoutingService routing,
        IReadOnlyList<string> pluginDirectories)
    {
        var routes = routing.GetOpenFormats()
            .Concat(routing.GetSaveFormats())
            .GroupBy(static route => route.FormatId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static route => route.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (jsonOutput)
        {
            await stdout.WriteLineAsync(JsonSerializer.Serialize(routes, jsonOptions));
            return CliExitCodes.Success;
        }

        if (routes.Count == 0)
        {
            await stdout.WriteLineAsync("No file format plugins were found.");
            await stdout.WriteLineAsync($"Plugin directories checked: {string.Join(", ", pluginDirectories)}");
            return CliExitCodes.Success;
        }

        foreach (var route in routes)
        {
            await stdout.WriteLineAsync($"{route.FormatId,-16} {route.DisplayName} [{string.Join(", ", route.Extensions)}]");
        }

        return CliExitCodes.Success;
    }

    private async Task<int> ExecutePluginsListAsync(
        bool jsonOutput,
        CliContributionRegistry registry,
        FileFormatRoutingService routing,
        IReadOnlyList<SkyCD.Plugin.Runtime.Discovery.DiscoveredPlugin> discoveredPlugins,
        IReadOnlyList<string> pluginDirectories)
    {
        var formatsByPlugin = routing.GetOpenFormats()
            .Concat(routing.GetSaveFormats())
            .GroupBy(static route => route.PluginId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(route => route.FormatId).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static id => id).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var pluginInfo = discoveredPlugins
            .Select(plugin => new
            {
                PluginId = plugin.Plugin.Descriptor.Id,
                DisplayName = plugin.Plugin.Descriptor.DisplayName,
                Capabilities = plugin.Capabilities.Select(static capability => capability.GetType().Name).OrderBy(static name => name).ToArray(),
                Formats = formatsByPlugin.TryGetValue(plugin.Plugin.Descriptor.Id, out var formats)
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

    private async Task<int> WriteBuiltInHelpAsync(string command, bool jsonOutput)
    {
        await WriteCommandHelpAsync(command, jsonOutput);
        return CliExitCodes.Success;
    }

    private async Task<int> ExecutePluginCommandAsync(
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

        if (args.Count == 1 && args[0].Equals("list-formats", StringComparison.OrdinalIgnoreCase))
        {
            command = "fileformats list";
            return true;
        }

        return false;
    }

    private static bool TryGetBuiltInHelpCommand(IReadOnlyList<string> args, out string command)
    {
        command = string.Empty;

        if (args.Count >= 2 &&
            args[0].Equals("open", StringComparison.OrdinalIgnoreCase) &&
            ContainsOnlyHelpTokens(args.Skip(1)))
        {
            command = "open";
            return true;
        }

        if (args.Count >= 2 &&
            args[0].Equals("convert", StringComparison.OrdinalIgnoreCase) &&
            ContainsOnlyHelpTokens(args.Skip(1)))
        {
            command = "convert";
            return true;
        }

        if (args.Count >= 2 &&
            args[0].Equals("plugins", StringComparison.OrdinalIgnoreCase) &&
            ContainsOnlyHelpTokens(args.Skip(1)))
        {
            command = "plugins";
            return true;
        }

        if (args.Count >= 3 &&
            args[0].Equals("plugins", StringComparison.OrdinalIgnoreCase) &&
            args[1].Equals("list", StringComparison.OrdinalIgnoreCase) &&
            ContainsOnlyHelpTokens(args.Skip(2)))
        {
            command = "plugins list";
            return true;
        }

        if (args.Count >= 2 &&
            args[0].Equals("fileformats", StringComparison.OrdinalIgnoreCase) &&
            ContainsOnlyHelpTokens(args.Skip(1)))
        {
            command = "fileformats";
            return true;
        }

        if (args.Count >= 3 &&
            args[0].Equals("fileformats", StringComparison.OrdinalIgnoreCase) &&
            args[1].Equals("list", StringComparison.OrdinalIgnoreCase) &&
            ContainsOnlyHelpTokens(args.Skip(2)))
        {
            command = "fileformats list";
            return true;
        }

        if (args.Count >= 2 &&
            args[0].Equals("list-formats", StringComparison.OrdinalIgnoreCase) &&
            ContainsOnlyHelpTokens(args.Skip(1)))
        {
            command = "fileformats list";
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

        if (args[0].Equals("plugins", StringComparison.OrdinalIgnoreCase))
        {
            command = "plugins";
            return true;
        }

        if (args[0].Equals("fileformats", StringComparison.OrdinalIgnoreCase))
        {
            command = "fileformats";
            return true;
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
               || token.Equals("-h", StringComparison.OrdinalIgnoreCase);
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

    private static string ResolveFormatId(
        string? explicitFormatId,
        string path,
        IReadOnlyList<FileFormatRoute> routes,
        string operation)
    {
        if (!string.IsNullOrWhiteSpace(explicitFormatId))
        {
            if (routes.Any(route => route.FormatId.Equals(explicitFormatId, StringComparison.OrdinalIgnoreCase)))
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

        var byExtension = routes.FirstOrDefault(route =>
            route.Extensions.Any(candidate => candidate.Equals(extension, StringComparison.OrdinalIgnoreCase)));
        if (byExtension is null)
        {
            throw new InvalidOperationException($"No format handler registered for '{extension}' ({operation}).");
        }

        return byExtension.FormatId;
    }

    private static string? ReadOption(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (!args[index].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return args[index + 1];
        }

        return null;
    }

    private static string GetVersionText()
    {
        var version = typeof(CliHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? typeof(CliHost).Assembly.GetName().Version?.ToString()
                      ?? "unknown";
        return $"SkyCD {version}";
    }
}
