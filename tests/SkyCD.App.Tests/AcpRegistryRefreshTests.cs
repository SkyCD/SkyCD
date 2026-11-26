using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Ai.Description;
using SkyCD.Plugin.Ai.Description.Exceptions;
using SkyCD.Plugin.Ai.Description.Sources;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Issue #1211: the ACP registry refresh downloads through the system temp path and only the
/// validated cache lands in the configuration directory.
/// </summary>
public sealed class AcpRegistryRefreshTests : IDisposable
{
    private readonly TestWorkspace workspace = new();

    public void Dispose() => workspace.Dispose();

    private const string RegistryJson = """
        {"version":"1.0","agents":[{"id":"demo","name":"Demo","version":"1","description":"d","icon":"https://cdn.agentclientprotocol.com/registry/v1/demo.svg","distribution":{"npx":{"package":"demo-pkg","args":[]}}}]}
        """;

    [Fact]
    public async Task RefreshRegistry_PublishesCacheWithoutTempFilesBesideIt()
    {
        var leftoversBefore = TempDownloadFolders();
        var plugin = RefreshPlugin(RegistryJson);

        await plugin.RefreshRegistryAsync(CancellationToken.None);

        var configurationDirectory = Path.GetDirectoryName(ConfigurationPath())!;
        var cachePath = Path.Combine(configurationDirectory, AcpAgentRegistry.FileName);
        Assert.True(File.Exists(cachePath));
        Assert.Equal("demo", Assert.Single(AcpAgentRegistry.Load(cachePath).Agents).Id);
        Assert.Empty(Directory.GetFiles(configurationDirectory, "*.tmp"));
        Assert.Equal(leftoversBefore, TempDownloadFolders());
    }

    [Fact]
    public async Task RefreshRegistry_EmptyRegistryFailsAndCleansTheTempFolder()
    {
        var leftoversBefore = TempDownloadFolders();
        var plugin = RefreshPlugin("""{"version":"1.0","agents":[]}""");

        var exception = await Assert.ThrowsAsync<AcpRegistryException>(
            () => plugin.RefreshRegistryAsync(CancellationToken.None));

        Assert.Contains("no supported agents", exception.Message, StringComparison.Ordinal);
        Assert.Equal(leftoversBefore, TempDownloadFolders());
    }

    [Fact]
    public async Task RefreshRegistry_PublishFailureNamesTheCacheDirectory()
    {
        var leftoversBefore = TempDownloadFolders();
        var configurationDirectory = Path.GetDirectoryName(ConfigurationPath())!;
        Directory.CreateDirectory(Path.Combine(configurationDirectory, AcpAgentRegistry.FileName));
        var plugin = RefreshPlugin(RegistryJson);

        var exception = await Assert.ThrowsAsync<AcpRegistryException>(
            () => plugin.RefreshRegistryAsync(CancellationToken.None));

        Assert.Contains(configurationDirectory, exception.Message, StringComparison.Ordinal);
        Assert.Equal(leftoversBefore, TempDownloadFolders());
    }

    [Fact]
    public async Task RefreshRegistry_FailedDownloadPropagatesAndCleansTheTempFolder()
    {
        var leftoversBefore = TempDownloadFolders();
        var plugin = new AiDescriptionPlugin(ConfigurationPath(), (_, _, _) =>
            Task.FromResult(new GeneratedDescription("x", "x", null, "1.0", "1", "en", "test", "unknown", DateTimeOffset.UtcNow)),
            registryClient: new HttpClient(new StatusHandler(HttpStatusCode.ServiceUnavailable)));

        await Assert.ThrowsAsync<HttpRequestException>(() => plugin.RefreshRegistryAsync(CancellationToken.None));

        Assert.Equal(leftoversBefore, TempDownloadFolders());
    }

    [Fact]
    public async Task RefreshRegistry_TempWriteFailureNamesTheDownloadDirectory()
    {
        var leftoversBefore = TempDownloadFolders();
        var plugin = new AiDescriptionPlugin(ConfigurationPath(), (_, _, _) =>
            Task.FromResult(new GeneratedDescription("x", "x", null, "1.0", "1", "en", "test", "unknown", DateTimeOffset.UtcNow)),
            registryClient: new HttpClient(new FailingReadHandler()));

        var exception = await Assert.ThrowsAsync<AcpRegistryException>(
            () => plugin.RefreshRegistryAsync(CancellationToken.None));

        Assert.Contains("download directory", exception.Message, StringComparison.Ordinal);
        Assert.Contains("skycd-acp-registry-", exception.Message, StringComparison.Ordinal);
        Assert.Equal(leftoversBefore, TempDownloadFolders());
    }

    [Fact]
    public async Task RefreshRegistry_CancellationCleansTheTempFolder()
    {
        var leftoversBefore = TempDownloadFolders();
        var plugin = RefreshPlugin(RegistryJson);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => plugin.RefreshRegistryAsync(cancelled.Token));

        Assert.Equal(leftoversBefore, TempDownloadFolders());
    }

    private AiDescriptionPlugin RefreshPlugin(string registryJson) =>
        new(ConfigurationPath(), (_, _, _) =>
            Task.FromResult(new GeneratedDescription("x", "x", null, "1.0", "1", "en", "test", "unknown", DateTimeOffset.UtcNow)),
            registryClient: new HttpClient(new RegistryHandler(registryJson)));

    private string ConfigurationPath() => Path.Combine(workspace.Root, "config", "ai-descriptions.json");

    private static int TempDownloadFolders() =>
        Directory.GetDirectories(Path.GetTempPath(), "skycd-acp-registry-*").Length;

    private sealed class RegistryHandler(string payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith("/registry.json", StringComparison.Ordinal) == true)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload)
                });
            }

            // Icon and asset fetches miss on purpose: an unavailable icon must not fail the refresh.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class FailingReadHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new FailingContent()
            });

        private sealed class FailingContent : HttpContent
        {
            protected override Task<Stream> CreateContentReadStreamAsync() =>
                Task.FromResult<Stream>(new FailingStream());

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
                Task.FromException(new IOException("simulated read failure"));

            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }

            private sealed class FailingStream : Stream
            {
                public override bool CanRead => true;
                public override bool CanSeek => false;
                public override bool CanWrite => false;
                public override long Length => throw new NotSupportedException();
                public override long Position
                {
                    get => throw new NotSupportedException();
                    set => throw new NotSupportedException();
                }

                public override int Read(byte[] buffer, int offset, int count) =>
                    throw new IOException("simulated read failure");

                public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                    Task.FromException<int>(new IOException("simulated read failure"));

                public override void Flush()
                {
                }

                public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
                public override void SetLength(long value) => throw new NotSupportedException();
                public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            }
        }
    }
}
