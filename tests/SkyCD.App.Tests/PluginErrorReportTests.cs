using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions;
using Xunit;

namespace SkyCD.App.Tests;

public sealed class PluginErrorReportTests
{
    [Fact]
    public async Task PackageContainsOnlyReviewedMetadataAndPersistsAnonymousIdentity()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            const string secret = "password=secret token=abcdef C:\\Users\\private /home/private account page";
            var failure = PluginFailure.Create(typeof(PluginErrorReportTests), "menu", new InvalidOperationException(secret));
            var report = PluginErrorReport.Create(failure, directory);
            var next = PluginErrorReport.Create(failure, directory);
            Assert.Equal(report.InstallationId, next.InstallationId);
            Assert.NotEqual(Guid.Empty, report.InstallationId);
            Assert.NotEqual(report.ReportId, next.ReportId);
            Assert.False(report.FollowFixes);
            Assert.DoesNotContain(secret, report.ToJson());
            Assert.DoesNotContain("private", report.ToJson());
            Assert.Contains(failure.Fingerprint, Uri.UnescapeDataString(report.ReviewUrl));
            Assert.Contains(failure.Fingerprint, Uri.UnescapeDataString(report.FixesUrl));
            using var stream = new MemoryStream();
            await report.WritePackageAsync(stream);
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = Assert.Single(archive.Entries);
            Assert.Equal("report.json", entry.FullName);
            using var reader = new StreamReader(entry.Open());
            Assert.Equal(report.ToJson(), await reader.ReadToEndAsync());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void FingerprintIgnoresSensitiveMessagesAndSeparatesOperations()
    {
        var first = PluginFailure.Create(GetType(), "menu", new InvalidOperationException("token=one"));
        var second = PluginFailure.Create(GetType(), "menu", new InvalidOperationException("token=two"));
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.NotEqual(first.Fingerprint, PluginFailure.Create(GetType(), "read", new InvalidOperationException()).Fingerprint);
        Assert.NotEqual(first.Fingerprint, PluginFailure.Create(GetType(), "menu", new IOException()).Fingerprint);
        Assert.Equal("plugin", PluginFailure.Create(GetType(), "secret path", new IOException()).Operation);
    }
}
