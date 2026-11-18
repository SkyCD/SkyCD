using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace SkyCD.Plugin.Adc.Tests;

/// <summary>
/// The checked-in fixtures and manifest are the compatibility oracle for normal development
/// and CI. Every expectation is read from the manifest, not recomputed by the plugin.
/// </summary>
public sealed class AdcCheckedInFixtureTests
{
    private readonly AdcPlugin plugin = new();

    [Theory]
    [InlineData("empty.cat")]
    [InlineData("nested.cat")]
    [InlineData("multivolume.cat")]
    [InlineData("comments.cat")]
    public async Task ReadAsync_CheckedInFixture_MatchesManifest(string fileName)
    {
        var fixture = await LoadFixtureManifestAsync(fileName);
        var bytes = await File.ReadAllBytesAsync(FixturePath(fileName));

        using var stream = new MemoryStream(bytes, writable: false);
        var result = await plugin.ReadAsync(AdcFixtures.ReadRequest(stream, fileName));
        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<AdcReportDocument>(result.Payload);

        Assert.Equal(fixture.GetProperty("recordCount").GetInt32(), document.Summary.TotalRecordsRead);
        Assert.Equal(fixture.GetProperty("commentCount").GetInt32(), document.Summary.CommentCount);
        Assert.Equal(fixture.GetProperty("categoryCount").GetInt32(), document.Summary.CategoryCount);
        Assert.Equal(fixture.GetProperty("formatVersion").GetString(), document.Summary.DetectedVersion);
        Assert.False(document.Summary.IsPartialReport);
        Assert.Empty(document.Summary.SkippedReasons);

        var rowsById = document.Rows.ToDictionary(row => row["nodeId"]!.ToString()!);
        var media = document.Rows.Where(row => Kind(row) == "media").ToList();
        var expectedVolumes = fixture.GetProperty("volumes").EnumerateArray().ToList();
        Assert.Equal(expectedVolumes.Count, media.Count);
        Assert.Equal(expectedVolumes.Count, document.Summary.VolumeCount);

        foreach (var expectedVolume in expectedVolumes)
        {
            var id = expectedVolume.GetProperty("id").GetUInt32().ToString();
            var row = Assert.Single(media, candidate => Props(candidate)["AdcVolumeId"]?.ToString() == id);
            var properties = Props(row);
            Assert.Equal(expectedVolume.GetProperty("label").GetString(), row["name"]?.ToString());
            Assert.Equal(expectedVolume.GetProperty("type").GetString(), properties["AdcVolumeType"]?.ToString());
            Assert.Equal(
                expectedVolume.GetProperty("typeCode").GetByte().ToString(),
                properties["AdcVolumeTypeCode"]?.ToString());
            Assert.Equal(
                expectedVolume.GetProperty("serial").GetString(),
                properties["AdcSerialNumber"]?.ToString());
            Assert.Equal(
                expectedVolume.GetProperty("capacityBytes").GetString(),
                properties["AdcCapacityBytes"]?.ToString());
            Assert.Equal(
                expectedVolume.GetProperty("flags").GetByte().ToString(),
                properties["AdcVolumeFlags"]?.ToString());

            var source = expectedVolume.GetProperty("source").GetString();
            if (string.IsNullOrEmpty(source))
            {
                Assert.False(properties.ContainsKey("AdcSourcePath"));
            }
            else
            {
                Assert.Equal(source, properties["AdcSourcePath"]?.ToString());
            }

            AssertProperties(expectedVolume.GetProperty("properties"), properties);
        }

        var entries = fixture.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(entries.Count, document.Summary.ItemCount);
        foreach (var expected in entries)
        {
            var id = expected.GetProperty("id").GetUInt32().ToString();
            var row = Assert.Single(document.Rows, candidate =>
                Kind(candidate) is "file" or "folder" &&
                Props(candidate)["AdcItemId"]?.ToString() == id);
            var properties = Props(row);

            Assert.Equal(expected.GetProperty("kind").GetString(), Kind(row));
            Assert.Equal(expected.GetProperty("name").GetString(), row["name"]?.ToString());
            Assert.Equal(expected.GetProperty("sizeBytes").GetString(), row["sizeBytes"]?.ToString());
            Assert.Equal(
                expected.GetProperty("volumeId").GetUInt32().ToString(),
                properties["AdcVolumeId"]?.ToString());
            Assert.Equal(
                expected.GetProperty("attributes").GetByte().ToString(),
                properties["AdcAttributes"]?.ToString());
            Assert.Equal(
                expected.GetProperty("typeCode").GetUInt32().ToString(),
                properties["AdcTypeCode"]?.ToString());

            var expectedParentId = expected.GetProperty("parentId").GetUInt32();
            var expectedParentNode = expectedParentId == 0
                ? $"media:{expected.GetProperty("volumeId").GetUInt32()}"
                : $"folder:{expectedParentId}";
            Assert.Equal(expectedParentNode, row["parentId"]?.ToString());

            AssertOptional(properties, "Comments", expected, "comment");
            AssertOptional(properties, "AdcCreatedUtc", expected, "createdUtc");
            AssertOptional(properties, "AdcModifiedUtc", expected, "modifiedUtc");
            AssertOptional(properties, "AdcAccessedUtc", expected, "accessedUtc");

            var categories = expected.GetProperty("categories").EnumerateArray()
                .Select(value => value.GetString()).ToList();
            if (categories.Count == 0)
            {
                Assert.False(properties.ContainsKey("Categories"));
                Assert.False(properties.ContainsKey("AdcCategoryIds"));
            }
            else
            {
                Assert.Equal(string.Join("; ", categories), properties["Categories"]?.ToString());
                var categoryIds = expected.GetProperty("categoryIds").EnumerateArray()
                    .Select(value => value.GetUInt32().ToString());
                Assert.Equal(string.Join(",", categoryIds), properties["AdcCategoryIds"]?.ToString());
            }

            AssertProperties(expected.GetProperty("properties"), properties);
            Assert.Equal(expected.GetProperty("path").GetString(), PathOf(row, rowsById));
        }
    }

    [Theory]
    [InlineData("malformed/truncated.cat", "truncated")]
    [InlineData("malformed/corrupt.cat", "signature")]
    [InlineData("malformed/unsupported.cat", "unsupported")]
    [InlineData("windows-security.cat", "Authenticode")]
    public async Task ReadAsync_MalformedFixture_ReturnsDedicatedFailure(string fixtureName, string expectedError)
    {
        var bytes = await File.ReadAllBytesAsync(FixturePath(fixtureName));

        using (var detectionStream = new MemoryStream(bytes, writable: false))
        {
            Assert.False(plugin.CanHandleContent(detectionStream, Path.GetFileName(fixtureName)));
        }

        using var stream = new MemoryStream(bytes, writable: false);
        var result = await plugin.ReadAsync(AdcFixtures.ReadRequest(stream, Path.GetFileName(fixtureName)));
        Assert.False(result.Success);
        Assert.Contains(expectedError, result.Error);
    }

    internal static string FixturePath(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, AdcFixtures.FixtureDirectory, name);
        Assert.True(File.Exists(path), $"fixture '{name}' was not copied to the output directory.");
        return path;
    }

    private static async Task<JsonElement> LoadFixtureManifestAsync(string fileName)
    {
        using var manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(FixturePath("manifest.json")));
        return manifest.RootElement.GetProperty("fixtures").EnumerateArray()
            .Single(fixture => fixture.GetProperty("file").GetString() == fileName)
            .Clone();
    }

    private static string? Kind(Dictionary<string, object?> row) => row["kind"]?.ToString();

    private static Dictionary<string, object?> Props(Dictionary<string, object?> row) =>
        (Dictionary<string, object?>)row["properties"]!;

    private static void AssertOptional(
        Dictionary<string, object?> properties,
        string propertyKey,
        JsonElement expected,
        string expectedKey)
    {
        var value = expected.GetProperty(expectedKey);
        if (value.ValueKind == JsonValueKind.Null)
        {
            Assert.False(properties.ContainsKey(propertyKey));
            return;
        }

        Assert.Equal(value.GetString(), properties[propertyKey]?.ToString());
    }

    private static void AssertProperties(JsonElement expected, Dictionary<string, object?> properties)
    {
        var expectedProperties = expected.EnumerateObject().ToList();
        foreach (var property in expectedProperties)
        {
            Assert.Equal(property.Value.GetString(), properties[property.Name]?.ToString());
        }

        if (expectedProperties.Count == 0)
        {
            Assert.DoesNotContain(properties.Keys, key => key.StartsWith("AdcProp_", System.StringComparison.Ordinal));
        }
    }

    private static string PathOf(
        Dictionary<string, object?> row,
        Dictionary<string, Dictionary<string, object?>> rowsById)
    {
        var segments = new List<string> { row["name"]!.ToString()! };
        var parentId = row["parentId"]?.ToString();
        while (!string.IsNullOrWhiteSpace(parentId) && rowsById.TryGetValue(parentId, out var parent))
        {
            segments.Insert(0, parent["name"]!.ToString()!);
            parentId = parent["parentId"]?.ToString();
        }

        return string.Join("/", segments);
    }
}
