using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Runtime.Tests.Marketplace;

/// <summary>
/// Deterministic stand-in for every remote document the catalog reads. Routes are matched by absolute URL and
/// mutated through <see cref="Serve"/> between refreshes, so tests model publishers repoisting a feed without
/// editing the catalog. Every requested URL is recorded for assertions.
/// </summary>
internal sealed class FakePluginFeed : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, Task<HttpResponseMessage>>> routes =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> requests = [];

    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (requests)
            {
                return requests.ToArray();
            }
        }
    }

    public int RequestCount
    {
        get
        {
            lock (requests)
            {
                return requests.Count;
            }
        }
    }

    public FakePluginFeed Serve(string url, string json, HttpStatusCode status = HttpStatusCode.OK,
        string contentType = "application/json")
    {
        return Serve(url, _ => Task.FromResult(Text(json, status, contentType)));
    }

    public FakePluginFeed Serve(string url, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);
        routes[url] = request => Task.FromResult(respond(request));
        return this;
    }

    public FakePluginFeed Serve(string url, Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);
        routes[url] = respond;
        return this;
    }

    public FakePluginFeed Redirect(string url, string location)
    {
        return Serve(url, _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            return response;
        });
    }

    public FakePluginFeed Fail(string url, HttpStatusCode status = HttpStatusCode.InternalServerError)
    {
        return Serve(url, "{}", status);
    }

    public FakePluginFeed Remove(string url)
    {
        routes.Remove(url);
        return this;
    }

    /// <summary>Serves a whole NuGet v3 feed: service index and version lists for the given packages.</summary>
    public FakePluginFeed ServeNuGetFeed(string feedIndexUrl, string packageBaseAddress,
        params (string Id, string[] Versions)[] packages)
    {
        Serve(feedIndexUrl, $$"""
            {
              "version": "3.0.0",
              "resources": [
                { "@id": "https://api.nuget.org/v3/registration5-gz-semver2/index.json", "@type": "RegistrationsBaseUrl" },
                { "@id": "{{packageBaseAddress}}", "@type": "PackageBaseAddress/3.0.0" }
              ]
            }
            """);

        foreach (var package in packages)
        {
            var id = package.Id.ToLowerInvariant();
            var versions = string.Join(", ", Array.ConvertAll(package.Versions,
                version => $"\"{version.ToLowerInvariant()}\""));
            Serve($"{packageBaseAddress.TrimEnd('/')}/{id}/index.json", $$"""
                { "versions": [ {{versions}} ] }
                """);
        }

        return this;
    }

    public static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK,
        string contentType = "application/json")
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType)
        };
    }

    /// <summary>A response whose body never completes until the test signals it.</summary>
    public static HttpResponseMessage Hanging()
    {
        var content = new StreamContent(new NeverEndingStream());
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.AbsoluteUri;
        lock (requests)
        {
            requests.Add(url);
        }

        if (!routes.TryGetValue(url, out var respond))
        {
            return Task.FromResult(Text("""{"error":"not found"}""", HttpStatusCode.NotFound));
        }

        return respond(request);
    }

    private sealed class NeverEndingStream : System.IO.Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, System.IO.SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }
    }
}
