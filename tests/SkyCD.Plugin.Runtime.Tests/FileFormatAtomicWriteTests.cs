using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SkyCD.Plugin.Runtime.Exceptions;
using SkyCD.Plugin.Runtime.Managers;
using SkyCD.SDK.Capabilities;
using SkyCD.SDK.Capabilities.FileFormats;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.Runtime.Tests;

/// <summary>
/// Issue #1149: saving into a protected folder must explain itself instead of leaking the internal
/// temp name, must fall back to an in-place write when only replacing is allowed, and must never
/// leave a temp file behind.
/// </summary>
public sealed class FileFormatAtomicWriteTests
{
    private static readonly byte[] NewPayload = Encoding.UTF8.GetBytes("new-catalog-bytes");

    [Fact]
    public async Task WriteFileAsync_WritesAtomicallyAndLeavesNoTemp()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.Root;
        var destination = Path.Combine(directory, "catalog.ascd");
        var manager = new FileFormatManager([new WritingFileFormatCapability()]);

        await manager.WriteFileAsync("test", destination, new object(), cancellationToken: CancellationToken.None);

        Assert.Equal(NewPayload, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task WriteFileAsync_WhenReplaceDenied_ThrowsNamingFolderNotTemp()
    {
        // A read-only destination makes the atomic replace fail on Windows; Unix renames over it.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new TestWorkspace();
        var directory = workspace.Root;
        var destination = workspace.CreateFile("catalog.ascd", "old-catalog-bytes");
        File.SetAttributes(destination, FileAttributes.ReadOnly);
        try
        {
            var manager = new FileFormatManager([new WritingFileFormatCapability()]);

            var exception = await Assert.ThrowsAsync<CatalogSaveIOException>(() =>
                manager.WriteFileAsync("test", destination, new object(), cancellationToken: CancellationToken.None));

            Assert.Equal(directory, exception.DirectoryPath);
            Assert.DoesNotContain(".tmp", exception.Message, StringComparison.Ordinal);
            Assert.Equal("old-catalog-bytes", await File.ReadAllTextAsync(destination));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            File.SetAttributes(destination, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task WriteFileAsync_WhenSiblingCreationDeniedButDestinationWritable_FallsBackInPlace()
    {
        // Unix permission bits deny creating the temp sibling while the catalog file itself stays
        // writable; Windows ACLs cannot express this portably in a unit test.
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            await UnixSiblingCreationDeniedButDestinationWritableCore();
        }
    }

    [Fact]
    public async Task WriteFileAsync_WhenSiblingCreationDeniedAndDestinationReadOnly_ThrowsNamingFolder()
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            await UnixSiblingCreationDeniedAndDestinationReadOnlyCore();
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private async Task UnixSiblingCreationDeniedButDestinationWritableCore()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.Root;
        var destination = workspace.CreateFile("catalog.ascd", "old-catalog-bytes");
        SetUnixMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead |
            UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        SetUnixMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead |
            UnixFileMode.OtherRead);
        try
        {
            if (CanCreateSiblings(directory))
            {
                // Running with elevated rights bypasses permission bits; the fixture cannot apply.
                return;
            }

            var manager = new FileFormatManager([new WritingFileFormatCapability()]);

            await manager.WriteFileAsync("test", destination, new object(), cancellationToken: CancellationToken.None);

            Assert.Equal(NewPayload, await File.ReadAllBytesAsync(destination));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            SetUnixMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            SetUnixMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private async Task UnixSiblingCreationDeniedAndDestinationReadOnlyCore()
    {
        using var workspace = new TestWorkspace();
        var directory = workspace.Root;
        var destination = workspace.CreateFile("catalog.ascd", "old-catalog-bytes");
        SetUnixMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead |
            UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        SetUnixMode(destination, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        try
        {
            if (CanCreateSiblings(directory))
            {
                return;
            }

            var manager = new FileFormatManager([new WritingFileFormatCapability()]);

            var exception = await Assert.ThrowsAsync<CatalogSaveIOException>(() =>
                manager.WriteFileAsync("test", destination, new object(), cancellationToken: CancellationToken.None));

            Assert.Equal(directory, exception.DirectoryPath);
            Assert.DoesNotContain(".tmp", exception.Message, StringComparison.Ordinal);
            Assert.Equal("old-catalog-bytes", await File.ReadAllTextAsync(destination));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
        }
        finally
        {
            SetUnixMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            SetUnixMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static void SetUnixMode(string path, UnixFileMode mode) => File.SetUnixFileMode(path, mode);

    private static bool CanCreateSiblings(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".probe.{Guid.NewGuid():N}.tmp");
            using (File.Create(probe))
            {
            }

            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class WritingFileFormatCapability : IFileFormatPluginCapability
    {
        // Assembly-level discovery instantiates every IPluginCapability that has a public parameterless
        // constructor; the descriptor parameter keeps this fixture out of the discovered capability set so
        // its ".ascd" extension cannot become the fixture plugin's primary extension.
        public WritingFileFormatCapability(FileFormatDescriptor? descriptor = null) =>
            SupportedFormat = descriptor
                ?? new FileFormatDescriptor("test", "Test", [".ascd"], ["application/test"], true, true);

        public FileFormatDescriptor SupportedFormat { get; }

        public Task<FileFormatReadResult> ReadAsync(FileFormatReadRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<FileFormatWriteResult> WriteAsync(FileFormatWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            await request.Target.WriteAsync(NewPayload, cancellationToken);
            return new FileFormatWriteResult { Success = true };
        }
    }
}
