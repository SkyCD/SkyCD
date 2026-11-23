using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.PackagePaths;
using SkyCD.Plugin.Metadata.Archives;
using SkyCD.Plugin.Metadata.Archives.Rar;
using SkyCD.Plugin.Metadata.Archives.Rar.Exceptions;
using SkyCD.Plugin.Metadata.Archives.Rar.Tests;
using Xunit;
using SkyCD.Plugin.Metadata.Archives.Models;
using SkyCD.Plugin.Metadata.Archives.Rar.Parsing;

namespace SkyCD.Plugin.Metadata.Archives.Rar.Tests;

public sealed class RarInternalsTests
{
    [Fact]
    public void Signature_RecognizesRar4AndRar5Markers()
    {
        Assert.True(RarArchiveSignature.Matches("Rar!\u001a\u0007\u0000\u0000"u8));
        Assert.True(RarArchiveSignature.Matches("Rar!\u001a\u0007\u0001\u0000"u8));
        Assert.False(RarArchiveSignature.Matches("PK\u0003\u0004"u8));
        Assert.False(RarArchiveSignature.Matches("Rar!\u001a\u0007\u0002\u0000"u8));
        Assert.False(RarArchiveSignature.Matches("Rar!"u8));
    }

    [Fact]
    public void Signature_MatchesFromAnyStreamPosition_AndRestoresIt()
    {
        using var archive = RarTestArchiveBuilder.CreateStored(("a.txt", "a"));
        archive.Position = 3;

        Assert.True(RarArchiveSignature.Matches(archive));
        Assert.Equal(3, archive.Position);
    }

    [Fact]
    public void HeaderScan_NonRarSource_FailsWithDamagedCode()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("definitely not rar"));

        var status = RarHeaderScan.Inspect(source, hasCompanionVolume: false);

        Assert.False(status.IsReadable);
        Assert.False(status.IsMultiVolume);
        Assert.False(status.IsEncrypted);
        Assert.StartsWith(RarFailures.DamagedCode, status.Error);
    }

    [Fact]
    public void HeaderScan_StoredArchive_IsReadable()
    {
        using var archive = RarTestArchiveBuilder.CreateStored(("folder/file.txt", "abc"));

        var status = RarHeaderScan.Inspect(archive, hasCompanionVolume: false);

        Assert.True(status.IsReadable);
        Assert.Null(status.Error);
    }

    [Fact]
    public void HeaderScan_EncryptedEntries_AreReportedAsEncrypted()
    {
        using var archive = RarTestArchiveBuilder.CreateEncrypted(("secret.txt", "hidden"));

        var status = RarHeaderScan.Inspect(archive, hasCompanionVolume: false);

        Assert.True(status.IsEncrypted);
        Assert.False(status.IsReadable);
        Assert.StartsWith(RarFailures.EncryptedCode, status.Error);
    }

    [Fact]
    public void HeaderScan_CompanionVolume_IsReportedAsMultiVolume()
    {
        using var archive = RarTestArchiveBuilder.CreateStored(("file.txt", "abc"));

        var status = RarHeaderScan.Inspect(archive, hasCompanionVolume: true);

        Assert.True(status.IsMultiVolume);
        Assert.False(status.IsReadable);
    }

    [Fact]
    public void HeaderScan_SplitEntry_IsReportedAsMultiVolume()
    {
        using var archive = RarTestArchiveBuilder.CreateSplitEntry("big.bin", "chunk");

        var status = RarHeaderScan.Inspect(archive, hasCompanionVolume: false);

        Assert.True(status.IsMultiVolume);
    }

    [Fact]
    public async Task ListAsync_ReturnsEveryEntryInHeaderOrder()
    {
        using var archive = RarTestArchiveBuilder.CreateStored(("b.txt", "12"), ("a/c.txt", "345"));

        var entries = await RarHeaderScan.ListAsync(archive, CancellationToken.None);

        Assert.Equal(["b.txt", "a/c.txt"], entries.ConvertAll(entry => entry.Key));
        Assert.Equal([2L, 3L], entries.ConvertAll(entry => entry.SizeBytes));
        Assert.All(entries, entry => Assert.NotNull(entry.ModifiedUtc));
    }

    [Theory]
    [InlineData("folder/file.txt", "folder/file.txt", true)]
    [InlineData("folder\\file.txt", "folder/file.txt", true)]
    [InlineData("./folder/file.txt", "folder/file.txt", true)]
    [InlineData("folder/../file.txt", "folder/../file.txt", false)]
    [InlineData("../escape.txt", "../escape.txt", false)]
    [InlineData("/absolute.txt", "absolute.txt", false)]
    [InlineData("C:\\windows\\file.txt", "C:/windows/file.txt", false)]
    public void EntryPath_NormalizesAndFlagsUnsafePaths(string raw, string expected, bool safe)
    {
        Assert.True(PackageEntryPath.TryNormalize(raw, out var normalized, out var isSafe, PackageEntryPathRules.SkipCurrentDirectorySegments));
        Assert.Equal(expected, normalized);
        Assert.Equal(safe, isSafe);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EntryPath_RejectsEmptyNames(string raw)
    {
        Assert.False(PackageEntryPath.TryNormalize(raw, out _, out _, PackageEntryPathRules.SkipCurrentDirectorySegments));
    }

    [Fact]
    public void EntryPath_TraversalCannotEscapeRoot()
    {
        var root = RarTestHelpers.CreateTemporaryDirectory();
        try
        {
            Assert.True(PackageEntryPath.TryResolveUnderRoot(root, "sub/file.txt", out var inside));
            Assert.StartsWith(root, inside, StringComparison.OrdinalIgnoreCase);
            Assert.False(PackageEntryPath.TryResolveUnderRoot(root, "../outside.txt", out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("set.rar", "set.part1.rar", "set.r00")]
    [InlineData("set.part1.rar", "set.part2.rar", "set.rar", "set.r00")]
    [InlineData("set.part2.rar", "set.part1.rar", "set.part3.rar", "set.rar", "set.r00")]
    [InlineData("set.r00", "set.r00")]
    public void VolumeCompanions_ProduceCandidateNames(string fileName, params string[] expected)
    {
        var names = RarVolumeCompanions.CompanionNames(fileName)
            .Select(name => name.ToLowerInvariant())
            .Distinct()
            .ToArray();

        Assert.Equal(expected, names);
    }

    [Fact]
    public void VolumeCompanions_FindCompanionNextToSource_AndIgnoreUnrelatedFiles()
    {
        var directory = RarTestHelpers.CreateTemporaryDirectory();
        try
        {
            var first = Path.Combine(directory, "archive.part1.rar");
            File.WriteAllBytes(first, [0]);
            Assert.Null(RarVolumeCompanions.FindCompanion(first));
            Assert.Null(RarVolumeCompanions.FindCompanion(null));

            var second = Path.Combine(directory, "archive.part2.rar");
            File.WriteAllBytes(second, [0]);
            Assert.Equal(second, RarVolumeCompanions.FindCompanion(first));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task VolumeAwareStream_RaisesMultiVolumeFailure_InsteadOfSilentEndOfData()
    {
        using var inner = new MemoryStream("abc"u8.ToArray());
        await using var wrapped = RarVolumeAwareStream.Wrap(inner);
        var buffer = new byte[3];

        Assert.Equal(3, await wrapped.ReadAsync(buffer));
        Assert.Equal(0, await wrapped.ReadAsync(buffer));
        await Assert.ThrowsAsync<MultiVolumeMissingException>(() => wrapped.ReadAsync(buffer).AsTask());
    }

    [Fact]
    public void Failures_MapExceptionsToTypedCodes()
    {
        Assert.Equal(RarFailures.EncryptedCode, RarFailures.CodeFor(new System.Security.Cryptography.CryptographicException()));
        Assert.Equal(RarFailures.EncryptedCode, RarFailures.CodeFor(new SharpCompress.Common.CryptographicException("locked")));
        Assert.Equal(RarFailures.UnsupportedCode, RarFailures.CodeFor(new NotSupportedException()));
        Assert.Equal(RarFailures.MultivolumeCode,
            RarFailures.CodeFor(new SharpCompress.Common.MultiVolumeExtractionException("missing volume")));
        Assert.Equal(RarFailures.DamagedCode, RarFailures.CodeFor(new InvalidOperationException()));
        Assert.Equal(RarFailures.DamagedCode, RarFailures.CodeFor(new IOException()));
        Assert.True(RarFailures.IsArchiveFailure(new SharpCompress.Common.InvalidFormatException()));
        Assert.False(RarFailures.IsArchiveFailure(new NullReferenceException()));
        Assert.StartsWith("RAR_DAMAGED: ", RarFailures.Format(new IOException("bad")));
    }
}
