using System.Text.Json.Nodes;

namespace SkyCD.Cli.Mcp;

public sealed record CliMcpToolExecutionResult(
    bool Success,
    int ExitCode,
    JsonObject? Data,
    string? Error);
