using System;
using System.Linq;
using SkyCD.Presentation.Scanning;
using Xunit;

namespace SkyCD.App.Tests;

public class FtpListingParserTests
{
    [Fact]
    public void Parse_ReadsMlsdUnixAndWindowsEntries()
    {
        const string listing = """
            type=cdir;perm=el; .
            type=pdir;perm=el; ..
            type=dir;modify=20240506070809;unix.mode=0755;perm=el; docs
            type=file;size=11;modify=20240506070809;unix.mode=0644;perm=r; my file.txt
            drwxr-xr-x 1 owner group 0 May 06 07:08 nested
            -rw-r--r-- 1 owner group 4 May 06 07:08 link.txt -> target
            05-06-24  07:08AM       <DIR>          windows-dir
            05-06-24  07:08AM                   11 windows file.txt
            total 2
            """;

        var entries = FtpListingParser.Parse(listing, "/pub");

        Assert.Equal(6, entries.Count);
        var docs = Assert.Single(entries, entry => entry.Name == "docs");
        Assert.True(docs.IsDirectory);
        Assert.Equal("/pub/docs", docs.FullName);
        Assert.Equal("rwxr-xr-x", docs.Permission);
        Assert.Equal(new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc), docs.ModifiedUtc);

        var spaced = Assert.Single(entries, entry => entry.Name == "my file.txt");
        Assert.False(spaced.IsDirectory);
        Assert.Equal(11, spaced.Size);
        Assert.Equal("/pub/my file.txt", spaced.FullName);
        Assert.Equal("rw-r--r--", spaced.Permission);

        var link = Assert.Single(entries, entry => entry.Name == "link.txt");
        Assert.Equal("/pub/link.txt", link.FullName);
        Assert.False(link.IsDirectory);

        var windowsFile = Assert.Single(entries, entry => entry.Name == "windows file.txt");
        Assert.Equal(11, windowsFile.Size);
        Assert.Equal(new DateTime(2024, 5, 6, 7, 8, 0, DateTimeKind.Utc), windowsFile.ModifiedUtc);
        Assert.Contains(entries, entry => entry.Name == "windows-dir" && entry.IsDirectory);
        Assert.DoesNotContain(entries, entry => entry.Name is "." or "..");
    }

    [Fact]
    public void Parse_StripsCredentialsFromThePublicAddress()
    {
        var location = FtpLocation.Parse("ftp://alice:s3cret-pass@127.0.0.1:2121/pub/docs/");

        Assert.Equal("127.0.0.1", location.Host);
        Assert.Equal(2121, location.Port);
        Assert.Equal("/pub/docs", location.Path);
        Assert.DoesNotContain("s3cret-pass", location.PublicUrl, StringComparison.Ordinal);
        Assert.DoesNotContain("@", location.PublicUrl, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:2121", location.PublicUrl, StringComparison.Ordinal);

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
