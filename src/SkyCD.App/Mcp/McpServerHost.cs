using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SkyCD.Cli.Mcp;

namespace SkyCD.App.Mcp;

public sealed class McpServerHost : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Lock sync = new();
    private WebApplication? webApp;
    private Task? webAppTask;
    private string? baseUrl;

    public bool IsRunning
    {
        get
        {
            lock (sync)
            {
                return webAppTask is { IsCompleted: false };
            }
        }
    }

    public string? BaseUrl
    {
        get
        {
            lock (sync)
            {
                return baseUrl;
            }
        }
    }

    public void Configure(bool enabled, int port)
    {
        var normalizedPort = Math.Clamp(port, 1, 65535);
        var desiredBaseUrl = $"http://127.0.0.1:{normalizedPort}/mcp";

        lock (sync)
        {
            if (!enabled)
            {
                StopInternal();
                return;
            }

            if (webAppTask is { IsCompleted: false } && string.Equals(baseUrl, desiredBaseUrl, StringComparison.Ordinal))
            {
                return;
            }

            StopInternal();
            try
            {
                StartInternal(normalizedPort, desiredBaseUrl);
            }
            catch
            {
                StopInternal();
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            StopInternal();
        }
    }

    private void StartInternal(int port, string desiredBaseUrl)
    {
        var rootUrl = $"http://127.0.0.1:{port}";
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, rootUrl);

        var bridge = new CliMcpBridge(desiredBaseUrl);
        var descriptors = Task.Run(() => bridge.ListToolsAsync()).GetAwaiter().GetResult();
        var serverTools = descriptors.Select(descriptor => CreateTool(bridge, descriptor))
            .ToArray();

        builder.Services.AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.Stateless = false;
#pragma warning disable MCP9004
                options.EnableLegacySse = true;
#pragma warning restore MCP9004
            })
            .WithTools(serverTools);

        var app = builder.Build();
        app.MapMcp("/mcp");

        // Keep legacy helper endpoints for manual diagnostics and backwards compatibility.
        app.MapGet("/mcp/tools", async (CancellationToken cancellationToken) =>
        {
            var tools = await bridge.ListToolsAsync(cancellationToken);
            return Results.Json(new { tools }, JsonOptions);
        });
        app.MapMethods("/mcp/tools/{**toolPath}", ["GET", "POST"], async (HttpContext context,
            string toolPath, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(toolPath))
            {
                return Results.BadRequest(new { error = "Tool path is required." });
            }

            JsonObject? payload = null;
            if (HttpMethods.IsPost(context.Request.Method))
            {
                payload = await JsonSerializer.DeserializeAsync<JsonObject>(context.Request.Body, cancellationToken: cancellationToken);
            }

            IReadOnlyDictionary<string, JsonNode?>? input = null;
            if (payload?["input"] is JsonObject inputObject)
            {
                var map = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in inputObject)
                {
                    map[item.Key] = item.Value;
                }

                input = map;
            }

            var result = await bridge.InvokeToolAsync($"skycd.{toolPath.Replace('/', '.')}", input, cancellationToken);
            return result.Success
                ? Results.Json(result, JsonOptions)
                : Results.BadRequest(result);
        });

        webApp = app;
        webAppTask = app.RunAsync();
        baseUrl = desiredBaseUrl;
    }

    private void StopInternal()
    {
        if (webApp is not null)
        {
            try
            {
                webApp.StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // Ignore stop errors.
            }

            webApp.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        webApp = null;
        webAppTask = null;
        baseUrl = null;
    }

    private static McpServerTool CreateTool(CliMcpBridge bridge, CliMcpToolDescriptor descriptor)
    {
        Func<RequestContext<CallToolRequestParams>, CancellationToken, Task<object?>> del =
            (request, cancellationToken) => InvokeBridgeToolAsync(bridge, descriptor.Name, request, cancellationToken);

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = descriptor.Name,
            Description = descriptor.CommandPath,
            ReadOnly = false
        });
    }

    private static async Task<object?> InvokeBridgeToolAsync(CliMcpBridge bridge, string toolName,
        RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        var arguments = request.Params?.Arguments;
        IReadOnlyDictionary<string, JsonNode?>? input = null;
        if (arguments is not null)
        {
            var map = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
            foreach (var argument in arguments)
            {
                map[argument.Key] = JsonSerializer.SerializeToNode(argument.Value, JsonOptions);
            }

            input = map;
        }

        var result = await bridge.InvokeToolAsync(toolName, input, cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Error ?? $"Tool failed: {toolName}");
        }

        return result.Data;
    }
}
