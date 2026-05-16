using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Cli.Mcp;

namespace SkyCD.App.Mcp;

public sealed class McpServerHost : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Lock sync = new();
    private HttpListener? listener;
    private CancellationTokenSource? cancellationTokenSource;
    private Task? listenerTask;
    private string? baseUrl;

    public bool IsRunning
    {
        get
        {
            lock (sync)
            {
                return listener is { IsListening: true };
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

            if (listener is { IsListening: true } && string.Equals(baseUrl, desiredBaseUrl, StringComparison.Ordinal))
            {
                return;
            }

            StopInternal();
            try
            {
                StartInternal(desiredBaseUrl);
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

    private void StartInternal(string desiredBaseUrl)
    {
        var httpListener = new HttpListener();
        httpListener.Prefixes.Add($"{desiredBaseUrl.TrimEnd('/')}/");
        httpListener.Start();

        listener = httpListener;
        baseUrl = desiredBaseUrl;
        cancellationTokenSource = new CancellationTokenSource();
        listenerTask = Task.Run(() => RunListenerLoopAsync(httpListener, desiredBaseUrl, cancellationTokenSource.Token));
    }

    private void StopInternal()
    {
        cancellationTokenSource?.Cancel();
        try
        {
            listener?.Stop();
            listener?.Close();
        }
        catch
        {
            // Ignore listener shutdown errors.
        }

        listener = null;
        cancellationTokenSource?.Dispose();
        cancellationTokenSource = null;
        listenerTask = null;
        baseUrl = null;
    }

    private static async Task RunListenerLoopAsync(HttpListener httpListener, string desiredBaseUrl,
        CancellationToken cancellationToken)
    {
        var bridge = new CliMcpBridge(desiredBaseUrl);
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await httpListener.GetContextAsync();
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }

            _ = Task.Run(() => HandleRequestAsync(context, bridge, desiredBaseUrl, cancellationToken), cancellationToken);
        }
    }

    private static async Task HandleRequestAsync(HttpListenerContext context, CliMcpBridge bridge,
        string baseUrl, CancellationToken cancellationToken)
    {
        try
        {
            var request = context.Request;
            var path = request.Url?.AbsolutePath ?? "/";
            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase)
                && (path.Equals("/mcp", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/mcp/", StringComparison.OrdinalIgnoreCase)))
            {
                var toolsPath = $"{baseUrl.TrimEnd('/')}/tools";
                await WriteJsonAsync(context.Response, HttpStatusCode.OK, new
                {
                    name = "SkyCD MCP Server",
                    status = "ok",
                    endpoints = new
                    {
                        tools = toolsPath,
                        invoke = $"{toolsPath}/{{toolPath}}"
                    },
                    hint = "GET tools endpoint to list tools; POST invoke endpoint with { input: {...} }."
                });
                return;
            }

            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase)
                && path.Equals("/mcp/tools", StringComparison.OrdinalIgnoreCase))
            {
                var tools = await bridge.ListToolsAsync(cancellationToken);
                await WriteJsonAsync(context.Response, HttpStatusCode.OK, new { tools });
                return;
            }

            if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase)
                && path.StartsWith("/mcp/tools/", StringComparison.OrdinalIgnoreCase))
            {
                var toolPath = path["/mcp/tools/".Length..].Trim('/');
                if (string.IsNullOrWhiteSpace(toolPath))
                {
                    await WriteJsonAsync(context.Response, HttpStatusCode.BadRequest, new { error = "Tool path is required." });
                    return;
                }

                var toolName = $"skycd.{toolPath.Replace('/', '.')}";
                var payload = await ReadBodyAsJsonAsync(request);
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

                var result = await bridge.InvokeToolAsync(toolName, input, cancellationToken);
                await WriteJsonAsync(context.Response, result.Success ? HttpStatusCode.OK : HttpStatusCode.BadRequest, result);
                return;
            }

            await WriteJsonAsync(context.Response, HttpStatusCode.NotFound, new { error = "Not found." });
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(context.Response, HttpStatusCode.InternalServerError, new { error = ex.Message });
        }
    }

    private static async Task<JsonObject?> ReadBodyAsJsonAsync(HttpListenerRequest request)
    {
        if (!request.HasEntityBody)
        {
            return null;
        }

        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var json = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<JsonObject>(json);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, HttpStatusCode statusCode, object payload)
    {
        response.StatusCode = (int)statusCode;
        response.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var writer = new StreamWriter(response.OutputStream);
        await writer.WriteAsync(json);
        await writer.FlushAsync();
        response.Close();
    }
}
