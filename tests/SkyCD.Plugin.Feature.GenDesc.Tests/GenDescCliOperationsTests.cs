using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SkyCD.Plugin.Feature.GenDesc.Cli;
using SkyCD.TestSupport;
using Xunit;

namespace SkyCD.Plugin.Feature.GenDesc.Tests;

public class GenDescCliOperationsTests
{
    private static string Templates(TestWorkspace workspace) => workspace.GetPath("templates");

    private static string Source(TestWorkspace workspace, string name = "Movie.mkv") =>
        workspace.CreateFile(name, new byte[1024]);

    [Fact]
    public async Task NfoIsWrittenNextToTheSourceFile()
    {
        using var workspace = new TestWorkspace();
        var source = Source(workspace);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exit = await GenDescCliOperations.GenerateNfoAsync(source, null, null, "en", null, null, false, false,
            stdout, stderr, Templates(workspace));

        Assert.Equal(GenDescCliExitCodes.Success, exit);
        Assert.Equal(string.Empty, stderr.ToString());
        var target = Path.ChangeExtension(source, ".nfo");
        Assert.Equal(target, stdout.ToString().Trim());
        var written = await File.ReadAllTextAsync(target);
        Assert.Contains("File: Movie.mkv", written);
        Assert.Contains("Category: Movie", written);
    }

    [Fact]
    public async Task ExplicitOutputCreatesMissingDirectories()
    {
        using var workspace = new TestWorkspace();
        var target = workspace.GetPath(Path.Combine("nested", "out", "Movie.nfo"));

        var exit = await GenDescCliOperations.GenerateNfoAsync(Source(workspace), target, null, "en", null, null,
            false, false, new StringWriter(), new StringWriter(), Templates(workspace));

        Assert.Equal(GenDescCliExitCodes.Success, exit);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task ExistingOutputRequiresForce()
    {
        using var workspace = new TestWorkspace();
        var source = Source(workspace);
        var target = Path.ChangeExtension(source, ".nfo");
        await File.WriteAllTextAsync(target, "old");
        var stderr = new StringWriter();

        var refused = await GenDescCliOperations.GenerateNfoAsync(source, null, null, "en", null, null, false,
            false, new StringWriter(), stderr, Templates(workspace));

        Assert.Equal(GenDescCliExitCodes.CommandFailed, refused);
        Assert.Contains("--force", stderr.ToString());
        Assert.Equal("old", await File.ReadAllTextAsync(target));

        var forced = await GenDescCliOperations.GenerateNfoAsync(source, null, null, "en", null, null, true, false,
            new StringWriter(), new StringWriter(), Templates(workspace));

        Assert.Equal(GenDescCliExitCodes.Success, forced);
        Assert.Contains("File: Movie.mkv", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task DashWritesTheNfoToStandardOutput()
    {
        using var workspace = new TestWorkspace();
        var source = Source(workspace);
        var stdout = new StringWriter();

        var exit = await GenDescCliOperations.GenerateNfoAsync(source, "-", null, "en", null, null, false, false,
            stdout, new StringWriter(), Templates(workspace));

        Assert.Equal(GenDescCliExitCodes.Success, exit);
        Assert.Contains("File: Movie.mkv", stdout.ToString());
        Assert.False(File.Exists(Path.ChangeExtension(source, ".nfo")));
    }

    [Fact]
    public async Task NfoJsonReportsTheSelectedTemplate()
    {
        using var workspace = new TestWorkspace();
        var stdout = new StringWriter();

        var exit = await GenDescCliOperations.GenerateNfoAsync(Source(workspace), "-", null, "en", null, null, false,
            true, stdout, new StringWriter(), Templates(workspace));

        Assert.Equal(GenDescCliExitCodes.Success, exit);
        using var document = JsonDocument.Parse(stdout.ToString());
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("gendesc nfo", root.GetProperty("command").GetString());
        Assert.Equal("movie", root.GetProperty("category").GetString());
        Assert.Equal("Movie", root.GetProperty("categoryTitle").GetString());
        Assert.Equal("default-nfo-movie", root.GetProperty("template").GetString());
        Assert.Equal("-", root.GetProperty("output").GetString());
        Assert.Contains("File: Movie.mkv", root.GetProperty("text").GetString());
    }

    [Fact]
    public async Task DescriptionIsPrintedAsBbCode()
    {
        using var workspace = new TestWorkspace();
        var stdout = new StringWriter();

        var exit = await GenDescCliOperations.GenerateDescriptionAsync(Source(workspace), null, "en", null, null,
            false, stdout, new StringWriter(), Templates(workspace));

        Assert.Equal(GenDescCliExitCodes.Success, exit);
        var text = stdout.ToString().Trim();
        Assert.StartsWith("[b]Movie[/b]", text);
        Assert.DoesNotContain("File:", text);
    }

    [Fact]
    public async Task DescriptionJsonReportsTheText()
    {
        using var workspace = new TestWorkspace();
        var stdout = new StringWriter();

        var exit = await GenDescCliOperations.GenerateDescriptionAsync(Source(workspace), null, "en", null, null,
            true, stdout, new StringWriter(), Templates(workspace));

        Assert.Equal(GenDescCliExitCodes.Success, exit);
        using var document = JsonDocument.Parse(stdout.ToString());
        var root = document.RootElement;
        Assert.Equal("gendesc description", root.GetProperty("command").GetString());
        Assert.Equal("default-description-movie", root.GetProperty("template").GetString());
        Assert.StartsWith("[b]Movie[/b]", root.GetProperty("text").GetString());
    }

    [Fact]
    public async Task TemplatesFromTheOverriddenDirectoryAreUsed()
    {
        using var workspace = new TestWorkspace();
        var directory = Templates(workspace);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "custom.tpl"),
            "## id=custom\n## kind=description\n## category=movie\n## lang=en\n## ext=.mkv\n{{ Title }} secret");
        var stdout = new StringWriter();

        var exit = await GenDescCliOperations.GenerateDescriptionAsync(Source(workspace), "custom", "en", null, null,
            false, stdout, new StringWriter(), directory);

        Assert.Equal(GenDescCliExitCodes.Success, exit);
        Assert.Equal("Movie secret", stdout.ToString().Trim());
    }

    [Fact]
    public async Task FactOverridesFeedTheTemplate()
    {
        using var workspace = new TestWorkspace();
        var stdout = new StringWriter();

        var exit = await GenDescCliOperations.GenerateDescriptionAsync(Source(workspace), null, "en", null,
            ["Title=Renamed", "Year=1999"], false, stdout, new StringWriter(), Templates(workspace));

        Assert.Equal(GenDescCliExitCodes.Success, exit);
        Assert.StartsWith("[b]Renamed (1999)[/b]", stdout.ToString());
    }

    [Fact]
    public async Task MimeOverrideChangesTheDetectedCategory()
    {
        using var workspace = new TestWorkspace();
        var source = Source(workspace, "clip.xyz");
        var stdout = new StringWriter();

        var exit = await GenDescCliOperations.GenerateDescriptionAsync(source, null, "en", "video/mp4", null, true,
            stdout, new StringWriter(), Templates(workspace));

        Assert.Equal(GenDescCliExitCodes.Success, exit);
        using var document = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("movie", document.RootElement.GetProperty("category").GetString());
    }

    [Fact]
    public async Task MalformedFactsAreRejected()
    {
        using var workspace = new TestWorkspace();
        var stderr = new StringWriter();

        var exit = await GenDescCliOperations.GenerateDescriptionAsync(Source(workspace), null, "en", null,
            ["missing-equals"], false, new StringWriter(), stderr, Templates(workspace));

        Assert.Equal(GenDescCliExitCodes.InvalidArguments, exit);
        Assert.Contains("key=value", stderr.ToString());
    }

    [Fact]
    public async Task MissingFilesFailTheCommand()
    {
        using var workspace = new TestWorkspace();
        var stderr = new StringWriter();

        var exit = await GenDescCliOperations.GenerateNfoAsync(workspace.GetPath("absent.mkv"), null, null, "en",
            null, null, false, false, new StringWriter(), stderr, Templates(workspace));

        Assert.Equal(GenDescCliExitCodes.CommandFailed, exit);
        Assert.Contains("absent.mkv", stderr.ToString());
    }

    [Fact]
    public async Task BlankFileIsAnInvalidArgument()
    {
        using var workspace = new TestWorkspace();
        var stderr = new StringWriter();

        var exit = await GenDescCliOperations.GenerateNfoAsync(" ", null, null, "en", null, null, false, false,
            new StringWriter(), stderr, Templates(workspace));

        Assert.Equal(GenDescCliExitCodes.InvalidArguments, exit);
        Assert.NotEqual(string.Empty, stderr.ToString());
    }
}
