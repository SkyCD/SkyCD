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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SkyCD.Cli.Mcp;
using SkyCD.Core.BackgroundServices;

namespace SkyCD.App.Mcp;

public sealed class McpServerHost : IAppBackgroundService, IDisposable
{
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Lock sync = new();
    private readonly ILogger<McpServerHost>? logger;
    private WebApplication? webApp;
    private Task? webAppTask;
    private string? baseUrl;
    private bool desiredEnabled;
    private int desiredPort = 8090;
    private bool disposed;

    public McpServerHost(ILogger<McpServerHost>? logger = null)
    {
        this.logger = logger;
    }

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

    /// <summary>
    /// Records the desired configuration; <see cref="ApplyConfigurationAsync"/> or
    /// <see cref="StartAsync"/> applies it.
    /// </summary>
    public void UpdateConfiguration(bool enabled, int port)
    {
        lock (sync)
        {
            desiredEnabled = enabled;
            desiredPort = Math.Clamp(port, 1, 65535);
        }
    }

    public async Task ApplyConfigurationAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (disposed)
            {
                return;
            }

            bool enabled;
            int port;
            lock (sync)
            {
                enabled = desiredEnabled;
                port = desiredPort;
            }

            var desiredBaseUrl = $"http://127.0.0.1:{port}/mcp";
            if (!enabled)
            {
                await StopInternalAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            lock (sync)
            {
                if (webAppTask is { IsCompleted: false } && string.Equals(baseUrl, desiredBaseUrl, StringComparison.Ordinal))
                {
                    return;
                }
            }

            await StopInternalAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await StartInternalAsync(port, desiredBaseUrl, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger?.LogError(exception, "Failed to start the MCP server on port {Port}.", port);
                await StopInternalAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => ApplyConfigurationAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        gate.Wait();
        try
        {
            StopInternalAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task StartInternalAsync(int port, string desiredBaseUrl, CancellationToken cancellationToken)
    {
        var rootUrl = $"http://127.0.0.1:{port}";
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, rootUrl);

        var bridge = new CliMcpBridge(desiredBaseUrl);
        var descriptors = await bridge.ListToolsAsync(cancellationToken).ConfigureAwait(false);
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
        try
        {
            app.MapMcp("/mcp");

            // Keep legacy helper endpoints for manual diagnostics and backwards compatibility.
            app.MapGet("/mcp/tools", async (CancellationToken toolCancellationToken) =>
            {
                var tools = await bridge.ListToolsAsync(toolCancellationToken);
                return Results.Json(new { tools }, JsonOptions);
            });
            app.MapMethods("/mcp/tools/{**toolPath}", ["GET", "POST"], async (HttpContext context,
                string toolPath, CancellationToken toolCancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(toolPath))
                {
                    return Results.BadRequest(new { error = "Tool path is required." });
                }

                JsonObject? payload = null;
                if (HttpMethods.IsPost(context.Request.Method))
                {
                    payload = await JsonSerializer.DeserializeAsync<JsonObject>(context.Request.Body,
                        cancellationToken: toolCancellationToken);
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

                var result = await bridge.InvokeToolAsync($"skycd.{toolPath.Replace('/', '.')}", input, toolCancellationToken);
                return result.Success
                    ? Results.Json(result, JsonOptions)
                    : Results.BadRequest(result);
            });

            await app.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await DisposeApplicationAsync(app).ConfigureAwait(false);
            throw;
        }

        lock (sync)
        {
            webApp = app;
            baseUrl = desiredBaseUrl;
            webAppTask = WaitForShutdownAsync(app, desiredBaseUrl);
        }
    }

    private async Task StopInternalAsync(CancellationToken cancellationToken)
    {
        WebApplication? app;
        Task? running;
        lock (sync)
        {
            app = webApp;
            running = webAppTask;
            webApp = null;
            webAppTask = null;
            baseUrl = null;
        }

        if (app is null)
        {
            return;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ShutdownTimeout);

        try
        {
            await app.StopAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            logger?.LogWarning("The MCP server did not stop within {Timeout}.", ShutdownTimeout);
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "The MCP server failed to stop cleanly.");
        }

        await DisposeApplicationAsync(app, cts.Token).ConfigureAwait(false);

        if (running is not null)
        {
            try
            {
                await running.WaitAsync(ShutdownTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                ObserveFailure(running);
                logger?.LogDebug("The MCP server shutdown task did not complete within {Timeout}.", ShutdownTimeout);
            }
            catch (Exception exception)
            {
                logger?.LogDebug(exception, "The MCP server shutdown task faulted.");
            }
        }
    }

    private async Task DisposeApplicationAsync(WebApplication app, CancellationToken cancellationToken = default)
    {
        var disposeTask = app.DisposeAsync().AsTask();
        try
        {
            if (cancellationToken.CanBeCanceled)
            {
                await disposeTask.WaitAsync(ShutdownTimeout, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await disposeTask.WaitAsync(ShutdownTimeout).ConfigureAwait(false);
            }
        }
        catch (TimeoutException)
        {
            ObserveFailure(disposeTask);
            logger?.LogWarning("Disposing the MCP server timed out after {Timeout}.", ShutdownTimeout);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ObserveFailure(disposeTask);
            logger?.LogWarning("Disposing the MCP server was cancelled after {Timeout}.", ShutdownTimeout);
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "The MCP server failed to dispose cleanly.");
        }
    }

    private static void ObserveFailure(Task task) =>
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private async Task WaitForShutdownAsync(WebApplication app, string desiredBaseUrl)
    {
        try
        {
            await app.WaitForShutdownAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "The MCP server at {BaseUrl} stopped unexpectedly.", desiredBaseUrl);
        }
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
