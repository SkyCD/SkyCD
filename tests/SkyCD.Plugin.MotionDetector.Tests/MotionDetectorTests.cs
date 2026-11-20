using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Abstractions.Capabilities.Menu;
using Xunit;

namespace SkyCD.Plugin.MotionDetector.Tests;

public sealed class MotionDetectorTests
{
    [Fact]
    public void ExposesToolsCommandThroughHostManager()
    {
        var manager = new SkyCD.Plugin.Host.Menu.MenuExtensionManager([new MotionDetectorMenuPlugin()]);
        var command = Assert.Single(manager.GetMenuContributions("Tools"));
        Assert.Equal(MotionDetectorMenuPlugin.CommandId, command.CommandId);
    }

    [Fact]
    public async Task CancellationDoesNotImportFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "motion.exe"), "");
            using var cancellation = new CancellationTokenSource();
            var host = new Host();
            var plugin = new MotionDetectorMenuPlugin(directory, async (_, token) =>
            {
                cancellation.Cancel();
                await Task.Delay(Timeout.Infinite, token);
            });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => plugin.ExecuteMenuCommandAsync(
                MotionDetectorMenuPlugin.CommandId, new MenuCommandContext { HostApi = host }, cancellation.Token));
            Assert.Empty(host.Files);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(".AVI", "Movies")]
    [InlineData(".mpg", "Movies")]
    [InlineData(".wmv", "Movies")]
    [InlineData(".jpg", "Pictures")]
    [InlineData(".bmp", "Pictures")]
    [InlineData(".exe", null)]
    public void ClassifiesLegacyExtensions(string extension, string? expected) => Assert.Equal(expected, MotionDetectorMenuPlugin.Classify(extension));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportsOnlyNewCapturesOrReportsMissingExecutable(bool executableExists)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            if (executableExists) File.WriteAllText(Path.Combine(directory, "motion.exe"), "");
            File.WriteAllText(Path.Combine(directory, "old.jpg"), "old");
            var host = new Host();
            var plugin = new MotionDetectorMenuPlugin(directory, (_, _) =>
            {
                File.WriteAllText(Path.Combine(directory, "new.AVI"), "video");
                File.WriteAllText(Path.Combine(directory, "new.bmp"), "image");
                return Task.CompletedTask;
            });
            Assert.Single(plugin.GetMenuContributions());
            await plugin.ExecuteMenuCommandAsync(MotionDetectorMenuPlugin.CommandId, new MenuCommandContext { HostApi = host });
            if (!executableExists) { Assert.Contains("motion.exe", host.Message); Assert.Empty(host.Files); return; }
            Assert.Equal(2, host.Files.Count);
            Assert.Contains(host.Files, x => x.Folder == "Movies");
            Assert.Contains(host.Files, x => x.Folder == "Pictures");
            Assert.All(host.Files, file =>
            {
                Assert.Equal(DateTime.Now.ToString("yyyy-MM-dd"), file.Date);
                Assert.Equal(5, file.Size);
                Assert.Equal(8, file.Properties.Count);
                Assert.Equal(file.Name, file.Properties["Name"]);
            });
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class Host : ICatalogCaptureHostApi
    {
        public IReadOnlyList<CapturedCatalogFile> Files = [];
        public string Message = "";
        public Task ImportCapturedFilesAsync(IReadOnlyList<CapturedCatalogFile> files, CancellationToken cancellationToken = default) { Files = files; return Task.CompletedTask; }
        public Task NavigateToNodeAsync(long nodeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NotifyAsync(string message, CancellationToken cancellationToken = default) { Message = message; return Task.CompletedTask; }
    }
}
