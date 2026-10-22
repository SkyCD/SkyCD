using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CommandDotNet;
using SkyCD.Cli.Console;
using SkyCD.Cli.Console.FileFormats;
using SkyCD.Cli.Console.Plugins;
using SkyCD.Cli.Enum;

namespace SkyCD.Cli.Mcp;

public sealed class CliMcpBridge
{
    private static readonly IReadOnlyDictionary<string, Type> BuiltInCommandTypeMap = BuildBuiltInCommandTypeMap();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
    private readonly string mcpBaseUrl;

    public CliMcpBridge(string? mcpBaseUrl = null)
    {
        this.mcpBaseUrl = ResolveMcpBaseUrl(mcpBaseUrl);
    }

    public async Task<IReadOnlyList<CliMcpToolDescriptor>> ListToolsAsync(CancellationToken cancellationToken = default)
    {
        var commandPaths = await DiscoverCommandPathsAsync(cancellationToken);
        return commandPaths
            .Select(BuildDescriptor)
            .OrderBy(static tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<CliMcpToolExecutionResult> InvokeToolAsync(
        string toolName,
        IReadOnlyDictionary<string, JsonNode?>? input = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryToolNameToCommandPath(toolName, out var commandPath))
        {
            return new CliMcpToolExecutionResult(
                Success: false,
                ExitCode: (int)CliExitCodes.InvalidArguments,
                Data: null,
                Error: $"Unknown tool '{toolName}'.");
        }

        if (!BuiltInCommandTypeMap.ContainsKey(commandPath) &&
            !CliHost.GetSystemCommandPaths().Contains(commandPath))
        {
            return new CliMcpToolExecutionResult(
                Success: false,
                ExitCode: (int)CliExitCodes.InvalidArguments,
                Data: null,
                Error: $"Unknown tool '{toolName}'.");
        }

        var args = BuildCliArgs(commandPath, input);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var host = new CliHost(stdout, stderr);
        var result = await host.TryRunAsync(args, cancellationToken);

        var errorText = stderr.ToString().Trim();
        if (result.ExitCode != CliExitCodes.Success)
        {
            return new CliMcpToolExecutionResult(
                Success: false,
                ExitCode: (int)result.ExitCode,
                Data: null,
                Error: string.IsNullOrWhiteSpace(errorText) ? "Command failed." : errorText);
        }

        var outputText = stdout.ToString();
        var data = NormalizeToolOutput(commandPath, outputText);
        return new CliMcpToolExecutionResult(
            Success: true,
            ExitCode: (int)result.ExitCode,
            Data: data,
            Error: null);
    }

    private CliMcpToolDescriptor BuildDescriptor(string commandPath)
    {
        var toolName = CommandPathToToolName(commandPath);
        var toolUrl = BuildToolUrl(commandPath);
        if (!BuiltInCommandTypeMap.TryGetValue(commandPath, out var commandType))
        {
            return new CliMcpToolDescriptor(
                toolName,
                toolUrl,
                commandPath,
                CreateGenericInputSchema(),
                CreateGenericOutputSchema());
        }

        return new CliMcpToolDescriptor(
            toolName,
            toolUrl,
            commandPath,
            CreateInputSchemaFromCommandType(commandType),
            CreateOutputSchema(commandPath));
    }

    private static JsonObject CreateOutputSchema(string commandPath)
    {
        return commandPath.ToLowerInvariant() switch
        {
            "open" => CreateOpenOutputSchema(),
            "convert" => CreateConvertOutputSchema(),
            "fileformats list" => CreateFileFormatsListOutputSchema(),
            "plugins list" => CreatePluginsListOutputSchema(),
            _ => CreateGenericOutputSchema()
        };
    }

    private static JsonObject CreateGenericOutputSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["description"] = "Normalized MCP command result.",
            ["properties"] = new JsonObject
            {
                ["success"] = new JsonObject { ["type"] = "boolean" },
                ["command"] = new JsonObject { ["type"] = "string" },
                ["data"] = new JsonObject { ["description"] = "Parsed JSON output when command returns JSON." },
                ["output"] = new JsonObject { ["type"] = "string", ["description"] = "Plain text output when JSON is not returned." },
                ["error"] = new JsonObject { ["type"] = "string" }
            }
        };
    }

    private static JsonObject CreateOpenOutputSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["description"] = "Open command result.",
            ["properties"] = new JsonObject
            {
                ["success"] = new JsonObject { ["type"] = "boolean" },
                ["command"] = new JsonObject { ["type"] = "string", ["const"] = "open" },
                ["file"] = new JsonObject { ["type"] = "string" },
                ["formatId"] = new JsonObject { ["type"] = "string" },
                ["error"] = new JsonObject { ["type"] = "string" }
            }
        };
    }

    private static JsonObject CreateConvertOutputSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["description"] = "Convert command result.",
            ["properties"] = new JsonObject
            {
                ["success"] = new JsonObject { ["type"] = "boolean" },
                ["command"] = new JsonObject { ["type"] = "string", ["const"] = "convert" },
                ["input"] = new JsonObject { ["type"] = "string" },
                ["output"] = new JsonObject { ["type"] = "string" },
                ["inputFormat"] = new JsonObject { ["type"] = "string" },
                ["outputFormat"] = new JsonObject { ["type"] = "string" },
                ["error"] = new JsonObject { ["type"] = "string" }
            }
        };
    }

    private static JsonObject CreateFileFormatsListOutputSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["description"] = "List of supported file formats.",
            ["properties"] = new JsonObject
            {
                ["success"] = new JsonObject { ["type"] = "boolean" },
                ["command"] = new JsonObject { ["type"] = "string", ["const"] = "fileformats list" },
                ["data"] = new JsonObject
                {
                    ["type"] = "array",
                    ["description"] = "Array of format descriptors."
                },
                ["error"] = new JsonObject { ["type"] = "string" }
            }
        };
    }

    private static JsonObject CreatePluginsListOutputSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["description"] = "Installed plugins and available CLI command paths.",
            ["properties"] = new JsonObject
            {
                ["plugins"] = new JsonObject { ["type"] = "array" },
                ["cliCommands"] = new JsonObject { ["type"] = "array" },
                ["pluginDirectory"] = new JsonObject { ["type"] = "string" },
                ["error"] = new JsonObject { ["type"] = "string" }
            }
        };
    }

    private static JsonObject CreateGenericInputSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["args"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" }
                }
            }
        };
    }

    private static JsonObject CreateInputSchemaFromCommandType(Type commandType)
    {
        var executeMethod = commandType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(static method => method.GetCustomAttribute<DefaultCommandAttribute>() is not null);
        if (executeMethod is null)
        {
            return CreateGenericInputSchema();
        }

        var properties = new JsonObject();
        foreach (var parameter in executeMethod.GetParameters())
        {
            var optionAttribute = parameter.GetCustomAttribute<OptionAttribute>();
            var operandAttribute = parameter.GetCustomAttribute<OperandAttribute>();
            var inputName = optionAttribute?.LongName ?? operandAttribute?.Name ?? parameter.Name ?? "arg";
            var schema = new JsonObject
            {
                ["type"] = parameter.ParameterType == typeof(bool) ? "boolean" : "string"
            };

            if (optionAttribute is not null)
            {
                schema["cliOption"] = $"--{optionAttribute.LongName}";
            }

            if (operandAttribute is not null)
            {
                schema["operand"] = true;
            }

            properties[inputName] = schema;
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };
    }

    private static string[] BuildCliArgs(string commandPath, IReadOnlyDictionary<string, JsonNode?>? input)
    {
        var args = commandPath.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (input is not null && input.TryGetValue("args", out var passthroughArgsNode)
                          && passthroughArgsNode is JsonArray passthroughArgs)
        {
            foreach (var arg in passthroughArgs)
            {
                if (arg is null)
                {
                    continue;
                }

                var value = arg.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    args.Add(value.Trim());
                }
            }

            args.Add("--json");
            return args.ToArray();
        }

        if (BuiltInCommandTypeMap.TryGetValue(commandPath, out var commandType))
        {
            AppendTypedArguments(args, commandType, input);
        }

        args.Add("--json");
        return args.ToArray();
    }

    private static void AppendTypedArguments(
        IList<string> args,
        Type commandType,
        IReadOnlyDictionary<string, JsonNode?>? input)
    {
        if (input is null)
        {
            return;
        }

        var executeMethod = commandType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(static method => method.GetCustomAttribute<DefaultCommandAttribute>() is not null);
        if (executeMethod is null)
        {
            return;
        }

        foreach (var parameter in executeMethod.GetParameters())
        {
            var optionAttribute = parameter.GetCustomAttribute<OptionAttribute>();
            var operandAttribute = parameter.GetCustomAttribute<OperandAttribute>();
            var inputName = optionAttribute?.LongName ?? operandAttribute?.Name ?? parameter.Name ?? "arg";
            if (!input.TryGetValue(inputName, out var valueNode) || valueNode is null)
            {
                continue;
            }

            if (optionAttribute is not null)
            {
                if (parameter.ParameterType == typeof(bool))
                {
                    if (valueNode is JsonValue boolNode && boolNode.TryGetValue<bool>(out var boolValue) && boolValue)
                    {
                        args.Add($"--{optionAttribute.LongName}");
                    }

                    continue;
                }

                if (TryReadString(valueNode, out var optionValue))
                {
                    args.Add($"--{optionAttribute.LongName}");
                    args.Add(optionValue);
                }

                continue;
            }

            if (operandAttribute is not null && TryReadString(valueNode, out var operandValue))
            {
                args.Add(operandValue);
            }
        }
    }

    private static bool TryReadString(JsonNode node, out string value)
    {
        value = string.Empty;
        if (node is not JsonValue jsonValue)
        {
            return false;
        }

        if (!jsonValue.TryGetValue<string>(out var stringValue) || string.IsNullOrWhiteSpace(stringValue))
        {
            return false;
        }

        value = stringValue.Trim();
        return true;
    }

    private async Task<IReadOnlyCollection<string>> DiscoverCommandPathsAsync(CancellationToken cancellationToken)
    {
        var commands = CliHost.GetSystemCommandPaths().ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var host = new CliHost(stdout, stderr);
        var result = await host.TryRunAsync(["plugins", "list", "--json"], cancellationToken);
        if (result.ExitCode != CliExitCodes.Success)
        {
            return commands.OrderBy(static command => command, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        var rootNode = TryParseJson(stdout.ToString());
        if (rootNode is null)
        {
            return commands.OrderBy(static command => command, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        if (rootNode["cliCommands"] is not JsonArray cliCommands)
        {
            return commands.OrderBy(static command => command, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        foreach (var node in cliCommands)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out var commandPath)
                                       && !string.IsNullOrWhiteSpace(commandPath))
            {
                commands.Add(commandPath.Trim());
            }
        }

        return commands.OrderBy(static command => command, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static JsonNode? TryParseJson(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonNode>(output, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static JsonObject NormalizeToolOutput(string commandPath, string output)
    {
        var parsed = TryParseJson(output);
        if (parsed is JsonObject objectPayload)
        {
            return objectPayload;
        }

        if (parsed is not null)
        {
            return new JsonObject
            {
                ["success"] = true,
                ["command"] = commandPath,
                ["data"] = parsed
            };
        }

        var trimmed = output.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return new JsonObject
            {
                ["success"] = true,
                ["command"] = commandPath
            };
        }

        return new JsonObject
        {
            ["success"] = true,
            ["command"] = commandPath,
            ["output"] = trimmed
        };
    }

    private static bool TryToolNameToCommandPath(string toolName, out string commandPath)
    {
        commandPath = string.Empty;
        if (!toolName.StartsWith("skycd.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = toolName["skycd.".Length..].Trim();
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return false;
        }

        commandPath = suffix.Replace('.', ' ');
        return true;
    }

    private static string CommandPathToToolName(string commandPath)
    {
        return $"skycd.{commandPath.Replace(' ', '.')}";
    }

    private string BuildToolUrl(string commandPath)
    {
        var path = commandPath.Replace(' ', '/');
        return $"{mcpBaseUrl.TrimEnd('/')}/tools/{path}";
    }

    private static string ResolveMcpBaseUrl(string? overrideBaseUrl)
    {
        if (!string.IsNullOrWhiteSpace(overrideBaseUrl))
        {
            return overrideBaseUrl.Trim();
        }

        var envBaseUrl = Environment.GetEnvironmentVariable("SKYCD_MCP_BASE_URL");
        if (!string.IsNullOrWhiteSpace(envBaseUrl))
        {
            return envBaseUrl.Trim();
        }

        var envPort = Environment.GetEnvironmentVariable("SKYCD_MCP_PORT");
        if (int.TryParse(envPort, out var parsedPort) && parsedPort is >= 1 and <= 65535)
        {
            return $"http://127.0.0.1:{parsedPort}/mcp";
        }

        return "http://127.0.0.1:8765/mcp";
    }

    private static IReadOnlyDictionary<string, Type> BuildBuiltInCommandTypeMap()
    {
        return new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["open"] = typeof(OpenCommand),
            ["convert"] = typeof(ConvertCommand),
            ["fileformats list"] = typeof(FileFormatsListCommand),
            ["plugins list"] = typeof(PluginsListSubcommand)
        };
    }
}
