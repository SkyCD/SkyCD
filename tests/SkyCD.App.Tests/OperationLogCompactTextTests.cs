using System;
using System.IO;
using SkyCD.Presentation.Operations;
using SkyCD.Presentation.Scanning;
using SkyCD.Presentation.ViewModels;
using Xunit;

namespace SkyCD.App.Tests;

public class OperationLogCompactTextTests
{
    private static readonly DateTimeOffset Timestamp = new(2026, 11, 26, 12, 29, 30, 705, TimeSpan.Zero);
    private static readonly string Sep = Path.DirectorySeparatorChar.ToString();

    private static string RootDir(string name) =>
        Path.Combine(Path.GetTempPath(), "skycd-compact-" + name);

    private static OperationLogEntry Entry(
        OperationLogLevel level,
        string message,
        string? stage = null,
        string? itemPath = null,
        string? details = null,
        string? scanRoot = null) =>
        new OperationLogEntry(level, message, stage, itemPath, details, Timestamp) { ScanRoot = scanRoot };

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static Exception ThrownInvalidOperation(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public void CompactTimestamp_DropsMilliseconds()
    {
        var entry = Entry(OperationLogLevel.Information, "Probe");

        Assert.Equal("12:29:30", entry.CompactTimestamp);
        Assert.DoesNotContain(".705", entry.CompactText, StringComparison.Ordinal);
        Assert.StartsWith("[12:29:30] ", entry.CompactText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(OperationLogLevel.Debug, "🔍")]
    [InlineData(OperationLogLevel.Information, "ℹ️")]
    [InlineData(OperationLogLevel.StageChanged, "⏳")]
    [InlineData(OperationLogLevel.Warning, "⚠️")]
    [InlineData(OperationLogLevel.Error, "❌")]
    [InlineData(OperationLogLevel.ItemProcessed, "✅")]
    [InlineData(OperationLogLevel.ItemSkipped, "↪️")]
    [InlineData(OperationLogLevel.ItemFailed, "🛑")]
    public void LevelGlyph_MapsEachLevelToItsGlyph(OperationLogLevel level, string expected)
    {
        var entry = Entry(level, "Probe");

        Assert.Equal(expected, entry.LevelGlyph);
        Assert.StartsWith($"[12:29:30] {expected} ", entry.CompactText, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactText_OmitsStageWhileDisplayTextKeepsIt()
    {
        var root = RootDir("stage");
        var entry = Entry(
            OperationLogLevel.Information,
            $"Starting folder scan: {root}",
            stage: "process-file",
            itemPath: root,
            scanRoot: root);

        Assert.DoesNotContain("process-file", entry.CompactText, StringComparison.Ordinal);
        Assert.Contains("[process-file]", entry.DisplayText, StringComparison.Ordinal);
    }

    [Fact]
    public void SkipReason_AppearsOnceWithRelativePathAfterIt()
    {
        var root = RootDir("skip");
        var file = Path.Combine(root, "desktop.ini");
        var entry = Entry(
            OperationLogLevel.ItemSkipped,
            "Skipped: Well-known system entry excluded by policy.",
            stage: "process-file",
            itemPath: file,
            details: "Well-known system entry excluded by policy.",
            scanRoot: root);

        var expected = $"[12:29:30] ↪️ Skipped due Well-known system entry excluded by policy: .{Sep}desktop.ini";
        Assert.Equal(expected, entry.CompactText);
        Assert.Equal(1, CountOccurrences(entry.CompactText, "Well-known system entry excluded by policy"));
        Assert.DoesNotContain(":.", entry.CompactText, StringComparison.Ordinal);
    }

    [Fact]
    public void ChildPath_RendersRelativeToScanRoot()
    {
        var root = RootDir("child");
        var child = Path.Combine(root, "Screen Recordings");
        var entry = Entry(
            OperationLogLevel.ItemProcessed,
            "Scanning folder: Screen Recordings",
            stage: "process-directory",
            itemPath: child,
            scanRoot: root);

        Assert.Equal($"[12:29:30] ✅ Scanning folder: .{Sep}Screen Recordings", entry.CompactText);
        Assert.DoesNotContain(child, entry.CompactText, StringComparison.Ordinal);
    }

    [Fact]
    public void DeepChildPath_RendersRelativeWithSubfolders()
    {
        var root = RootDir("deep");
        var file = Path.Combine(root, "Sub", "track.mp3");
        var entry = Entry(
            OperationLogLevel.ItemProcessed,
            "Added file: track.mp3",
            stage: "process-file",
            itemPath: file,
            scanRoot: root);

        Assert.Equal($"[12:29:30] ✅ Added file: .{Sep}Sub{Sep}track.mp3", entry.CompactText);
    }

    [Fact]
    public void ScanRoot_KeepsAbsolutePathExactlyOnce()
    {
        var root = RootDir("root");
        var entry = Entry(
            OperationLogLevel.Information,
            $"Starting folder scan: {root}",
            itemPath: root,
            scanRoot: root);

        Assert.Equal($"[12:29:30] ℹ️ Starting folder scan: {root}", entry.CompactText);
        Assert.Equal(1, CountOccurrences(entry.CompactText, root));
    }

    [Fact]
    public void FailureCause_IsSingleLineWithExceptionTypeAndMessage()
    {
        var root = RootDir("fail");
        var file = Path.Combine(root, "c.dat");
        var failure = ThrownInvalidOperation("CRC fail");
        var entry = Entry(
            OperationLogLevel.ItemFailed,
            "Failed: Read failed",
            stage: "process-file",
            itemPath: file,
            details: failure.ToString(),
            scanRoot: root);

        Assert.DoesNotContain("\n", entry.CompactText, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: CRC fail", entry.CompactText, StringComparison.Ordinal);
        Assert.StartsWith($"[12:29:30] 🛑 Failed due System.InvalidOperationException: CRC fail: .{Sep}c.dat", entry.CompactText, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureWithoutException_UsesReason()
    {
        var root = RootDir("failreason");
        var file = Path.Combine(root, "c.dat");
        var entry = Entry(
            OperationLogLevel.ItemFailed,
            "Failed: CRC mismatch",
            stage: "process-file",
            itemPath: file,
            details: "CRC mismatch",
            scanRoot: root);

        Assert.Equal($"[12:29:30] 🛑 Failed due CRC mismatch: .{Sep}c.dat", entry.CompactText);
    }

    [Fact]
    public void ErrorDetails_AreReducedToFirstLine()
    {
        var root = RootDir("err");
        var file = Path.Combine(root, "bad.bin");
        var failure = ThrownInvalidOperation("CRC fail");
        var entry = Entry(
            OperationLogLevel.Error,
            "Corrupted file",
            itemPath: file,
            details: failure.ToString(),
            scanRoot: root);

        Assert.DoesNotContain("\n", entry.CompactText, StringComparison.Ordinal);
        Assert.Contains("System.InvalidOperationException: CRC fail", entry.CompactText, StringComparison.Ordinal);
    }

    [Fact]
    public void ViewModel_StampsLearnedScanRootOnLaterEntries()
    {
        var root = RootDir("stamp");
        var vm = new AddingProgressDialogViewModel();

        vm.ReportInfo($"Starting folder scan: {root}", itemPath: root);
        vm.ReportLog(
            OperationLogLevel.ItemProcessed,
            "Scanning folder: Sub",
            stage: "process-directory",
            itemPath: Path.Combine(root, "Sub"));

        Assert.Equal(2, vm.LogEntries.Count);
        Assert.Contains(root, vm.LogEntries[0].CompactText, StringComparison.Ordinal);
        Assert.Equal($"Scanning folder: .{Sep}Sub", vm.LogEntries[1].CompactMessage);
        Assert.DoesNotContain(root, vm.LogEntries[1].CompactMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void CopyLog_KeepsVerboseLines()
    {
        var root = RootDir("verbose");
        var file = Path.Combine(root, "c.dat");
        var failure = ThrownInvalidOperation("CRC fail");
        var vm = new AddingProgressDialogViewModel();

        vm.ReportInfo($"Starting folder scan: {root}", itemPath: root);
        vm.ReportLog(
            OperationLogLevel.ItemFailed,
            "Failed: Read failed",
            stage: "process-file",
            itemPath: file,
            details: failure.ToString());

        var logText = vm.GetFullLogText();
        Assert.Contains("[ItemFailed]", logText, StringComparison.Ordinal);
        Assert.Contains("[process-file]", logText, StringComparison.Ordinal);
        Assert.Contains(file, logText, StringComparison.Ordinal);
        Assert.Contains(failure.ToString(), logText, StringComparison.Ordinal);
    }

    [Fact]
    public void FolderScan_RendersCompactRelativeLines()
    {
        var root = Path.Combine(Path.GetTempPath(), "skycd-compact-scan-" + Guid.NewGuid().ToString("N"));
        var sub = Path.Combine(root, "Screen Recordings");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "clip.txt"), "x");
        var vm = new AddingProgressDialogViewModel();
        try
        {
            new FolderCatalogScanner().Scan(new FolderScanOptions(root), vm, vm.CancellationToken);

            var start = Assert.Single(
                vm.LogEntries,
                entry => entry.Message.StartsWith("Starting folder scan:", StringComparison.Ordinal));
            Assert.Contains(root, start.CompactText, StringComparison.Ordinal);
            Assert.Equal(1, CountOccurrences(start.CompactText, root));

            var folder = Assert.Single(
                vm.LogEntries,
                entry => entry.Message.StartsWith("Scanning folder:", StringComparison.Ordinal));
            Assert.Equal($"Scanning folder: .{Sep}Screen Recordings", folder.CompactMessage);

            var file = Assert.Single(
                vm.LogEntries,
                entry => entry.Message.StartsWith("Added file:", StringComparison.Ordinal));
            Assert.Equal($"Added file: .{Sep}Screen Recordings{Sep}clip.txt", file.CompactMessage);

            foreach (var entry in vm.LogEntries)
            {
                Assert.DoesNotContain(".705", entry.CompactText, StringComparison.Ordinal);
                Assert.DoesNotContain('\n', entry.CompactText);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
