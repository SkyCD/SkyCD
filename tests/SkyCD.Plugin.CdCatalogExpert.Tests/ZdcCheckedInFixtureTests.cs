using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace SkyCD.Plugin.CdCatalogExpert.Tests;

/// <summary>
/// The checked-in fixtures plus their expectation files are the compatibility
/// oracle for normal development and CI.
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

        using (var stream = ZdcFixture.StreamOf(bytes))
        {
            var database = ZdcReader.Read(stream);
            Assert.Equal(expectedMembers.Count, database.Members.Count);
            foreach (var expected in expectedMembers)
            {
                var name = expected.GetProperty("name").GetString();
                var member = Assert.Single(database.Members, m => string.Equals(m.FileName, name, StringComparison.Ordinal));
                Assert.Equal(expected.GetProperty("payloadBytes").GetInt32(), member.Payload.Length);
                Assert.Equal(
                    expected.GetProperty("payloadSha256").GetString(),
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(member.Payload)));
            }
        }

        using var readStream = ZdcFixture.StreamOf(bytes);
        var result = await plugin.ReadAsync(ZdcFixture.ReadRequest(readStream, "sample.zdc"));
        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<ZdcReportDocument>(result.Payload);
        Assert.Contains(document.Rows, r => r["name"]?.ToString() == "Sample Catalog");
        Assert.Equal(2, document.Rows.Count(r => r["kind"]?.ToString() == "media"));
    }

    [Fact]
    public void Read_SampleFixture_RewritesByteIdentical()
    {
        var bytes = File.ReadAllBytes(FixturePath("sample.zdc"));
        using var stream = new MemoryStream(bytes, writable: false);
        var restored = ZdcReader.Read(stream);
        Assert.Equal(bytes, ZdcFixture.Serialize(restored));
    }

    [Theory]
    [InlineData("minimal.zdc", "minimal.expected.json")]
    [InlineData("full.zdc", "full.expected.json")]
    [InlineData("boundary.zdc", "boundary.expected.json")]
    public async Task ReadAsync_CheckedInFixture_MatchesExpectation(
        string containerName,
        string expectationName)
    {
        using var expectation = JsonDocument.Parse(
            await File.ReadAllTextAsync(FixturePath(expectationName)));
        var expected = expectation.RootElement;

        using var stream = ZdcFixture.StreamOf(await File.ReadAllBytesAsync(FixturePath(containerName)));
        var result = await plugin.ReadAsync(ZdcFixture.ReadRequest(stream, containerName));
        Assert.True(result.Success, result.Error);
        var document = Assert.IsType<ZdcReportDocument>(result.Payload);

        var catalog = Assert.Single(document.Rows, r => Kind(r) == "catalog");
        Assert.Equal(expected.GetProperty("catalogName").GetString(), catalog["name"]?.ToString());

        var media = document.Rows.Where(r => r["kind"]?.ToString() == "media").ToList();
        Assert.Equal(expected.GetProperty("media").GetArrayLength(), media.Count);
        foreach (var expectedMedia in expected.GetProperty("media").EnumerateArray())
        {
            var actual = Assert.Single(media, m => m["name"]?.ToString() == expectedMedia.GetProperty("name").GetString());
            var props = Assert.IsType<Dictionary<string, object?>>(actual["properties"]);
            foreach (var field in expectedMedia.GetProperty("fields").EnumerateObject())
            {
                Assert.Equal(field.Value.GetString(), props[field.Name]?.ToString());
            }
        }

        var files = document.Rows.Where(r => r["kind"]?.ToString() == "file").ToList();
        Assert.Equal(expected.GetProperty("files").GetArrayLength(), files.Count);
        foreach (var expectedFile in expected.GetProperty("files").EnumerateArray())
        {
            var actual = Assert.Single(files, f => f["name"]?.ToString() == expectedFile.GetProperty("name").GetString());
            Assert.Equal(expectedFile.GetProperty("size").GetString(), actual["sizeBytes"]?.ToString());
            var props = Assert.IsType<Dictionary<string, object?>>(actual["properties"]);
            Assert.Equal(expectedFile.GetProperty("attributes").GetString(), props["ZdcAttributes"]?.ToString());
        }

        var folders = document.Rows.Where(r => Kind(r) == "directory").ToList();
        Assert.Equal(expected.GetProperty("directories").GetInt32(), folders.Count);

        var members = ZdcReader.Read(ZdcFixture.StreamOf(await File.ReadAllBytesAsync(FixturePath(containerName))))
            .Members;
        Assert.Equal(expected.GetProperty("members").GetArrayLength(), members.Count);
        foreach (var expectedMember in expected.GetProperty("members").EnumerateArray())
        {
            var name = expectedMember.GetProperty("name").GetString();
            var member = Assert.Single(members, m => string.Equals(m.FileName, name, StringComparison.Ordinal));
            Assert.Equal(
                expectedMember.GetProperty("sha256").GetString(),
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(member.Payload)));
        }

        foreach (var warning in expected.GetProperty("warnings").EnumerateArray())
        {
            Assert.Contains(warning.GetString(), document.Summary.SkippedReasons);
        }
    }

    [Theory]
    [InlineData("malformed/missing-member.zdc", "missing disc-tree member")]
    [InlineData("malformed/bad-guid.zdc", "not a valid GUID")]
    [InlineData("malformed/invalid-number.zdc", "invalid signed decimal value")]
    [InlineData("malformed/invalid-encoding.zdc", "not well-formed XML")]
    [InlineData("malformed/invalid-xml.zdc", "well-formed")]
    [InlineData("malformed/excessive-depth.zdc", "nesting exceeds")]
    public async Task ReadAsync_MalformedFixture_ReturnsFailure(string containerName, string expectedError)
    {
        using var stream = ZdcFixture.StreamOf(await File.ReadAllBytesAsync(FixturePath(containerName)));
        var result = await plugin.ReadAsync(ZdcFixture.ReadRequest(stream, Path.GetFileName(containerName)));
        Assert.False(result.Success);
        Assert.Contains(expectedError, result.Error);
    }

    [Fact]
    public async Task ReadAsync_CorruptContainerFixture_ReturnsFailure()
    {
        using var stream = ZdcFixture.StreamOf(await File.ReadAllBytesAsync(FixturePath("malformed/corrupt-container.zdc")));
        var result = await plugin.ReadAsync(ZdcFixture.ReadRequest(stream, "corrupt-container.zdc"));
        Assert.False(result.Success);
        Assert.NotEmpty(result.Error ?? string.Empty);
    }

    private static string? Kind(Dictionary<string, object?> row) =>
        row["properties"] is Dictionary<string, object?> props && props.TryGetValue("ZdcNodeKind", out var kind)
            ? kind?.ToString()
            : null;
}
