using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Core.OAuth;

/// <summary>External-browser login with a listener bound only to IPv4 loopback.</summary>
public sealed class SystemOAuthBrowser : IOAuthBrowser
{
    private readonly Action<Uri> _openBrowser;

    public SystemOAuthBrowser() : this(static uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }))
    {
    }

    public SystemOAuthBrowser(Action<Uri> openBrowser) => _openBrowser = openBrowser;

    public async Task<Uri> AuthorizeAsync(Func<Uri, Uri> buildAuthorizationUri, int port, CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        var localPort = ((IPEndPoint)listener.LocalEndpoint).Port;
        var redirect = new Uri($"http://127.0.0.1:{localPort}/oauth/callback");
        cancellationToken.ThrowIfCancellationRequested();
        _openBrowser(buildAuthorizationUri(redirect));

        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            // Bound both request size and time so an unrelated local connection cannot stall login.
            // The budget must tolerate a machine under heavy load: a browser sends its single callback
            // and never retries, so discarding it here would leave the login waiting for a new connection.
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            var buffer = new byte[8192];
            var length = 0;
            try
            {
                while (length < buffer.Length && !HeadersComplete(buffer.AsSpan(0, length)))
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(length), requestTimeout.Token);
                    if (read == 0)
                        break;
                    length += read;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            var requestLine = Encoding.ASCII.GetString(buffer, 0, length).Split("\r\n")[0].Split(' ');
            var valid = HeadersComplete(buffer.AsSpan(0, length))
                && requestLine.Length == 3 && requestLine[0] == "GET"
                && requestLine[1].StartsWith("/oauth/callback?", StringComparison.Ordinal)
                && Uri.TryCreate(redirect.GetLeftPart(UriPartial.Authority) + requestLine[1], UriKind.Absolute, out _);
            const string message = "Return to SkyCD to complete sign-in. You can close this window.";
            var status = valid ? "200 OK" : "404 Not Found";
            var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {message.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n{message}");
            try
            {
                await stream.WriteAsync(response, requestTimeout.Token);
            }
            catch (IOException) { }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
            if (valid)
                return new Uri(redirect.GetLeftPart(UriPartial.Authority) + requestLine[1]);
        }
    }

    private static bool HeadersComplete(ReadOnlySpan<byte> header) => header.Length >= 4
        && header[^4] == (byte)'\r' && header[^3] == (byte)'\n' && header[^2] == (byte)'\r' && header[^1] == (byte)'\n';
}
