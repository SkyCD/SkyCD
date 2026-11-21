using System;
using System.IO;
using Xunit;
using SkyCD.Plugin.Abstractions.Capabilities.Media;
using SkyCD.Plugin.AiProvider.Exceptions;
using SkyCD.Plugin.AiProvider.Models;

namespace SkyCD.Plugin.AiProvider.Tests;

public sealed class AiProviderSettingsStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(),
        "skycd-ai-provider-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Read_WhenMissing_ReturnsDefaults()
    {
        var store = new AiProviderSettingsStore(Path.Combine(directory, "missing.json"));

        var settings = store.Read();

        Assert.Equal("lmstudio", settings.ProviderId);
        Assert.False(settings.Enabled);
        Assert.Equal("google/gemma-4-e2b", settings.Model);
        Assert.True(settings.ResizeLargeImages);
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
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "broken.json"), "{ not json");

        var store = new AiProviderSettingsStore(Path.Combine(directory, "broken.json"));

        Assert.Throws<AiProviderException>(() => store.Read());
    }

    [Fact]
    public void Read_WhenFileTooLarge_ThrowsAiProviderException()
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "large.json");
        File.WriteAllBytes(path, new byte[AiProviderSettingsStore.MaximumBytes + 1]);

        var store = new AiProviderSettingsStore(path);

        Assert.Throws<AiProviderException>(() => store.Read());
    }
}
