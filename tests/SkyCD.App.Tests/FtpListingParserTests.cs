using System;
using SkyCD.Presentation.Scanning;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// FTP address, credential and scan-description handling. The listing dialects themselves are
/// parsed by FluentFTP (#580) and covered by <see cref="FtpRemoteListingMapperTests"/> and
/// <see cref="FtpCatalogTransportTests"/>.
/// </summary>
public class FtpListingParserTests
{
    [Fact]
    public void Parse_ReadsHostPortAndPathWithoutCredentials()
    {
        var location = RemoteLocation.Parse("ftp://alice:s3cret-pass@127.0.0.1:2121/pub/docs/");

        Assert.Equal("127.0.0.1", location.Host);
        Assert.Equal(2121, location.Port);
        Assert.Equal("/pub/docs", location.Path);
        Assert.DoesNotContain("s3cret-pass", location.PublicUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("@", location.PublicUrl, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:2121", location.PublicUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void TryReadUserInfo_UnescapesCredentials()
    {
        Assert.True(RemoteLocation.TryReadUserInfo("ftp://alice:s3cret-pass@127.0.0.1:2121/pub/docs/", out var username, out var password));
        Assert.Equal("alice", username);
        Assert.Equal("s3cret-pass", password);

        Assert.True(RemoteLocation.TryReadUserInfo("ftp://alice:p%40ss:word@127.0.0.1/pub", out username, out password));
        Assert.Equal("alice", username);
        Assert.Equal("p@ss:word", password);

        Assert.False(RemoteLocation.TryReadUserInfo("ftp://127.0.0.1/pub", out _, out _));
    }

    [Fact]
    public void Parse_RejectsNonFtpSchemesAndOmitsSecretsFromTheScanDescription()
    {
        var error = Assert.Throws<RemoteSchemeNotSupportedException>(() => RemoteLocation.Parse("http://files.example/pub"));
        Assert.DoesNotContain("password", error.Message, StringComparison.OrdinalIgnoreCase);

        const string password = "s3cret-pass";
        var options = new RemoteScanOptions(
            $"ftp://alice:{password}@127.0.0.1/pub",
            "alice",
            password,
            anonymous: false,
            mediaName: "Remote",
            targetParentId: "library",
            includeExtendedInfo: true);

        Assert.StartsWith("FTP ", options.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(password, options.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("alice:", options.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_FtpsScheme_DefaultsToImplicitTlsAndPort990()
    {
        var location = RemoteLocation.Parse("ftps://files.example/pub");

        Assert.Equal("ftps", location.Scheme);
        Assert.Equal(990, location.Port);
        Assert.Equal(RemoteTlsMode.Implicit, location.TlsMode);
        Assert.Equal("ftps://files.example:990/pub", location.PublicUrl);
    }

    [Fact]
    public void Parse_TlsQuery_OverridesTheSchemeDefaultAndStaysOutOfThePublicUrl()
    {
        Assert.Equal(RemoteTlsMode.Off, RemoteLocation.Parse("ftp://files.example/pub").TlsMode);
        Assert.Equal(RemoteTlsMode.Explicit, RemoteLocation.Parse("ftp://files.example/pub?tls=explicit").TlsMode);
        Assert.Equal(RemoteTlsMode.Off, RemoteLocation.Parse("ftps://files.example/pub?tls=off").TlsMode);
        Assert.Equal(RemoteTlsMode.Explicit, RemoteLocation.Parse("ftps://files.example/pub?tls=EXPLICIT").TlsMode);

        var location = RemoteLocation.Parse("ftp://files.example:2121/pub?tls=explicit");
        Assert.Equal(2121, location.Port);
        Assert.Equal("ftp://files.example:2121/pub", location.PublicUrl);
    }

    [Fact]
    public void Parse_UnknownTlsValue_FailsClearlyWithoutEchoingSecrets()
    {
        var error = Assert.Throws<RemoteConnectionException>(() =>
            RemoteLocation.Parse("ftp://alice:s3cret-pass@files.example/pub?tls=banana"));
        Assert.DoesNotContain("s3cret-pass", error.ToString(), StringComparison.Ordinal);
    }
}
