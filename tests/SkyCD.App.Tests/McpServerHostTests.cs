using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using SkyCD.App.Mcp;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class McpServerHostTests
{
    [Fact]
    public async Task Configure_Enabled_StartsServerAndServesToolsEndpoint()
    {
        using var host = new McpServerHost();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var port = GetFreeTcpPort();

        host.Configure(enabled: true, port);
        var baseUrl = host.BaseUrl;
        Assert.True(host.IsRunning);
        Assert.Equal($"http://127.0.0.1:{port}/mcp", baseUrl);

        var body = await RetryGetStringAsync(client, $"{baseUrl}/tools");
        var payload = JsonNode.Parse(body)?.AsObject();
        var tools = payload?["tools"]?.AsArray();

        Assert.NotNull(tools);
        Assert.Contains(tools!, tool =>
            tool?["name"]?.GetValue<string>().Equals("skycd.open", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public async Task Configure_Enabled_McpRootEndpointReturnsHelpfulMetadata()
    {
        using var host = new McpServerHost();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var port = GetFreeTcpPort();

        host.Configure(enabled: true, port);
        var baseUrl = host.BaseUrl;
        Assert.NotNull(baseUrl);

        var body = await RetryGetStringAsync(client, baseUrl!);
        var payload = JsonNode.Parse(body)?.AsObject();

        Assert.Equal("SkyCD MCP Server", payload?["name"]?.GetValue<string>());
        Assert.Equal("ok", payload?["status"]?.GetValue<string>());
        Assert.Equal($"{baseUrl}/tools", payload?["endpoints"]?["tools"]?.GetValue<string>());
        Assert.Equal($"{baseUrl}/tools/{{toolPath}}", payload?["endpoints"]?["invoke"]?.GetValue<string>());
    }

    [Fact]
    public async Task Configure_PortChange_RestartsServerOnNewUrl()
    {
        using var host = new McpServerHost();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var firstPort = GetFreeTcpPort();
        var secondPort = GetFreeTcpPort();

        host.Configure(enabled: true, firstPort);
        var firstUrl = host.BaseUrl;
        Assert.Equal($"http://127.0.0.1:{firstPort}/mcp", firstUrl);
        await RetryGetStringAsync(client, $"{firstUrl}/tools");

        host.Configure(enabled: true, secondPort);
        var secondUrl = host.BaseUrl;
        Assert.Equal($"http://127.0.0.1:{secondPort}/mcp", secondUrl);
        await RetryGetStringAsync(client, $"{secondUrl}/tools");

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync($"{firstUrl}/tools"));
    }

    [Fact]
    public async Task Configure_Disabled_StopsServerAndClearsStatus()
    {
        using var host = new McpServerHost();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var port = GetFreeTcpPort();

        host.Configure(enabled: true, port);
        var url = host.BaseUrl;
        Assert.True(host.IsRunning);
        Assert.NotNull(url);

        host.Configure(enabled: false, port);

        Assert.False(host.IsRunning);
        Assert.Null(host.BaseUrl);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync($"{url}/tools"));
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<string> RetryGetStringAsync(HttpClient client, string url)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                return await client.GetStringAsync(url);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                lastError = ex;
                await Task.Delay(50);
            }
        }

        throw new TimeoutException($"Failed to reach MCP endpoint: {url}", lastError);
    }
}
