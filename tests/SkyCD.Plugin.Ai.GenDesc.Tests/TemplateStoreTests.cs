using System;
using System.IO;
using System.Linq;
using SkyCD.Plugin.Ai.GenDesc.Models;
using SkyCD.Plugin.Ai.GenDesc.Parsing;
using Xunit;

namespace SkyCD.Plugin.Ai.GenDesc.Tests;

public class TemplateStoreTests : IDisposable
{
    private readonly string workingDirectory;

    public TemplateStoreTests()
    {
        workingDirectory = Path.Combine(Path.GetTempPath(), "gendesc-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDirectory);
    }

    public void Dispose()
    {
        try { Directory.Delete(workingDirectory, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void LoadIncludesBothBuiltInDefaults()
    {
        var store = TemplateStore.Load(null);
        var ids = store.Templates.Select(t => t.Id).ToArray();
        Assert.Contains(BuiltInTemplates.EnglishId, ids);
        Assert.Contains(BuiltInTemplates.LithuanianId, ids);
    }

    [Fact]
    public void SelectEnglishPrefersEnglishTemplate()
    {
        var store = TemplateStore.Load(null);
        Assert.Equal(BuiltInTemplates.EnglishId, store.Select("default", "en")?.Id);
    }

    [Fact]
    public void SelectLithuanianPrefersLithuanianTemplate()
    {
        var store = TemplateStore.Load(null);
        Assert.Equal(BuiltInTemplates.LithuanianId, store.Select("default", "lt")?.Id);
    }

    [Fact]
    public void SelectUnknownLanguageFallsBackToEnglish()
    {
        var store = TemplateStore.Load(null);
        Assert.Equal(BuiltInTemplates.EnglishId, store.Select("imdb", "de")?.Id);
    }

    [Fact]
    public void ExplicitTemplateIdWinsOverScoring()
    {
        var store = TemplateStore.Load(null);
        Assert.Equal(BuiltInTemplates.LithuanianId, store.Select("default", "en", BuiltInTemplates.LithuanianId)?.Id);
    }

    [Fact]
    public void UserFileWithSameIdOverridesBuiltIn()
    {
        var path = Path.Combine(workingDirectory, BuiltInTemplates.EnglishId + ".tpl");
        File.WriteAllText(path, "## id=default-english\n## site=default\n## lang=en\n## version=9\n[b]custom[/b]");

        var store = TemplateStore.Load(workingDirectory);
        var template = store.Select("default", "en");

        Assert.Equal(BuiltInTemplates.EnglishId, template?.Id);
        Assert.Equal("9", template?.Version);
        Assert.Equal("[b]custom[/b]", template?.Body);
    }

    [Fact]
    public void UserTemplateForASiteIsSelectedForThatSite()
    {
        var path = Path.Combine(workingDirectory, "imdb-english.tpl");
        File.WriteAllText(path, "## id=imdb-english\n## site=imdb\n## lang=en\n## version=1\n[i]from imdb template[/i]");

        var store = TemplateStore.Load(workingDirectory);
        var template = store.Select("imdb", "en");

        Assert.Equal("imdb-english", template?.Id);
    }

    [Fact]
    public void SeedUserTemplatesWritesDefaultsOnce()
    {
        var store = TemplateStore.Load(null);

        Assert.Equal(2, store.SeedUserTemplates(workingDirectory));
        Assert.Equal(0, store.SeedUserTemplates(workingDirectory));
        Assert.True(File.Exists(Path.Combine(workingDirectory, BuiltInTemplates.EnglishId + ".tpl")));
        Assert.True(File.Exists(Path.Combine(workingDirectory, BuiltInTemplates.LithuanianId + ".tpl")));
    }
}
