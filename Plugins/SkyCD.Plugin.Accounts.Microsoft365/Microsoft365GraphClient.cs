using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SkyCD.Plugin.Accounts.Microsoft365;

/// <summary>Reads owned subscriptions from Microsoft Graph with the OAuth bearer token.</summary>
public sealed class Microsoft365GraphClient(HttpClient httpClient)
{
    private readonly HttpClient httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    /// <summary>
    /// Reads company subscriptions, which include renewal dates, and falls back to subscribed
    /// SKUs when the tenant exposes only the older endpoint.
    /// </summary>
    public async Task<IReadOnlyList<Microsoft365Subscription>> GetSubscriptionsAsync(
        Microsoft365Settings settings,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var directoryResponse = await SendAsync(settings.GetCompanySubscriptionsUri(), accessToken,
            cancellationToken);
        if (directoryResponse.IsSuccessStatusCode)
        {
            using var directoryDocument = JsonDocument.Parse(
                await directoryResponse.Content.ReadAsStringAsync(cancellationToken));
            return ParseCompanySubscriptions(directoryDocument.RootElement);
        }

        using var skuResponse = await SendAsync(settings.GetSubscribedSkusUri(), accessToken, cancellationToken);
        if (!skuResponse.IsSuccessStatusCode)
        {
            throw new Microsoft365ApiException(skuResponse.StatusCode);
        }

        using var skuDocument = JsonDocument.Parse(
            await skuResponse.Content.ReadAsStringAsync(cancellationToken));
        return ParseSubscribedSkus(skuDocument.RootElement);
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, string accessToken,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await httpClient.SendAsync(request, cancellationToken);
    }

    private static IReadOnlyList<Microsoft365Subscription> ParseCompanySubscriptions(JsonElement root)
    {
        if (!TryGetValueArray(root, out var values))
        {
            throw new Microsoft365ApiResponseException();
        }

        var subscriptions = new List<Microsoft365Subscription>();
        foreach (var element in values.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new Microsoft365ApiResponseException();
            }

            var skuPartNumber = GetString(element, "skuPartNumber") ?? GetString(element, "skuId") ?? string.Empty;
            subscriptions.Add(new Microsoft365Subscription
            {
                SubscriptionId = GetString(element, "id") ?? skuPartNumber,
                SkuPartNumber = skuPartNumber,
                ProductName = Microsoft365Products.ResolveName(skuPartNumber),
                Seats = Math.Max(0, GetInt32(element, "totalLicenses") ?? 0),
                RenewsUtc = ParseDate(element, "nextLifecycleDateTime"),
                IsTrial = GetBoolean(element, "isTrial"),
                ProviderStatus = GetString(element, "status") ?? string.Empty
            });
        }

        return subscriptions;
    }

    private static IReadOnlyList<Microsoft365Subscription> ParseSubscribedSkus(JsonElement root)
    {
        if (!TryGetValueArray(root, out var values))
        {
            throw new Microsoft365ApiResponseException();
        }

        var subscriptions = new List<Microsoft365Subscription>();
        foreach (var element in values.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new Microsoft365ApiResponseException();
            }

            var skuPartNumber = GetString(element, "skuPartNumber") ?? GetString(element, "skuId") ?? string.Empty;
            var prepaidUnits = GetObject(element, "prepaidUnits");
            subscriptions.Add(new Microsoft365Subscription
            {
                SubscriptionId = GetString(element, "skuId") ?? GetString(element, "id") ?? skuPartNumber,
                SkuPartNumber = skuPartNumber,
                ProductName = Microsoft365Products.ResolveName(skuPartNumber),
                Seats = Math.Max(0, GetInt32(prepaidUnits, "enabled") ?? 0),
                ProviderStatus = GetString(element, "capabilityStatus") ?? string.Empty
            });
        }

        return subscriptions;
    }

    private static bool TryGetValueArray(JsonElement root, out JsonElement values)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("value", out values)
            && values.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        values = default;
        return false;
    }

    private static JsonElement? GetObject(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static string? GetString(JsonElement? element, string property)
    {
        if (element is not { } value || !value.TryGetProperty(property, out var found))
        {
            return null;
        }

        return found.ValueKind == JsonValueKind.String ? found.GetString() : null;
    }

    private static int? GetInt32(JsonElement? element, string property)
    {
        if (element is not { } value || !value.TryGetProperty(property, out var found)
            || found.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return found.TryGetInt32(out var number) ? number : null;
    }

    private static bool GetBoolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out var found) && found.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? ParseDate(JsonElement element, string property)
    {
        var value = GetString(element, property);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
            ? date
            : null;
    }
}
