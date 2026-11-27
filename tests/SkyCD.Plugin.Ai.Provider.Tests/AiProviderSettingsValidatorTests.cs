using Xunit;
using SkyCD.SDK.Capabilities.Media;
using SkyCD.Plugin.Ai.Provider.Models;
using SkyCD.Plugin.Ai.Provider.Configuration;
using SkyCD.Plugin.Ai.Provider.Exceptions;

namespace SkyCD.Plugin.Ai.Provider.Tests;

public sealed class AiProviderSettingsValidatorTests
{
    private static AiProviderSettings Enabled() => new() { Enabled = true };

    [Fact]
    public void Validate_WhenDefaultsEnabled_Passes()
    {
        AiProviderSettingsValidator.Validate(Enabled(), requireEnabled: true);
    }

    [Fact]
    public void Validate_WhenDisabledAndRequired_Throws()
    {
        var exception = Assert.Throws<AiProviderException>(() =>
            AiProviderSettingsValidator.Validate(new AiProviderSettings(), requireEnabled: true));

        Assert.Contains("disabled", exception.Message);
    }

    [Theory]
    [InlineData("ftp")]
    [InlineData("HTTP")]
    [InlineData("")]
    public void Validate_WhenProtocolUnsupported_Throws(string protocol)
    {
        var settings = Enabled() with { Protocol = protocol };

        Assert.Throws<AiProviderException>(() =>
            AiProviderSettingsValidator.Validate(settings, requireEnabled: false));
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("localhost:443")]
    [InlineData("localhost/path")]
    [InlineData(".local")]
    [InlineData("")]
    public void Validate_WhenAddressHasSchemePortOrPath_Throws(string address)
    {
        var settings = Enabled() with { Address = address };

        Assert.Throws<AiProviderException>(() =>
            AiProviderSettingsValidator.Validate(settings, requireEnabled: false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Validate_WhenPortOutOfRange_Throws(int port)
    {
        var settings = Enabled() with { Port = port };

        Assert.Throws<AiProviderException>(() =>
            AiProviderSettingsValidator.Validate(settings, requireEnabled: false));
    }

    [Fact]
    public void Validate_WhenHttpsUsesPort80_Throws()
    {
        var settings = Enabled() with { Protocol = "https", Port = 80 };

        Assert.Throws<AiProviderException>(() =>
            AiProviderSettingsValidator.Validate(settings, requireEnabled: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WhenModelMissing_Throws(string model)
    {
        var settings = Enabled() with { Model = model };

        Assert.Throws<AiProviderException>(() =>
            AiProviderSettingsValidator.Validate(settings, requireEnabled: false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8001)]
    public void Validate_WhenOutputLimitOutOfRange_Throws(int limit)
    {
        var settings = Enabled() with { MaxOutputCharacters = limit };

        Assert.Throws<AiProviderException>(() =>
            AiProviderSettingsValidator.Validate(settings, requireEnabled: false));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(301)]
    public void Validate_WhenTimeoutOutOfRange_Throws(int timeout)
    {
        var settings = Enabled() with { TimeoutSeconds = timeout };

        Assert.Throws<AiProviderException>(() =>
            AiProviderSettingsValidator.Validate(settings, requireEnabled: false));
    }

    [Fact]
    public void Validate_WhenProviderUnknown_Throws()
    {
        var settings = Enabled() with { ProviderId = "unknown" };

        Assert.Throws<AiProviderException>(() =>
            AiProviderSettingsValidator.Validate(settings, requireEnabled: false));
    }

    [Fact]
    public void Validate_WhenLoopbackHttpSettings_Passes()
    {
        var settings = Enabled() with
        {
            ProviderId = "ollama",
            Protocol = "http",
            Address = "127.0.0.1",
            Port = 11434,
            Model = "llama3.2"
        };

        AiProviderSettingsValidator.Validate(settings, requireEnabled: true);
    }
}
