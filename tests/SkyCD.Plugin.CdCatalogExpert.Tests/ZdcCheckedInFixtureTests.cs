using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace SkyCD.Plugin.CdCatalogExpert.Tests;

/// <summary>
/// The checked-in <c>sample.zdc</c> fixture plus its manifest are the
/// compatibility oracle for normal development and CI.
/// </summary>
public sealed class ZdcCheckedInFixtureTests
{
    private readonly ZdcPlugin plugin = new();

    private static string FixturePath(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        Assert.True(File.Exists(path), $"fixture '{name}' was not copied to the output directory.");
        return path;
    }

    [Fact]
    public async Task ReadAsync_SampleFixture_MatchesManifest()
    {
        var manifestJson = await File.ReadAllTextAsync(FixturePath("sample.manifest.json"));
        using var manifest = JsonDocument.Parse(manifestJson);
        var expectedMembers = manifest.RootElement.GetProperty("members").EnumerateArray().ToList();

        var bytes = await File.ReadAllBytesAsync(FixturePath("sample.zdc"));
        Assert.Equal(
            manifest.RootElement.GetProperty("container").GetProperty("totalBytes").GetInt32(),
            bytes.Length);

        using var stream = ZdcFixture.StreamOf(bytes);
        var result = await plugin.ReadAsync(ZdcFixture.ReadRequest(stream, "sample.zdc"));
        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<ZdcReportDocument>(result.Payload);

        var files = document.Rows.Where(r => r["kind"]?.ToString() == "file").ToList();
        Assert.Equal(expectedMembers.Count, files.Count);
        foreach (var expected in expectedMembers)
        {
            var name = expected.GetProperty("name").GetString();
            var file = Assert.Single(files, f => string.Equals(f["name"]?.ToString(), name, StringComparison.Ordinal));
            var props = Assert.IsType<Dictionary<string, object?>>(file["properties"]);
            Assert.Equal(
                expected.GetProperty("storedLength").GetUInt64().ToString(),
                props["ZdcStoredLength"]?.ToString());
            var payload = Convert.FromBase64String(Assert.IsType<string>(props["ZdcPayload"]));
            Assert.Equal(expected.GetProperty("payloadBytes").GetInt32(), payload.Length);
            Assert.Equal(
                expected.GetProperty("payloadSha256").GetString(),
                props["ZdcPayloadSha256"]?.ToString());
        }
    }

    [Fact]
    public void Read_SampleFixture_RewritesByteIdentical()
    {
        var bytes = File.ReadAllBytes(FixturePath("sample.zdc"));
        using var stream = ZdcFixture.StreamOf(bytes);
        var restored = ZdcReader.Read(stream);
        Assert.Equal(bytes, ZdcFixture.Serialize(restored));
    }
}
