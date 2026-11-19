using System;
using System.IO;
using System.Linq;
using System.Threading;
using SkyCD.Presentation.Scanning;
using SkyCD.UI.Localization;
using Xunit;

namespace SkyCD.App.Tests;

/// <summary>
/// Provider coverage for the Windows SMB transport (#626) against a fake session
/// (<see cref="FakeScanFileSystem"/>), as the license policy blocks every NuGet SMB client
/// (SMBLibrary is LGPL-3.0-or-later) and CI cannot host a controlled SMB server. Live
/// verification against a real share is documented in docs/architecture/catalog-scan-traversal.md.
/// The fake-session tests use anonymous access so no real network connection is established;
/// explicit credentials are covered by the translation matrix and the account-name builder.
/// </summary>
public sealed class SmbCatalogTransportTests
{
    private const string Host = "fileserver";

    // ------------------------------------------------------------------ helpers

    private static RemoteLocation LocationFor(string uncPath) => RemoteLocation.Parse(uncPath);

    private static RemoteCredentials Guest() => new(null, null, anonymous: true);

    private static void SeedShare(FakeScanFileSystem fileSystem)
    {
        fileSystem.AddDirectory($@"\\{Host}\share");
        fileSystem.AddDirectory($@"\\{Host}\share\docs");
        fileSystem.AddFile($@"\\{Host}\share\docs\file one.txt", length: 42);
        fileSystem.AddDirectory($@"\\{Host}\share\docs\sub");
        fileSystem.AddFile($@"\\{Host}\share\docs\sub\deep.txt", length: 7);
    }

    // ------------------------------------------------------------------ listings

    [Fact]
    public void Connect_NestedListings_CarryNamesSizesAndTimestamps()
    {
        var fileSystem = new FakeScanFileSystem();
        SeedShare(fileSystem);
        var connector = new SmbCatalogConnector(fileSystem);

        using var session = connector.Connect(
            LocationFor($@"\\{Host}\share\docs"), Guest(), CancellationToken.None);
        var entries = session.ListDirectory("/share/docs");

        var file = Assert.Single(entries, entry => entry.Name == "file one.txt");
        Assert.False(file.IsDirectory);
        Assert.Equal(42, file.Size);
        Assert.Equal("/share/docs/file one.txt", file.FullName);
        Assert.NotNull(file.ModifiedUtc);

        var sub = Assert.Single(entries, entry => entry.Name == "sub");
        Assert.True(sub.IsDirectory);
        Assert.Equal(0, sub.Size);
    }

    [Fact]
    public void Connect_UncAndSmbAddresses_ProduceIdenticalOutput()
    {
        var fileSystem = new FakeScanFileSystem();
        SeedShare(fileSystem);
        var connector = new SmbCatalogConnector(fileSystem);

        var uncLocation = RemoteLocation.Parse($@"\\{Host}\share\docs");
        var smbLocation = RemoteLocation.Parse($"smb://{Host}/share/docs");
        Assert.Equal(uncLocation.Scheme, smbLocation.Scheme);
        Assert.Equal(uncLocation.Host, smbLocation.Host);
        Assert.Equal(uncLocation.Path, smbLocation.Path);
        Assert.Equal(uncLocation.PublicUrl, smbLocation.PublicUrl);

        using var fromUnc = connector.Connect(uncLocation, Guest(), CancellationToken.None);
        using var fromSmb = connector.Connect(smbLocation, Guest(), CancellationToken.None);
        Assert.Equal(
            fromUnc.ListDirectory("/share/docs").Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal),
            fromSmb.ListDirectory("/share/docs").Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void Connect_ReparsePointDirectory_CarriesTheResolvedVisitedIdentity()
    {
        var fileSystem = new FakeScanFileSystem();
        SeedShare(fileSystem);
        fileSystem.LinkDirectory($@"\\{Host}\share\docs\loop", $@"\\{Host}\share\docs");
        var connector = new SmbCatalogConnector(fileSystem);

        using var session = connector.Connect(
            LocationFor($@"\\{Host}\share\docs"), Guest(), CancellationToken.None);
        var entry = Assert.Single(session.ListDirectory("/share/docs"), item => item.Name == "loop");

        Assert.True(entry.IsDirectory);
        Assert.Equal("/share/docs", entry.VisitedIdentity);
    }

    // ------------------------------------------------------------------ typed errors

    [Fact]
    public void Connect_MissingShare_FailsWithTypedConnectionError()
    {
        var fileSystem = new FakeScanFileSystem();
        fileSystem.AddDirectory($@"\\{Host}\share");
        fileSystem.SetEnumerateFailure(
            $@"\\{Host}\share",
            () => new IOException("The network path was not found.", unchecked((int)0x80070035)),
            failTimes: int.MaxValue);
        var connector = new SmbCatalogConnector(fileSystem);

        var exception = Assert.Throws<RemoteConnectionException>(() => connector.Connect(
            LocationFor($@"\\{Host}\share"), Guest(), CancellationToken.None));

        Assert.StartsWith(
            LocalizationService.Current.Format(TextKeys.SmbConnectionFailed, $@"\\{Host}\share"),
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Connect_RejectedCredentials_FailsWithTypedAuthenticationError()
    {
        var fileSystem = new FakeScanFileSystem();
        fileSystem.AddDirectory($@"\\{Host}\share");
        fileSystem.SetEnumerateFailure(
            $@"\\{Host}\share",
            () => new IOException("Logon failure: unknown user name or bad password.", unchecked((int)0x8007052E)),
            failTimes: int.MaxValue);
        var connector = new SmbCatalogConnector(fileSystem);

        var exception = Assert.Throws<RemoteAuthenticationException>(() => connector.Connect(
            LocationFor($@"\\{Host}\share"), Guest(), CancellationToken.None));

        Assert.Equal(
            LocalizationService.Current.Get(TextKeys.SmbAuthenticationFailed),
            exception.Message);
    }

    [Fact]
    public void ListDirectory_MissingFolder_FailsWithTypedDirectoryError()
    {
        var fileSystem = new FakeScanFileSystem();
        SeedShare(fileSystem);
        var connector = new SmbCatalogConnector(fileSystem);

        using var session = connector.Connect(
            LocationFor($@"\\{Host}\share\docs"), Guest(), CancellationToken.None);
        var exception = Assert.Throws<RemoteDirectoryNotFoundException>(() =>
            session.ListDirectory("/share/docs/missing"));

        Assert.Contains("/share/docs/missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ListDirectory_AccessDenied_FailsWithTypedListingError()
    {
        var fileSystem = new FakeScanFileSystem();
        SeedShare(fileSystem);
        fileSystem.AddDirectory($@"\\{Host}\share\docs\locked");
        fileSystem.SetEnumerateFailure(
            $@"\\{Host}\share\docs\locked",
            () => new UnauthorizedAccessException("Access to the path is denied."),
            failTimes: int.MaxValue);
        var connector = new SmbCatalogConnector(fileSystem);

        using var session = connector.Connect(
            LocationFor($@"\\{Host}\share\docs"), Guest(), CancellationToken.None);
        var exception = Assert.Throws<RemoteListingNotSupportedException>(() =>
            session.ListDirectory("/share/docs/locked"));

        Assert.Equal(
            LocalizationService.Current.Format(TextKeys.SmbListingNotSupported, "/share/docs/locked"),
            exception.Message);
    }

    // ------------------------------------------------------------------ cancellation and platform

    [Fact]
    public void Connect_CancelledToken_ThrowsOperationCanceled()
    {
        var fileSystem = new FakeScanFileSystem();
        SeedShare(fileSystem);
        var connector = new SmbCatalogConnector(fileSystem);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => connector.Connect(
            LocationFor($@"\\{Host}\share\docs"), Guest(), cancelled.Token));
    }

    [Fact]
    public void Connect_UnsupportedPlatform_FailsWithTypedPlatformError()
    {
        var connector = new SmbCatalogConnector(new FakeScanFileSystem());

        if (OperatingSystem.IsWindows())
        {
            // The platform guard is not reachable here; the wiring itself is still exercised.
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => connector.Connect(
                LocationFor($@"\\{Host}\share\docs"), Guest(), cancelled.Token));
            return;
        }

        var exception = Assert.Throws<RemotePlatformNotSupportedException>(() => connector.Connect(
            LocationFor($@"\\{Host}\share\docs"), Guest(), CancellationToken.None));
        Assert.Equal(
            LocalizationService.Current.Format(TextKeys.RemotePlatformNotSupported, "SMB"),
            exception.Message);
    }

    // ------------------------------------------------------------------ translation matrix

    [Theory]
    [InlineData(unchecked((int)0x80070005), typeof(RemoteAuthenticationException))]
    [InlineData(unchecked((int)0x8007052E), typeof(RemoteAuthenticationException))]
    [InlineData(unchecked((int)0x80070035), typeof(RemoteConnectionException))]
    [InlineData(unchecked((int)0x80070043), typeof(RemoteConnectionException))]
    [InlineData(unchecked((int)0x8007274C), typeof(RemoteConnectionException))]
    public void TranslateException_MapsWin32Failures(int hresult, Type expected)
    {
        var location = RemoteLocation.Parse($@"\\{Host}\share\docs");
        var translated = SmbCatalogConnector.TranslateException(
            new IOException("boom", hresult), $@"\\{Host}\share", location, isListing: false);

        Assert.IsType(expected, translated);
    }

    [Theory]
    [InlineData(null, "user", "user")]
    [InlineData("", "user", "user")]
    [InlineData("CORP", "user", @"CORP\user")]
    [InlineData("CORP", @"other\user", @"other\user")]
    public void FormatUser_BuildsTheWindowsAccountName(string? domain, string username, string expected)
    {
        Assert.Equal(expected, SmbNetworkConnection.FormatUser(domain, username));
    }
}
