using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Accounts.JetBrains.Exceptions;
using SkyCD.Plugin.Accounts.JetBrains.Models;

namespace SkyCD.Plugin.Accounts.JetBrains.Sources;

/// <summary>Reads owned licenses from the JetBrains Account API with the OAuth bearer token.</summary>
public sealed class JetBrainsLicenseClient(HttpClient httpClient)
{
    private readonly HttpClient httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<IReadOnlyList<JetBrainsLicense>> GetLicensesAsync(
        JetBrainsSettings settings,
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var request = new HttpRequestMessage(HttpMethod.Get, settings.GetLicensesUri());
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new JetBrainsApiException(response.StatusCode);
        }

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        return ParseLicenses(document.RootElement);
    }

    private static IReadOnlyList<JetBrainsLicense> ParseLicenses(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("licenses", out var nested))
        {
            root = nested;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new JetBrainsApiResponseException();
        }

        var licenses = new List<JetBrainsLicense>();
        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                throw new JetBrainsApiResponseException();
            }

            licenses.Add(ParseLicense(element));
        }

        return licenses;
    }

    private static JetBrainsLicense ParseLicense(JsonElement element)
    {
        var product = GetObject(element, "product");
        var productCode = GetString(product, "code") ?? GetString(product, "name") ?? string.Empty;
        var productName = GetString(product, "name");
        if (string.IsNullOrWhiteSpace(productName))
        {
            productName = JetBrainsProducts.ResolveName(productCode);
        }

        var subscription = GetObject(element, "subscription");
        var perpetual = GetObject(element, "perpetual");
        var isTrial = GetBoolean(element, "isTrial");
        var suspended = GetBoolean(element, "isSuspended");
        var licenseType = isTrial
            ? "trial"
            : subscription is not null
                ? "subscription"
                : perpetual is not null
                    ? "perpetual"
                    : "license";

        var expires = ParseDate(subscription, "validUntilDate") ?? ParseDate(perpetual, "upgradeDueDate");

        return new JetBrainsLicense
        {
            LicenseId = GetString(element, "licenseId") ?? string.Empty,
            ProductCode = string.IsNullOrWhiteSpace(productCode) ? "UNKNOWN" : productCode.Trim(),
            ProductName = productName!,
            LicenseType = licenseType,
            ExpiresUtc = expires,
            RenewsAutomatically = GetBoolean(subscription, "isAutomaticallyRenewed"),
            Suspended = suspended,
            IsTrial = isTrial,
            Seats = GetSeats(element),
            AssigneeEmail = GetString(GetObject(element, "assignee"), "email")
        };
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

    private static bool GetBoolean(JsonElement? element, string property)
    {
        if (element is not { } value || !value.TryGetProperty(property, out var found))
        {
            return false;
        }

        return found.ValueKind == JsonValueKind.True;
    }

    private static int GetSeats(JsonElement element)
    {
        if (!element.TryGetProperty("seats", out var seats)
            && !element.TryGetProperty("seatCount", out seats))
        {
            return 1;
        }

        return seats.ValueKind == JsonValueKind.Number && seats.TryGetInt32(out var count) && count > 0
            ? count
            : 1;
    }

    private static DateTimeOffset? ParseDate(JsonElement? element, string property)
    {
        var value = GetString(element, property);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
            ? date
            : null;
    }
}
