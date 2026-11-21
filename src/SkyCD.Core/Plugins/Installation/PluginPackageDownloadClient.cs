using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Runtime.Installation;
using SkyCD.Plugin.Runtime.Installation.Exceptions;

namespace SkyCD.Core.Plugins.Installation;

/// <summary>
/// Downloads one exact package from a NuGet v3 feed over HTTPS. The client rejects insecure URLs, follows no
/// redirects, enforces a hard size bound while streaming and writes only into the caller-provided staging file.
/// </summary>
internal sealed class PluginPackageDownloadClient : IDisposable
{
    public const string PackageBaseAddressResourceType = "PackageBaseAddress/3.0.0";

    private readonly HttpClient httpClient;
    private readonly long maxPackageBytes;

    public PluginPackageDownloadClient(HttpMessageHandler? handler, TimeSpan timeout, long maxPackageBytes)
    {
        if (maxPackageBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPackageBytes));
        }

        httpClient = handler is null
            ? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
            : new HttpClient(handler, disposeHandler: false);
        httpClient.Timeout = timeout;
        this.maxPackageBytes = maxPackageBytes;
    }

    public long MaxPackageBytes => maxPackageBytes;

    /// <summary>Downloads the package content into <paramref name="destinationPath"/> and returns its length.</summary>
    public async Task<long> DownloadPackageAsync(PluginPackageRequest request, string destinationPath,
        CancellationToken cancellationToken)
    {
        var packageId = request.NormalizedPackageId;
        var version = request.NormalizedVersion;
        var feedUri = ValidateFeedUri(request);
        var baseAddress = await ResolvePackageBaseAddressAsync(feedUri, cancellationToken).ConfigureAwait(false);
        var lowerId = Uri.EscapeDataString(packageId.ToLowerInvariant());
        var lowerVersion = Uri.EscapeDataString(version.ToLowerInvariant());
        var packageUri = new Uri(baseAddress, $"{lowerId}/{lowerVersion}/{lowerId}.{lowerVersion}.nupkg");

        HttpStatusCode statusCode;
        HttpResponseMessage response;
        try
        {
            response = await httpClient
                .GetAsync(packageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            statusCode = response.StatusCode;
        }
        catch (HttpRequestException)
        {
            throw new PluginPackageDownloadException(packageId, version, "the feed could not be reached");
        }

        using var packageResponse = response;
        if (statusCode != HttpStatusCode.OK)
        {
            throw new PluginPackageDownloadException(packageId, version,
                $"the feed returned HTTP {(int)statusCode}");
        }

        if (packageResponse.Content.Headers.ContentLength > maxPackageBytes)
        {
            throw new PluginPackageDownloadException(packageId, version,
                $"the package exceeds the {maxPackageBytes} byte limit");
        }

        try
        {
            await using var source = await packageResponse.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 81920, useAsync: true);

            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > maxPackageBytes)
                {
                    throw new PluginPackageDownloadException(packageId, version,
                        $"the package exceeds the {maxPackageBytes} byte limit");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            return total;
        }
        catch (HttpRequestException)
        {
            throw new PluginPackageDownloadException(packageId, version, "the transfer was interrupted");
        }
        catch (IOException)
        {
            throw new PluginPackageDownloadException(packageId, version, "the transfer was interrupted");
        }
    }

    public void Dispose() => httpClient.Dispose();

    private static Uri ValidateFeedUri(PluginPackageRequest request)
    {
        if (!Uri.TryCreate(request.serviceIndexUrl?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new PluginFeedUnavailableException(request.serviceIndexUrl ?? string.Empty,
                "the service index must be an absolute HTTPS URL");
        }

        return uri;
    }

    private async Task<Uri> ResolvePackageBaseAddressAsync(Uri feedUri, CancellationToken cancellationToken)
    {
        JsonDocument document;
        try
        {
            using var response = await httpClient.GetAsync(feedUri, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new PluginFeedUnavailableException(feedUri.AbsoluteUri,
                    $"the feed returned HTTP {(int)response.StatusCode}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (PluginFeedUnavailableException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException)
        {
            throw new PluginFeedUnavailableException(feedUri.AbsoluteUri, "the service index could not be read");
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("resources", out var resources) ||
                resources.ValueKind != JsonValueKind.Array)
            {
                throw new PluginFeedUnavailableException(feedUri.AbsoluteUri, "the service index has no resources");
            }

            foreach (var resource in resources.EnumerateArray())
            {
                if (resource.ValueKind != JsonValueKind.Object ||
                    !resource.TryGetProperty("@type", out var type) ||
                    type.ValueKind != JsonValueKind.String ||
                    !string.Equals(type.GetString(), PackageBaseAddressResourceType, StringComparison.OrdinalIgnoreCase) ||
                    !resource.TryGetProperty("@id", out var id) ||
                    id.ValueKind != JsonValueKind.String ||
                    !Uri.TryCreate(id.GetString(), UriKind.Absolute, out var baseAddress) ||
                    baseAddress.Scheme != Uri.UriSchemeHttps)
                {
                    continue;
                }

                return baseAddress.AbsoluteUri.EndsWith('/')
                    ? baseAddress
                    : new Uri(baseAddress.AbsoluteUri + "/");
            }
        }

        throw new PluginFeedUnavailableException(feedUri.AbsoluteUri,
            $"the '{PackageBaseAddressResourceType}' resource is missing or not HTTPS");
    }
}
