using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace SkyCD.TestSupport.Tests;

public sealed class TestWorkspaceTests
{
    [Fact]
    public void TwoWorkspaces_UseDistinctRootsUnderTheTempPath()
    {
        using var first = new TestWorkspace();
        using var second = new TestWorkspace();

        Assert.NotEqual(first.Root, second.Root, StringComparer.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(first.Root));
        Assert.True(Directory.Exists(second.Root));
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(first.Root), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Root_IsNotCreatedUntilUsed()
    {
        var workspace = new TestWorkspace();
        var root = workspace.Root;
        Assert.True(Directory.Exists(root));

        workspace.Dispose();
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public void CreateFile_WritesBytesAndParentDirectories()
    {
        using var workspace = new TestWorkspace();

        byte[] bytes = [1, 2, 3, 4];
        var path = workspace.CreateFile(Path.Combine("nested", "deeper", "data.bin"), bytes);

        Assert.True(File.Exists(path));
        Assert.Equal(Path.Combine(workspace.Root, "nested", "deeper", "data.bin"), path);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void CreateFile_StringOverload_WritesText()
    {
        using var workspace = new TestWorkspace();

        var path = workspace.CreateFile("notes.txt", "hello");
        var empty = workspace.CreateEmptyFile(Path.Combine("empty", "zero.dat"));
        var directory = workspace.CreateDirectory(Path.Combine("a", "b"));

        Assert.Equal("hello", File.ReadAllText(path, Encoding.UTF8));
        Assert.Equal(0, new FileInfo(empty).Length);
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    public void Dispose_RemovesTheTree()
    {
        var workspace = new TestWorkspace();
        var root = workspace.CreateDirectory("sub");
        workspace.CreateEmptyFile(Path.Combine("sub", "file.txt"));

        workspace.Dispose();

        Assert.False(Directory.Exists(root));
        Assert.False(Directory.Exists(Path.GetDirectoryName(root)!));
    }

    [Fact]
    public void Dispose_Twice_IsSafe()
    {
        var workspace = new TestWorkspace();
        var root = workspace.CreateEmptyFile("file.txt");

        workspace.Dispose();
        workspace.Dispose();

        Assert.False(File.Exists(root));
    }

    [Fact]
    public async Task DisposeAsync_IsSafeAfterDispose()
    {
        var workspace = new TestWorkspace();
        var root = workspace.CreateEmptyFile("file.txt");

        await workspace.DisposeAsync();
        await workspace.DisposeAsync();

        Assert.False(File.Exists(root));
    }

    [Fact]
    public void Dispose_AfterRootWasDeletedExternally_IsSafe()
    {
        var workspace = new TestWorkspace();
        var root = workspace.Root;
        workspace.CreateEmptyFile(Path.Combine("sub", "file.txt"));
        Directory.Delete(root, recursive: true);

        workspace.Dispose();

        Assert.False(Directory.Exists(root));
    }
}
