using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Presentation.Scanning;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Integration coverage for the WebDav.Client-based WebDAV transport (#625). Every test drives
/// the real <c>WebDavCatalogConnector</c> against a fake <see cref="HttpMessageHandler"/>, so the
/// PROPFIND requests, the 207 Multi-Status mapping and the exception semantics are exercised
/// without a live server. Basic/Digest authentication itself is delegated to
/// <see cref="HttpClientHandler"/>; live-server verification is documented in the PR.
/// </summary>
public sealed class WebDavCatalogTransportTests
{
    private const string Username = "catalog-user";
    private const string Password = "s3cret-pass";

    // ------------------------------------------------------------------ helpers

    private static RemoteLocation Location(int port, string path = "/pub/docs") =>
        RemoteLocation.Parse($"webdav://127.0.0.1:{port}{path}");

    private static RemoteCredentials Credentials(string? password = Password, bool anonymous = false) =>
        new(Username, password, anonymous);

    private static WebDavCatalogConnector Connector(FakeWebDavHandler handler) =>
        new("webdav", 80, handler);

    private static HttpResponseMessage Multistatus(string body) =>
        new(HttpStatusCode.MultiStatus)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/xml"),
        };

    private static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    private const string SelfResponse = """
        <D:response>
          <D:href>/pub/docs/</D:href>
          <D:propstat>
            <D:prop><D:resourcetype><D:collection/></D:resourcetype></D:prop>
            <D:status>HTTP/1.1 200 OK</D:status>
          </D:propstat>
        </D:response>
        """;

    private const string ListingBody = """
        <?xml version="1.0" encoding="utf-8"?>
        <D:multistatus xmlns:D="DAV:">
        """ + SelfResponse + """
          <D:response>
            <D:href>/pub/docs/file%20one.txt</D:href>
            <D:propstat>
              <D:prop>
                <D:resourcetype/>
                <D:getcontentlength>42</D:getcontentlength>
                <D:getlastmodified>2024-05-06T07:08:09Z</D:getlastmodified>
              </D:prop>
              <D:status>HTTP/1.1 200 OK</D:status>
            </D:propstat>
          </D:response>
          <D:response>
            <D:href>sub/</D:href>
            <D:propstat>
              <D:prop>
                <D:resourcetype><D:collection/></D:resourcetype>
                <D:getlastmodified>2024-05-06T07:08:09Z</D:getlastmodified>
              </D:prop>
              <D:status>HTTP/1.1 200 OK</D:status>
            </D:propstat>
          </D:response>
        </D:multistatus>
        """;

    /// <summary>Answers PROPFIND requests from a script keyed on the request.</summary>
    private sealed class FakeWebDavHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond;

        public FakeWebDavHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((request, _) => Task.FromResult(respond(request)))
        {
        }

        public FakeWebDavHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
            this.respond = respond;

        public List<string> RequestLines { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestLines.Add($"{request.Method} {request.RequestUri} depth={Depth(request)}");
            return respond(request, cancellationToken);
        }

        public static string? Depth(HttpRequestMessage request) =>
            request.Headers.TryGetValues("Depth", out var values) ? values.FirstOrDefault() : null;
    }

    // ------------------------------------------------------------------ nested listings

    [Fact]
    public void Connect_NestedListings_CarryNamesSizesAndTimestamps()
    {
        using var handler = new FakeWebDavHandler(request =>
            FakeWebDavHandler.Depth(request) == "0"
                ? Multistatus("<?xml version=\"1.0\"?><D:multistatus xmlns:D=\"DAV:\">" + SelfResponse + "</D:multistatus>")
                : Multistatus(ListingBody));
        var connector = Connector(handler);

        using var session = connector.Connect(Location(2121), Credentials(), CancellationToken.None);
        var entries = session.ListDirectory("/pub/docs");

        var file = Assert.Single(entries, entry => entry.Name == "file one.txt");
        Assert.False(file.IsDirectory);
        Assert.Equal(42, file.Size);
        Assert.Equal("/pub/docs/file one.txt", file.FullName);
        Assert.Equal(new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc), file.ModifiedUtc);

        var sub = Assert.Single(entries, entry => entry.Name == "sub");
        Assert.True(sub.IsDirectory);
        Assert.Equal(0, sub.Size);

        // The collection's own record never reaches the catalog and the trailing slash
        // collection semantics are honoured.
        Assert.Equal(2, entries.Count);
        Assert.Contains("depth=0", handler.RequestLines[0], StringComparison.Ordinal);
        Assert.Contains("depth=1", handler.RequestLines[1], StringComparison.Ordinal);
        Assert.Contains("http://127.0.0.1:2121/pub/docs/", handler.RequestLines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void ListDirectory_ChildCollection_UsesTheResolvedRequestUri()
    {
        using var handler = new FakeWebDavHandler(request =>
            FakeWebDavHandler.Depth(request) == "0"
                ? Multistatus("<?xml version=\"1.0\"?><D:multistatus xmlns:D=\"DAV:\">" + SelfResponse + "</D:multistatus>")
                : Multistatus(ListingBody));
        var connector = Connector(handler);

        using var session = connector.Connect(Location(2121), Credentials(), CancellationToken.None);
        session.ListDirectory("/pub/docs/sub");

        Assert.Contains("http://127.0.0.1:2121/pub/docs/sub/", handler.RequestLines[^1], StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ authentication

    [Fact]
    public void Connect_RejectedCredentials_FailsWithTypedAuthenticationError()
    {
        using var handler = new FakeWebDavHandler(_ => Status(HttpStatusCode.Unauthorized));
        var connector = Connector(handler);
        const string wrongPassword = "wrong-secret-9234";

        var exception = Assert.Throws<RemoteAuthenticationException>(() =>
            connector.Connect(Location(2121), Credentials(wrongPassword), CancellationToken.None));

        Assert.Equal(
            LocalizationService.Current.Get(TextKeys.WebDavAuthenticationFailed),
            exception.Message);
        Assert.DoesNotContain(wrongPassword, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Password, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Connect_AnonymousAccess_ConnectsWithoutCredentials()
    {
        using var handler = new FakeWebDavHandler(_ =>
            Multistatus("<?xml version=\"1.0\"?><D:multistatus xmlns:D=\"DAV:\">" + SelfResponse + "</D:multistatus>"));
        var connector = Connector(handler);

        using var session = connector.Connect(Location(2121), Credentials(anonymous: true), CancellationToken.None);
        Assert.NotNull(session);
    }

    // ------------------------------------------------------------------ typed status mapping

    [Fact]
    public void ListDirectory_MissingCollection_FailsWithTypedDirectoryError()
    {
        // The connect probe is lenient about a missing path; the listing reports it typed.
        using var handler = new FakeWebDavHandler(_ => Status(HttpStatusCode.NotFound));
        var connector = Connector(handler);

        using var session = connector.Connect(Location(2121, "/missing"), Credentials(), CancellationToken.None);
        var exception = Assert.Throws<RemoteDirectoryNotFoundException>(() => session.ListDirectory("/missing"));
        Assert.Contains("/missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ListDirectory_Forbidden_FailsWithTypedListingError()
    {
        using var handler = new FakeWebDavHandler(request =>
            FakeWebDavHandler.Depth(request) == "0"
                ? Multistatus("<?xml version=\"1.0\"?><D:multistatus xmlns:D=\"DAV:\">" + SelfResponse + "</D:multistatus>")
                : Status(HttpStatusCode.Forbidden));
        var connector = Connector(handler);

        using var session = connector.Connect(Location(2121), Credentials(), CancellationToken.None);
        var exception = Assert.Throws<RemoteListingNotSupportedException>(() => session.ListDirectory("/pub/docs"));

        Assert.Equal(
            LocalizationService.Current.Format(TextKeys.WebDavListingNotSupported, "/pub/docs"),
            exception.Message);
    }

    [Fact]
    public void ListDirectory_MalformedMultistatus_FailsWithTypedListingError()
    {
        using var handler = new FakeWebDavHandler(request =>
            FakeWebDavHandler.Depth(request) == "0"
                ? Multistatus("<?xml version=\"1.0\"?><D:multistatus xmlns:D=\"DAV:\">" + SelfResponse + "</D:multistatus>")
                : Multistatus("<html><body>not a multistatus</body></html>"));
        var connector = Connector(handler);

        using var session = connector.Connect(Location(2121), Credentials(), CancellationToken.None);
        Assert.Throws<RemoteListingNotSupportedException>(() => session.ListDirectory("/pub/docs"));
    }

    [Fact]
    public void ListDirectory_MissingMultistatusBody_FailsWithTypedListingError()
    {
        using var handler = new FakeWebDavHandler(request =>
            FakeWebDavHandler.Depth(request) == "0"
                ? Multistatus("<?xml version=\"1.0\"?><D:multistatus xmlns:D=\"DAV:\">" + SelfResponse + "</D:multistatus>")
                : Multistatus(string.Empty));
        var connector = Connector(handler);

        using var session = connector.Connect(Location(2121), Credentials(), CancellationToken.None);
        Assert.Throws<RemoteListingNotSupportedException>(() => session.ListDirectory("/pub/docs"));
    }

    [Fact]
    public void ListDirectory_ServerError_FailsWithConnectionError()
    {
        using var handler = new FakeWebDavHandler(request =>
            FakeWebDavHandler.Depth(request) == "0"
                ? Multistatus("<?xml version=\"1.0\"?><D:multistatus xmlns:D=\"DAV:\">" + SelfResponse + "</D:multistatus>")
                : Status(HttpStatusCode.ServiceUnavailable));
        var connector = Connector(handler);

        using var session = connector.Connect(Location(2121), Credentials(), CancellationToken.None);
        var exception = Assert.Throws<RemoteConnectionException>(() => session.ListDirectory("/pub/docs"));

        Assert.StartsWith(
            LocalizationService.Current.Format(TextKeys.WebDavConnectionFailed, "127.0.0.1:2121"),
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("503", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Password, exception.ToString(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ partial failures

    [Fact]
    public void ListDirectory_PartialPropertyFailure_MapsTheAvailableFields()
    {
        const string partial = """
            <?xml version="1.0" encoding="utf-8"?>
            <D:multistatus xmlns:D="DAV:">
            """ + SelfResponse + """
              <D:response>
                <D:href>/pub/docs/broken.bin</D:href>
                <D:propstat>
                  <D:prop><D:resourcetype/></D:prop>
                  <D:status>HTTP/1.1 200 OK</D:status>
                </D:propstat>
                <D:propstat>
                  <D:prop><D:getcontentlength/><D:getlastmodified/></D:prop>
                  <D:status>HTTP/1.1 404 Not Found</D:status>
                </D:propstat>
              </D:response>
            </D:multistatus>
            """;
        using var handler = new FakeWebDavHandler(request =>
            FakeWebDavHandler.Depth(request) == "0"
                ? Multistatus("<?xml version=\"1.0\"?><D:multistatus xmlns:D=\"DAV:\">" + SelfResponse + "</D:multistatus>")
                : Multistatus(partial));
        var connector = Connector(handler);

        using var session = connector.Connect(Location(2121), Credentials(), CancellationToken.None);
        var entry = Assert.Single(session.ListDirectory("/pub/docs"), item => item.Name == "broken.bin");

        Assert.False(entry.IsDirectory);
        Assert.Equal(0, entry.Size);
        Assert.Null(entry.ModifiedUtc);
    }

    // ------------------------------------------------------------------ cancellation

    [Fact]
    public void Connect_CancelledToken_ThrowsOperationCanceled()
    {
        using var handler = new FakeWebDavHandler(_ =>
            Multistatus("<?xml version=\"1.0\"?><D:multistatus xmlns:D=\"DAV:\">" + SelfResponse + "</D:multistatus>"));
        var connector = Connector(handler);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            connector.Connect(Location(2121), Credentials(), cancelled.Token));
    }

    [Fact]
    public async Task ListDirectory_CancelledDuringRequest_ThrowsOperationCanceled()
    {
        using var cancelled = new CancellationTokenSource();
        using var handler = new FakeWebDavHandler(async (request, token) =>
        {
            if (FakeWebDavHandler.Depth(request) == "0")
            {
                return Multistatus("<?xml version=\"1.0\"?><D:multistatus xmlns:D=\"DAV:\">" + SelfResponse + "</D:multistatus>");
            }

            cancelled.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            return Multistatus(ListingBody);
        });
        var connector = Connector(handler);

        using var session = connector.Connect(Location(2121), Credentials(), cancelled.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Task.Run(() => session.ListDirectory("/pub/docs"), CancellationToken.None));
    }

    // ------------------------------------------------------------------ webdavs

    [Fact]
    public void Connect_WebDavS_RequestsHttps()
    {
        using var handler = new FakeWebDavHandler(_ =>
            Multistatus("<?xml version=\"1.0\"?><D:multistatus xmlns:D=\"DAV:\">" + SelfResponse + "</D:multistatus>"));
        var connector = new WebDavCatalogConnector("webdavs", 443, handler);

        using var session = connector.Connect(
            RemoteLocation.Parse("webdavs://127.0.0.1/pub/docs"), Credentials(), CancellationToken.None);
        Assert.NotNull(session);
        Assert.Contains("https://127.0.0.1/pub/docs/", handler.RequestLines[0], StringComparison.Ordinal);
    }
}
