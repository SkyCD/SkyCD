using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Host.Media;
using SkyCD.Plugin.Metadata.Text;
using SkyCD.Plugin.Metadata.YamlToml.Exceptions;
using SkyCD.Plugin.Metadata.YamlToml.Models;
using SkyCD.Plugin.Runtime.Factories;
using SkyCD.SDK.Capabilities.Media;
using Xunit;

namespace SkyCD.Plugin.Metadata.YamlToml.Tests;

public sealed class YamlTomlPluginTests
{
    private const string PluginId = "SkyCD.Plugin.YamlToml";

    private const string SingleYaml = """
        # heading
        "quoted key": value
        count: 2
        """;

    private const string MultiYaml = """
        name: one
        ---
        name: two
        """;

    private const string AnchorYaml = """
        base: &base
          a: 1
        copy: *base
        merged:
          <<: *base
          b: 2
        """;

    private const string TomlSample = """
        # root
        title = "demo"

        [owner]
        name = "Ada"

        [database]
        ports = [8000, 8001]
        enabled = true
        "dotted.key" = "quoted"

        [server.connection]
        when = 2024-01-02T03:04:05Z
        note = '''
        line one
        line two
        '''

        [[products]]
        name = "widget"

        [[products]]
        name = "gadget"
        """;

    [Fact]
    public async Task Yaml_PublishesStructureForASingleDocument()
    {
        var values = await ExtractAsync(".yaml", SingleYaml);

        Assert.Equal("yaml", Text(values, YamlTomlSchema.Format));
        Assert.True(Bool(values, YamlTomlSchema.Valid));
        Assert.True(Integer(values, YamlTomlSchema.MappingCount) >= 1);
        Assert.True(Integer(values, YamlTomlSchema.ScalarCount) >= 2);
        Assert.True(Integer(values, YamlTomlSchema.CommentCount) >= 1);
        Assert.Contains("quoted key", Text(values, YamlTomlSchema.TopLevelKeys), StringComparison.Ordinal);
        Assert.DoesNotContain(values, value => value.Key == YamlTomlSchema.FormatVersion);
    }

    [Fact]
    public async Task Yaml_CountsDocumentsAnchorsAliasesAndMergeKeys()
    {
        var multi = await ExtractAsync(".yml", MultiYaml);
        Assert.Equal(2, Integer(multi, YamlTomlSchema.DocumentCount));

        var anchored = await ExtractAsync(".yaml", AnchorYaml);
        Assert.True(Integer(anchored, YamlTomlSchema.AnchorCount) >= 1);
        Assert.True(Integer(anchored, YamlTomlSchema.AliasCount) >= 1);
        Assert.True(Integer(anchored, YamlTomlSchema.MergeKeyCount) >= 1);
        Assert.True(Integer(anchored, YamlTomlSchema.MaxDepth) >= 2);
    }

    [Fact]
    public async Task Yaml_PublishesTheDirectiveVersion()
    {
        var values = await ExtractAsync(".yaml", "%YAML 1.2\n---\nname: demo\n");
        Assert.Equal("1.2", Text(values, YamlTomlSchema.FormatVersion));
    }

    [Fact]
    public async Task Toml_PublishesTablesArraysOfTablesAndKeys()
    {
        var values = await ExtractAsync(".toml", TomlSample);

        Assert.Equal("toml", Text(values, YamlTomlSchema.Format));
        Assert.True(Bool(values, YamlTomlSchema.Valid));
        Assert.True(Integer(values, YamlTomlSchema.TableCount) >= 3);
        Assert.True(Integer(values, YamlTomlSchema.ArrayOfTablesCount) >= 1);
        Assert.True(Integer(values, YamlTomlSchema.KeyCount) >= 8);
        Assert.True(Integer(values, YamlTomlSchema.CommentCount) >= 1);
        Assert.Contains("owner", Text(values, YamlTomlSchema.TopLevelTables), StringComparison.Ordinal);
        Assert.Contains("products", Text(values, YamlTomlSchema.TopLevelTables), StringComparison.Ordinal);
        Assert.DoesNotContain(values, value => value.Key == YamlTomlSchema.MappingCount);
    }

    [Theory]
    [InlineData(".yaml", "name: [unterminated")]
    [InlineData(".toml", "name = \"unterminated")]
    [InlineData(".yaml", "name: \"unterminated")]
    public async Task InvalidInput_ReportsCodeAndPositionWithoutATree(string extension, string text)
    {
        var values = await ExtractAsync(extension, text);

        Assert.False(Bool(values, YamlTomlSchema.Valid));
        Assert.False(string.IsNullOrWhiteSpace(Text(values, YamlTomlSchema.ErrorCode)));
        Assert.True(Integer(values, YamlTomlSchema.ErrorLine) >= 1);
        Assert.DoesNotContain(values, value => value.Key is YamlTomlSchema.MappingCount
            or YamlTomlSchema.ScalarCount
            or YamlTomlSchema.TableCount
            or YamlTomlSchema.KeyCount
            or YamlTomlSchema.TopLevelKeys
            or YamlTomlSchema.TopLevelTables);
    }

    [Fact]
    public async Task Preview_IsDeterministicAndOmittedWhenTheRasterizerIsUnavailable()
    {
        var bytes = Encoding.UTF8.GetBytes(SingleYaml);
        var first = await PreviewAsync(bytes, new YamlTomlPreviewPlugin());
        var second = await PreviewAsync(bytes, new YamlTomlPreviewPlugin());
        Assert.Equal(first, second);
        Assert.True(first.Length > 8);

        var metadata = await ExtractAsync(".yaml", SingleYaml);
        Assert.True(Bool(metadata, YamlTomlSchema.Valid));

        var omitted = await new YamlTomlPreviewPlugin(YamlTomlPluginOptions.Default, new UnavailableRenderer())
            .GeneratePreviewAsync(
                new MediaItem(".yaml", "application/yaml", bytes.Length),
                new ByteArrayMediaSource(bytes),
                progress: null,
                CancellationToken.None);
        Assert.Empty(omitted.Artifacts);
    }

    [Fact]
    public async Task Source_IsReadOnceAndOversizedInputIsRejected()
    {
        var bytes = Encoding.UTF8.GetBytes("name: demo\n");
        var source = new ByteArrayMediaSource(bytes);
        await new YamlTomlMetadataPlugin().ExtractAsync(
            new MediaItem(".yaml", null, bytes.Length), source, null, CancellationToken.None);
        Assert.Equal(1, source.ReadCount);

        var limited = new YamlTomlMetadataPlugin(new YamlTomlPluginOptions(maxSourceBytes: 4));
        await Assert.ThrowsAsync<YamlTomlSourceTooLargeException>(() => limited.ExtractAsync(
            new MediaItem(".yaml", null, bytes.Length),
            new ByteArrayMediaSource(bytes),
            null,
            CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_IsHonoured()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new YamlTomlMetadataPlugin().ExtractAsync(
            new MediaItem(".yaml", null, 4),
            new ByteArrayMediaSource("a: 1"u8.ToArray()),
            null,
            source.Token));
    }

    [Fact]
    public async Task Host_PublishesMetadataAndAnIndexTimePage()
    {
        var bytes = Encoding.UTF8.GetBytes(TomlSample);
        var results = await CreateManager().ProcessAsync(
            new MediaItem(".toml", "application/toml", bytes.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(bytes)));

        Assert.Equal(2, results.Count);
        Assert.All(results, result => Assert.False(result.Failed));
        Assert.Contains(results, result => result.Schema?.Id == YamlTomlSchema.MetadataSchemaId);
        var preview = Assert.Single(results, result => result.Schema?.Id == YamlTomlSchema.PreviewSchemaId);
        Assert.Equal("image/png", Assert.Single(preview.Preview!.Artifacts).ContentType);

        var plugin = new YamlTomlPreviewPlugin();
        Assert.True(plugin.Descriptor.IndexTimePreview);
        Assert.True(plugin.Descriptor.Priority > 0);
    }

    [Fact]
    public async Task TextPlugin_StillPublishesAlongsideTheStructuredRecord()
    {
        var bytes = Encoding.UTF8.GetBytes(SingleYaml);
        var manager = new MediaExtensionManager(
        [
            new MediaProvider("SkyCD.Plugin.Text", "1.0.0", new TextMetadataPlugin()),
            new MediaProvider("SkyCD.Plugin.Text", "1.0.0", new TextPreviewPlugin()),
            new MediaProvider(PluginId, "1.0.0", new YamlTomlMetadataPlugin()),
            new MediaProvider(PluginId, "1.0.0", new YamlTomlPreviewPlugin())
        ]);

        var results = await manager.ProcessAsync(
            new MediaItem(".yaml", "text/yaml", bytes.Length),
            _ => Task.FromResult<Stream>(new MemoryStream(bytes)));

        Assert.Contains(results, result => result.Schema?.Id == "skycd.text.metadata" && !result.Failed);
        Assert.Contains(results, result => result.Schema?.Id == YamlTomlSchema.MetadataSchemaId && !result.Failed);
        Assert.Contains(results, result => result.Schema?.Id == "skycd.text.preview" && !result.Failed);
        Assert.Contains(results, result => result.Schema?.Id == YamlTomlSchema.PreviewSchemaId && !result.Failed);
    }

    [Fact]
    public void AssemblyDiscovery_FindsBothCapabilities()
    {
        var plugin = new DiscoveredPluginFactory().BuildFromAssembly(typeof(YamlTomlMetadataPlugin).Assembly);

        Assert.Equal(PluginId, plugin.Id);
        Assert.Equal(2, plugin.Capabilities.Count);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaMetadataCapability);
        Assert.Contains(plugin.Capabilities, capability => capability is IMediaPreviewCapability);
    }

    [Fact]
    public void OfficeImoPackages_ShareOneVersion()
    {
        var root = FindRepositoryRoot();
        var versions = Directory.EnumerateFiles(Path.Combine(root, "Plugins"), "*.csproj", SearchOption.AllDirectories)
            .SelectMany(path => Regex.Matches(
                File.ReadAllText(path),
                "PackageReference Include=\"(OfficeIMO\\.[^\"]+)\" Version=\"([^\"]+)\""))
            .Select(match => match.Groups[2].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(versions);
        Assert.Single(versions);
    }

    private static async Task<System.Collections.Generic.IReadOnlyList<MediaMetadataValue>> ExtractAsync(
        string extension, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var result = await new YamlTomlMetadataPlugin().ExtractAsync(
            new MediaItem(extension, null, bytes.Length),
            new ByteArrayMediaSource(bytes),
            progress: null,
            CancellationToken.None);
        return result.Values;
    }

    private static async Task<byte[]> PreviewAsync(byte[] bytes, YamlTomlPreviewPlugin plugin)
    {
        var result = await plugin.GeneratePreviewAsync(
            new MediaItem(".yaml", "application/yaml", bytes.Length),
            new ByteArrayMediaSource(bytes),
            progress: null,
            CancellationToken.None);
        return Assert.Single(result.Artifacts).Data.ToArray();
    }

    private static MediaExtensionManager CreateManager() =>
        new(
        [
            new MediaProvider(PluginId, "1.0.0", new YamlTomlMetadataPlugin()),
            new MediaProvider(PluginId, "1.0.0", new YamlTomlPreviewPlugin())
        ]);

    private static string Text(System.Collections.Generic.IReadOnlyList<MediaMetadataValue> values, string key) =>
        values.Single(value => value.Key == key).Text!;

    private static bool Bool(System.Collections.Generic.IReadOnlyList<MediaMetadataValue> values, string key) =>
        values.Single(value => value.Key == key).Boolean!.Value;

    private static long Integer(System.Collections.Generic.IReadOnlyList<MediaMetadataValue> values, string key) =>
        values.Single(value => value.Key == key).Integer!.Value;

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SkyCD.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
