using System;
using System.IO;
using Xunit;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Ai.Provider.Exceptions;
using SkyCD.Plugin.Ai.Provider.Models;
using SkyCD.TestSupport;

namespace SkyCD.Plugin.Ai.Provider.Tests;

public sealed class AiProviderSettingsStoreTests : IDisposable
{
    private readonly TestWorkspace workspace = new();

    public void Dispose()
    {
        workspace.Dispose();
    }

    private string directory => workspace.Root;

    [Fact]
    public void Read_WhenMissing_ReturnsDefaults()
    {
        var store = new AiProviderSettingsStore(Path.Combine(directory, "missing.json"));

        var settings = store.Read();

        Assert.Equal("ollama", settings.ProviderId);
        Assert.False(settings.Enabled);
        Assert.Equal("llama3.2", settings.Model);
        Assert.True(settings.ResizeLargeImages);
    }

    [Theory]
    [InlineData("", 1234, "google/gemma-4-e2b")]
    [InlineData(""","PORT":4321,"Model":"saved-model" """, 4321, "saved-model")]
    public void Read_WhenRetiredPreset_PreservesConnectionAsCustom(string fields, int port, string model)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "provider.json");
        File.WriteAllText(path, """{"providerId":"LMSTUDIO","address":"localhost","apiKey":"saved-key","enabled":true""" + fields + "}");

        var settings = new AiProviderSettingsStore(path).Read();

        Assert.Equal("custom", settings.ProviderId);
        Assert.Equal("localhost", settings.Address);
        Assert.Equal(port, settings.Port);
        Assert.Equal(model, settings.Model);
        Assert.Equal("saved-key", settings.ApiKey);
        Assert.True(settings.Enabled);
    }

    [Fact]
    public void WriteThenRead_RoundTripsAllFields()
    {
        var store = new AiProviderSettingsStore(Path.Combine(directory, "nested", "provider.json"));
        var expected = new AiProviderSettings
        {
            ProviderId = "ollama",
            Protocol = "http",
            Address = "127.0.0.1",
            Port = 11434,
            ApiKey = "secret",
            Model = "llama3.2",
            Language = "Lithuanian",
            ResizeLargeImages = false,
            Enabled = true,
            MaxOutputCharacters = 512,
            TimeoutSeconds = 30,
            Prompt = "Describe."
        };

        store.Write(expected);
        var actual = store.Read();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Read_WhenJsonInvalid_ThrowsAiProviderException()
    {
        workspace.CreateFile("broken.json", "{ not json");

        var store = new AiProviderSettingsStore(Path.Combine(directory, "broken.json"));

        Assert.Throws<AiProviderException>(() => store.Read());
    }

    [Fact]
    public void Read_WhenFileTooLarge_ThrowsAiProviderException()
    {
        var path = workspace.CreateFile("large.json", new byte[AiProviderSettingsStore.MaximumBytes + 1]);

        var store = new AiProviderSettingsStore(path);

        Assert.Throws<AiProviderException>(() => store.Read());
    }
}
