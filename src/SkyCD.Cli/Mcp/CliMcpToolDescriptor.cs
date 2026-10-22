using System.Text.Json.Nodes;

namespace SkyCD.Cli.Mcp;

public sealed record CliMcpToolDescriptor(
    string Name,
    string Url,
    string CommandPath,
    JsonObject InputSchema,
    JsonObject OutputSchema);
