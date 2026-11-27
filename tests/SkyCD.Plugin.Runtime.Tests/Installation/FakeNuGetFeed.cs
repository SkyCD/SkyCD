using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Runtime.Tests.Installation;

/// <summary>
/// In-memory NuGet v3 feed: service index, package base address and package content, with failure injection.
/// No real network access and no plugin execution.
/// </summary>
internal sealed class FakeNuGetFeed : HttpMessageHandler
{
    public const string ServiceIndexUrl = "https://feed.test/v3/index.json";

    public static readonly Uri PackageBaseAddress = new("https://feed.test/v3/flatcontainer/");

    private readonly Dictionary<string, byte[]> packages = new(StringComparer.OrdinalIgnoreCase);

    public int ServiceIndexRequests { get; private set; }

    public int PackageRequests { get; private set; }

    public List<Uri> RequestedUris { get; } = [];
    public Dictionary<Uri, Uri?> Redirects { get; } = [];

    /// <summary>Overrides the download response status; <see cref="HttpStatusCode.OK"/> by default.</summary>
    public HttpStatusCode DownloadStatus { get; set; } = HttpStatusCode.OK;

    /// <summary>Simulates a connection that fails before a response is produced.</summary>
    public bool DownloadThrowsConnectionError { get; set; }

    /// <summary>Serves a different body for the next download (for example a corrupt archive).</summary>
    public byte[]? DownloadOverride { get; set; }

    /// <summary>Simulates an interrupted body: only the first <see cref="TruncatedLength"/> bytes are written before the stream fails.</summary>
    public int? TruncatedLength { get; set; }

    public void AddPackage(string packageId, string version, byte[] content) =>
        packages[$"{packageId.ToLowerInvariant()}/{version.ToLowerInvariant()}/{packageId.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg"] = content;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.RequestUri);
        RequestedUris.Add(request.RequestUri);

        if (Redirects.TryGetValue(request.RequestUri, out var target))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = target } });
        }

        if (request.RequestUri.AbsoluteUri == ServiceIndexUrl)
        {
            ServiceIndexRequests++;
            return Task.FromResult(Json("""
                {
                  "version": "3.0.0",
                  "resources": [
                    { "@id": "https://feed.test/v3/flatcontainer/", "@type": "PackageBaseAddress/3.0.0" },
                    { "@id": "https://feed.test/v3/registration/", "@type": "RegistrationsBaseUrl/3.6.0" }
                  ]
                }
                """));
        }

        PackageRequests++;
        var relative = request.RequestUri.AbsolutePath.TrimStart('/');
        const string containerPath = "v3/flatcontainer/";
        if (relative.StartsWith(containerPath, StringComparison.Ordinal))
        {
            relative = relative[containerPath.Length..];
        }
        if (!packages.TryGetValue(relative, out var content))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        if (DownloadThrowsConnectionError)
        {
            throw new HttpRequestException("Simulated connection failure.");
        }

        if (DownloadStatus != HttpStatusCode.OK)
        {
            return Task.FromResult(new HttpResponseMessage(DownloadStatus));
        }

        var body = DownloadOverride ?? content;
        if (TruncatedLength is { } truncated)
        {
            var cut = new byte[Math.Min(truncated, body.Length)];
            Array.Copy(body, cut, cut.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ThrowingContent(cut)
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
        });
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class ThrowingContent(byte[] prefix) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeCoreAsync(stream);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context,
            CancellationToken cancellationToken) =>
            await SerializeCoreAsync(stream).ConfigureAwait(false);

        private async Task SerializeCoreAsync(Stream stream)
        {
            await stream.WriteAsync(prefix).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
            throw new IOException("Simulated interrupted transfer.");
        }
    }
}
