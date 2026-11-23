using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using SkyCD.App.Mcp;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class McpServerHostTests
{
    [Fact]
    public async Task Configure_Enabled_StartsServerAndServesToolsEndpoint()
    {
        using var host = CreateHost();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var port = GetFreeTcpPort();

        host.UpdateConfiguration(enabled: true, port);
        await host.ApplyConfigurationAsync();
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
    public async Task Configure_Enabled_McpRootEndpoint_IsReachableForMcpTransport()
    {
        using var host = CreateHost();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var port = GetFreeTcpPort();

        host.UpdateConfiguration(enabled: true, port);
        await host.ApplyConfigurationAsync();
        var baseUrl = host.BaseUrl;
        Assert.NotNull(baseUrl);

        var response = await client.GetAsync(baseUrl!);
        Assert.NotEqual(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Configure_Enabled_GetToolEndpoint_ReturnsPluginsListResult()
    {
        using var host = CreateHost();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var port = GetFreeTcpPort();

        host.UpdateConfiguration(enabled: true, port);
        await host.ApplyConfigurationAsync();
        var baseUrl = host.BaseUrl;
        Assert.NotNull(baseUrl);

        var body = await RetryGetStringAsync(client, $"{baseUrl}/tools/plugins/list");
        var payload = JsonNode.Parse(body)?.AsObject();

        Assert.True(payload?["success"]?.GetValue<bool>());
        Assert.Equal(0, payload?["exitCode"]?.GetValue<int>());
        Assert.NotNull(payload?["data"]);
    }

    [Fact]
    public async Task Configure_PortChange_RestartsServerOnNewUrl()
    {
        using var host = CreateHost();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var (firstPort, secondPort) = GetTwoFreeTcpPorts();

        host.UpdateConfiguration(enabled: true, firstPort);
        await host.ApplyConfigurationAsync();
        var firstUrl = host.BaseUrl;
        Assert.Equal($"http://127.0.0.1:{firstPort}/mcp", firstUrl);
        await RetryGetStringAsync(client, $"{firstUrl}/tools");

        host.UpdateConfiguration(enabled: true, secondPort);
        await host.ApplyConfigurationAsync();
        var secondUrl = host.BaseUrl;
        Assert.Equal($"http://127.0.0.1:{secondPort}/mcp", secondUrl);
        await RetryGetStringAsync(client, $"{secondUrl}/tools");

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync($"{firstUrl}/tools"));
    }

    [Fact]
    public async Task Configure_Disabled_StopsServerAndClearsStatus()
    {
        using var host = CreateHost();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var port = GetFreeTcpPort();

        host.UpdateConfiguration(enabled: true, port);
        await host.ApplyConfigurationAsync();
        var url = host.BaseUrl;
        Assert.True(host.IsRunning);
        Assert.NotNull(url);

        host.UpdateConfiguration(enabled: false, port);
        await host.ApplyConfigurationAsync();

        Assert.False(host.IsRunning);
        Assert.Null(host.BaseUrl);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync($"{url}/tools"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Configure_ExposedSubset_AdvertisesAndInvokesOnlyExposedTools(bool useProtocolClient)
    {
        using var host = CreateHost();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var port = GetFreeTcpPort();

        host.UpdateConfiguration(enabled: true, port, ["skycd.plugins.list"]);
        await host.ApplyConfigurationAsync();
        var baseUrl = host.BaseUrl;
        Assert.NotNull(baseUrl);

        if (useProtocolClient)
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(baseUrl!),
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false,
                ConnectionTimeout = TimeSpan.FromSeconds(10)
            });
            await using var protocolClient = await McpClient.CreateAsync(transport);
            var tools = await protocolClient.ListToolsAsync();

            Assert.Equal(["skycd.plugins.list"],
                tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal));
        }

        var legacyTools = await GetLegacyToolsAsync(client, baseUrl!);
        Assert.Equal(["skycd.plugins.list"], legacyTools.Select(name => name).OrderBy(name => name, StringComparer.Ordinal));

        var exposedBody = await RetryGetStringAsync(client, $"{baseUrl}/tools/plugins/list");
        var exposedPayload = JsonNode.Parse(exposedBody)?.AsObject();
        Assert.True(exposedPayload?["success"]?.GetValue<bool>());
    }

    [Fact]
    public async Task Configure_HiddenTool_IsRejectedThroughLegacyInvokeEndpoint()
    {
        using var host = CreateHost();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var port = GetFreeTcpPort();

        host.UpdateConfiguration(enabled: true, port, ["skycd.plugins.list"]);
        await host.ApplyConfigurationAsync();
        var baseUrl = host.BaseUrl;
        Assert.NotNull(baseUrl);

        var response = await client.GetAsync($"{baseUrl}/tools/open");
        var body = await response.Content.ReadAsStringAsync();
        var payload = JsonNode.Parse(body)?.AsObject();

        Assert.Equal(System.Net.HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("not exposed", payload?["error"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Configure_ExposedSetChange_RestartsServerWithNewSelection()
    {
        using var host = CreateHost();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var port = GetFreeTcpPort();

        host.UpdateConfiguration(enabled: true, port, ["skycd.plugins.list"]);
        await host.ApplyConfigurationAsync();
        var baseUrl = host.BaseUrl;
        Assert.NotNull(baseUrl);
        await RetryGetStringAsync(client, $"{baseUrl}/tools");

        host.UpdateConfiguration(enabled: true, port, ["skycd.open"]);
        await host.ApplyConfigurationAsync();

        Assert.True(host.IsRunning);
        var tools = await GetLegacyToolsAsync(client, baseUrl!);
        Assert.Contains("skycd.open", tools);
        Assert.DoesNotContain("skycd.plugins.list", tools);
    }

    [Fact]
    public async Task Configure_NullExposedSet_ExposesEveryDiscoveredTool()
    {
        using var host = CreateHost();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var port = GetFreeTcpPort();

        host.UpdateConfiguration(enabled: true, port);
        await host.ApplyConfigurationAsync();
        var baseUrl = host.BaseUrl;
        Assert.NotNull(baseUrl);

        var tools = await GetLegacyToolsAsync(client, baseUrl!);
        Assert.Contains("skycd.open", tools);
        Assert.Contains("skycd.plugins.list", tools);

        var response = await client.GetAsync($"{baseUrl}/tools/plugins/list");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void FilterExposedTools_NullSelectionKeepsEveryDescriptor()
    {
        var descriptors = new[]
        {
            Descriptor("skycd.open"),
            Descriptor("skycd.plugins.list")
        };

        var all = McpServerHost.FilterExposedTools(descriptors, exposed: null);
        var subset = McpServerHost.FilterExposedTools(descriptors,
            new HashSet<string>(["SKYCD.PLUGINS.LIST"], StringComparer.OrdinalIgnoreCase));
        var none = McpServerHost.FilterExposedTools(descriptors,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(["skycd.open", "skycd.plugins.list"], all.Select(descriptor => descriptor.Name));
        Assert.Equal(["skycd.plugins.list"], subset.Select(descriptor => descriptor.Name));
        Assert.Empty(none);
    }

    private static McpServerHost CreateHost() => new();

    private static SkyCD.Cli.Mcp.CliMcpToolDescriptor Descriptor(string name) =>
        new(name, $"http://127.0.0.1:1/mcp/tools/{name}", name.Replace("skycd.", string.Empty).Replace('.', ' '),
            new JsonObject(), new JsonObject());

    private static async Task<IReadOnlyList<string>> GetLegacyToolsAsync(HttpClient client, string baseUrl)
    {
        var body = await RetryGetStringAsync(client, $"{baseUrl}/tools");
        var tools = JsonNode.Parse(body)?["tools"]?.AsArray();
        Assert.NotNull(tools);
        return tools!.Select(tool => tool?["name"]?.GetValue<string>() ?? string.Empty).ToArray();
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static (int First, int Second) GetTwoFreeTcpPorts()
    {
        // Hold both listeners at once so the OS cannot hand out the same port twice.
        using var first = new TcpListener(System.Net.IPAddress.Loopback, 0);
        using var second = new TcpListener(System.Net.IPAddress.Loopback, 0);
        first.Start();
        second.Start();
        return (((System.Net.IPEndPoint)first.LocalEndpoint).Port,
            ((System.Net.IPEndPoint)second.LocalEndpoint).Port);
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
