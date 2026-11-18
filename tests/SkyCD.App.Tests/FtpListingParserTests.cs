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
        var location = FtpLocation.Parse("ftp://alice:s3cret-pass@127.0.0.1:2121/pub/docs/");

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
        Assert.True(FtpLocation.TryReadUserInfo("ftp://alice:s3cret-pass@127.0.0.1:2121/pub/docs/", out var username, out var password));
        Assert.Equal("alice", username);
        Assert.Equal("s3cret-pass", password);

        Assert.True(FtpLocation.TryReadUserInfo("ftp://alice:p%40ss:word@127.0.0.1/pub", out username, out password));
        Assert.Equal("alice", username);
        Assert.Equal("p@ss:word", password);

        Assert.False(FtpLocation.TryReadUserInfo("ftp://127.0.0.1/pub", out _, out _));
    }

    [Fact]
    public void Parse_RejectsNonFtpSchemesAndOmitsSecretsFromTheScanDescription()
    {
        var error = Assert.Throws<FtpSchemeNotSupportedException>(() => FtpLocation.Parse("http://files.example/pub"));
        Assert.DoesNotContain("password", error.Message, StringComparison.OrdinalIgnoreCase);

        const string password = "s3cret-pass";
        var options = new FtpScanOptions(
            $"ftp://alice:{password}@127.0.0.1/pub",
            "alice",
            password,
            anonymous: false,
            mediaName: "Remote",
            targetParentId: "library",
            includeExtendedInfo: true);

        Assert.DoesNotContain(password, options.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("alice:", options.ToString(), StringComparison.Ordinal);
    }
}
